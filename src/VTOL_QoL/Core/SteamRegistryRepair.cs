using System;
using System.Collections.Generic;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

// =========================================================================
// === ПРАВА НА РАЗДЕЛ STEAM В РЕЕСТРЕ ====================================
// Steam хранит свои данные в HKCU\Software\Valve\Steam и требует доступ на
// запись в этот раздел. Если записать не получается, клиент при запуске
// показывает ошибку «Путь к разделу Steam в реестре недоступен для записи.
// Это может вызывать проблемы с запуском игр и с некоторыми функциями
// клиента. Для запуска Steam необходимо исправить данную проблему и
// перезапустить клиент».
//
// Причин бывает две:
//   1) у запущенного процесса урезанный токен (песочница, AppContainer,
//      низкий уровень целостности) — тогда Steam, запущенный из него,
//      получает те же ограничения; права в реестре при этом целы;
//   2) права самого раздела испорчены (запреты, снятое наследование) —
//      это и лечится здесь.
//
// Ниже: проверка доступа на запись и восстановление стандартных прав.
// Значения, которые пишет сам Steam, не изменяются и не удаляются.
// =========================================================================
public static class SteamRegistryRepair
{
    private const string ValveKeyPath = @"Software\Valve";
    private const string SteamKeyPath = @"Software\Valve\Steam";
    private const string ProbeValueName = "QoL_WriteProbe";

    // Проверка: может ли текущий процесс писать в раздел Steam в реестре.
    // Пишем и сразу удаляем служебное значение — Steam его не видит.
    public static bool CanWrite(out string error)
    {
        error = "";
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SteamKeyPath, true))
            {
                if (key == null)
                {
                    error = "раздел HKCU\\Software\\Valve\\Steam не найден";
                    return false;
                }
                key.SetValue(ProbeValueName, 0, RegistryValueKind.DWord);
                try { key.DeleteValue(ProbeValueName, false); }
                catch { try { key.DeleteValue(ProbeValueName, false); } catch { } }
                return true;
            }
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }

    // Восстановление стандартных прав на разделы Valve и Valve\Steam:
    // снимаем запрещающие правила, возвращаем наследование от HKCU\Software,
    // выдаём полный доступ текущему пользователю, администраторам и SYSTEM.
    public static bool Repair(out string report)
    {
        StringBuilder log = new StringBuilder();
        bool ok = true;

        string[] paths = new string[]
        {
            ValveKeyPath,
            SteamKeyPath,
            SteamKeyPath + @"\ActiveProcess",
            SteamKeyPath + @"\Apps"
        };

        foreach (string path in paths)
        {
            try
            {
                if (RepairKey(path)) log.AppendLine("права в порядке: " + path);
                else log.AppendLine("раздел отсутствует (пропущен): " + path);
            }
            catch (Exception ex)
            {
                ok = false;
                log.AppendLine("ошибка: " + path + " — " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        string verifyError;
        if (CanWrite(out verifyError))
        {
            log.AppendLine("проверка записи в раздел Steam: разрешена");
        }
        else
        {
            ok = false;
            log.AppendLine("проверка записи в раздел Steam: запрещена — " + verifyError);
        }

        report = log.ToString();
        return ok;
    }

    // Восстанавливает права одного раздела. false — если раздела нет.
    // Если права уже в порядке, раздел не меняется вообще (сравниваем SDDL).
    private static bool RepairKey(string path)
    {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
            path,
            RegistryKeyPermissionCheck.ReadWriteSubTree,
            RegistryRights.ReadPermissions | RegistryRights.ChangePermissions))
        {
            if (key == null) return false;

            RegistrySecurity sec = key.GetAccessControl(AccessControlSections.Access);
            string before = sec.GetSecurityDescriptorSddlForm(AccessControlSections.Access);

            WindowsIdentity id = WindowsIdentity.GetCurrent();
            IdentityReference user = id.User;
            IdentityReference admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            IdentityReference system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            IdentityReference restricted = new SecurityIdentifier(WellKnownSidType.RestrictedCodeSid, null);

            // 1. Снимаем запреты для тех, кому доступ нужен (сначала собираем список,
            //    чтобы не менять коллекцию во время перебора).
            AuthorizationRuleCollection rules = sec.GetAccessRules(true, true, typeof(SecurityIdentifier));
            List<RegistryAccessRule> denyRules = new List<RegistryAccessRule>();
            foreach (AuthorizationRule rule in rules)
            {
                RegistryAccessRule rar = rule as RegistryAccessRule;
                if (rar == null) continue;
                if (rar.AccessControlType != AccessControlType.Deny) continue;
                if (rar.IdentityReference.Equals(user) || rar.IdentityReference.Equals(admins) || rar.IdentityReference.Equals(system))
                    denyRules.Add(rar);
            }
            foreach (RegistryAccessRule deny in denyRules)
                sec.RemoveAccessRuleSpecific(deny);

            // 2. Если наследование было отключено — возвращаем его (собственные
            //    правила раздела при этом сохраняются).
            if (sec.AreAccessRulesProtected)
                sec.SetAccessRuleProtection(false, true);

            // 3. Гарантируем полный доступ тем, кому он нужен. Уже выданные права
            //    (в т.ч. унаследованные от HKCU\Software) не дублируем.
            InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            AddAllowRuleIfMissing(sec, user, inherit);
            AddAllowRuleIfMissing(sec, admins, inherit);
            AddAllowRuleIfMissing(sec, system, inherit);

            // 4. Ограниченному коду (RC) в обычном профиле положено только чтение:
            //    если кто-то выдал ему больше — возвращаем как у прочих разделов HKCU.
            if (restricted != null && !HasReadOnlyRule(sec, restricted))
            {
                RemoveAllowRules(sec, restricted);
                sec.AddAccessRule(new RegistryAccessRule(
                    restricted,
                    RegistryRights.ReadKey,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
            }

            string after = sec.GetSecurityDescriptorSddlForm(AccessControlSections.Access);
            if (string.Equals(before, after, StringComparison.Ordinal))
                return true;   // менять нечего

            key.SetAccessControl(sec);
            return true;
        }
    }

    // Есть ли уже полный доступ для этой учётной записи.
    private static void AddAllowRuleIfMissing(RegistrySecurity sec, IdentityReference identity, InheritanceFlags inherit)
    {
        if (identity == null) return;
        foreach (AuthorizationRule rule in sec.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            RegistryAccessRule rar = rule as RegistryAccessRule;
            if (rar == null) continue;
            if (rar.AccessControlType != AccessControlType.Allow) continue;
            if (!rar.IdentityReference.Equals(identity)) continue;
            if ((rar.RegistryRights & RegistryRights.FullControl) == RegistryRights.FullControl) return;
        }
        sec.AddAccessRule(new RegistryAccessRule(
            identity,
            RegistryRights.FullControl,
            inherit,
            PropagationFlags.None,
            AccessControlType.Allow));
    }

    // У ограниченного кода уже ровно «только чтение»?
    private static bool HasReadOnlyRule(RegistrySecurity sec, IdentityReference identity)
    {
        bool found = false;
        foreach (AuthorizationRule rule in sec.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            RegistryAccessRule rar = rule as RegistryAccessRule;
            if (rar == null) continue;
            if (!rar.IdentityReference.Equals(identity)) continue;
            if (rar.AccessControlType == AccessControlType.Deny) return false;
            if (rar.RegistryRights != RegistryRights.ReadKey) return false;
            found = true;
        }
        return found;
    }

    private static void RemoveAllowRules(RegistrySecurity sec, IdentityReference identity)
    {
        List<RegistryAccessRule> toRemove = new List<RegistryAccessRule>();
        foreach (AuthorizationRule rule in sec.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            RegistryAccessRule rar = rule as RegistryAccessRule;
            if (rar == null) continue;
            if (rar.AccessControlType != AccessControlType.Allow) continue;
            if (!rar.IdentityReference.Equals(identity)) continue;
            toRemove.Add(rar);
        }
        foreach (RegistryAccessRule rar in toRemove)
            sec.RemoveAccessRuleSpecific(rar);
    }
}