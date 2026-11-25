using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using OpenFileDialog = System.Windows.Forms.OpenFileDialog;
using SaveFileDialog = System.Windows.Forms.SaveFileDialog;



// =========================================================================
// === ЕДИНЫЙ ЛОГ ОТЛАДКИ ПРОГРАММЫ (app_debug.log) ========================
// =========================================================================
public static class AppLogger
{
    private static readonly object logLock = new object();
    private static string logPath = null;

    public static string GetLogPath()
    {
        if (logPath == null)
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL");
            try { Directory.CreateDirectory(dir); } catch { }
            logPath = Path.Combine(dir, "app_debug.log");
        }
        return logPath;
    }

    public static void InitSession()
    {
        try
        {
            string p = GetLogPath();
            File.WriteAllText(p, "=== VTOL_QoL Debug Session: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===" + Environment.NewLine, Encoding.UTF8);
        }
        catch { }
    }

    public static void Log(string module, string message)
    {
        lock (logLock)
        {
            try
            {
                string line = string.Format("[{0}] [{1}] {2}", DateTime.Now.ToString("HH:mm:ss.fff"), module, message);
                File.AppendAllText(GetLogPath(), line + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }
    }

    public static void Error(string module, string context, Exception ex)
    {
        Log(module, "ERROR (" + context + "): " + ex.GetType().Name + " — " + ex.Message);
    }
}
// =========================================================================
// === ТЁМНАЯ РАМКА WINDOWS ДЛЯ ВСЕХ ДИАЛОГОВЫХ ОКОН (DWM DARK TITLE BAR) ==
// =========================================================================
public static class DarkWindowHelper
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public static void Apply(Form form)
    {
        if (form == null) return;
        Action applyAttr = () => {
            try
            {
                int useDark = 1;
                if (DwmSetWindowAttribute(form.Handle, 20, ref useDark, sizeof(int)) != 0)
                    DwmSetWindowAttribute(form.Handle, 19, ref useDark, sizeof(int));
            }
            catch { }
        };
        if (form.IsHandleCreated) applyAttr();
        form.HandleCreated += (s, e) => applyAttr();
    }
}


// =========================================================================
// === НАСТРОЙКИ (settings.ini в %LOCALAPPDATA%\VTOL_QoL) =================
// =========================================================================
public static class SettingsManager
{
    private static Dictionary<string, string> data = new Dictionary<string, string>();
    private static string filePath;

    public static void Load()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL");
        try { Directory.CreateDirectory(dir); } catch { }
        filePath = Path.Combine(dir, "settings.ini");
        data.Clear();
        if (File.Exists(filePath))
        {
            try
            {
                foreach (string line in File.ReadAllLines(filePath, Encoding.UTF8))
                {
                    string l = line.Trim();
                    if (l.Length == 0 || l.StartsWith(";") || l.StartsWith("[")) continue;
                    int idx = l.IndexOf('=');
                    if (idx > 0)
                    {
                        string k = l.Substring(0, idx).Trim();
                        string v = l.Substring(idx + 1).Trim();
                        data[k] = v;
                    }
                }
            }
            catch { }
        }
    }

    public static void Save()
    {
        if (string.IsNullOrEmpty(filePath)) return;
        try
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("; VTOL_QoL settings");
            foreach (KeyValuePair<string, string> kv in data)
                sb.AppendLine(kv.Key + "=" + kv.Value);
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }
        catch { }
    }

    public static string Get(string key, string def)
    {
        string v;
        if (data.TryGetValue(key, out v)) return v;
        return def;
    }

    public static bool GetBool(string key, bool def)
    {
        string v;
        if (data.TryGetValue(key, out v))
        {
            bool b;
            if (bool.TryParse(v, out b)) return b;
        }
        return def;
    }

    public static void Set(string key, string val) { data[key] = val; }
    public static void SetBool(string key, bool val) { data[key] = val.ToString(); }
}

// =========================================================================
// === ПАТТЕРНЫ РАСПОЗНАВАНИЯ ЛОГА (log_patterns.ini) =====================
// =========================================================================
public class LogPatterns
{
    public Dictionary<string, List<string>> Aircraft = new Dictionary<string, List<string>>();
    public List<string> CallsignPatterns = new List<string>();
    public List<string> KillTemplates = new List<string>();
    public List<string> EjectTemplates = new List<string>();
    public List<string> DiagnosticKeywords = new List<string>();

    public static string GetPath()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL");
        try { Directory.CreateDirectory(dir); } catch { }
        return Path.Combine(dir, "log_patterns.ini");
    }

    public void LoadOrCreateDefault()
    {
        string filePath = GetPath();
        if (!File.Exists(filePath))
        {
            try { File.WriteAllText(filePath, DefaultContent(), Encoding.UTF8); } catch { }
        }
        try { Parse(File.ReadAllLines(filePath, Encoding.UTF8)); } catch { ParseDefaultFallback(); }
    }

    private void ParseDefaultFallback()
    {
        Parse(DefaultContent().Replace("\r\n", "\n").Split('\n'));
    }

    private string DefaultContent()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("; Паттерны распознавания строк Player.log. Можно редактировать вручную.");
        sb.AppendLine("; Используйте {NICK} как заполнитель позывного пилота.");
        sb.AppendLine("[Aircraft:FA-26B]");
        sb.AppendLine("Pattern=FA-26B|FA26B|F/A-26B|F/A26B");
        sb.AppendLine("[Aircraft:F-45A]");
        sb.AppendLine("Pattern=F-45A|F45A");
        sb.AppendLine("[Aircraft:AV-42C]");
        sb.AppendLine("Pattern=AV-42C|AV42C|VTOL4");
        sb.AppendLine("[Aircraft:AH-94]");
        sb.AppendLine("Pattern=AH-94|AH94");
        sb.AppendLine("[Aircraft:T-55]");
        sb.AppendLine("Pattern=T-55|T55");
        sb.AppendLine("[Aircraft:EF-24G]");
        sb.AppendLine("Pattern=EF-24G|EF24G");
        sb.AppendLine("[Callsign]");
        sb.AppendLine("Pattern1=((?:Alpha|Bravo|Charlie|Delta|Echo|Foxtrot|Golf|Hotel|India|Juliet|Kilo|Lima|Mike|November|Oscar|Papa|Quebec|Romeo|Sierra|Tango|Uniform|Victor|Whiskey|X-?ray|Yankee|Zulu)\\s+\\d+-\\d+)");
        sb.AppendLine("[Kill]");
        sb.AppendLine("Pattern1=was killed by {NICK}");
        sb.AppendLine("[Eject]");
        sb.AppendLine(@"Pattern1=\${NICK}\$ ejected");
        sb.AppendLine(@"Pattern2=\${NICK}\$ was killed");
        sb.AppendLine("[Diagnostics]");
        sb.AppendLine("Keyword1=spawn");
        sb.AppendLine("Keyword2=vehicle");
        sb.AppendLine("Keyword3=callsign");
        sb.AppendLine("Keyword4=ejected");
        sb.AppendLine("Keyword5=killed");
        return sb.ToString();
    }

    private void Parse(string[] lines)
    {
        string section = "";
        foreach (string raw in lines)
        {
            string l = raw.Trim();
            if (l.Length == 0 || l.StartsWith(";")) continue;
            if (l.StartsWith("[") && l.EndsWith("]"))
            {
                section = l.Substring(1, l.Length - 2);
                continue;
            }
            int idx = l.IndexOf('=');
            if (idx < 0) continue;
            string val = l.Substring(idx + 1).Trim();
            if (section.StartsWith("Aircraft:"))
            {
                string plane = section.Substring("Aircraft:".Length);
                string[] alts = val.Split('|');
                List<string> list;
                if (!Aircraft.TryGetValue(plane, out list)) { list = new List<string>(); Aircraft[plane] = list; }
                list.AddRange(alts);
            }
            else if (section == "Callsign") CallsignPatterns.Add(val);
            else if (section == "Kill") KillTemplates.Add(val);
            else if (section == "Eject") EjectTemplates.Add(val);
            else if (section == "Diagnostics") DiagnosticKeywords.Add(val);
        }
    }

    public string NormalizeAircraft(string raw)
    {
        foreach (KeyValuePair<string, List<string>> kv in Aircraft)
            foreach (string alt in kv.Value)
                if (string.Equals(raw, alt, StringComparison.OrdinalIgnoreCase)) return kv.Key;
        return raw;
    }

    public string BuildAircraftRegex()
    {
        List<string> allAlts = new List<string>();
        foreach (KeyValuePair<string, List<string>> kv in Aircraft) allAlts.AddRange(kv.Value);
        for (int i = 0; i < allAlts.Count; i++) allAlts[i] = Regex.Escape(allAlts[i]);
        return "(" + string.Join("|", allAlts.ToArray()) + ")";
    }
}

// =========================================================================
// === COM-ИНТЕРОП ДЛЯ УПРАВЛЕНИЯ АУДИО УСТРОЙСТВАМИ =======================
// =========================================================================
public enum EDataFlow { eRender = 0, eCapture = 1, eAll = 2 }
public enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }

[Flags]
public enum DeviceState { Active = 0x1, Disabled = 0x2, NotPresent = 0x4, Unplugged = 0x8, All = 0xF }

[StructLayout(LayoutKind.Sequential)]
public struct PROPERTYKEY { public Guid fmtid; public int pid; }

[StructLayout(LayoutKind.Sequential)]
public struct PROPVARIANT
{
    public ushort vt;
    public ushort wReserved1, wReserved2, wReserved3;
    public IntPtr p;
    public int p2;
}

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
public class MMDeviceEnumeratorComObject { }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMDeviceEnumerator
{
    int EnumAudioEndpoints(EDataFlow dataFlow, DeviceState dwStateMask, out IMMDeviceCollection ppDevices);
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
    int GetDevice(string pwstrId, out IMMDevice ppDevice);
    int RegisterEndpointNotificationCallback(IntPtr pClient);
    int UnregisterEndpointNotificationCallback(IntPtr pClient);
}

[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMDeviceCollection
{
    int GetCount(out int pcDevices);
    int Item(int nDevice, out IMMDevice ppDevice);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMDevice
{
    int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    int OpenPropertyStore(int stgmAccess, out IPropertyStore ppProperties);
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
    int GetState(out int pdwState);
}

[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPropertyStore
{
    int GetCount(out int cProps);
    int GetAt(int iProp, out PROPERTYKEY pkey);
    int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
    int SetValue(ref PROPERTYKEY key, ref PROPVARIANT propvar);
    int Commit();
}

[ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPolicyConfig
{
    int GetMixFormat(string deviceId, IntPtr ppFormat);
    int GetDeviceFormat(string deviceId, bool bDefault, IntPtr ppFormat);
    int ResetDeviceFormat(string deviceId);
    int SetDeviceFormat(string deviceId, IntPtr pEndpointFormat, IntPtr mixFormat);
    int GetProcessingPeriod(string deviceId, bool bDefault, out long defaultPeriod, out long minimumPeriod);
    int SetProcessingPeriod(string deviceId, ref long period);
    int GetShareMode(string deviceId, IntPtr pMode);
    int SetShareMode(string deviceId, IntPtr mode);
    int GetPropertyValue(string deviceId, ref PROPERTYKEY key, IntPtr pv);
    int SetPropertyValue(string deviceId, ref PROPERTYKEY key, IntPtr pv);
    int SetDefaultEndpoint(string deviceId, ERole role);
    int SetEndpointVisibility(string deviceId, bool bVisible);
}

[ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
public class PolicyConfigClient { }

[ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioSessionManager2
{
    int GetAudioSessionControl(ref Guid audioSessionGuid, int streamFlags, out IntPtr sessionControl);
    int GetSimpleAudioVolume(ref Guid audioSessionGuid, int streamFlags, out IntPtr audioVolume);
    int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
}

[ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioSessionEnumerator
{
    int GetCount(out int count);
    int GetSession(int index, out IAudioSessionControl2 session);
}

[ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioSessionControl2
{
    int GetState(out int state);
    int GetDisplayName(out IntPtr name);
    int SetDisplayName(string name, ref Guid eventContext);
    int GetIconPath(out IntPtr path);
    int SetIconPath(string path, ref Guid eventContext);
    int GetGroupingParam(out Guid groupingId);
    int SetGroupingParam(ref Guid groupingId, ref Guid eventContext);
    int RegisterAudioSessionNotification(IntPtr notification);
    int UnregisterAudioSessionNotification(IntPtr notification);
    int GetSessionIdentifier(out IntPtr retVal);
    int GetSessionInstanceIdentifier(out IntPtr retVal);
    int GetProcessId(out int pid);
    int IsSystemSoundsSession();
    int SetDuckingPreference(bool optOut);
}

[ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ISimpleAudioVolume
{
    int SetMasterVolume(float level, ref Guid eventContext);
    int GetMasterVolume(out float level);
    int SetMute(bool mute, ref Guid eventContext);
    int GetMute(out bool mute);
}

public struct AudioDeviceInfo { public string Id; public string Name; }

public static class AudioInterop
{
    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PROPVARIANT pvar);

    private static readonly string[] vrHints = new string[]
    {
        "Oculus Virtual Audio", "Headphones (Oculus", "Headphones (Rift", "Rift S", "Quest",
        "Valve Index", "Virtual Desktop", "VIVE", "Pico", "Bigscreen Beyond", "SteamVR", "WMR", "Windows Mixed Reality"
    };

    public static List<AudioDeviceInfo> GetDevices(EDataFlow flow)
    {
        List<AudioDeviceInfo> result = new List<AudioDeviceInfo>();
        IMMDeviceEnumerator enumerator = null;
        IMMDeviceCollection coll = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            enumerator.EnumAudioEndpoints(flow, DeviceState.Active, out coll);
            int count;
            coll.GetCount(out count);
            for (int i = 0; i < count; i++)
            {
                IMMDevice dev;
                coll.Item(i, out dev);
                string id;
                dev.GetId(out id);
                string name = GetFriendlyName(dev);
                AudioDeviceInfo info = new AudioDeviceInfo();
                info.Id = id; info.Name = name;
                result.Add(info);
                Marshal.ReleaseComObject(dev);
            }
        }
        catch { }
        finally
        {
            if (coll != null) Marshal.ReleaseComObject(coll);
            if (enumerator != null) Marshal.ReleaseComObject(enumerator);
        }
        return result;
    }

    public static string GetDefaultDeviceId(EDataFlow flow, ERole role)
    {
        IMMDeviceEnumerator enumerator = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            IMMDevice dev;
            int hr = enumerator.GetDefaultAudioEndpoint(flow, role, out dev);
            if (hr != 0 || dev == null) return null;
            string id;
            dev.GetId(out id);
            Marshal.ReleaseComObject(dev);
            return id;
        }
        catch { return null; }
        finally { if (enumerator != null) Marshal.ReleaseComObject(enumerator); }
    }

    public static bool SetDefaultDevice(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId)) return false;
        try
        {
            IPolicyConfig pc = (IPolicyConfig)new PolicyConfigClient();
            pc.SetDefaultEndpoint(deviceId, ERole.eConsole);
            pc.SetDefaultEndpoint(deviceId, ERole.eMultimedia);
            pc.SetDefaultEndpoint(deviceId, ERole.eCommunications);
            Marshal.ReleaseComObject(pc);
            return true;
        }
        catch { return false; }
    }

    public static AudioDeviceInfo? AutoDetectVr(List<AudioDeviceInfo> devices)
    {
        foreach (string hint in vrHints)
            foreach (AudioDeviceInfo d in devices)
                if (d.Name != null && d.Name.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
                    return d;
        return null;
    }

    private static string GetFriendlyName(IMMDevice device)
    {
        string name = "";
        IPropertyStore store = null;
        try
        {
            device.OpenPropertyStore(0, out store);
            PROPERTYKEY pkey = new PROPERTYKEY();
            pkey.fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0");
            pkey.pid = 14;
            PROPVARIANT pv;
            store.GetValue(ref pkey, out pv);
            try { if (pv.p != IntPtr.Zero) name = Marshal.PtrToStringUni(pv.p); } catch { }
            PropVariantClear(ref pv);
        }
        catch { }
        finally { if (store != null) Marshal.ReleaseComObject(store); }
        return name == null ? "" : name;
    }
}

public static class AudioSessionDucking
{
    private static float? originalVolume = null;
    private static readonly object dlock = new object();

    public static void SetDuck(bool duck, string processName)
    {
        lock (dlock)
        {
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            IAudioSessionManager2 sm = null;
            IAudioSessionEnumerator se = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out device);
                if (hr != 0 || device == null) return;
                Guid iid = typeof(IAudioSessionManager2).GUID;
                object o;
                device.Activate(ref iid, 23, IntPtr.Zero, out o);
                sm = (IAudioSessionManager2)o;
                sm.GetSessionEnumerator(out se);
                int count;
                se.GetCount(out count);
                for (int i = 0; i < count; i++)
                {
                    IAudioSessionControl2 sc;
                    se.GetSession(i, out sc);
                    try
                    {
                        int pid;
                        sc.GetProcessId(out pid);
                        if (pid > 0)
                        {
                            Process proc = null;
                            try { proc = Process.GetProcessById(pid); } catch { }
                            if (proc != null && proc.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
                            {
                                ISimpleAudioVolume vol = (ISimpleAudioVolume)sc;
                                Guid ctx = Guid.Empty;
                                if (duck)
                                {
                                    float cur;
                                    vol.GetMasterVolume(out cur);
                                    if (originalVolume == null) originalVolume = cur;
                                    vol.SetMasterVolume(0.25f, ref ctx);
                                }
                                else
                                {
                                    float restore = originalVolume.HasValue ? originalVolume.Value : 1.0f;
                                    vol.SetMasterVolume(restore, ref ctx);
                                    originalVolume = null;
                                }
                            }
                        }
                    }
                    catch { }
                    finally { Marshal.ReleaseComObject(sc); }
                }
            }
            catch { }
            finally
            {
                if (se != null) Marshal.ReleaseComObject(se);
                if (sm != null) Marshal.ReleaseComObject(sm);
                if (device != null) Marshal.ReleaseComObject(device);
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }
        }
    }

    public static bool IsMicInUse()
    {
        try
        {
            string baseKey = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";
            using (RegistryKey root = Registry.CurrentUser.OpenSubKey(baseKey))
            {
                if (root == null) return false;
                foreach (string sub in root.GetSubKeyNames())
                {
                    if (sub.Equals("NonPackaged", StringComparison.OrdinalIgnoreCase))
                    {
                        using (RegistryKey npKey = root.OpenSubKey(sub))
                        {
                            if (npKey == null) continue;
                            foreach (string app in npKey.GetSubKeyNames())
                            {
                                if (app.IndexOf("discord", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    app.IndexOf("teamspeak", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    app.IndexOf("ts3client", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    using (RegistryKey appKey = npKey.OpenSubKey(app))
                                    {
                                        object stop = appKey != null ? appKey.GetValue("LastUsedTimeStop") : null;
                                        if (stop != null && Convert.ToInt64(stop) == 0) return true;
                                    }
                                }
                            }
                        }
                    }
                    else if (sub.IndexOf("discord", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        using (RegistryKey appKey = root.OpenSubKey(sub))
                        {
                            object stop = appKey != null ? appKey.GetValue("LastUsedTimeStop") : null;
                            if (stop != null && Convert.ToInt64(stop) == 0) return true;
                        }
                    }
                }
            }
        }
        catch { }
        return false;
    }
}
// =========================================================================
// === СИСТЕМА ОЗВУЧКИ СЕРИЙ УБИЙСТВ (WT + UNREAL TOURNAMENT 3) =============
// =========================================================================
public static class KillSoundPlayer
{
    private static readonly object stateLock = new object();
    private static int comboStreak = 0;
    private static DateTime lastKillUtc = DateTime.MinValue;
    private const int ComboTimeoutSeconds = 25;

    private static int requestedGeneration = 0;
    private static int requestedTier = 0;
    private static Thread playerThread = null;
    private static AutoResetEvent playSignal = new AutoResetEvent(false);

    private static readonly string[] tierFileBases = new string[]
    {
        "1_kill",          // 1: War Thunder
        "2_doublekill",    // 2: Double Kill
        "3_triplekill",    // 3: Triple Kill
        "4_megakill",      // 4: Mega Kill
        "5_ultrakill",     // 5: Ultra Kill
        "6_monsterkill",   // 6: Monster Kill
        "7_ludicrouskill", // 7: Ludicrous Kill
        "8_holyshit"       // 8+: Holy Shit
    };

    public static string GetSoundsDir()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL", "KillSounds");
        try { Directory.CreateDirectory(dir); } catch { }
        return dir;
    }

    public static void ResetCombo()
    {
        lock (stateLock)
        {
            comboStreak = 0;
            lastKillUtc = DateTime.MinValue;
        }
    }

    public static void RegisterKills(int count)
    {
        if (count <= 0) return;
        lock (stateLock)
        {
            DateTime now = DateTime.UtcNow;
            if ((now - lastKillUtc).TotalSeconds > ComboTimeoutSeconds)
                comboStreak = 0;

            comboStreak += count;
            requestedTier = Math.Min(comboStreak, 8);
            requestedGeneration++;
            lastKillUtc = now;

            if (playerThread == null || !playerThread.IsAlive)
            {
                playerThread = new Thread(AudioLoop);
                playerThread.IsBackground = true;
                playerThread.SetApartmentState(ApartmentState.STA);
                playerThread.Start();
            }
            playSignal.Set();
        }
    }

    private static string ResolveSoundFile(int tier)
    {
        string dir = GetSoundsDir();
        string baseName = tierFileBases[Math.Max(0, Math.Min(tier - 1, tierFileBases.Length - 1))];
        string[] exts = new string[] { ".mp3", ".wav" };

        // 1. Проверяем извлечённый или пользовательский файл
        foreach (string ext in exts)
        {
            string p = Path.Combine(dir, baseName + ext);
            if (File.Exists(p)) return p;
        }

        // 2. Извлекаем из вшитых ресурсов .exe
        foreach (string ext in exts)
        {
            string resName = baseName + ext;
            string targetPath = Path.Combine(dir, resName);
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resName))
                {
                    if (s != null)
                    {
                        using (FileStream fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write))
                            s.CopyTo(fs);
                        return targetPath;
                    }
                }
            }
            catch { }
        }

        if (tier > 1) return ResolveSoundFile(1);
        return null;
    }

    private static void AudioLoop()
    {
        System.Windows.Media.MediaPlayer player = new System.Windows.Media.MediaPlayer();
        player.Volume = 1.0;
        int currentGen = 0;

        while (true)
        {
            int genToPlay = 0;
            int tierToPlay = 0;

            lock (stateLock)
            {
                if (requestedGeneration != currentGen)
                {
                    genToPlay = requestedGeneration;
                    tierToPlay = requestedTier;
                }
            }

            if (genToPlay != 0)
            {
                currentGen = genToPlay;
                try
                {
                    // Мгновенно обрываем предыдущий звук, если он ещё играет
                    player.Stop();
                    player.Close();

                    string filePath = ResolveSoundFile(tierToPlay);
                    AppLogger.Log("SOUND", string.Format("Тир {0} (прерывание предыдущего): {1}", tierToPlay, filePath ?? "не найден"));

                    if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                    {
                        player.Open(new Uri(filePath, UriKind.Absolute));
                        player.Play();
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error("SOUND", "AudioLoop", ex);
                }
            }

            // Прокачиваем сообщения WPF Dispatcher в STA-потоке и ждём следующего сигнала
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(delegate { })
            );
            playSignal.WaitOne(25);
        }
    }
}

// =========================================================================
// === ТРЕКЕР ИГРЫ: DISCORD RPC + АУДИО VR + DUCKING + ЛОГБУК (ЕДИНСТВЕННЫЙ) =
// =========================================================================
public class GameTracker
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile
    );

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint OPEN_EXISTING = 3;

    // ВНИМАНИЕ: впишите сюда Application ID, созданный на https://discord.com
    // (кнопка "New Application" -> General Information -> Application ID).
    public const string DiscordClientId = "1573385169807872222";


    public volatile bool DiscordEnabled = true;
    public volatile bool DuckingEnabled = true;
    public volatile bool AudioSwitchEnabled = true;
    public volatile bool DiagnosticsEnabled = false;

    public volatile bool IsDiscordConnected = false;
    public volatile bool IsDiscordAccessDenied = false;
    public volatile bool IsDiscordLoggedIn = false;
    public volatile string DiscordUsername = "";
    public volatile bool IsGameActive = false;
    public volatile string ActivePilotName = "—";
    public volatile string ActiveFlightSummary = "В меню";


    private bool running = false;
    private static bool elevationAttempted = false;
    private LogPatterns patterns;

    private string originalPlaybackId = null;
    private string originalCaptureId = null;
    private bool switchedAudio = false;
    private string lastFiredPlayerMissile = "";
    private Dictionary<string, string> targetIdToMissile = new Dictionary<string, string>();

    public void Start()
    {
        if (running) return;
        running = true;
        patterns = new LogPatterns();
        patterns.LoadOrCreateDefault();
        ThreadPool.QueueUserWorkItem(delegate { Run(); });
    }

    private string JsonEscape(string s)
    {
        if (s == null) return "";
        StringBuilder sb = new StringBuilder();
        foreach (char c in s)
        {
            if (c == '\\') sb.Append("\\\\");
            else if (c == '"') sb.Append("\\\"");
            else if (c == '\n') sb.Append("\\n");
            else if (c == '\r') sb.Append("\\r");
            else if (c == '\t') sb.Append("\\t");
            else if (c < 0x20) sb.Append(string.Format("\\u{0:x4}", (int)c));
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private void SendPacket(Stream pipe, int op, string json)
    {
        byte[] jb = Encoding.UTF8.GetBytes(json);
        byte[] header = new byte[8];
        BitConverter.GetBytes(op).CopyTo(header, 0);
        BitConverter.GetBytes(jb.Length).CopyTo(header, 4);
        pipe.Write(header, 0, 8);
        pipe.Write(jb, 0, jb.Length);
        pipe.Flush();
    }

    private byte[] ReadExact(Stream pipe, int n)
    {
        byte[] buf = new byte[n];
        int total = 0;
        try
        {
            while (total < n)
            {
                int r = pipe.Read(buf, total, n - total);
                if (r <= 0) return null;
                total += r;
            }
        }
        catch { return null; }
        return buf;
    }

    private bool ReadPacket(Stream pipe, out int op, out string body)
    {
        op = 0; body = "";
        byte[] header = ReadExact(pipe, 8);
        if (header == null) return false;
        op = BitConverter.ToInt32(header, 0);
        int len = BitConverter.ToInt32(header, 4);
        if (len < 0 || len > 5000000) return false;
        if (len == 0) { body = ""; return true; }
        byte[] data = ReadExact(pipe, len);
        if (data == null) return false;
        body = Encoding.UTF8.GetString(data);
        return true;
    }

    private void WriteFlightLog(string action, string aircraft, string callsign, string dur, int kills, string note)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "flight_logbook.txt");
            string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string entry = ts + "|" + action + "|" + aircraft + "|" + callsign + "|" + dur + "|" + kills + "|" + note;
            File.AppendAllText(path, entry + Environment.NewLine, Encoding.UTF8);
        }
        catch { }
    }

    private void WriteDiagnostics(string line)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "log_diagnostics.txt");
            File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss") + " | " + line + Environment.NewLine, Encoding.UTF8);
        }
        catch { }
    }

    private void Run()
    {
        string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"AppData\LocalLow\Boundless Dynamics, LLC\VTOLVR\Player.log");
        string pilotsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Boundless Dynamics, LLC\VTOLVR\SaveData\pilots.cfg");

        string myNick = "";
        string myNickEsc = "";
        string currentAircraft = "В ангаре / Меню";
        string currentCallsign = "В ангаре / Меню";
        string currentMission = "";
        string currentCrewRole = "";
        int kills = 0;
        bool inSortie = false;
        DateTime sortieStart = DateTime.Now;
        long startEpoch = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
        long lastSeekPos = 0;
        bool wasRunning = false;

        Stream pipe = null;
        string aircraftRegexStr = patterns.BuildAircraftRegex();

        while (true)
        {
            try
            {
                // Перечитываем ник на случай смены профиля пилота
                if (File.Exists(pilotsFile))
                {
                    try
                    {
                        Match m = Regex.Match(File.ReadAllText(pilotsFile, Encoding.UTF8), @"(?m)^[ \t]*(?:lastPilot|pilotName)\s*=\s*(.+)\s*$");
                        if (m.Success)
                        {
                            string nick = m.Groups[1].Value.Trim();
                            if (nick != myNick)
                            {
                                myNick = nick;
                                myNickEsc = Regex.Escape(myNick);
                            }
                        }
                    }
                    catch { }
                }

                bool[] procsExist = new bool[] { Process.GetProcessesByName("VTOLVR").Length > 0 };
                bool isRunning = procsExist[0];

                if (!isRunning)
                {
                    if (wasRunning)
                    {
                        // Игра только что закрылась — шлём CLEAR и восстанавливаем аудио
                        if (DiscordEnabled && pipe != null)
                        {
                            try
                            {
                                string clearJson = "{\"cmd\":\"SET_ACTIVITY\",\"args\":{\"pid\":" + Process.GetCurrentProcess().Id + ",\"activity\":null},\"nonce\":\"" + Guid.NewGuid() + "\"}";
                                SendPacket(pipe, 1, clearJson);
                                int op; string body;
                                ReadPacket(pipe, out op, out body);
                            }
                            catch { }
                        }
                        if (switchedAudio && SettingsManager.GetBool("RestoreAudioAfterExit", true))
                        {
                            if (!string.IsNullOrEmpty(originalPlaybackId)) AudioInterop.SetDefaultDevice(originalPlaybackId);
                            if (!string.IsNullOrEmpty(originalCaptureId)) AudioInterop.SetDefaultDevice(originalCaptureId);
                        }
                        switchedAudio = false;
                        if (inSortie)
                        {
                            WriteFlightLog("LANDED", currentAircraft, currentCallsign, GetDurStr(sortieStart), kills, "Завершение сессии / выход из игры");
                        }
                        AudioSessionDucking.SetDuck(false, "VTOLVR");
                    }

                    if (pipe != null) { try { pipe.Dispose(); } catch { } pipe = null; }
                    IsDiscordConnected = false;
                    IsDiscordLoggedIn = false;
                    DiscordUsername = "";
                    IsGameActive = false;
                    ActiveFlightSummary = "В меню";
                    currentAircraft = "В ангаре / Меню";
                    currentCallsign = "В ангаре / Меню";
                    kills = 0;
                    inSortie = false;
                    wasRunning = false;
                    Thread.Sleep(1500);
                    startEpoch = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
                    continue;
                }

                if (!wasRunning)
                {
                    // Игра только что запустилась
                    wasRunning = true;
                    AppLogger.Log("TRACKER", "Обнаружен запуск VTOLVR.exe! Файл лога: " + logPath + " (существует: " + File.Exists(logPath) + ")");
                    if (File.Exists(logPath))
                    {
                        FileInfo fi = new FileInfo(logPath);
                        lastSeekPos = (DateTime.Now - fi.LastWriteTime).TotalSeconds < 15 ? 0 : fi.Length;
                    }
                    else
                    {
                        lastSeekPos = 0;
                    }
                    WriteFlightLog("SESSION", "В ангаре / Меню", "—", "00:00", 0, "Запуск VTOL VR");

                    if (AudioSwitchEnabled)
                    {
                        string vrPlayback = SettingsManager.Get("VrPlaybackId", "");
                        string vrCapture = SettingsManager.Get("VrCaptureId", "");
                        if (!string.IsNullOrEmpty(vrPlayback) || !string.IsNullOrEmpty(vrCapture))
                        {
                            originalPlaybackId = AudioInterop.GetDefaultDeviceId(EDataFlow.eRender, ERole.eMultimedia);
                            originalCaptureId = AudioInterop.GetDefaultDeviceId(EDataFlow.eCapture, ERole.eCommunications);
                            if (!string.IsNullOrEmpty(vrPlayback)) AudioInterop.SetDefaultDevice(vrPlayback);
                            if (!string.IsNullOrEmpty(vrCapture)) AudioInterop.SetDefaultDevice(vrCapture);
                            switchedAudio = true;
                        }
                    }
                }

                // Читаем новые строки Player.log
                if (File.Exists(logPath))
                {
                    using (FileStream fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        if (fs.Length < lastSeekPos) lastSeekPos = 0;
                        if (fs.Length > lastSeekPos)
                        {
                            fs.Seek(lastSeekPos, SeekOrigin.Begin);
                            using (StreamReader sr = new StreamReader(fs, Encoding.UTF8))
                            {
                                string chunk = sr.ReadToEnd();
                                lastSeekPos = fs.Position;

                                if (DiagnosticsEnabled && chunk.Length > 0)
                                {
                                    string[] linesArr = chunk.Replace("\r\n", "\n").Split('\n');
                                    foreach (string ln in linesArr)
                                    {
                                        bool interesting = (!string.IsNullOrEmpty(myNick) && ln.IndexOf(myNick, StringComparison.OrdinalIgnoreCase) >= 0);
                                        if (!interesting)
                                            foreach (string kw in patterns.DiagnosticKeywords)
                                                if (ln.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) { interesting = true; break; }
                                        if (interesting && ln.Trim().Length > 0) WriteDiagnostics(ln.Trim());
                                    }
                                }

                                // 0. Определяем актуальный ник пилота из строки "Set current pilot to shutze"
                                Match mPilot = Regex.Match(chunk, @"Set current pilot to\s+(?<val>[^\r\n]+)", RegexOptions.IgnoreCase);
                                if (mPilot.Success)
                                {
                                    string nick = mPilot.Groups["val"].Value.Trim();
                                    if (nick.Length > 0 && nick != myNick)
                                    {
                                        myNick = nick;
                                        myNickEsc = Regex.Escape(myNick);
                                        ActivePilotName = myNick;
                                        AppLogger.Log("TRACKER", "Активный пилот: " + myNick);
                                    }

                                }

                                // 1. Запоминаем выбранный самолёт ("Set current vehicle to F-45A"), но в статус пока не ставим
                                MatchCollection mVehMatches = Regex.Matches(chunk, @"Set current vehicle to\s+(?<val>[^\r\n]+)", RegexOptions.IgnoreCase);
                                foreach (Match mv in mVehMatches)
                                {
                                    string rawVeh = mv.Groups["val"].Value.Trim();
                                    if (rawVeh.Equals("none", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (inSortie || currentAircraft != "В ангаре / Меню")
                                        {
                                            if (inSortie)
                                                WriteFlightLog("LANDED", currentAircraft, currentCallsign, GetDurStr(sortieStart), kills, "Возврат в ангар / меню");
                                            currentAircraft = "В ангаре / Меню";
                                            currentCallsign = "В ангаре / Меню";
                                            inSortie = false;
                                            AppLogger.Log("TRACKER", "Сброс в ангар / меню (vehicle=none).");
                                        }
                                    }
                                    else
                                    {
                                        string normVeh = patterns.NormalizeAircraft(rawVeh);
                                        if (inSortie && currentAircraft != normVeh)
                                            currentAircraft = normVeh;
                                        SettingsManager.Set("LastSelectedVehicle", normVeh);
                                    }
                                }

                                // 1.5. Определяем название загруженной миссии или карты из Player.log
                                Match mScen = Regex.Match(chunk, @"(?:Loading campaign mission|Loading workshop scenario|Starting mission|Selected scenario|Scenario name)[:\s]+(?<val>[^\r\n\(\[]+)", RegexOptions.IgnoreCase);
                                if (mScen.Success)
                                {
                                    string sc = mScen.Groups["val"].Value.Trim().Trim('"', '\'', '.', ' ');
                                    if (sc.Length > 2 && sc.Length < 60 &&
                                        !Regex.IsMatch(sc, @"^(?:none|async|true|false|null|scene|level)$", RegexOptions.IgnoreCase))
                                    {
                                        currentMission = sc;
                                        AppLogger.Log("TRACKER", "Обнаружена миссия/карта: " + currentMission);
                                    }
                                }

                                // 2. Сброс при выходе из миссии (сингл или МП)
                                if (inSortie && Regex.IsMatch(chunk, @"(?:PlayerSpawn destroyed\.|selectedSlot\s*=>\s*-1)", RegexOptions.IgnoreCase))
                                {
                                    string acWithRole = FormatAircraftWithRole(currentAircraft, currentCrewRole);
                                    WriteFlightLog("LANDED", acWithRole, currentCallsign, GetDurStr(sortieStart), kills, "Завершение вылета / выход в брифинг");
                                    currentAircraft = "В ангаре / Меню";
                                    currentCallsign = "В ангаре / Меню";
                                    currentMission = "";
                                    currentCrewRole = "";
                                    inSortie = false;
                                    AppLogger.Log("TRACKER", "Вылет завершён (выход в брифинг / меню).");
                                }

                                // 3. Мультиплеер: занятие слота и определение роли в двухместном экипаже
                                if (!string.IsNullOrEmpty(myNickEsc))
                                {
                                    MatchCollection slotMatches = Regex.Matches(chunk, @"Setting up slot UI:(?<line>[^\r\n]*?\((?<val>[^)]+)\)[^\r\n]*?=\s*" + myNickEsc + @"\b[^\r\n]*)", RegexOptions.IgnoreCase);
                                    if (slotMatches.Count > 0)
                                    {
                                        Match lastSlot = slotMatches[slotMatches.Count - 1];
                                        string lastCs = lastSlot.Groups["val"].Value.Trim();
                                        string slotLine = lastSlot.Groups["line"].Value;
                                        SettingsManager.Set("LastSlotCallsign", lastCs);

                                        bool isSecondSeat = Regex.IsMatch(slotLine, @"\b(?:Gunner|Copilot|Co-Pilot|EWO|WSO|BackSeat|Rear|Seat\s*1|Seat\s*2|\[1\])\b", RegexOptions.IgnoreCase);
                                        SettingsManager.Set("LastSlotSecondSeat", isSecondSeat ? "true" : "false");

                                        if (inSortie)
                                        {
                                            currentCallsign = lastCs;
                                            currentCrewRole = ResolveCrewRole(currentAircraft, isSecondSeat);
                                        }
                                    }

                                    // Дополнительная проверка при непосредственной посадке игрока в кабину (МП)
                                    Match mSeat = Regex.Match(chunk, @"(?:Seat|SetOccupant|EnterVehicle)[^\r\n]*" + myNickEsc + @"[^\r\n]*?(?:seat(?:Idx)?\s*[=:]\s*(?<idx>\d+)|(?<role>Gunner|Copilot|EWO|WSO|BackSeat|Pilot))", RegexOptions.IgnoreCase);
                                    if (mSeat.Success)
                                    {
                                        bool isBack = false;
                                        if (mSeat.Groups["idx"].Success && mSeat.Groups["idx"].Value != "0") isBack = true;
                                        if (mSeat.Groups["role"].Success && !mSeat.Groups["role"].Value.Equals("Pilot", StringComparison.OrdinalIgnoreCase)) isBack = true;
                                        SettingsManager.Set("LastSlotSecondSeat", isBack ? "true" : "false");
                                        if (inSortie)
                                            currentCrewRole = ResolveCrewRole(currentAircraft, isBack);
                                    }
                                }


                                // 4. Старт реального вылета:
                                // В мультиплеере: "shutze has spawned" или "FlightLogger: shutze has spawned"
                                // В одиночной игре: "PlayerSpawn:OnSpawnUnit()" или "Registering actor Player"
                                bool mpSpawned = !string.IsNullOrEmpty(myNickEsc) && Regex.IsMatch(chunk, @"(?:FlightLogger:\s*)?" + myNickEsc + @"\s+has spawned", RegexOptions.IgnoreCase);
                                bool spSpawned = Regex.IsMatch(chunk, @"PlayerSpawn:OnSpawnUnit\(\)|Registering actor Player \[\d+\]", RegexOptions.IgnoreCase);

                                if (mpSpawned || spSpawned)
                                {
                                    string veh = SettingsManager.Get("LastSelectedVehicle", "В ангаре / Меню");
                                    // Проверяем, нет ли в этом же куске лога строки "AV-42C (shutze)" или "F-45A (shutze)"
                                    if (!string.IsNullOrEmpty(myNickEsc))
                                    {
                                        Match mActorVeh = Regex.Match(chunk, @"(?<val>" + aircraftRegexStr + @")\s*\(" + myNickEsc + @"\)", RegexOptions.IgnoreCase);
                                        if (mActorVeh.Success)
                                            veh = patterns.NormalizeAircraft(mActorVeh.Groups["val"].Value);
                                    }

                                    string cs = mpSpawned
                                        ? SettingsManager.Get("LastSlotCallsign", "В полёте")
                                        : "Одиночный вылет";

                                    bool isSecondSeat = mpSpawned && SettingsManager.GetBool("LastSlotSecondSeat", false);
                                    string role = ResolveCrewRole(veh, isSecondSeat);

                                    if (!inSortie || currentAircraft != veh || currentCallsign != cs || currentCrewRole != role)
                                    {
                                        currentAircraft = veh;
                                        currentCallsign = cs;
                                        currentCrewRole = role;
                                        inSortie = true;
                                        sortieStart = DateTime.Now;
                                        string acDisplay = FormatAircraftWithRole(currentAircraft, currentCrewRole);
                                        AppLogger.Log("TRACKER", string.Format("Старт вылета ({0}): Борт={1}, Позывной={2}", mpSpawned ? "MP" : "SP", acDisplay, currentCallsign));
                                        WriteFlightLog("SORTIE", acDisplay, currentCallsign, "00:00", 0, mpSpawned ? "Вылет (мультиплеер)" : "Вылет (одиночная миссия)");
                                    }
                                }


                                // 5. Боевая телеметрия: пуски ракет, захваты целей и подтверждённые сбития
                                if (!string.IsNullOrEmpty(myNickEsc))
                                {
                                    // 5а. Фиксируем пуск ракеты игроком ("Starting missile info for AIM-120C(101) by Player [0] (shutze)")
                                    MatchCollection mcLaunch = Regex.Matches(
                                        chunk,
                                        @"Starting missile info for\s+(?<msl>[^\r\n\(\[]+)(?:\(\d+\))?\s+by\s+[^\r\n]*?\(" + myNickEsc + @"\)",
                                        RegexOptions.IgnoreCase
                                    );
                                    foreach (Match ml in mcLaunch)
                                    {
                                        string mslName = ml.Groups["msl"].Value.Trim();
                                        if (!string.IsNullOrEmpty(mslName))
                                        {
                                            lastFiredPlayerMissile = mslName;
                                            RecordWeaponStat(mslName, 1, 0);
                                        }
                                    }

                                    // 5б. Связываем ракету с ID захваченной цели ("AIM-120 (shutze) acquired radar target: ASF-30 [11] (ASF-30)")
                                    MatchCollection mcLock = Regex.Matches(
                                        chunk,
                                        @"(?<msl>[A-Za-z0-9\-]+)\s*\(" + myNickEsc + @"\)\s+acquired\s+(?:radar\s+|optical\s+|heat\s+)?target:\s*[^\r\n\[]+\[(?<tid>\d+)\]",
                                        RegexOptions.IgnoreCase
                                    );
                                    foreach (Match mLock in mcLock)
                                    {
                                        string tid = mLock.Groups["tid"].Value;
                                        string lockMsl = mLock.Groups["msl"].Value.Trim();
                                        // Если точное имя с модификацией (например AIM-120C) совпадает по базе с AIM-120 — берём полное имя
                                        if (!string.IsNullOrEmpty(lastFiredPlayerMissile) &&
                                            lastFiredPlayerMissile.StartsWith(lockMsl, StringComparison.OrdinalIgnoreCase))
                                        {
                                            lockMsl = lastFiredPlayerMissile;
                                        }
                                        targetIdToMissile[tid] = lockMsl;
                                    }

                                    // 5в. Подтверждённые сбития ("FlightLogger: ASF-30 [13] (ASF-30) was killed by Player [0] (shutze). explosion")
                                    int newKills = 0;
                                    string lastTarget = "Цель";
                                    string acDisplay = FormatAircraftWithRole(currentAircraft, currentCrewRole);

                                    MatchCollection mcKill = Regex.Matches(
                                        chunk,
                                        @"FlightLogger:\s*(?<target>[^\r\n]+?)\s+was killed by\s+[^\r\n]*?\(" + myNickEsc + @"\)(?:\s+with\s+(?<weapon>[^\r\n\.]+))?",
                                        RegexOptions.IgnoreCase
                                    );
                                    foreach (Match mk in mcKill)
                                    {
                                        string rawTarget = mk.Groups["target"].Value.Trim();
                                        if (rawTarget.IndexOf("(" + myNick + ")", StringComparison.OrdinalIgnoreCase) >= 0)
                                            continue;

                                        string targetName = CleanUnitDisplayName(rawTarget);
                                        string weapon = mk.Groups["weapon"].Success ? CleanUnitDisplayName(mk.Groups["weapon"].Value) : "";

                                        // Ищем ракету по ID цели [13] из словаря захватов
                                        Match mTid = Regex.Match(rawTarget, @"\[(?<tid>\d+)\]");
                                        if (string.IsNullOrEmpty(weapon) && mTid.Success)
                                        {
                                            string mappedMsl;
                                            if (targetIdToMissile.TryGetValue(mTid.Groups["tid"].Value, out mappedMsl))
                                            {
                                                weapon = mappedMsl;
                                                targetIdToMissile.Remove(mTid.Groups["tid"].Value);
                                            }
                                        }

                                        if (!string.IsNullOrEmpty(weapon))
                                            RecordWeaponStat(weapon, 0, 1);
                                        else
                                            RecordWeaponStat("Пушка / Неупр. АСП", 0, 1);

                                        string killNote = string.IsNullOrEmpty(weapon)
                                            ? ("Уничтожен: " + targetName)
                                            : ("Уничтожен: " + targetName + "  |  Оружие: " + weapon);

                                        newKills++;
                                        lastTarget = targetName;
                                        kills++;
                                        WriteFlightLog("KILL", acDisplay, currentCallsign, GetDurStr(sortieStart), kills, killNote);
                                    }

                                    if (newKills > 0)
                                    {
                                        AppLogger.Log("TRACKER", string.Format("Подтверждено сбитие ({0}): всего за сессию = {1}", lastTarget, kills));
                                        if (SettingsManager.GetBool("KillSoundEnabled", true))
                                            KillSoundPlayer.RegisterKills(newKills);
                                    }
                                }


                                // 6. Катапультирование / гибель борта с точным указанием убийцы, оружия или причины крушения
                                bool hit = false;
                                string deathEventType = "KIA";
                                string deathDetails = "Борт потерян";

                                if (!string.IsNullOrEmpty(myNickEsc))
                                {
                                    // Вариант А: Сбит противником или ботом ("FlightLogger: F/A-26B (shutze) was killed by <Killer> with <Weapon>.")
                                    Match mKilledBy = Regex.Match(
                                        chunk,
                                        @"FlightLogger:\s*[^\r\n]*?\(" + myNickEsc + @"\)[^\r\n]*?\bwas killed by\s+(?<killer>[^\r\n\.]+?)(?:\s+with\s+(?<weapon>[^\r\n\.]+))?(?:\.|$)",
                                        RegexOptions.IgnoreCase
                                    );
                                    if (mKilledBy.Success)
                                    {
                                        hit = true;
                                        deathEventType = "KIA";
                                        string killer = CleanUnitDisplayName(mKilledBy.Groups["killer"].Value);
                                        string weap = mKilledBy.Groups["weapon"].Success ? CleanUnitDisplayName(mKilledBy.Groups["weapon"].Value) : "";
                                        deathDetails = string.IsNullOrEmpty(weap)
                                            ? ("Сбит: " + killer)
                                            : ("Сбит: " + killer + "  |  Оружие: " + weap);
                                    }

                                    // Вариант Б: Катапультирование ("FlightLogger: F/A-26B (shutze) ejected.")
                                    if (!hit && Regex.IsMatch(chunk, @"(?:FlightLogger:\s*[^\r\n]*?\(" + myNickEsc + @"\)|(?:\$|\b)" + myNickEsc + @"\$?)[^\r\n]*?\bejected\b", RegexOptions.IgnoreCase))
                                    {
                                        hit = true;
                                        deathEventType = "EJECT";
                                        deathDetails = "Катапультирование пилота";
                                    }

                                    // Вариант В: Столкновение / крушение / самоуничтожение
                                    if (!hit && Regex.IsMatch(chunk, @"(?:FlightLogger:\s*[^\r\n]*?\(" + myNickEsc + @"\)|(?:\$|\b)" + myNickEsc + @"\$?)[^\r\n]*?\b(?:was killed|died|crashed)\b", RegexOptions.IgnoreCase))
                                    {
                                        hit = true;
                                        deathEventType = "KIA";
                                        if (chunk.IndexOf("crashed", StringComparison.OrdinalIgnoreCase) >= 0 || chunk.IndexOf("terrain", StringComparison.OrdinalIgnoreCase) >= 0)
                                            deathDetails = "Столкновение с землёй / крушение";
                                        else
                                            deathDetails = "Борт уничтожен (авария / столкновение)";
                                    }

                                    if (!hit)
                                    {
                                        foreach (string ejTpl in patterns.EjectTemplates)
                                        {
                                            string pat = ejTpl.Replace("{NICK}", myNickEsc);
                                            if (Regex.IsMatch(chunk, pat, RegexOptions.IgnoreCase)) { hit = true; break; }
                                        }
                                    }
                                }

                                if (hit)
                                {
                                    KillSoundPlayer.ResetCombo();
                                    string acDisplay = FormatAircraftWithRole(currentAircraft, currentCrewRole);
                                    WriteFlightLog(deathEventType, acDisplay, currentCallsign, GetDurStr(sortieStart), kills, deathDetails);
                                    currentAircraft = "В ангаре / Меню";
                                    currentCallsign = "В ангаре / Меню";
                                    inSortie = false;
                                    AppLogger.Log("TRACKER", "Пилот " + myNick + " погиб/катапультировался (" + deathEventType + ").");

                                    if (SettingsManager.GetBool("Shuffle", true))
                                    {
                                        string mDir = SettingsManager.Get("RadioMusicDir", "");
                                        if (!string.IsNullOrEmpty(mDir) && Directory.Exists(mDir))
                                        {
                                            bool shuffled = MusicShuffler.ShuffleNow(mDir, true, true);
                                            AppLogger.Log("MUSIC", "Перемешивание треков после гибели/катапультирования: " + shuffled);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                // Ducking


                if (DuckingEnabled)
                {
                    bool voiceActive = AudioSessionDucking.IsMicInUse();
                    AudioSessionDucking.SetDuck(voiceActive, "VTOLVR");
                }

                // Discord IPC
                if (DiscordEnabled)
                {
                    if (pipe == null)
                    {
                        for (int i = 0; i < 10; i++)
                        {
                            string pipeName = @"\\.\pipe\discord-ipc-" + i;
                            Microsoft.Win32.SafeHandles.SafeFileHandle hPipe = CreateFile(
                                pipeName,
                                GENERIC_READ | GENERIC_WRITE,
                                0,
                                IntPtr.Zero,
                                OPEN_EXISTING,
                                0,
                                IntPtr.Zero
                            );

                            if (!hPipe.IsInvalid)
                            {
                                try
                                {
                                    FileStream fsPipe = new FileStream(hPipe, FileAccess.ReadWrite, 4096, false);
                                    pipe = fsPipe;
                                    string hs = "{\"v\":1,\"client_id\":\"" + DiscordClientId + "\"}";
                                    SendPacket(pipe, 0, hs);
                                    int op; string body;
                                    bool ok = ReadPacket(pipe, out op, out body);
                                    AppLogger.Log("DISCORD", string.Format("Пайп discord-ipc-{0}: ok={1}, op={2}, body={3}", i, ok, op, body));
                                    if (!ok)
                                    {
                                        pipe.Dispose();
                                        pipe = null;
                                        IsDiscordConnected = false;
                                    }
                                    else
                                    {
                                        IsDiscordConnected = true;
                                        IsDiscordAccessDenied = false;
                                        Match mUser = Regex.Match(body, @"""username""\s*:\s*""(?<val>[^""]+)""");
                                        if (mUser.Success && !string.IsNullOrEmpty(mUser.Groups["val"].Value))
                                        {
                                            IsDiscordLoggedIn = true;
                                            DiscordUsername = mUser.Groups["val"].Value;
                                        }
                                        else
                                        {
                                            IsDiscordLoggedIn = false;
                                            DiscordUsername = "";
                                        }
                                        break;
                                    }

                                }
                                catch (Exception exPipe)
                                {
                                    AppLogger.Log("DISCORD", string.Format("Ошибка обмена на discord-ipc-{0}: [{1}] {2}", i, exPipe.GetType().Name, exPipe.Message));
                                    if (pipe != null) { try { pipe.Dispose(); } catch { } pipe = null; }
                                }
                            }
                            else
                            {
                                int err = Marshal.GetLastWin32Error();
                                hPipe.Dispose();
                                if (err != 2)
                                {
                                    if (err == 5) IsDiscordAccessDenied = true;
                                    AppLogger.Log("DISCORD", string.Format("CreateFile discord-ipc-{0} Win32Error={1}", i, err));
                                    if (err == 5 && !elevationAttempted)

                                    {
                                        elevationAttempted = true;
                                        bool isAdmin = new System.Security.Principal.WindowsPrincipal(
                                            System.Security.Principal.WindowsIdentity.GetCurrent()
                                        ).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

                                        if (!isAdmin)
                                        {
                                            AppLogger.Log("DISCORD", "Discord запущен с повышенными правами. Перезапуск VTOL_QoL от имени администратора...");
                                            try
                                            {
                                                ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath);
                                                psi.Verb = "runas";
                                                psi.UseShellExecute = true;
                                                Process.Start(psi);
                                                Environment.Exit(0);
                                            }
                                            catch (Exception exElev)
                                            {
                                                AppLogger.Log("DISCORD", "Пользователь отменил запрос прав администратора: " + exElev.Message);
                                            }
                                        }
                                    }
                                }

                            }
                        }
                    }

                    if (pipe != null)
                    {
                        IsGameActive = true;
                        string acFullDisplay = FormatAircraftWithRole(currentAircraft, currentCrewRole);
                        ActiveFlightSummary = inSortie ? (acFullDisplay + " (" + currentCallsign + ")") : "В ангаре / Меню";
                        if (!string.IsNullOrEmpty(myNick)) ActivePilotName = myNick;

                        string det = inSortie
                            ? ("Борт: " + acFullDisplay + (kills > 0 ? " | Сбито: " + kills : ""))
                            : "В ангаре / Меню";

                        string st = inSortie
                            ? ("Позывной: " + currentCallsign + (!string.IsNullOrEmpty(currentMission) ? " | " + currentMission : ""))
                            : (!string.IsNullOrEmpty(currentMission) ? "Брифинг: " + currentMission : "Ожидание вылета");

                        string acAssetKey = GetAircraftAssetKey(currentAircraft);
                        string assetsJson = string.IsNullOrEmpty(acAssetKey)
                            ? "\"large_image\":\"vtol_logo\",\"large_text\":\"VTOL QoL by L.Shutze\""
                            : "\"large_image\":\"vtol_logo\",\"large_text\":\"VTOL QoL by L.Shutze\",\"small_image\":\"" + acAssetKey + "\",\"small_text\":\"" + JsonEscape(currentAircraft) + "\"";

                        string json = "{\"cmd\":\"SET_ACTIVITY\",\"args\":{\"pid\":" + Process.GetCurrentProcess().Id +
                                      ",\"activity\":{\"details\":\"" + JsonEscape(det) +
                                      "\",\"state\":\"" + JsonEscape(st) +
                                      "\",\"timestamps\":{\"start\":" + startEpoch +
                                      "},\"assets\":{" + assetsJson + "}}},\"nonce\":\"" + Guid.NewGuid() + "\"}";

                        try
                        {
                            SendPacket(pipe, 1, json);
                            int op; string body;
                            if (ReadPacket(pipe, out op, out body))
                            {
                                if (op == 3)
                                {
                                    AppLogger.Log("DISCORD", "Discord вернул ошибку SET_ACTIVITY: " + body);
                                    try { pipe.Dispose(); } catch { }
                                    pipe = null;
                                }
                            }
                            else
                            {
                                AppLogger.Log("DISCORD", "ReadPacket вернул false после SET_ACTIVITY");
                                try { pipe.Dispose(); } catch { }
                                pipe = null;
                            }
                        }
                        catch (Exception exSend)
                        {
                            AppLogger.Error("DISCORD", "SET_ACTIVITY", exSend);
                            try { pipe.Dispose(); } catch { }
                            pipe = null;
                        }
                    }
                }
                else if (pipe != null)
                {
                    try { pipe.Dispose(); } catch { }
                    pipe = null;
                }

            }
            catch
            {
                if (pipe != null) { try { pipe.Dispose(); } catch { } pipe = null; }
            }
            Thread.Sleep(2000);
        }
    }
    private string ResolveCrewRole(string aircraft, bool isSecondSeat)
    {
        if (string.IsNullOrEmpty(aircraft)) return "";
        string ac = aircraft.ToUpperInvariant();
        if (ac == "AH-94") return isSecondSeat ? "Стрелок" : "Пилот";
        if (ac == "EF-24G") return isSecondSeat ? "Оператор РЭБ" : "Пилот";
        if (ac == "T-55") return isSecondSeat ? "Оператор Вооружения" : "Пилот";
        return "";
    }

    private string FormatAircraftWithRole(string aircraft, string role)
    {
        if (string.IsNullOrEmpty(aircraft) || string.IsNullOrEmpty(role)) return aircraft;
        return aircraft + " — " + role;
    }

    private string GetAircraftAssetKey(string aircraft)
    {
        if (string.IsNullOrEmpty(aircraft)) return "";
        switch (aircraft.ToUpperInvariant())
        {
            case "FA-26B": return "fa26b";
            case "F-45A": return "f45a";
            case "AV-42C": return "av42c";
            case "AH-94": return "ah94";
            case "T-55": return "t55";
            case "EF-24G": return "ef24g";
            default: return "";
        }
    }
    private string GetDurStr(DateTime start)
    {
        TimeSpan span = DateTime.Now - start;
        return string.Format("{0:D2}:{1:D2}", (int)span.TotalMinutes, span.Seconds);
    }

    private string CleanUnitDisplayName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        string s = Regex.Replace(raw, @"\s*\[\d+\]\s*\([^)]+\)", "");
        s = Regex.Replace(s, @"\s*\[\d+\]", "");
        return s.Trim().Trim('.', ' ');
    }

    private void RecordWeaponStat(string weaponName, int addFired, int addHits)
    {
        if (string.IsNullOrEmpty(weaponName)) return;
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "weapon_stats.ini");

            Dictionary<string, int[]> stats = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(path))
            {
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string[] p = line.Split('|');
                    if (p.Length >= 3)
                    {
                        int f, h;
                        int.TryParse(p[1], out f);
                        int.TryParse(p[2], out h);
                        stats[p[0]] = new int[] { f, h };
                    }
                }
            }

            int[] cur;
            if (!stats.TryGetValue(weaponName, out cur))
                cur = new int[] { 0, 0 };

            cur[0] += addFired;
            cur[1] += addHits;
            stats[weaponName] = cur;

            List<string> outLines = new List<string>();
            foreach (KeyValuePair<string, int[]> kv in stats)
                outLines.Add(kv.Key + "|" + kv.Value[0] + "|" + kv.Value[1]);
            File.WriteAllLines(path, outLines.ToArray(), Encoding.UTF8);
        }
        catch { }
    }
}

// =========================================================================
// === ШАФЛ МУЗЫКИ (ТОЛЬКО КОГДА ИГРА НЕ ЗАПУЩЕНА) =========================
// =========================================================================
public static class MusicShuffler
{
    public static bool IsGameRunning()
    {
        return Process.GetProcessesByName("VTOLVR").Length > 0;
    }

    private static string HistoryFile()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL");
        try { Directory.CreateDirectory(dir); } catch { }
        return Path.Combine(dir, "shuffle_history.txt");
    }

    public static bool ShuffleNow(string musicDir, bool smart, bool forceInGame = false)
    {
        if (!Directory.Exists(musicDir)) return false;
        if (!forceInGame && IsGameRunning()) return false;

        string[] files;
        try { files = Directory.GetFiles(musicDir, "*.mp3", SearchOption.TopDirectoryOnly); }
        catch { return false; }
        if (files.Length == 0) return false;

        List<string> history = new List<string>();
        string histPath = HistoryFile();
        if (smart && File.Exists(histPath))
        {
            try { history.AddRange(File.ReadAllLines(histPath, Encoding.UTF8)); } catch { }
        }

        List<string> list = new List<string>(files);
        using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
        {
            byte[] buf4 = new byte[4];
            for (int i = list.Count - 1; i > 0; i--)
            {
                rng.GetBytes(buf4);
                int r = BitConverter.ToInt32(buf4, 0) & int.MaxValue;
                int j = r % (i + 1);
                string tmp = list[i]; list[i] = list[j]; list[j] = tmp;
            }
        }

        if (smart && history.Count > 0 && list.Count > 1)
        {
            int moveTries = 0;
            while (moveTries < list.Count)
            {
                string cleanFirst = Regex.Replace(Path.GetFileName(list[0]), @"^\d{4}_", "");
                bool wasFirst = false;
                foreach (string h in history)
                    if (string.Equals(h, cleanFirst, StringComparison.OrdinalIgnoreCase)) { wasFirst = true; break; }
                if (!wasFirst) break;
                string moved = list[0];
                list.RemoveAt(0);
                list.Add(moved);
                moveTries++;
            }
        }

        // Фаза 1: временные уникальные имена
        string sessionTag = Guid.NewGuid().ToString("N").Substring(0, 8);
        List<string> tempPaths = new List<string>();
        List<string> cleanNames = new List<string>();
        for (int i = 0; i < list.Count; i++)
        {
            string cleanName = Regex.Replace(Path.GetFileName(list[i]), @"^\d{4}_", "");
            string tempPath = Path.Combine(musicDir, "__tmp_" + sessionTag + "_" + i + ".mp3");
            try
            {
                File.Move(list[i], tempPath);
                tempPaths.Add(tempPath);
            }
            catch
            {
                tempPaths.Add(list[i]);
            }
            cleanNames.Add(cleanName);
        }

        // Фаза 2:
        // Фаза 2: финальные имена NNNN_
        for (int i = 0; i < tempPaths.Count; i++)
        {
            string finalName = string.Format("{0:D4}_{1}", i + 1, cleanNames[i]);
            string finalPath = Path.Combine(musicDir, finalName);
            try
            {
                if (File.Exists(tempPaths[i])) File.Move(tempPaths[i], finalPath);
            }
            catch { }
        }

        if (smart)
        {
            try
            {
                List<string> newHistory = new List<string>();
                if (cleanNames.Count > 0) newHistory.Add(cleanNames[0]);
                if (File.Exists(histPath))
                {
                    string[] old = File.ReadAllLines(histPath, Encoding.UTF8);
                    foreach (string h in old) if (newHistory.Count < 10 && !newHistory.Contains(h)) newHistory.Add(h);
                }
                File.WriteAllLines(histPath, newHistory.ToArray(), Encoding.UTF8);
            }
            catch { }
        }

        return true;
    }
}
// =========================================================================
// === МЕНЕДЖЕР ТРЕКОВ (RadioMusic) ========================================
// =========================================================================
public class MusicManagerForm : Form
{
    private string musicDir;
    private ListBox lstTracks;
    private Label lblCount;

    public MusicManagerForm(string dir)
    {
        this.musicDir = dir;
        this.Text = "VTOL QoL — Управление треками RadioMusic";
        DarkWindowHelper.Apply(this);
        this.Size = new Size(640, 500);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.BackColor = Color.FromArgb(18, 22, 20);
        this.ForeColor = Color.FromArgb(0, 255, 102);
        this.Font = new Font("Consolas", 9.5f, FontStyle.Regular);

        Label lblTitle = new Label() { Text = "СПИСОК ТРЕКОВ В RADIOMUSIC:", Left = 20, Top = 15, AutoSize = true, Font = new Font("Consolas", 10f, FontStyle.Bold) };
        this.Controls.Add(lblTitle);

        lstTracks = new ListBox();
        lstTracks.Location = new Point(20, 42);
        lstTracks.Size = new Size(430, 360);
        lstTracks.BackColor = Color.FromArgb(24, 28, 26);
        lstTracks.ForeColor = Color.White;
        lstTracks.BorderStyle = BorderStyle.FixedSingle;
        lstTracks.SelectionMode = SelectionMode.MultiExtended;

        this.Controls.Add(lstTracks);

        int top = 42, btnW = 150, btnH = 36, gap = 10;

        Button btnPlay = CreateBtn("▶ Прослушать", ref top, btnW, btnH, gap, Color.FromArgb(0, 110, 140));
        btnPlay.Click += (s, e) => {
            if (lstTracks.SelectedItem == null) return;
            string path = Path.Combine(musicDir, lstTracks.SelectedItem.ToString());
            if (File.Exists(path)) try { Process.Start(path); } catch { }
        };

        Button btnShuffle = CreateBtn("🔀 Перемешать", ref top, btnW, btnH, gap, Color.FromArgb(70, 50, 110));
        btnShuffle.Click += (s, e) => {
            if (MusicShuffler.IsGameRunning()) { MessageBox.Show("Закройте VTOL VR перед перемешиванием треков.", "Игра запущена", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (MusicShuffler.ShuffleNow(musicDir, true))
            {
                LoadTracks();
                lblCount.Text = "Всего треков: " + lstTracks.Items.Count + "  |  Плейлист перемешан!";
            }
        };


        Button btnCleanPrefix = CreateBtn("Убрать 0001_", ref top, btnW, btnH, gap, Color.FromArgb(45, 75, 60));
        btnCleanPrefix.Click += (s, e) => {
            if (MusicShuffler.IsGameRunning()) { MessageBox.Show("Закройте VTOL VR перед переименованием.", "Игра запущена", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            foreach (string f in Directory.GetFiles(musicDir, "*.mp3"))
            {
                string name = Path.GetFileName(f);
                string clean = Regex.Replace(name, @"^\d{4}_", "");
                if (clean != name) try { File.Move(f, Path.Combine(musicDir, clean)); } catch { }
            }
            LoadTracks();
        };

        Button btnDelete = CreateBtn("Удалить выбранные", ref top, btnW, btnH, gap, Color.FromArgb(140, 35, 35));
        btnDelete.Click += BtnDelete_Click;

        Button btnRefresh = CreateBtn("Обновить список", ref top, btnW, btnH, gap, Color.FromArgb(40, 50, 45));
        btnRefresh.Click += (s, e) => LoadTracks();

        lblCount = new Label() { Left = 20, Top = 415, Width = 430, ForeColor = Color.Silver };
        this.Controls.Add(lblCount);

        Button btnClose = new Button() { Text = "Закрыть", Left = 460, Top = 410, Width = 150, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(30, 36, 33), ForeColor = Color.White };
        btnClose.Click += (s, e) => this.Close();
        this.Controls.Add(btnClose);

        LoadTracks();
    }

    private Button CreateBtn(string text, ref int top, int width, int height, int gap, Color backColor)
    {
        Button btn = new Button() { Text = text, Location = new Point(460, top), Size = new Size(width, height), FlatStyle = FlatStyle.Flat, BackColor = backColor, ForeColor = Color.White };
        this.Controls.Add(btn);
        top += height + gap;
        return btn;
    }

    private void LoadTracks()
    {
        lstTracks.Items.Clear();
        if (!Directory.Exists(musicDir)) return;
        string[] files = Directory.GetFiles(musicDir, "*.mp3");
        Array.Sort(files);
        foreach (string f in files) lstTracks.Items.Add(Path.GetFileName(f));
        lblCount.Text = "Всего треков: " + lstTracks.Items.Count;
    }

    private void BtnDelete_Click(object sender, EventArgs e)
    {
        if (lstTracks.SelectedItems.Count == 0) return;
        if (MessageBox.Show("Удалить выбранные треки (" + lstTracks.SelectedItems.Count + " шт.)?", "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            foreach (object item in lstTracks.SelectedItems)
            {
                string path = Path.Combine(musicDir, item.ToString());
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
            LoadTracks();
        }
    }
}

// =========================================================================
// === НАСТРОЙКИ АУДИО ДЛЯ VR (ВЫБОР УСТРОЙСТВ) ============================
// =========================================================================
public class AudioSettingsForm : Form
{
    private ComboBox cmbPlayback;
    private ComboBox cmbCapture;
    private CheckBox chkRestoreAfterExit;
    private List<AudioDeviceInfo> playbackDevices;
    private List<AudioDeviceInfo> captureDevices;

    public AudioSettingsForm()
    {
        this.Text = "VTOL QoL — Аудио-устройства для VR";
        DarkWindowHelper.Apply(this);
        this.Size = new Size(520, 300);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.BackColor = Color.FromArgb(18, 22, 20);
        this.ForeColor = Color.FromArgb(0, 255, 102);
        this.Font = new Font("Consolas", 9.5f, FontStyle.Regular);

        Label l1 = new Label() { Text = "Устройство воспроизведения (наушники VR):", Left = 20, Top = 20, AutoSize = true, ForeColor = Color.LightGray };
        this.Controls.Add(l1);
        cmbPlayback = new ComboBox() { Left = 20, Top = 42, Width = 460, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(30, 36, 33), ForeColor = Color.White };
        this.Controls.Add(cmbPlayback);

        Label l2 = new Label() { Text = "Устройство записи (микрофон VR):", Left = 20, Top = 85, AutoSize = true, ForeColor = Color.LightGray };
        this.Controls.Add(l2);
        cmbCapture = new ComboBox() { Left = 20, Top = 107, Width = 460, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(30, 36, 33), ForeColor = Color.White };
        this.Controls.Add(cmbCapture);

        chkRestoreAfterExit = new CheckBox() { Text = "Восстанавливать прежние устройства после выхода из игры", Left = 20, Top = 150, Width = 460, Checked = SettingsManager.GetBool("RestoreAudioAfterExit", true), ForeColor = Color.FromArgb(210, 240, 220) };
        this.Controls.Add(chkRestoreAfterExit);

        Button btnAuto = new Button() { Text = "Автоопределение VR-устройств", Left = 20, Top = 190, Width = 230, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 110, 140), ForeColor = Color.White };
        btnAuto.Click += (s, e) => AutoDetect();
        this.Controls.Add(btnAuto);

        Button btnSave = new Button() { Text = "Сохранить", Left = 260, Top = 190, Width = 110, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 140, 60), ForeColor = Color.White, DialogResult = DialogResult.OK };
        btnSave.Click += (s, e) => SaveAndClose();
        this.Controls.Add(btnSave);

        Button btnCancel = new Button() { Text = "Отмена", Left = 380, Top = 190, Width = 100, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(40, 50, 45), ForeColor = Color.White, DialogResult = DialogResult.Cancel };
        this.Controls.Add(btnCancel);

        LoadDevices();
    }

    private void LoadDevices()
    {
        playbackDevices = AudioInterop.GetDevices(EDataFlow.eRender);
        captureDevices = AudioInterop.GetDevices(EDataFlow.eCapture);
        cmbPlayback.Items.Clear();
        cmbCapture.Items.Clear();
        cmbPlayback.Items.Add("(не переключать)");
        cmbCapture.Items.Add("(не переключать)");
        foreach (AudioDeviceInfo d in playbackDevices) cmbPlayback.Items.Add(d.Name);
        foreach (AudioDeviceInfo d in captureDevices) cmbCapture.Items.Add(d.Name);

        string savedPlayback = SettingsManager.Get("VrPlaybackId", "");
        string savedCapture = SettingsManager.Get("VrCaptureId", "");
        cmbPlayback.SelectedIndex = 0;
        cmbCapture.SelectedIndex = 0;
        for (int i = 0; i < playbackDevices.Count; i++) if (playbackDevices[i].Id == savedPlayback) cmbPlayback.SelectedIndex = i + 1;
        for (int i = 0; i < captureDevices.Count; i++) if (captureDevices[i].Id == savedCapture) cmbCapture.SelectedIndex = i + 1;
    }

    private void AutoDetect()
    {
        AudioDeviceInfo? pb = AudioInterop.AutoDetectVr(playbackDevices);
        AudioDeviceInfo? cp = AudioInterop.AutoDetectVr(captureDevices);
        if (pb.HasValue)
        {
            for (int i = 0; i < playbackDevices.Count; i++) if (playbackDevices[i].Id == pb.Value.Id) cmbPlayback.SelectedIndex = i + 1;
        }
        if (cp.HasValue)
        {
            for (int i = 0; i < captureDevices.Count; i++) if (captureDevices[i].Id == cp.Value.Id) cmbCapture.SelectedIndex = i + 1;
        }
        if (!pb.HasValue && !cp.HasValue)
            MessageBox.Show("VR-устройства не найдены автоматически. Выберите вручную из списка.", "Внимание", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void SaveAndClose()
    {
        string pbId = cmbPlayback.SelectedIndex > 0 ? playbackDevices[cmbPlayback.SelectedIndex - 1].Id : "";
        string cpId = cmbCapture.SelectedIndex > 0 ? captureDevices[cmbCapture.SelectedIndex - 1].Id : "";
        SettingsManager.Set("VrPlaybackId", pbId);
        SettingsManager.Set("VrCaptureId", cpId);
        SettingsManager.SetBool("RestoreAudioAfterExit", chkRestoreAfterExit.Checked);
        SettingsManager.Save();
        this.DialogResult = DialogResult.OK;
        this.Close();
    }
}

// =========================================================================
// === ДИАГНОСТИКА ЛОГА (ПОДБОР ПАТТЕРНОВ) ================================
// =========================================================================
public class LogDiagnosticsForm : Form
{
    private TextBox txtLog;
    private CheckBox chkEnabled;

    public LogDiagnosticsForm()
    {
        this.Text = "VTOL QoL — Диагностика Player.log";
        DarkWindowHelper.Apply(this);
        this.Size = new Size(760, 560);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.BackColor = Color.FromArgb(18, 22, 20);
        this.ForeColor = Color.FromArgb(0, 255, 102);
        this.Font = new Font("Consolas", 9f);

        chkEnabled = new CheckBox() { Text = "Включить запись диагностики (для подбора паттернов)", Left = 20, Top = 15, Width = 500, Checked = SettingsManager.GetBool("DiagnosticsEnabled", false), ForeColor = Color.FromArgb(210, 240, 220) };
        this.Controls.Add(chkEnabled);

        txtLog = new TextBox();
        txtLog.Left = 20; txtLog.Top = 45; txtLog.Width = 705; txtLog.Height = 420;
        txtLog.Multiline = true; txtLog.ScrollBars = ScrollBars.Both; txtLog.ReadOnly = true; txtLog.WordWrap = false;
        txtLog.BackColor = Color.FromArgb(24, 28, 26); txtLog.ForeColor = Color.White; txtLog.Font = new Font("Consolas", 8.5f);
        this.Controls.Add(txtLog);

        Button btnRefresh = new Button() { Text = "Обновить", Left = 20, Top = 478, Width = 120, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(35, 45, 40), ForeColor = Color.White };
        btnRefresh.Click += (s, e) => LoadDiag();
        this.Controls.Add(btnRefresh);

        Button btnOpenPatterns = new Button() { Text = "Открыть log_patterns.ini", Left = 150, Top = 478, Width = 200, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 110, 140), ForeColor = Color.White };
        btnOpenPatterns.Click += (s, e) => { try { Process.Start("notepad.exe", LogPatterns.GetPath()); } catch { } };
        this.Controls.Add(btnOpenPatterns);

        Button btnClear = new Button() { Text = "Очистить", Left = 360, Top = 478, Width = 120, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(140, 35, 35), ForeColor = Color.White };
        btnClear.Click += (s, e) => { try { File.Delete(GetDiagPath()); } catch { } LoadDiag(); };
        this.Controls.Add(btnClear);

        Button btnSave = new Button() { Text = "Сохранить", Left = 585, Top = 478, Width = 140, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 140, 60), ForeColor = Color.White, DialogResult = DialogResult.OK };
        btnSave.Click += (s, e) => { SettingsManager.SetBool("DiagnosticsEnabled", chkEnabled.Checked); SettingsManager.Save(); };
        this.Controls.Add(btnSave);

        LoadDiag();
    }

    private string GetDiagPath()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL", "log_diagnostics.txt");
    }

    private void LoadDiag()
    {
        try
        {
            string path = GetDiagPath();
            txtLog.Text = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "(пусто — включите диагностику и полетайте)";
            txtLog.SelectionStart = txtLog.Text.Length;
            txtLog.ScrollToCaret();
        }
        catch { }
    }
}

// =========================================================================
// === ПАРСЕР ЛИВРЕЙ =======================================================
// =========================================================================
public class LiveryEntry
{
    public string FolderPath;
    public string Name;
    public string Aircraft;
    public string TexturePath;
    public string PreviewPath;
    public string ModelHint;
    public bool Valid;
    public string Source; // "Local" / "Workshop"
}

public static class LiveryParser
{
    private static readonly Dictionary<string, string> aircraftAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "FA26B", "FA-26B" }, { "F/A-26B", "FA-26B" }, { "F/A26B", "FA-26B" }, { "FA-26B", "FA-26B" },
        { "VTOL4", "AV-42C" }, { "AV42C", "AV-42C" }, { "AV-42C", "AV-42C" },
        { "F45A", "F-45A" }, { "F-45A", "F-45A" },
        { "AH94", "AH-94" }, { "AH-94", "AH-94" },
        { "T55", "T-55" }, { "T-55", "T-55" },
        { "EF24G", "EF-24G" }, { "EF-24G", "EF-24G" }
    };

    public static string NormalizeAircraftName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "Не определен";
        string trimmed = raw.Trim();
        string v;
        if (aircraftAliases.TryGetValue(trimmed, out v)) return v;
        return trimmed;
    }

    private static int FindImageHeaderOffset(byte[] h)
    {
        int len = Math.Min(128, h.Length);
        for (int i = 0; i <= len - 4; i++)
        {
            if (h[i] == 0x89 && h[i + 1] == 0x50 && h[i + 2] == 0x4E && h[i + 3] == 0x47) return i;
            if (h[i] == 0xFF && h[i + 1] == 0xD8 && h[i + 2] == 0xFF) return i;
        }
        return -1;
    }

    private static Dictionary<string, string> userAircraftMap = null;

    private static string GetMapFilePath()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL");
        try { Directory.CreateDirectory(dir); } catch { }
        return Path.Combine(dir, "livery_aircraft_map.ini");
    }

    public static void LoadUserAircraftMap()
    {
        userAircraftMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string p = GetMapFilePath();
        if (!File.Exists(p)) return;
        try
        {
            foreach (string line in File.ReadAllLines(p, Encoding.UTF8))
            {
                int idx = line.IndexOf('=');
                if (idx > 0)
                {
                    string k = line.Substring(0, idx).Trim();
                    string v = line.Substring(idx + 1).Trim();
                    if (k.Length > 0 && v.Length > 0) userAircraftMap[k] = v;
                }
            }
        }
        catch { }
    }

    public static void SaveUserAircraftMapping(string folderPath, string aircraft)
    {
        if (userAircraftMap == null) LoadUserAircraftMap();
        string key = Path.GetFileName(folderPath);
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(aircraft) || aircraft == "Не определен") return;
        userAircraftMap[key] = aircraft;
        try
        {
            List<string> lines = new List<string>();
            foreach (KeyValuePair<string, string> kv in userAircraftMap)
                lines.Add(kv.Key + "=" + kv.Value);
            File.WriteAllLines(GetMapFilePath(), lines.ToArray(), Encoding.UTF8);
        }
        catch { }
    }

    public static bool IsLiveryFolder(string folder)
    {
        try
        {
            string[] allFiles = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories);
            bool hasLiveryMarker = false;
            bool hasMissionOrMap = false;

            foreach (string f in allFiles)
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                string fn = Path.GetFileName(f).ToLowerInvariant();
                if (ext == ".vtl" || ext == ".vtlb" || fn == "livery.cfg" || fn.StartsWith("texture."))
                    hasLiveryMarker = true;
                if (ext == ".vts" || ext == ".vtsb" || ext == ".vtm" || ext == ".vtmb" || ext == ".vtc" || ext == ".vtcb")
                    hasMissionOrMap = true;
            }

            if (hasMissionOrMap && !hasLiveryMarker) return false;
            return hasLiveryMarker;
        }
        catch { return false; }
    }

    private static Dictionary<string, string> acfTagsCache = null;

    private static string GetSteamWorkshopTag(string folder)
    {
        string itemId = Path.GetFileName(folder);

        // 1. Проверяем теги внутри WorkshopItemInfo.xml
        string wsXml = Path.Combine(folder, "WorkshopItemInfo.xml");
        if (File.Exists(wsXml))
        {
            try
            {
                string xml = File.ReadAllText(wsXml, Encoding.UTF8);
                foreach (Match mTag in Regex.Matches(xml, @"<(?:Tag|string)>(.*?)</(?:Tag|string)>", RegexOptions.IgnoreCase))
                {
                    string raw = mTag.Groups[1].Value.Trim();
                    string norm = NormalizeAircraftName(raw);
                    if (norm == "FA-26B" || norm == "F-45A" || norm == "AV-42C" || norm == "AH-94" || norm == "T-55" || norm == "EF-24G")
                        return norm;
                }
            }
            catch { }
        }

        // 2. Проверяем кэш тегов Steam (appworkshop_667970.acf)
        try
        {
            string wsRoot = Path.GetFullPath(Path.Combine(folder, @"..\.."));
            string acfPath = Path.Combine(wsRoot, "appworkshop_667970.acf");
            if (acfTagsCache == null && File.Exists(acfPath))
            {
                acfTagsCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string acf = File.ReadAllText(acfPath, Encoding.UTF8);
                foreach (Match mItem in Regex.Matches(acf, @"""(\d+)""\s*\{([^{}]*(?:\{[^{}]*\}[^{}]*)*)\}"))
                {
                    string id = mItem.Groups[1].Value;
                    string block = mItem.Groups[2].Value;
                    Match mTags = Regex.Match(block, @"""tags""\s*""([^""]+)""", RegexOptions.IgnoreCase);
                    if (mTags.Success)
                    {
                        foreach (string rawTag in mTags.Groups[1].Value.Split(','))
                        {
                            string norm = NormalizeAircraftName(rawTag.Trim());
                            if (norm == "FA-26B" || norm == "F-45A" || norm == "AV-42C" || norm == "AH-94" || norm == "T-55" || norm == "EF-24G")
                            {
                                acfTagsCache[id] = norm;
                                break;
                            }
                        }
                    }
                }
            }
            string found;
            if (acfTagsCache != null && acfTagsCache.TryGetValue(itemId, out found))
                return found;
        }
        catch { }

        return null;
    }

    public static LiveryEntry ParseFolder(string folder, string source)
    {
        if (userAircraftMap == null) LoadUserAircraftMap();

        LiveryEntry e = new LiveryEntry();
        e.FolderPath = folder;
        e.Name = Path.GetFileName(folder);
        e.Source = source;
        e.Valid = false;
        e.Aircraft = "Не определен";

        try
        {
            string wsXml = Path.Combine(folder, "WorkshopItemInfo.xml");
            if (File.Exists(wsXml))
            {
                try
                {
                    string xml = File.ReadAllText(wsXml, Encoding.UTF8);
                    Match mTitle = Regex.Match(xml, @"<Title>(.*?)</Title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    if (mTitle.Success && mTitle.Groups[1].Value.Trim().Length > 0)
                        e.Name = mTitle.Groups[1].Value.Trim();
                }
                catch { }
            }

            // 1. Ручная привязка пользователя имеет высший приоритет
            string folderKey = Path.GetFileName(folder);
            string savedAc;
            if (userAircraftMap.TryGetValue(folderKey, out savedAc))
                e.Aircraft = savedAc;

            // 2. Официальный тег из Steam Workshop
            if (e.Aircraft == "Не определен" && source == "Workshop")
            {
                string wsTag = GetSteamWorkshopTag(folder);
                if (!string.IsNullOrEmpty(wsTag)) e.Aircraft = wsTag;
            }

            // 3. Данные из конфига ливреи (.cfg / .vtl / .vtlb)
            List<string> allCfg = new List<string>();
            allCfg.AddRange(Directory.GetFiles(folder, "*.cfg", SearchOption.AllDirectories));
            allCfg.AddRange(Directory.GetFiles(folder, "*.vtl", SearchOption.AllDirectories));
            allCfg.AddRange(Directory.GetFiles(folder, "*.vtlb", SearchOption.AllDirectories));

            foreach (string cfgFile in allCfg)
            {
                if (Path.GetExtension(cfgFile).Equals(".vtlb", StringComparison.OrdinalIgnoreCase) && e.Name == Path.GetFileName(folder))
                    e.Name = Path.GetFileNameWithoutExtension(cfgFile);

                if (e.Aircraft != "Не определен") continue;

                try
                {
                    string asciiText = Encoding.ASCII.GetString(File.ReadAllBytes(cfgFile));
                    Match m = Regex.Match(asciiText, @"(?im)(?:vehicle|vehicleName|craft)\s*=\s*""?([A-Za-z0-9\-/]+)""?");
                    if (m.Success)
                    {
                        e.Aircraft = NormalizeAircraftName(m.Groups[1].Value);
                        break;
                    }
                    foreach (KeyValuePair<string, string> kv in aircraftAliases)
                    {
                        if (asciiText.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            e.Aircraft = kv.Value;
                            break;
                        }
                    }
                }
                catch { }
            }

            // 4. Поиск текстуры и превью
            List<string> imgs = new List<string>();
            imgs.AddRange(Directory.GetFiles(folder, "*.png", SearchOption.AllDirectories));
            imgs.AddRange(Directory.GetFiles(folder, "*.jpg", SearchOption.AllDirectories));
            imgs.AddRange(Directory.GetFiles(folder, "*.jpgb", SearchOption.AllDirectories));
            imgs.AddRange(Directory.GetFiles(folder, "*.pngb", SearchOption.AllDirectories));

            string bestTex = null, bestPreview = null;
            long bestSize = -1;
            foreach (string img in imgs)
            {
                string fn = Path.GetFileNameWithoutExtension(img);
                bool isPrev = fn.Equals("image", StringComparison.OrdinalIgnoreCase) ||
                              fn.IndexOf("preview", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              fn.IndexOf("thumb", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              fn.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isPrev)
                {
                    if (bestPreview == null || fn.Equals("image", StringComparison.OrdinalIgnoreCase)) bestPreview = img;
                    continue;
                }
                long sz = new FileInfo(img).Length;
                if (sz > bestSize) { bestSize = sz; bestTex = img; }
            }

            e.TexturePath = bestTex;
            e.PreviewPath = bestPreview ?? bestTex;
            if (imgs.Count > 0) e.Valid = true;
        }
        catch { e.Valid = false; }

        return e;
    }


    public static System.Windows.Media.Imaging.BitmapImage LoadTextureBitmap(string path)
    {
        byte[] raw = File.ReadAllBytes(path);
        int offset = FindImageHeaderOffset(raw);
        if (offset < 0 && raw.Length > 16)
        {
            byte[] decoded = TryAutoDecryptImage(raw);
            if (decoded != null) { raw = decoded; offset = 0; }
        }
        if (offset < 0) throw new Exception("Формат " + Path.GetExtension(path) + " зашифрован.");

        using (MemoryStream ms = new MemoryStream(raw, offset, raw.Length - offset))
        {
            System.Windows.Media.Imaging.BitmapImage bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = ms;
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
    }

    private static byte[] TryAutoDecryptImage(byte[] data)
    {
        for (int start = 0; start <= 16; start++)
        {
            if (start + 4 >= data.Length) break;
            byte xJpg = (byte)(data[start] ^ 0xFF);
            if ((byte)(data[start + 1] ^ xJpg) == 0xD8 && (byte)(data[start + 2] ^ xJpg) == 0xFF)
            {
                byte[] res = new byte[data.Length - start];
                for (int i = 0; i < res.Length; i++) res[i] = (byte)(data[start + i] ^ xJpg);
                return res;
            }
            byte xPng = (byte)(data[start] ^ 0x89);
            if ((byte)(data[start + 1] ^ xPng) == 0x50 && (byte)(data[start + 2] ^ xPng) == 0x4E && (byte)(data[start + 3] ^ xPng) == 0x47)
            {
                byte[] res = new byte[data.Length - start];
                for (int i = 0; i < res.Length; i++) res[i] = (byte)(data[start + i] ^ xPng);
                return res;
            }
            int dJpg = (data[start] - 0xFF) & 0xFF;
            if (((data[start + 1] - dJpg) & 0xFF) == 0xD8 && ((data[start + 2] - dJpg) & 0xFF) == 0xFF)
            {
                byte[] res = new byte[data.Length - start];
                for (int i = 0; i < res.Length; i++) res[i] = (byte)((data[start + i] - dJpg) & 0xFF);
                return res;
            }
            int dPng = (data[start] - 0x89) & 0xFF;
            if (((data[start + 1] - dPng) & 0xFF) == 0x50 && ((data[start + 2] - dPng) & 0xFF) == 0x4E && ((data[start + 3] - dPng) & 0xFF) == 0x47)
            {
                byte[] res = new byte[data.Length - start];
                for (int i = 0; i < res.Length; i++) res[i] = (byte)((data[start + i] - dPng) & 0xFF);
                return res;
            }
        }
        return null;
    }

    public static List<LiveryEntry> ScanLocal(string customLiveriesDir)
    {
        List<LiveryEntry> result = new List<LiveryEntry>();
        if (!Directory.Exists(customLiveriesDir)) return result;
        foreach (string sub in Directory.GetDirectories(customLiveriesDir))
            result.Add(ParseFolder(sub, "Local"));
        return result;
    }

    public static List<LiveryEntry> ScanWorkshop(string steamLibraryRoot)
    {
        List<LiveryEntry> result = new List<LiveryEntry>();
        try
        {
            string workshopDir = Path.Combine(steamLibraryRoot, @"steamapps\workshop\content\667970");
            if (!Directory.Exists(workshopDir)) return result;
            foreach (string sub in Directory.GetDirectories(workshopDir))
            {
                if (!IsLiveryFolder(sub)) continue;
                result.Add(ParseFolder(sub, "Workshop"));
            }
        }
        catch { }
        return result;
    }



    public static bool InstallZip(string zipPath, string targetLiveriesDir)
    {
        try
        {
            string name = Path.GetFileNameWithoutExtension(zipPath);
            string dest = Path.Combine(targetLiveriesDir, name);
            if (Directory.Exists(dest)) Directory.Delete(dest, true);
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, dest);
            return true;
        }
        catch { return false; }
    }
}
// =========================================================================
// === ОНЛАЙН-КАТАЛОГ И ЗАГРУЗЧИК STEAM WORKSHOP (ЧЕРЕЗ STEAMCMD) ==========
// =========================================================================
public class WorkshopCatalogItem
{
    public string PublishedFileId;
    public string Title;
    public string PreviewUrl;
    public string AircraftTag;
}

public static class WorkshopBrowser
{
    public static void EnableTls12()
    {
        try { System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072; } catch { }
    }

    public static List<WorkshopCatalogItem> SearchLiveries(string query, string aircraftTag, int page)
    {
        EnableTls12();
        List<WorkshopCatalogItem> list = new List<WorkshopCatalogItem>();

        string qTrim = !string.IsNullOrEmpty(query) ? query.Trim() : "";
        AppLogger.Log("WORKSHOP", string.Format("Поиск: query='{0}', tag='{1}', page={2}", qTrim, aircraftTag, page));

        Match mDirectId = Regex.Match(qTrim, @"(?:id=|^)(?<val>\d{7,12})(?:&|$)");
        if (mDirectId.Success)
        {
            string directId = mDirectId.Groups["val"].Value;
            List<WorkshopCatalogItem> singleList = FetchDetailsBatch(new List<string> { directId }, aircraftTag);
            if (singleList.Count > 0) return singleList;
            WorkshopCatalogItem single = FetchSingleWorkshopItem(directId, aircraftTag);
            if (single != null) list.Add(single);
            return list;
        }

        string host = "steamcommunity.com";
        string url = "https://" + host + "/workshop/browse/?appid=667970&browsesort=trend&section=readytouseitems&requiredtags%5B0%5D=Liveries&p=" + page;

        if (!string.IsNullOrEmpty(aircraftTag) && aircraftTag != "Все самолеты" && aircraftTag != "Не определен")
        {
            string steamVehicleTag = aircraftTag == "FA-26B" ? "F/A-26B" : aircraftTag;
            url += "&requiredtags%5B1%5D=" + Uri.EscapeDataString(steamVehicleTag);
        }
        if (!string.IsNullOrEmpty(qTrim))
            url += "&searchtext=" + Uri.EscapeDataString(qTrim);

        AppLogger.Log("WORKSHOP", "GET URL: " + url);

        System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
        req.Method = "GET";
        req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36";
        req.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
        req.CookieContainer = new System.Net.CookieContainer();
        req.CookieContainer.Add(new Uri("https://" + host), new System.Net.Cookie("birthtime", "283993201"));
        req.CookieContainer.Add(new Uri("https://" + host), new System.Net.Cookie("wants_mature_content", "1"));
        req.AllowAutoRedirect = true;
        req.AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate;

        string html = "";
        using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)req.GetResponse())
        using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            html = sr.ReadToEnd();

        List<string> ids = new List<string>();
        MatchCollection idMatches = Regex.Matches(html, @"sharedfiles/filedetails/\?id=(?<val>\d{7,12})", RegexOptions.IgnoreCase);
        foreach (Match mId in idMatches)
        {
            string id = mId.Groups["val"].Value;
            if (!ids.Contains(id)) ids.Add(id);
        }

        AppLogger.Log("WORKSHOP", "Найдено уникальных ID на странице: " + ids.Count);
        if (ids.Count == 0) return list;

        List<WorkshopCatalogItem> rawList = FetchDetailsBatch(ids, "Не определен");
        if (string.IsNullOrEmpty(aircraftTag) || aircraftTag == "Все самолеты" || aircraftTag == "Не определен")
            return rawList;

        foreach (WorkshopCatalogItem it in rawList)
        {
            string ac = it.AircraftTag;
            // Если в тегах Steam борт не был указан, пробуем определить его по названию мода
            if (ac == "Не определен")
            {
                string t = it.Title;
                if (Regex.IsMatch(t, @"(?i)(EF-?24|F-?14|Tomcat|Mischief)")) ac = "EF-24G";
                else if (Regex.IsMatch(t, @"(?i)(F-?45|F-?22|F-?35|Ghost)")) ac = "F-45A";
                else if (Regex.IsMatch(t, @"(?i)(AV-?42|Kestrel)")) ac = "AV-42C";
                else if (Regex.IsMatch(t, @"(?i)(AH-?94|AH-?1|Cobra|Viper|Dragonfly)")) ac = "AH-94";
                else if (Regex.IsMatch(t, @"(?i)(T-?55|Tyro|Trainer)")) ac = "T-55";
                else if (Regex.IsMatch(t, @"(?i)(FA-?26|F/?A-?26|Wasp|F-?15|F-?16|F-?18|Su-?57|Felon)")) ac = "FA-26B";
                it.AircraftTag = ac;
            }

            if (it.AircraftTag.Equals(aircraftTag, StringComparison.OrdinalIgnoreCase))
                list.Add(it);
        }

        AppLogger.Log("WORKSHOP", string.Format("После строгого фильтра для {0} осталось: {1} из {2}", aircraftTag, list.Count, rawList.Count));
        return list;
    }


    private static List<WorkshopCatalogItem> FetchDetailsBatch(List<string> ids, string defaultAircraftTag)
    {
        List<WorkshopCatalogItem> result = new List<WorkshopCatalogItem>();
        try
        {
            StringBuilder postData = new StringBuilder();
            postData.Append("itemcount=" + ids.Count);
            for (int i = 0; i < ids.Count; i++)
                postData.Append("&publishedfileids[" + i + "]=" + ids[i]);

            byte[] bytes = Encoding.UTF8.GetBytes(postData.ToString());

            string apiHost = "api." + "steampowered" + ".com";
            string apiUrl = "https://" + apiHost + "/ISteamRemoteStorage/GetPublishedFileDetails/v1/";
            System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(apiUrl);

            req.Method = "POST";
            req.Timeout = 8000;
            req.ContentType = "application/x-www-form-urlencoded";
            req.ContentLength = bytes.Length;
            req.AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate;

            using (Stream reqStream = req.GetRequestStream())
                reqStream.Write(bytes, 0, bytes.Length);

            string json = "";
            using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)req.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                json = sr.ReadToEnd();

            string[] blocks = Regex.Split(json, @"""publishedfileid""\s*:\s*""");
            Dictionary<string, WorkshopCatalogItem> map = new Dictionary<string, WorkshopCatalogItem>();

            for (int i = 1; i < blocks.Length; i++)
            {
                string b = blocks[i];
                int qEnd = b.IndexOf('"');
                if (qEnd <= 0) continue;
                string id = b.Substring(0, qEnd);

                Match mTitle = Regex.Match(b, @"""title""\s*:\s*""(?<val>(?:\\.|[^""\\])*)""");
                string title = mTitle.Success ? Regex.Unescape(mTitle.Groups["val"].Value).Trim() : ("Workshop_" + id);
                if (string.IsNullOrEmpty(title)) title = "Workshop_" + id;

                Match mPrev = Regex.Match(b, @"""preview_url""\s*:\s*""(?<val>(?:\\.|[^""\\])*)""");
                string prevUrl = mPrev.Success ? Regex.Unescape(mPrev.Groups["val"].Value).Replace(@"\/", "/") : "";

                string detectedAc = (!string.IsNullOrEmpty(defaultAircraftTag) && defaultAircraftTag != "Все самолеты") ? defaultAircraftTag : "Не определен";
                foreach (Match mTag in Regex.Matches(b, @"""tag""\s*:\s*""(?<val>[^""]+)"""))
                {
                    string norm = LiveryParser.NormalizeAircraftName(mTag.Groups["val"].Value.Trim());
                    if (norm == "FA-26B" || norm == "F-45A" || norm == "AV-42C" || norm == "AH-94" || norm == "T-55" || norm == "EF-24G")
                    {
                        detectedAc = norm;
                        break;
                    }
                }

                map[id] = new WorkshopCatalogItem
                {
                    PublishedFileId = id,
                    Title = title,
                    PreviewUrl = prevUrl,
                    AircraftTag = detectedAc
                };
            }

            foreach (string id in ids)
            {
                WorkshopCatalogItem item;
                if (map.TryGetValue(id, out item)) result.Add(item);
            }
            AppLogger.Log("WORKSHOP", "GetPublishedFileDetails вернул метаданные для " + result.Count + " модов.");
        }
        catch (Exception ex)
        {
            AppLogger.Error("WORKSHOP", "FetchDetailsBatch", ex);
        }
        return result;
    }

    private static WorkshopCatalogItem FetchSingleWorkshopItem(string id, string aircraftTag)
    {
        try
        {
            string host = "steamcommunity.com";
            string url = "https://" + host + "/sharedfiles/filedetails/?id=" + id;
            AppLogger.Log("WORKSHOP", "Загрузка одиночного предмета: " + url);
            System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
            req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
            req.AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate;
            string html = "";
            using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)req.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                html = sr.ReadToEnd();

            Match mTitle = Regex.Match(html, @"class=""workshopItemTitle""[^>]*>(?<val>.*?)</div>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            string title = mTitle.Success ? System.Net.WebUtility.HtmlDecode(Regex.Replace(mTitle.Groups["val"].Value, "<.*?>", "")).Trim() : ("Workshop_" + id);

            Match mImg = Regex.Match(html, @"id=""previewImageMain""[^>]+src=""(?<val>https?://[^""]+)""", RegexOptions.IgnoreCase);
            if (!mImg.Success) mImg = Regex.Match(html, @"<link rel=""image_src"" href=""(?<val>https?://[^""]+)""", RegexOptions.IgnoreCase);
            string prevUrl = mImg.Success ? mImg.Groups["val"].Value : "";

            return new WorkshopCatalogItem
            {
                PublishedFileId = id,
                Title = title,
                PreviewUrl = prevUrl,
                AircraftTag = (!string.IsNullOrEmpty(aircraftTag) && aircraftTag != "Все самолеты") ? aircraftTag : "Не определен"
            };
        }
        catch (Exception ex)
        {
            AppLogger.Error("WORKSHOP", "FetchSingleWorkshopItem", ex);
            return new WorkshopCatalogItem { PublishedFileId = id, Title = "Workshop_" + id, PreviewUrl = "", AircraftTag = "Не определен" };
        }
    }
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public static void MinimizeSteamWindows()
    {
        try
        {
            string[] names = new string[] { "steam", "steamwebhelper" };
            foreach (string name in names)
            {
                foreach (Process p in Process.GetProcessesByName(name))
                {
                    if (p.MainWindowHandle != IntPtr.Zero)
                        ShowWindow(p.MainWindowHandle, 6);
                }
            }
        }
        catch { }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate bool SteamAPI_InitDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr SteamUGCDelegate();

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate ulong SubscribeItemDelegate(IntPtr self, ulong nPublishedFileID);

    private static bool TryNativeSteamSubscribe(string gameRoot, ulong fileId)
    {
        try
        {
            string dllPath = Path.Combine(gameRoot, @"VTOLVR_Data\Plugins\x86_64\steam_api64.dll");
            if (!File.Exists(dllPath))
                dllPath = Path.Combine(gameRoot, @"VTOLVR_Data\Plugins\steam_api64.dll");
            if (!File.Exists(dllPath)) return false;

            Environment.SetEnvironmentVariable("SteamAppId", "667970");
            Environment.SetEnvironmentVariable("SteamGameId", "667970");

            IntPtr hMod = LoadLibrary(dllPath);
            if (hMod == IntPtr.Zero) return false;

            IntPtr pInit = GetProcAddress(hMod, "SteamAPI_Init");
            if (pInit == IntPtr.Zero) return false;

            SteamAPI_InitDelegate fnInit = (SteamAPI_InitDelegate)Marshal.GetDelegateForFunctionPointer(pInit, typeof(SteamAPI_InitDelegate));
            if (!fnInit()) return false;

            string[] ugcExports = new string[] { "SteamAPI_SteamUGC_v020", "SteamAPI_SteamUGC_v018", "SteamAPI_SteamUGC_v017", "SteamAPI_SteamUGC_v016", "SteamAPI_SteamUGC_v015", "SteamUGC" };
            IntPtr ugcPtr = IntPtr.Zero;
            foreach (string exp in ugcExports)
            {
                IntPtr pGetUgc = GetProcAddress(hMod, exp);
                if (pGetUgc != IntPtr.Zero)
                {
                    SteamUGCDelegate fnUgc = (SteamUGCDelegate)Marshal.GetDelegateForFunctionPointer(pGetUgc, typeof(SteamUGCDelegate));
                    ugcPtr = fnUgc();
                    if (ugcPtr != IntPtr.Zero) break;
                }
            }
            if (ugcPtr == IntPtr.Zero) return false;

            IntPtr vtable = Marshal.ReadIntPtr(ugcPtr);
            IntPtr pSub = Marshal.ReadIntPtr(vtable, 27 * IntPtr.Size);
            SubscribeItemDelegate fnSub = (SubscribeItemDelegate)Marshal.GetDelegateForFunctionPointer(pSub, typeof(SubscribeItemDelegate));
            ulong callHandle = fnSub(ugcPtr, fileId);
            AppLogger.Log("WORKSHOP", "Native SteamUGC SubscribeItem вызван, handle=" + callHandle);
            return callHandle != 0;
        }
        catch (Exception ex)
        {
            AppLogger.Error("WORKSHOP", "TryNativeSteamSubscribe", ex);
            return false;
        }
    }

    public static volatile bool CancelWait = false;

    public static bool SubscribeAndDownloadWorkshop(WorkshopCatalogItem item, string gameRoot, Action<string> onStatus)
    {
        CancelWait = false;
        AppLogger.Log("WORKSHOP", "Подписка на ID=" + item.PublishedFileId + " (" + item.Title + ")");
        if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot))
            throw new Exception("Не определена папка игры VTOL VR.");

        string steamLibRoot = Path.GetFullPath(Path.Combine(gameRoot, @"..\..\.."));
        string gameWorkshopContent = Path.Combine(steamLibRoot, @"steamapps\workshop\content\667970");
        Directory.CreateDirectory(gameWorkshopContent);
        string targetFolder = Path.Combine(gameWorkshopContent, item.PublishedFileId);

        ulong fileIdNum;
        ulong.TryParse(item.PublishedFileId, out fileIdNum);

        bool nativeOk = (IntPtr.Size == 8 && fileIdNum > 0) && TryNativeSteamSubscribe(gameRoot, fileIdNum);
        if (nativeOk)
        {
            if (onStatus != null) onStatus("Подписка оформлена! Ожидание загрузки...");
        }
        else
        {
            if (onStatus != null) onStatus("Нажмите «Подписаться» в Steam (клик сюда — отмена)");
            Process.Start(new ProcessStartInfo("steam://url/CommunityFilePage/" + item.PublishedFileId) { UseShellExecute = true });
        }

        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (CancelWait) return false;

            if (Directory.Exists(targetFolder) && Directory.GetFiles(targetFolder, "*.*", SearchOption.AllDirectories).Length > 0)
            {
                Thread.Sleep(1000);
                WriteWorkshopMetadata(targetFolder, item);
                MinimizeSteamWindows();
                AppLogger.Log("WORKSHOP", "Мод успешно скачан клиентом Steam в " + targetFolder);
                return true;
            }
            Thread.Sleep(500);
        }

        return false;
    }


    private static void WriteWorkshopMetadata(string targetFolder, WorkshopCatalogItem item)
    {
        string xmlPath = Path.Combine(targetFolder, "WorkshopItemInfo.xml");
        if (!File.Exists(xmlPath))
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<WorkshopItemInfo>");
            sb.AppendLine("  <Title>" + System.Security.SecurityElement.Escape(item.Title) + "</Title>");
            if (!string.IsNullOrEmpty(item.AircraftTag) && item.AircraftTag != "Не определен")
                sb.AppendLine("  <Tag>" + System.Security.SecurityElement.Escape(item.AircraftTag) + "</Tag>");
            sb.AppendLine("</WorkshopItemInfo>");
            File.WriteAllText(xmlPath, sb.ToString(), Encoding.UTF8);
        }
        if (!string.IsNullOrEmpty(item.AircraftTag) && item.AircraftTag != "Не определен")
            LiveryParser.SaveUserAircraftMapping(targetFolder, item.AircraftTag);
    }
    private static bool TryNativeSteamUnsubscribe(string gameRoot, ulong fileId)
    {
        try
        {
            string dllPath = Path.Combine(gameRoot, @"VTOLVR_Data\Plugins\x86_64\steam_api64.dll");
            if (!File.Exists(dllPath))
                dllPath = Path.Combine(gameRoot, @"VTOLVR_Data\Plugins\steam_api64.dll");
            if (!File.Exists(dllPath)) return false;

            Environment.SetEnvironmentVariable("SteamAppId", "667970");
            Environment.SetEnvironmentVariable("SteamGameId", "667970");

            IntPtr hMod = LoadLibrary(dllPath);
            if (hMod == IntPtr.Zero) return false;

            IntPtr pInit = GetProcAddress(hMod, "SteamAPI_Init");
            if (pInit == IntPtr.Zero) return false;

            SteamAPI_InitDelegate fnInit = (SteamAPI_InitDelegate)Marshal.GetDelegateForFunctionPointer(pInit, typeof(SteamAPI_InitDelegate));
            if (!fnInit()) return false;

            string[] ugcExports = new string[] { "SteamAPI_SteamUGC_v020", "SteamAPI_SteamUGC_v018", "SteamAPI_SteamUGC_v017", "SteamAPI_SteamUGC_v016", "SteamAPI_SteamUGC_v015", "SteamUGC" };
            IntPtr ugcPtr = IntPtr.Zero;
            foreach (string exp in ugcExports)
            {
                IntPtr pGetUgc = GetProcAddress(hMod, exp);
                if (pGetUgc != IntPtr.Zero)
                {
                    SteamUGCDelegate fnUgc = (SteamUGCDelegate)Marshal.GetDelegateForFunctionPointer(pGetUgc, typeof(SteamUGCDelegate));
                    ugcPtr = fnUgc();
                    if (ugcPtr != IntPtr.Zero) break;
                }
            }
            if (ugcPtr == IntPtr.Zero) return false;

            IntPtr vtable = Marshal.ReadIntPtr(ugcPtr);
            IntPtr pUnsub = Marshal.ReadIntPtr(vtable, 28 * IntPtr.Size);
            SubscribeItemDelegate fnUnsub = (SubscribeItemDelegate)Marshal.GetDelegateForFunctionPointer(pUnsub, typeof(SubscribeItemDelegate));
            ulong callHandle = fnUnsub(ugcPtr, fileId);
            AppLogger.Log("WORKSHOP", "Native SteamUGC UnsubscribeItem вызван, handle=" + callHandle);
            return callHandle != 0;
        }
        catch (Exception ex)
        {
            AppLogger.Error("WORKSHOP", "TryNativeSteamUnsubscribe", ex);
            return false;
        }
    }

    public static void UnsubscribeAndDeleteWorkshop(string folderPath, string gameRoot)
    {
        string itemId = Path.GetFileName(folderPath);
        AppLogger.Log("WORKSHOP", "Удаление Workshop-ливреи ID=" + itemId);

        ulong fileIdNum;
        if (IntPtr.Size == 8 && ulong.TryParse(itemId, out fileIdNum) && !string.IsNullOrEmpty(gameRoot))
            TryNativeSteamUnsubscribe(gameRoot, fileIdNum);

        if (Directory.Exists(folderPath))
            Directory.Delete(folderPath, true);

        try
        {
            string wsRoot = Path.GetFullPath(Path.Combine(folderPath, @"..\.."));
            string acfPath = Path.Combine(wsRoot, "appworkshop_667970.acf");
            if (File.Exists(acfPath))
            {
                string acf = File.ReadAllText(acfPath, Encoding.UTF8);
                string updated = Regex.Replace(acf, @"\s*""" + Regex.Escape(itemId) + @"""\s*\{[^{}]*(?:\{[^{}]*\}[^{}]*)*\}", "");
                if (updated != acf)
                    File.WriteAllText(acfPath, updated, new UTF8Encoding(false));
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("WORKSHOP", "Очистка appworkshop_667970.acf", ex);
        }
    }

}


// =========================================================================
// === ЗАГРУЗЧИК FBX (РАЗДЕЛЕНИЕ КОРПУСА/ДЕТАЛЕЙ + АНТИ-Z-FIGHTING) =========
// =========================================================================
public class FbxSubMesh
{
    public List<System.Windows.Media.Media3D.Point3D> Vertices = new List<System.Windows.Media.Media3D.Point3D>();
    public List<int> Indices = new List<int>();
    public List<System.Windows.Point> UVs = new List<System.Windows.Point>();
}

public class FbxLightMarker
{
    public string Kind; // "NavRed", "NavGreen", "NavWhite", "Strobe", "Landing"
    public System.Windows.Media.Media3D.Point3D Position;
}

public class FbxMeshData
{
    public FbxSubMesh Body = new FbxSubMesh();
    public FbxSubMesh Details = new FbxSubMesh();
    public FbxSubMesh Glass = new FbxSubMesh();
    public List<FbxLightMarker> Lights = new List<FbxLightMarker>();

    public int TotalVertices
    {
        get { return Body.Vertices.Count + Details.Vertices.Count + Glass.Vertices.Count; }
    }
}


public static class FbxLoader
{
    private class FbxNode
    {
        public string Name;
        public List<object> Properties = new List<object>();
        public List<FbxNode> Children = new List<FbxNode>();
    }

    private class FbxModelTransform
    {
        public long Id;
        public string Name;
        public double Tx, Ty, Tz, Rx, Ry, Rz;
        public double Sx = 1, Sy = 1, Sz = 1;
        public long ParentId = 0;
    }

    public static bool UseSecondUV = true;
    public static bool FlipUVY = true;

    private class FbxGeomPart
    {
        public long Id;
        public List<System.Windows.Media.Media3D.Point3D> RawVerts = new List<System.Windows.Media.Media3D.Point3D>();
        public List<int> RawPolyIndices = new List<int>();
        public List<System.Windows.Point> RawUVs = new List<System.Windows.Point>();
        public List<int> UVIndices = new List<int>();
        public string UVMapping = "ByPolygonVertex";
        public string UVRef = "Direct";
    }

    private static object GetProp(FbxNode n, int index)
    {
        if (n == null || n.Properties == null || index < 0 || index >= n.Properties.Count) return null;
        int cur = 0;
        foreach (object obj in n.Properties)
        {
            if (cur == index) return obj;
            cur++;
        }
        return null;
    }

    public static FbxMeshData Load(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        if (data.Length > 23 && Encoding.ASCII.GetString(data, 0, 18) == "Kaydara FBX Binary")
        {
            try { return LoadBinary(data); }
            catch (Exception ex) { AppLogger.Error("FBX", Path.GetFileName(path), ex); return null; }
        }
        return null;
    }

    private static FbxMeshData LoadBinary(byte[] data)
    {
        int pos = 27;
        uint version = BitConverter.ToUInt32(data, 23);
        bool is64 = version >= 7500;

        List<FbxNode> topNodes = new List<FbxNode>();
        while (pos < data.Length - 4)
        {
            FbxNode n = ReadNode(data, ref pos, is64);
            if (n == null) break;
            topNodes.Add(n);
        }

        FbxNode objects = FindChild(topNodes, "Objects");
        if (objects == null) return null;

        Dictionary<long, FbxGeomPart> geoms = new Dictionary<long, FbxGeomPart>();
        Dictionary<long, FbxModelTransform> models = new Dictionary<long, FbxModelTransform>();
        Dictionary<long, string> materials = new Dictionary<long, string>();
        Dictionary<long, long> geomToModel = new Dictionary<long, long>();
        Dictionary<long, List<string>> modelToMaterials = new Dictionary<long, List<string>>();

        foreach (FbxNode child in objects.Children)
        {
            if (child.Name == "Geometry" && child.Properties.Count > 0)
            {
                long id = Convert.ToInt64(GetProp(child, 0));
                FbxGeomPart part = ParseGeometryNode(child, id);
                if (part != null && part.RawVerts.Count > 0) geoms[id] = part;
            }
            else if (child.Name == "Model" && child.Properties.Count > 0)
            {
                long id = Convert.ToInt64(GetProp(child, 0));
                models[id] = ParseModelNode(child, id);
            }
            else if (child.Name == "Material" && child.Properties.Count > 1)
            {
                long id = Convert.ToInt64(GetProp(child, 0));
                materials[id] = GetProp(child, 1) as string ?? "";
            }
        }

        FbxNode conns = FindChild(topNodes, "Connections");
        if (conns != null)
        {
            foreach (FbxNode c in conns.Children)
            {
                if (c.Name == "C" && c.Properties.Count >= 3 && (GetProp(c, 0) as string) == "OO")
                {
                    long childId = Convert.ToInt64(GetProp(c, 1));
                    long parentId = Convert.ToInt64(GetProp(c, 2));
                    if (geoms.ContainsKey(childId) && models.ContainsKey(parentId))
                        geomToModel[childId] = parentId;
                    else if (models.ContainsKey(childId) && models.ContainsKey(parentId))
                        models[childId].ParentId = parentId;
                    else if (materials.ContainsKey(childId) && models.ContainsKey(parentId))
                    {
                        List<string> list;
                        if (!modelToMaterials.TryGetValue(parentId, out list))
                        {
                            list = new List<string>();
                            modelToMaterials[parentId] = list;
                        }
                        list.Add(materials[childId]);
                    }
                }
            }
        }

        return AssembleFullMesh(geoms, models, geomToModel, modelToMaterials);
    }

    private static string GetFullHierarchyName(long modelId, Dictionary<long, FbxModelTransform> models, Dictionary<long, List<string>> modelToMaterials)
    {
        StringBuilder sb = new StringBuilder();
        HashSet<long> visited = new HashSet<long>();
        long cur = modelId;
        while (cur != 0 && models.ContainsKey(cur) && !visited.Contains(cur))
        {
            visited.Add(cur);
            FbxModelTransform m = models[cur];
            if (!string.IsNullOrEmpty(m.Name)) sb.Append(" ").Append(m.Name);
            List<string> mats;
            if (modelToMaterials.TryGetValue(cur, out mats))
                foreach (string mat in mats) sb.Append(" ").Append(mat);
            cur = m.ParentId;
        }
        return sb.ToString();
    }

    private static bool ContainsAny(string text, params string[] keywords)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (string kw in keywords)
            if (text.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    private static FbxMeshData AssembleFullMesh(
        Dictionary<long, FbxGeomPart> geoms,
        Dictionary<long, FbxModelTransform> models,
        Dictionary<long, long> geomToModel,
        Dictionary<long, List<string>> modelToMaterials)
    {
        bool hasDetailedParts = false;
        foreach (var kv in geoms) if (kv.Value.RawVerts.Count > 30) { hasDetailedParts = true; break; }

        FbxMeshData result = new FbxMeshData();
        HashSet<string> seenGeometrySignatures = new HashSet<string>();

        foreach (var kv in geoms)
        {
            FbxGeomPart part = kv.Value;
            if (hasDetailedParts && !(part.RawVerts.Count > 24))
            {
                string quickName = "";
                long tmpId;
                if (geomToModel.TryGetValue(part.Id, out tmpId))
                    quickName = GetFullHierarchyName(tmpId, models, modelToMaterials);
                if (!ContainsAny(quickName, "Rotor", "Blade", "Prop"))
                    continue;
            }

            System.Windows.Media.Media3D.Matrix3D worldMat = System.Windows.Media.Media3D.Matrix3D.Identity;
            string fullTag = "";
            long modelId;
            string selfName = "";
            if (geomToModel.TryGetValue(part.Id, out modelId))
            {
                if (models.ContainsKey(modelId) && !string.IsNullOrEmpty(models[modelId].Name))
                    selfName = models[modelId].Name;

                fullTag = GetFullHierarchyName(modelId, models, modelToMaterials);

                // 1. Отсекаем все повреждённые дубликаты (_damaged, _dmg, _destroyed, _broken, dmg1..4),
                // негнущиеся дубли (_unbent), дальние LOD (кроме двигателей vgEngineLowPoly!),
                // осколки стекла, царапины, аватарки, камеры и шлем/руки пилота
                bool isEngineNozzle = ContainsAny(fullTag, "vgEngine", "mat_vgLowpoly", "leftEngine", "rightEngine", "t55-engine");

                if (!isEngineNozzle)
                {
                    if (ContainsAny(selfName,
                        "_damaged", "_dmg", "_destroyed", ".destroyed", "_broken", "tailBroken", "brokenWing",
                        "LOD 1", "LOD 2", "LOD 3", "LOD 4", "LOD1", "LOD2", "LOD3", "LOD4",
                        "LowPoly", "_lod", "GlassBreak", ".scratch", "_glare", "hudGlare", "hudScratch",
                        "steamImg", "TailArt", "Collider", "Hitbox", "Trigger", "Shadow",
                        "Flame", "Afterburner", "ExhaustCone", "JetFlame", "NavLight", "Strobe", "LandingLight", "TaxiLight"))
                        continue;

                    if (ContainsAny(fullTag,
                        "_damaged", "_dmg", "_destroyed", ".destroyed", "_broken", "tailBroken", "brokenWing",
                        "dmg1", "dmg2", "dmg3", "dmg4", "Blurred",
                        "sevtfLowPoly", "body_LOD2", "LOD 1", "LOD1", "LOD 2", "LOD2", "LOD3", "LowPoly",
                        "steamUserImage", "FlybyCam", "SpectatorCam", "Camcorder", "Remote", "BobblePilot",
                        "[CameraRig]", "Camera (eye)", "Helmet", "hqh", "flesh", "headSphere", "hmdVisor", "tintVisor",
                        "DashCanvasFront", "DashCanvasRear", "PassengerOnlyObjs", "LowerCenterDash"))
                        continue;
                }


                worldMat = BuildWorldMatrix(modelId, models);
            }

            List<System.Windows.Media.Media3D.Point3D> worldVerts = new List<System.Windows.Media.Media3D.Point3D>(part.RawVerts.Count);
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

            for (int i = 0; i < part.RawVerts.Count; i++)
            {
                System.Windows.Media.Media3D.Point3D wp = worldMat.Transform(part.RawVerts[i]);
                worldVerts.Add(wp);
                if (wp.X < minX) minX = wp.X; if (wp.X > maxX) maxX = wp.X;
                if (wp.Y < minY) minY = wp.Y; if (wp.Y > maxY) maxY = wp.Y;
                if (wp.Z < minZ) minZ = wp.Z; if (wp.Z > maxZ) maxZ = wp.Z;
            }

            if (part.RawVerts.Count <= 6 && part.RawPolyIndices.Count <= 6)
                continue;

            string sig = string.Format("{0}_{1}_{2:F3}_{3:F3}_{4:F3}_{5:F3}_{6:F3}_{7:F3}",
                part.RawVerts.Count, part.RawPolyIndices.Count,
                minX, minY, minZ, maxX, maxY, maxZ);
            if (seenGeometrySignatures.Contains(sig))
            {
                if (ContainsAny(fullTag, "T-55") && ContainsAny(fullTag, "flap", "wing", "body", "airbrake"))
                    AppLogger.Log("T55_DUP", string.Format("Self='{0}', Verts={1}, Tag='{2}'", selfName.Trim(), part.RawVerts.Count, fullTag.Trim()));
                continue;
            }
            seenGeometrySignatures.Add(sig);

            if (ContainsAny(fullTag, "EF-24", "ef24", "ew"))
                AppLogger.Log("EF24_PART", string.Format("Self='{0}' | Verts={1} | BBox=({2:F1},{3:F1},{4:F1})..({5:F1},{6:F1},{7:F1}) | Tag='{8}'",
                    selfName.Trim(), part.RawVerts.Count, minX, minY, minZ, maxX, maxY, maxZ, fullTag.Trim()));



            FbxSubMesh targetSub = result.Body;

            // 2. Стекло фонаря кабины (полупрозрачный плексиглас)
            bool isGlassMesh = ContainsAny(selfName, "Canopy", "Glass", "Windshield", "Transparent") &&
                               !ContainsAny(selfName, "Frame", "Cylinder", "Piston", "Switch", "Button", "Sensor", "Tads", "Optic", "Camera", "Turret");
            bool isGlassMat = ContainsAny(fullTag, "mat_f45CanopyExt", "mat_ewCanopy", "CanopyGlass", "CockpitGlass", "HudGlass");

            if ((isGlassMesh || isGlassMat) && !ContainsAny(selfName, "Frame", "detCord", "Cylinder", "Piston"))
            {
                targetSub = result.Glass;
            }
            // 3. Сопла двигателей (vgEngineLowPoly, _engine), стойки/колёса шасси (_ext2, landingGear), интерьер кабины (_int, cockpitProps, acesSeat)
            else if (part.RawUVs.Count == 0 || (!ContainsAny(selfName, "gearDoor", "GearDoor", "centerRails", "sidePylons", "sideRails", "UnderRailNoseCone") && ContainsAny(fullTag,
    "vgEngine", "mat_vgLowpoly", "_ext2", "_engine", "_int", "Interior", "cockpitProps", "NonUIDash", "DashCanvas", "Instruments",
                "acesSeat", "EjectorSeat", "Seat", "Stick", "Throttle", "Pedal", "MFD", "Switch", "Button", "Knob", "Gauge",
                "LandingGear", "landingGear", "FrontGear", "LeftGear", "RightGear", "Wheel", "Tire", "Tyre", "Strut", "Piston", "catHook",
                "Nozzle", "gimbalFin", "Turbine", "Compressor", "liftFanCylinder", "liftFanPiston", "IntakeGuides",
                "mat_tgp", "eyeHolder", "Pylon", "Rail", "Launcher", "Rack", "Hardpoint", "Missile", "Bomb", "tempMirror")))
            {
                targetSub = result.Details;
            }

            List<int> polyVerts = new List<int>();
            List<int> polyCorners = new List<int>();
            int cornerIdx = 0;

            foreach (int raw in part.RawPolyIndices)
            {
                bool last = raw < 0;
                int vIdx = last ? (-raw - 1) : raw;
                polyVerts.Add(vIdx);
                polyCorners.Add(cornerIdx);
                cornerIdx++;

                if (last)
                {
                    for (int k = 1; k + 1 < polyVerts.Count; k++)
                    {
                        int v0 = 0; foreach (int x in polyVerts) { v0 = x; break; }
                        int c0 = 0; foreach (int x in polyCorners) { c0 = x; break; }
                        int[] triV = new int[] { v0, polyVerts[k], polyVerts[k + 1] };
                        int[] triC = new int[] { c0, polyCorners[k], polyCorners[k + 1] };

                        for (int t = 0; t < 3; t++)
                        {
                            int vi = triV[t];
                            if (vi < 0 || vi >= worldVerts.Count) continue;

                            targetSub.Indices.Add(targetSub.Vertices.Count);
                            targetSub.Vertices.Add(worldVerts[vi]);

                            System.Windows.Point uv = ResolveUV(part, vi, triC[t]);
                            targetSub.UVs.Add(new System.Windows.Point(uv.X, FlipUVY ? (1.0 - uv.Y) : uv.Y));
                        }
                    }
                    polyVerts.Clear();
                    polyCorners.Clear();
                }
            }
        }

        foreach (var mkv in models)
        {
            FbxModelTransform m = mkv.Value;
            string mName = m.Name ?? "";
            string hName = GetFullHierarchyName(m.Id, models, modelToMaterials);

            // Исключаем повреждённые дубли, тумблеры в кабине (Switch, Interactable, DashCanvas)
            // и пустые корневые контейнеры-контроллеры (Controller, ExtLights, LightSwitches)
            if (ContainsAny(hName,
    "_damaged", "_dmg", "_destroyed", ".destroyed", "_broken",
    "LOD 2", "LOD2", "Folded", "BrokenWing", "Camcorder", "Spectator",
    "Switch", "Interactable", "Controller", "DashCanvas", "Overhead",
    "LightSwitches"))
                continue;

            // Пропускаем только пустые родительские группы, но не их дочерние лампы!
            if (ContainsAny(mName, "NavLights", "StrobeLights", "ExtLights", "LightObjs"))
                continue;

            string kind = null;
            if (ContainsAny(mName, "RedStrobe", "redStrobe", "Beacon", "AntiCol"))
            {
                if (!ContainsAny(hName, "ah94", "AH-94")) continue;
                kind = "RedStrobe";
            }

            else if (ContainsAny(mName, "Strobe"))
                kind = "Strobe";
            else if (ContainsAny(mName, "LandingLight", "TaxiLight", "NoseLight", "Spotlight"))
                kind = "Landing";
            else if (ContainsAny(mName, "NavLight", "Nav_L", "Nav_R", "WingLight", "TailLight"))
            {
                if (ContainsAny(mName, "Tail", "White", "Rear")) kind = "NavWhite";
                else kind = "NavAuto";
            }


            if (kind != null)
            {
                System.Windows.Media.Media3D.Matrix3D wm = BuildWorldMatrix(m.Id, models);
                System.Windows.Media.Media3D.Point3D pos = wm.Transform(new System.Windows.Media.Media3D.Point3D(0, 0, 0));
                if (kind == "RedStrobe" && Math.Abs(pos.X) > 450.0) continue;
                if (pos.X * pos.X + pos.Y * pos.Y + pos.Z * pos.Z < 1.0) continue;

                // Проверяем дубли только среди огней ТОГО ЖЕ типа (чтобы строб не склеивался с БАНО!)
                bool dup = false;
                for (int i = 0; i < result.Lights.Count; i++)
                {
                    if (result.Lights[i].Kind == kind && (result.Lights[i].Position - pos).LengthSquared < 16000.0)
                    {
                        dup = true;
                        break;
                    }
                }
                if (!dup)
                    result.Lights.Add(new FbxLightMarker { Kind = kind, Position = pos });
            }
        }


        if (result.Body.Vertices.Count == 0 && result.Details.Vertices.Count > 0)
        {
            result.Body = result.Details;
            result.Details = new FbxSubMesh();
        }

        return result.TotalVertices > 0 ? result : null;
    }

    private static System.Windows.Point ResolveUV(FbxGeomPart part, int vertIdx, int cornerIdx)
    {
        if (part.RawUVs.Count == 0) return new System.Windows.Point(0, 0);

        int lookup = (part.UVMapping == "ByVertice") ? vertIdx : cornerIdx;
        if (part.UVIndices.Count == 0 && part.RawUVs.Count == part.RawVerts.Count)
            lookup = vertIdx;

        int uvIdx = lookup;
        if (part.UVIndices.Count > 0 && lookup >= 0 && lookup < part.UVIndices.Count)
            uvIdx = part.UVIndices[lookup];

        if (uvIdx >= 0 && uvIdx < part.RawUVs.Count) return part.RawUVs[uvIdx];
        if (vertIdx >= 0 && vertIdx < part.RawUVs.Count) return part.RawUVs[vertIdx];
        return new System.Windows.Point(0, 0);
    }

    private static System.Windows.Media.Media3D.Matrix3D BuildWorldMatrix(long modelId, Dictionary<long, FbxModelTransform> models)
    {
        System.Windows.Media.Media3D.Matrix3D mat = System.Windows.Media.Media3D.Matrix3D.Identity;
        HashSet<long> visited = new HashSet<long>();
        long cur = modelId;
        while (cur != 0 && models.ContainsKey(cur) && !visited.Contains(cur))
        {
            visited.Add(cur);
            FbxModelTransform m = models[cur];
            System.Windows.Media.Media3D.Transform3DGroup tg = new System.Windows.Media.Media3D.Transform3DGroup();
            tg.Children.Add(new System.Windows.Media.Media3D.ScaleTransform3D(m.Sx, m.Sy, m.Sz));
            tg.Children.Add(new System.Windows.Media.Media3D.RotateTransform3D(new System.Windows.Media.Media3D.AxisAngleRotation3D(new System.Windows.Media.Media3D.Vector3D(1, 0, 0), m.Rx)));
            tg.Children.Add(new System.Windows.Media.Media3D.RotateTransform3D(new System.Windows.Media.Media3D.AxisAngleRotation3D(new System.Windows.Media.Media3D.Vector3D(0, 1, 0), m.Ry)));
            tg.Children.Add(new System.Windows.Media.Media3D.RotateTransform3D(new System.Windows.Media.Media3D.AxisAngleRotation3D(new System.Windows.Media.Media3D.Vector3D(0, 0, 1), m.Rz)));
            tg.Children.Add(new System.Windows.Media.Media3D.TranslateTransform3D(m.Tx, m.Ty, m.Tz));
            mat = System.Windows.Media.Media3D.Matrix3D.Multiply(mat, tg.Value);
            cur = m.ParentId;
        }
        return mat;
    }

    private static FbxModelTransform ParseModelNode(FbxNode node, long id)
    {
        FbxModelTransform tr = new FbxModelTransform { Id = id };

        if (node.Properties.Count > 1) tr.Name = node.Properties[1] as string;
    FbxNode p70 = FindChild(node.Children, "Properties70");
    if (p70 != null)
    {
        foreach (FbxNode p in p70.Children)
        {
            if (p.Name == "P" && p.Properties.Count >= 7)
            {
                string propName = p.Properties[0] as string;
                double x = Convert.ToDouble(p.Properties[4]);
                double y = Convert.ToDouble(p.Properties[5]);
                double z = Convert.ToDouble(p.Properties[6]);
                if (propName == "Lcl Translation") { tr.Tx = x; tr.Ty = y; tr.Tz = z; }
                else if (propName == "Lcl Rotation") { tr.Rx = x; tr.Ry = y; tr.Rz = z; }
                else if (propName == "Lcl Scaling") { tr.Sx = x; tr.Sy = y; tr.Sz = z; }
            }
        }
    }
    return tr;
}
    private static bool HasValidUVData(FbxNode uvNode)
    {
        if (uvNode == null) return false;
        FbxNode uvData = FindChild(uvNode.Children, "UV");
        if (uvData == null || uvData.Properties.Count == 0) return false;
        double[] arr = GetProp(uvData, 0) as double[];
        if (arr == null || arr.Length < 4) return false;
        for (int i = 0; i < arr.Length; i++)
            if (Math.Abs(arr[i]) > 0.0001) return true;
        return false;
    }

    private static FbxGeomPart ParseGeometryNode(FbxNode geometry, long id)
{
        FbxGeomPart part = new FbxGeomPart { Id = id };
        FbxNode verticesNode = FindChild(geometry.Children, "Vertices");
        FbxNode indicesNode = FindChild(geometry.Children, "PolygonVertexIndex");
        List<FbxNode> uvLayers = new List<FbxNode>();
        foreach (FbxNode ch in geometry.Children)
            if (ch.Name == "LayerElementUV") uvLayers.Add(ch);

        FbxNode uvNode = null;
        if (uvLayers.Count > 0)
        {
            int idx = (UseSecondUV && uvLayers.Count > 1) ? 1 : 0;
            uvNode = uvLayers[idx];
            // Если выбранный UV-слой пустой или все координаты в (0,0) — переключаемся на другой слой
            if (uvLayers.Count > 1 && !HasValidUVData(uvNode))
                uvNode = uvLayers[1 - idx];
        }



        if (verticesNode != null && verticesNode.Properties.Count > 0)
    {
        double[] arr = verticesNode.Properties[0] as double[];
        if (arr != null)
            for (int i = 0; i + 2 < arr.Length; i += 3)
                part.RawVerts.Add(new System.Windows.Media.Media3D.Point3D(arr[i], arr[i + 1], arr[i + 2]));
    }

    if (indicesNode != null && indicesNode.Properties.Count > 0)
    {
        int[] arr = indicesNode.Properties[0] as int[];
        if (arr != null) part.RawPolyIndices.AddRange(arr);
    }

    if (uvNode != null)
    {
        FbxNode mapNode = FindChild(uvNode.Children, "MappingInformationType");
        if (mapNode != null && mapNode.Properties.Count > 0) part.UVMapping = mapNode.Properties[0] as string ?? "ByPolygonVertex";

        FbxNode refNode = FindChild(uvNode.Children, "ReferenceInformationType");
        if (refNode != null && refNode.Properties.Count > 0) part.UVRef = refNode.Properties[0] as string ?? "Direct";

        FbxNode uvData = FindChild(uvNode.Children, "UV");
        if (uvData != null && uvData.Properties.Count > 0)
        {
            double[] arr = uvData.Properties[0] as double[];
            if (arr != null)
                for (int i = 0; i + 1 < arr.Length; i += 2)
                    part.RawUVs.Add(new System.Windows.Point(arr[i], arr[i + 1]));
        }

        FbxNode uvIdxData = FindChild(uvNode.Children, "UVIndex");
        if (uvIdxData != null && uvIdxData.Properties.Count > 0)
        {
            int[] arr = uvIdxData.Properties[0] as int[];
            if (arr != null) part.UVIndices.AddRange(arr);
        }
    }
    return part;
}

private static FbxNode FindChild(List<FbxNode> nodes, string name)
{
    foreach (FbxNode n in nodes) if (n.Name == name) return n;
    return null;
}

private static FbxNode ReadNode(byte[] data, ref int pos, bool is64)
{
    long endOffset, numProps, propListLen;
    if (is64)
    {
        endOffset = BitConverter.ToInt64(data, pos); pos += 8;
        numProps = BitConverter.ToInt64(data, pos); pos += 8;
        propListLen = BitConverter.ToInt64(data, pos); pos += 8;
    }
    else
    {
        endOffset = BitConverter.ToUInt32(data, pos); pos += 4;
        numProps = BitConverter.ToUInt32(data, pos); pos += 4;
        propListLen = BitConverter.ToUInt32(data, pos); pos += 4;
    }
    byte nameLen = data[pos]; pos += 1;
    if (endOffset == 0 && numProps == 0 && propListLen == 0 && nameLen == 0) return null;
    string name = Encoding.ASCII.GetString(data, pos, nameLen); pos += nameLen;

    FbxNode node = new FbxNode { Name = name };
    for (int i = 0; i < numProps; i++) node.Properties.Add(ReadProperty(data, ref pos));
    while (pos < endOffset)
    {
        FbxNode child = ReadNode(data, ref pos, is64);
        if (child == null) break;
        node.Children.Add(child);
    }
    pos = (int)endOffset;
    return node;
}

private static object ReadProperty(byte[] data, ref int pos)
{
    char type = (char)data[pos]; pos += 1;
    switch (type)
    {
        case 'Y': { short v = BitConverter.ToInt16(data, pos); pos += 2; return (int)v; }
        case 'C': { bool v = data[pos] != 0; pos += 1; return v; }
        case 'I': { int v = BitConverter.ToInt32(data, pos); pos += 4; return v; }
        case 'F': { float v = BitConverter.ToSingle(data, pos); pos += 4; return v; }
        case 'D': { double v = BitConverter.ToDouble(data, pos); pos += 8; return v; }
        case 'L': { long v = BitConverter.ToInt64(data, pos); pos += 8; return v; }
        case 'f': return ReadArray(data, ref pos, 4, true);
        case 'd': return ReadArray(data, ref pos, 8, true);
        case 'l': return ReadArray(data, ref pos, 8, false);
        case 'i': return ReadArray(data, ref pos, 4, false);
        case 'b': return ReadArray(data, ref pos, 1, false);
        case 'S': { int len = BitConverter.ToInt32(data, pos); pos += 4; string s = Encoding.UTF8.GetString(data, pos, len); pos += len; return s; }
        case 'R': { int len = BitConverter.ToInt32(data, pos); pos += 4; pos += len; return null; }
        default: throw new Exception("Неизвестный тип свойства FBX: " + type);
    }
}

private static object ReadArray(byte[] data, ref int pos, int elemSize, bool isFloat)
{
    int arrayLen = BitConverter.ToInt32(data, pos); pos += 4;
    int encoding = BitConverter.ToInt32(data, pos); pos += 4;
    int compressedLen = BitConverter.ToInt32(data, pos); pos += 4;

    byte[] raw;
    if (encoding == 1)
    {
        byte[] compressed = new byte[compressedLen];
        Array.Copy(data, pos, compressed, 0, compressedLen);
        pos += compressedLen;
        raw = ZlibDecompress(compressed);
    }
    else
    {
        raw = new byte[arrayLen * elemSize];
        Array.Copy(data, pos, raw, 0, raw.Length);
        pos += raw.Length;
    }

    if (elemSize == 8 && isFloat) { double[] r = new double[arrayLen]; for (int i = 0; i < arrayLen; i++) r[i] = BitConverter.ToDouble(raw, i * 8); return r; }
    if (elemSize == 4 && isFloat) { float[] r = new float[arrayLen]; for (int i = 0; i < arrayLen; i++) r[i] = BitConverter.ToSingle(raw, i * 4); return r; }
    if (elemSize == 4 && !isFloat) { int[] r = new int[arrayLen]; for (int i = 0; i < arrayLen; i++) r[i] = BitConverter.ToInt32(raw, i * 4); return r; }
    if (elemSize == 8 && !isFloat) { long[] r = new long[arrayLen]; for (int i = 0; i < arrayLen; i++) r[i] = BitConverter.ToInt64(raw, i * 8); return r; }
    return null;
}
private static byte[] ZlibDecompress(byte[] compressed)
{
    using (MemoryStream ms = new MemoryStream(compressed, 2, compressed.Length - 2))
    using (System.IO.Compression.DeflateStream ds = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionMode.Decompress))
    using (MemoryStream outMs = new MemoryStream())
    {
        byte[] buf = new byte[8192];
        int read;
        while ((read = ds.Read(buf, 0, buf.Length)) > 0) outMs.Write(buf, 0, read);
        return outMs.ToArray();
    }
}
}

// =========================================================================
// === 3D-ПРОСМОТРЩИК ЛИВРЕЙ (WPF Viewport3D в ElementHost) ================
// =========================================================================
public class Livery3DViewerForm : Form
{
    private System.Windows.Forms.Integration.ElementHost host;
    private System.Windows.Controls.Viewport3D viewport;
    private System.Windows.Media.Media3D.PerspectiveCamera camera;
    private System.Windows.Media.Media3D.GeometryModel3D model;
    private System.Windows.Media.ImageBrush textureBrush;
    private LiveryEntry entry;
    private System.Windows.Point lastMouse;
    private bool dragging = false;
    private double orbitYaw = 45, orbitPitch = 20, zoomDist = 4;

    private ComboBox cmbModelSelect;
    private System.Windows.Controls.TextBlock lblMissingModel;
    private System.Windows.Media.Media3D.ModelVisual3D modelVisual;
    private System.Windows.Media.Media3D.ModelVisual3D cloudsVisual;
    private System.Windows.Media.Media3D.ModelVisual3D rainVisual;
    private System.Windows.Media.Media3D.ModelVisual3D aircraftLightsVisual;
    private List<System.Windows.Media.SolidColorBrush> strobeBrushes = new List<System.Windows.Media.SolidColorBrush>();
    private List<System.Windows.Media.Media3D.PointLight> strobePointLights = new List<System.Windows.Media.Media3D.PointLight>();
    private System.Windows.Forms.Timer navLightTimer;
    private int strobePhaseTick = 0;
    private bool navLightsEnabled = true;
    private bool strobeLightsEnabled = true;
    private bool landingLightEnabled = true;
    private System.Windows.Media.Media3D.TranslateTransform3D rainDropTransform;
    private System.Windows.Media.SolidColorBrush rainMatBrush;
    private System.Windows.Media.Media3D.Model3DGroup lightGroup;
    private System.Windows.Controls.Canvas bgCanvas;
    private System.Windows.Controls.Canvas fgCanvas;
    private int lightPreset = 0; // 0 = Студия, 1 = Солнце, 2 = Ночь

    private class CloudBank3D
    {
        public double Yaw, Pitch, Dist, SpeedYaw, Scale;
        public System.Windows.Controls.Canvas Visual;
    }

    private class PassThroughCloud
    {
        public double Progress; // 0.0 -> 1.0 (пролёт мимо/сквозь самолёт)
        public double OffsetX, OffsetY, Speed, Scale;
        public System.Windows.Controls.Canvas Visual;
    }

    private List<CloudBank3D> activeCloudBanks = new List<CloudBank3D>();
    private List<PassThroughCloud> passClouds = new List<PassThroughCloud>();
    private System.Windows.Forms.Timer cloudAnimTimer;

    // Поля для ночной грозы (lightPreset == 3)
    private System.Windows.Media.Media3D.DirectionalLight stormFlashLight;
    private double lightningFlashAlpha = 0.0; // 0.0 -> 1.0 (яркость текущей вспышки)
    private int lightningPulsesLeft = 0;
    private double lightningYaw = 215.0;
    private double lightningPitch = 8.0;
    private int lightningTicksUntilNext = 90;
    private int lightningSeed = 1337;
    private double rainPhase = 0.0;




    public Livery3DViewerForm(LiveryEntry liveryEntry)
        {
            this.entry = liveryEntry;
        this.Text = "VTOL QoL — 3D просмотр ливреи: " + liveryEntry.Name;
        DarkWindowHelper.Apply(this);
        this.Size = new Size(860, 680);
        this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = Color.FromArgb(18, 22, 20);

            host = new System.Windows.Forms.Integration.ElementHost();
            host.Dock = DockStyle.Fill;
            this.Controls.Add(host);

        System.Windows.Controls.Grid grid = new System.Windows.Controls.Grid();
        grid.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(26, 31, 35));
        bgCanvas = new System.Windows.Controls.Canvas();
        bgCanvas.IsHitTestVisible = false;
        grid.Children.Add(bgCanvas);

        viewport = new System.Windows.Controls.Viewport3D();
        viewport.IsHitTestVisible = false;
        grid.Children.Add(viewport);

        fgCanvas = new System.Windows.Controls.Canvas();
        fgCanvas.IsHitTestVisible = false;
        grid.Children.Add(fgCanvas);

        grid.SizeChanged += (s, e) => RenderBackgroundDecor();
        host.Child = grid;



        Panel pnlBottom = new Panel();
            pnlBottom.Dock = DockStyle.Bottom;
            pnlBottom.Height = 42;
            pnlBottom.BackColor = Color.FromArgb(24, 28, 26);
            this.Controls.Add(pnlBottom);

            Button btnTexture = new Button() { Text = "Открыть свою текстуру", Left = 10, Top = 7, Width = 175, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 110, 140), ForeColor = Color.White };
            btnTexture.Click += (s, e) => OpenCustomTexture();
            pnlBottom.Controls.Add(btnTexture);

            Button btnReset = new Button() { Text = "Сбросить вид", Left = 195, Top = 7, Width = 110, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(40, 50, 45), ForeColor = Color.White };
            btnReset.Click += (s, e) => { orbitYaw = 45; orbitPitch = 20; zoomDist = 3.2; UpdateCamera(); };
            pnlBottom.Controls.Add(btnReset);

            Label lblModel = new Label() { Text = "Борт:", Left = 315, Top = 13, AutoSize = true, ForeColor = Color.LightGray };
            pnlBottom.Controls.Add(lblModel);

            cmbModelSelect = new ComboBox();
            cmbModelSelect.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbModelSelect.Location = new Point(360, 9);
            cmbModelSelect.Size = new Size(115, 24);
            cmbModelSelect.BackColor = Color.FromArgb(30, 36, 33);
            cmbModelSelect.ForeColor = Color.Cyan;
            string[] planes = new string[] { "FA-26B", "F-45A", "AV-42C", "AH-94", "T-55", "EF-24G" };
            foreach (string p in planes) cmbModelSelect.Items.Add(p);
            int foundIdx = Array.IndexOf(planes, entry.Aircraft);
            cmbModelSelect.SelectedIndex = foundIdx >= 0 ? foundIdx : 0;
        cmbModelSelect.SelectedIndexChanged += (s, e) => {
            entry.Aircraft = cmbModelSelect.SelectedItem.ToString();
            LiveryParser.SaveUserAircraftMapping(entry.FolderPath, entry.Aircraft);
            LoadModelAndTexture();
        };

        pnlBottom.Controls.Add(cmbModelSelect);

        Button btnUvLayer = new Button() { Text = "UV слой", Left = 485, Top = 7, Width = 80, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 75, 60), ForeColor = Color.White };
        btnUvLayer.Click += (s, e) => { FbxLoader.UseSecondUV = !FbxLoader.UseSecondUV; LoadModelAndTexture(); };
        pnlBottom.Controls.Add(btnUvLayer);

        Button btnFlipY = new Button() { Text = "Flip Y", Left = 572, Top = 7, Width = 70, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 75, 60), ForeColor = Color.White };
        btnFlipY.Click += (s, e) => { FbxLoader.FlipUVY = !FbxLoader.FlipUVY; LoadModelAndTexture(); };
        pnlBottom.Controls.Add(btnFlipY);

        Button btnLight = new Button() { Text = "☀ Свет", Left = 648, Top = 7, Width = 72, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(95, 75, 25), ForeColor = Color.White };
        btnLight.Click += (s, e) => { lightPreset = (lightPreset + 1) % 4; ApplyLightingPreset(); };
        pnlBottom.Controls.Add(btnLight);

        Button btnNavToggle = new Button() { Text = "NAV", Left = 726, Top = 7, Width = 46, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 75), ForeColor = Color.White };
        btnNavToggle.Click += (s, e) => {
            navLightsEnabled = !navLightsEnabled;
            btnNavToggle.BackColor = navLightsEnabled ? Color.FromArgb(0, 120, 75) : Color.FromArgb(40, 46, 44);
            if (lastLoadedMeshData != null) BuildAircraftLights(lastLoadedMeshData);
        };
        pnlBottom.Controls.Add(btnNavToggle);

        Button btnStrobeToggle = new Button() { Text = "STRB", Left = 776, Top = 7, Width = 50, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 75), ForeColor = Color.White };
        btnStrobeToggle.Click += (s, e) => {
            strobeLightsEnabled = !strobeLightsEnabled;
            btnStrobeToggle.BackColor = strobeLightsEnabled ? Color.FromArgb(0, 120, 75) : Color.FromArgb(40, 46, 44);
            if (lastLoadedMeshData != null) BuildAircraftLights(lastLoadedMeshData);
        };
        pnlBottom.Controls.Add(btnStrobeToggle);

        Button btnLandToggle = new Button() { Text = "LAND", Left = 830, Top = 7, Width = 52, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 75), ForeColor = Color.White };
        btnLandToggle.Click += (s, e) => {
            landingLightEnabled = !landingLightEnabled;
            btnLandToggle.BackColor = landingLightEnabled ? Color.FromArgb(0, 120, 75) : Color.FromArgb(40, 46, 44);
            if (lastLoadedMeshData != null) BuildAircraftLights(lastLoadedMeshData);
        };
        pnlBottom.Controls.Add(btnLandToggle);

        Label lblHint = new Label() { Text = "ЛКМ", Left = 888, Top = 13, AutoSize = true, ForeColor = Color.Silver };
        pnlBottom.Controls.Add(lblHint);



        grid.MouseLeftButtonDown += (s, e) => { dragging = true; lastMouse = e.GetPosition(grid); grid.CaptureMouse(); };
            grid.MouseLeftButtonUp += (s, e) => { dragging = false; grid.ReleaseMouseCapture(); };
            grid.MouseMove += (s, e) => {
                if (!dragging) return;
                System.Windows.Point pos = e.GetPosition(grid);
                orbitYaw -= (pos.X - lastMouse.X) * 0.45;
                orbitPitch += (pos.Y - lastMouse.Y) * 0.45;
                if (orbitPitch > 89) orbitPitch = 89;
                if (orbitPitch < -89) orbitPitch = -89;
                lastMouse = pos;
                UpdateCamera();
            };
            grid.MouseWheel += (s, e) => {
                zoomDist -= e.Delta / 240.0;
                if (zoomDist < 0.8) zoomDist = 0.8;
                if (zoomDist > 30) zoomDist = 30;
                UpdateCamera();
            };

        this.FormClosing += (s, e) => {
            if (cloudAnimTimer != null) { cloudAnimTimer.Stop(); cloudAnimTimer.Dispose(); cloudAnimTimer = null; }
            if (navLightTimer != null) { navLightTimer.Stop(); navLightTimer.Dispose(); navLightTimer = null; }
        };

        InitScene();
        entry.Aircraft = cmbModelSelect.SelectedItem.ToString();
        LoadModelAndTexture();
    }

    private void InitScene()
    {
        camera = new System.Windows.Media.Media3D.PerspectiveCamera();
        camera.FieldOfView = 45;
        viewport.Camera = camera;

        System.Windows.Media.Media3D.ModelVisual3D lightVisual = new System.Windows.Media.Media3D.ModelVisual3D();
        lightGroup = new System.Windows.Media.Media3D.Model3DGroup();
        lightVisual.Content = lightGroup;
        viewport.Children.Add(lightVisual);

        ApplyLightingPreset();
        UpdateCamera();
    }

    private void ApplyLightingPreset()
    {
        if (lightGroup == null) return;
        lightGroup.Children.Clear();
        if (fgCanvas != null) fgCanvas.Children.Clear();
        System.Windows.Controls.Grid grid = (System.Windows.Controls.Grid)host.Child;

        if (lightPreset == 0)
        {
            System.Windows.Media.RadialGradientBrush rad = new System.Windows.Media.RadialGradientBrush(
                System.Windows.Media.Color.FromRgb(34, 42, 46),
                System.Windows.Media.Color.FromRgb(18, 22, 24));
            grid.Background = rad;

            lightGroup.Children.Add(new System.Windows.Media.Media3D.AmbientLight(System.Windows.Media.Color.FromRgb(95, 102, 110)));
            lightGroup.Children.Add(new System.Windows.Media.Media3D.DirectionalLight(
                System.Windows.Media.Color.FromRgb(255, 248, 235),
                new System.Windows.Media.Media3D.Vector3D(-1.2, -2.0, -1.5)));
            lightGroup.Children.Add(new System.Windows.Media.Media3D.DirectionalLight(
                System.Windows.Media.Color.FromRgb(150, 180, 220),
                new System.Windows.Media.Media3D.Vector3D(1.8, -0.5, 1.0)));
            lightGroup.Children.Add(new System.Windows.Media.Media3D.DirectionalLight(
                System.Windows.Media.Color.FromRgb(130, 145, 165),
                new System.Windows.Media.Media3D.Vector3D(0.3, 1.4, 1.8)));
        }
        else if (lightPreset == 1)
        {
            System.Windows.Media.LinearGradientBrush sky = new System.Windows.Media.LinearGradientBrush();
            sky.StartPoint = new System.Windows.Point(0.5, 0);
            sky.EndPoint = new System.Windows.Point(0.5, 1);
            sky.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(16, 44, 84), 0.0));
            sky.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(42, 92, 148), 0.52));
            sky.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(118, 162, 202), 0.85));
            sky.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(176, 198, 214), 1.0));
            grid.Background = sky;


            lightGroup.Children.Add(new System.Windows.Media.Media3D.AmbientLight(System.Windows.Media.Color.FromRgb(90, 105, 125)));
            lightGroup.Children.Add(new System.Windows.Media.Media3D.DirectionalLight(
                System.Windows.Media.Color.FromRgb(255, 244, 214),
                new System.Windows.Media.Media3D.Vector3D(1.5, -2.2, -1.2)));
            lightGroup.Children.Add(new System.Windows.Media.Media3D.DirectionalLight(
                System.Windows.Media.Color.FromRgb(125, 165, 215),
                new System.Windows.Media.Media3D.Vector3D(-1.2, -0.8, 1.4)));
            lightGroup.Children.Add(new System.Windows.Media.Media3D.DirectionalLight(
                System.Windows.Media.Color.FromRgb(105, 100, 90),
                new System.Windows.Media.Media3D.Vector3D(0.0, 2.0, 0.0)));
        }
        else if (lightPreset == 2)
        {
            System.Windows.Media.LinearGradientBrush night = new System.Windows.Media.LinearGradientBrush();
            night.StartPoint = new System.Windows.Point(0.5, 0);
            night.EndPoint = new System.Windows.Point(0.5, 1);
            night.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(7, 10, 18), 0.0));
            night.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(14, 22, 38), 1.0));
            grid.Background = night;

            lightGroup.Children.Add(new System.Windows.Media.Media3D.AmbientLight(System.Windows.Media.Color.FromRgb(42, 52, 68)));
            lightGroup.Children.Add(new System.Windows.Media.Media3D.DirectionalLight(
                System.Windows.Media.Color.FromRgb(195, 220, 255),
                new System.Windows.Media.Media3D.Vector3D(-1.2, -2.2, -1.0)));
            lightGroup.Children.Add(new System.Windows.Media.Media3D.DirectionalLight(
                System.Windows.Media.Color.FromRgb(70, 110, 160),
                new System.Windows.Media.Media3D.Vector3D(1.5, 0.5, 1.5)));
        }
        else
        {
            // 3: Ночная гроза (свинцовое небо + вспышки 3D-молний на самолёте)
            System.Windows.Media.LinearGradientBrush stormSky = new System.Windows.Media.LinearGradientBrush();
            stormSky.StartPoint = new System.Windows.Point(0.5, 0);
            stormSky.EndPoint = new System.Windows.Point(0.5, 1);
            stormSky.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(9, 12, 19), 0.0));
            stormSky.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(18, 25, 36), 0.65));
            stormSky.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(26, 35, 48), 1.0));
            grid.Background = stormSky;

            lightGroup.Children.Add(new System.Windows.Media.Media3D.AmbientLight(System.Windows.Media.Color.FromRgb(32, 40, 52)));
            lightGroup.Children.Add(new System.Windows.Media.Media3D.DirectionalLight(
                System.Windows.Media.Color.FromRgb(95, 125, 165),
                new System.Windows.Media.Media3D.Vector3D(-1.0, -2.0, -1.0)));
            lightGroup.Children.Add(new System.Windows.Media.Media3D.DirectionalLight(
                System.Windows.Media.Color.FromRgb(45, 65, 95),
                new System.Windows.Media.Media3D.Vector3D(1.5, 0.8, 1.2)));

            // Динамический источник света от вспышки молнии (в обычное время выключен = 0,0,0)
            stormFlashLight = new System.Windows.Media.Media3D.DirectionalLight(
                System.Windows.Media.Color.FromRgb(0, 0, 0),
                new System.Windows.Media.Media3D.Vector3D(0.5, -1.5, -1.0));
            lightGroup.Children.Add(stormFlashLight);
        }
        if (lastLoadedMeshData != null)
            BuildAircraftLights(lastLoadedMeshData);
        UpdateVolumetricClouds();
        RenderBackgroundDecor();

    }
    private System.Windows.Controls.Canvas BuildCumulusCloudElement(double scale, bool isPassThrough, bool isStorm = false)
    {
        System.Windows.Controls.Canvas cloud = new System.Windows.Controls.Canvas();
        cloud.Width = 960 * scale;
        cloud.Height = 380 * scale;
        cloud.IsHitTestVisible = false;

        // Широкий многослойный облачный фронт (28 пересекающихся клубов без резких границ "отдельных плюшек")
        double[,] puffs = new double[,]
        {
            // Нижняя плотная теневая основа (широкое основание гряды)
            { 160, 255, 190, 72, 1, 135 },
            { 310, 262, 220, 78, 1, 150 },
            { 480, 265, 240, 82, 1, 155 },
            { 650, 260, 215, 76, 1, 145 },
            { 800, 252, 185, 68, 1, 130 },
            { 240, 240, 210, 75, 1, 130 },
            { 400, 242, 230, 80, 1, 140 },
            { 570, 242, 225, 78, 1, 140 },
            { 730, 240, 195, 72, 1, 130 },

            // Широкая средняя облачная пелена (соединяет всё в единый массив)
            { 130, 215, 180, 95,  0, 175 },
            { 250, 195, 210, 115, 0, 205 },
            { 390, 180, 235, 130, 0, 225 },
            { 540, 182, 240, 132, 0, 225 },
            { 685, 195, 210, 115, 0, 205 },
            { 820, 215, 175, 92,  0, 170 },
            { 320, 205, 220, 118, 0, 210 },
            { 470, 202, 245, 125, 0, 220 },
            { 615, 205, 220, 118, 0, 210 },

            // Верхние пушистые кучевые гребни
            { 220, 155, 145, 95,  0, 205 },
            { 330, 130, 165, 112, 0, 230 },
            { 450, 112, 185, 125, 0, 242 },
            { 565, 122, 175, 118, 0, 235 },
            { 670, 148, 150, 98,  0, 210 },
            { 400, 145, 170, 108, 0, 225 },
            { 515, 142, 175, 110, 0, 228 },
            { 485, 88,  130, 92,  0, 225 },
            { 375, 102, 120, 86,  0, 210 },
            { 595, 100, 125, 88,  0, 212 }
        };

        double alphaMul = isPassThrough ? 0.58 : 1.0;

        for (int i = 0; i < puffs.GetLength(0); i++)
        {
            double cx = puffs[i, 0] * scale;
            double cy = puffs[i, 1] * scale;
            double rx = puffs[i, 2] * scale;
            double ry = puffs[i, 3] * scale;
            bool isShadow = (puffs[i, 4] > 0.5);
            byte maxAlpha = (byte)(puffs[i, 5] * alphaMul);

            byte r = isStorm ? (isShadow ? (byte)16 : (byte)36) : (isShadow ? (byte)138 : (byte)255);
            byte g = isStorm ? (isShadow ? (byte)21 : (byte)45) : (isShadow ? (byte)162 : (byte)254);
            byte b = isStorm ? (isShadow ? (byte)30 : (byte)62) : (isShadow ? (byte)192 : (byte)252);


            System.Windows.Media.RadialGradientBrush rg = new System.Windows.Media.RadialGradientBrush();
            rg.GradientOrigin = new System.Windows.Point(0.45, 0.35);
            rg.Center = new System.Windows.Point(0.5, 0.5);
            rg.GradientStops.Add(new System.Windows.Media.GradientStop(
                System.Windows.Media.Color.FromArgb(maxAlpha, r, g, b), 0.0));
            rg.GradientStops.Add(new System.Windows.Media.GradientStop(
                System.Windows.Media.Color.FromArgb((byte)(maxAlpha * 0.58), r, g, b), 0.45));
            rg.GradientStops.Add(new System.Windows.Media.GradientStop(
                System.Windows.Media.Color.FromArgb((byte)(maxAlpha * 0.16), r, g, b), 0.76));
            rg.GradientStops.Add(new System.Windows.Media.GradientStop(
                System.Windows.Media.Color.FromArgb(0, r, g, b), 1.0));
            rg.Freeze();

            System.Windows.Shapes.Ellipse el = new System.Windows.Shapes.Ellipse()
            {
                Width = rx * 2.0,
                Height = ry * 2.0,
                Fill = rg
            };
            System.Windows.Controls.Canvas.SetLeft(el, cx - rx);
            System.Windows.Controls.Canvas.SetTop(el, cy - ry);
            cloud.Children.Add(el);
        }

        return cloud;
    }

    private void UpdateVolumetricClouds()
    {
        if (cloudAnimTimer != null)
        {
            cloudAnimTimer.Stop();
            cloudAnimTimer.Dispose();
            cloudAnimTimer = null;
        }
        activeCloudBanks.Clear();
        passClouds.Clear();
        if (fgCanvas != null) fgCanvas.Children.Clear();

        if (viewport != null && cloudsVisual != null)
        {
            viewport.Children.Remove(cloudsVisual);
            cloudsVisual = null;
        }
        if (viewport != null && rainVisual != null)
        {
            viewport.Children.Remove(rainVisual);
            rainVisual = null;
            rainDropTransform = null;
            rainMatBrush = null;
        }

        if (lightPreset != 1 && lightPreset != 3) return;
        bool isStorm = (lightPreset == 3);

        if (isStorm && viewport != null)
            Build3DRainVolume();


        // Сплошные перекрывающиеся облачные гряды по всему небу (от горизонта -4° до зенита +62°)
        double[,] presets = new double[,]
        {
            // Нижний сплошной облачный горизонт (перекрываются каждые ~30°)
            {   0.0, -3.5, 1.35, 0.035, 1.0 },
            {  32.0, -2.0, 1.45, 0.038, 1.0 },
            {  65.0, -4.0, 1.40, 0.032, 1.0 },
            {  98.0, -1.5, 1.50, 0.040, 1.0 },
            { 130.0, -3.5, 1.35, 0.034, 1.0 },
            { 162.0, -2.0, 1.45, 0.036, 1.0 },
            { 195.0, -4.2, 1.55, 0.030, 1.0 },
            { 228.0, -1.8, 1.40, 0.038, 1.0 },
            { 260.0, -3.5, 1.45, 0.033, 1.0 },
            { 292.0, -2.2, 1.50, 0.037, 1.0 },
            { 325.0, -3.8, 1.40, 0.035, 1.0 },

            // Средний ярус облачных полей (12°..26° высоты)
            {  18.0, 14.0, 1.25, 0.052, 1.1 },
            {  75.0, 19.0, 1.30, 0.055, 1.1 },
            { 135.0, 13.0, 1.25, 0.050, 1.1 },
            { 195.0, 21.0, 1.35, 0.058, 1.1 },
            { 255.0, 15.0, 1.25, 0.052, 1.1 },
            { 312.0, 20.0, 1.30, 0.055, 1.1 },

            // Верхний ярус над головой (36°..62° высоты — плотное небо при взгляде снизу вверх)
            {  25.0, 38.0, 1.20, 0.068, 1.2 },
            {  95.0, 46.0, 1.25, 0.072, 1.2 },
            { 165.0, 36.0, 1.20, 0.066, 1.2 },
            { 235.0, 52.0, 1.30, 0.075, 1.2 },
            { 305.0, 42.0, 1.22, 0.070, 1.2 },
            { 120.0, 62.0, 1.35, 0.080, 1.3 }
        };

        for (int i = 0; i < presets.GetLength(0); i++)
        {
            double sc = presets[i, 2];
            activeCloudBanks.Add(new CloudBank3D
            {
                Yaw = presets[i, 0],
                Pitch = presets[i, 1],
                Scale = sc,
                SpeedYaw = presets[i, 3],
                Dist = presets[i, 4],
                Visual = BuildCumulusCloudElement(sc, false, isStorm)
            });
        }

        if (!isStorm)
        {
            passClouds.Add(new PassThroughCloud
            {
                Progress = -1.2,
                OffsetX = 65,
                OffsetY = -25,
                Speed = 0.0022,
                Scale = 1.05,
                Visual = BuildCumulusCloudElement(1.05, true, false)
            });
        }



        cloudAnimTimer = new System.Windows.Forms.Timer();
        cloudAnimTimer.Interval = 33;
        cloudAnimTimer.Tick += (s, e) => SortAndSyncClouds(true);
        cloudAnimTimer.Start();
    }

    private void SortAndSyncClouds(bool advanceMovement)
    {
        if (lightPreset != 1 && lightPreset != 3)
        {
            if (fgCanvas != null && fgCanvas.Children.Count > 0) fgCanvas.Children.Clear();
            return;
        }

        for (int i = 0; i < activeCloudBanks.Count; i++)
        {
            if (advanceMovement)
                activeCloudBanks[i].Yaw = (activeCloudBanks[i].Yaw + activeCloudBanks[i].SpeedYaw * (lightPreset == 3 ? 1.6 : 1.0)) % 360.0;
        }

        if (lightPreset == 3 && advanceMovement)
        {
            rainPhase = (rainPhase + 22.0) % 1000.0;
            if (lightningFlashAlpha > 0.0)
            {
                lightningFlashAlpha -= 0.16;
                if (lightningFlashAlpha <= 0.25 && lightningPulsesLeft > 0)
                {
                    lightningPulsesLeft--;
                    lightningFlashAlpha = 0.88; // однократный повторный импульс без зацикливания
                }
                if (lightningFlashAlpha < 0.0) lightningFlashAlpha = 0.0;
            }
            else
            {
                lightningTicksUntilNext--;
                if (lightningTicksUntilNext <= 0)
                {
                    Random rnd = new Random(Environment.TickCount);
                    lightningFlashAlpha = 1.0;
                    lightningPulsesLeft = rnd.Next(0, 2); // 0 или 1 повторная вспышка
                    lightningSeed = rnd.Next(1, 99999);
                    lightningYaw = (orbitYaw + 180.0 + (rnd.NextDouble() * 140.0 - 70.0) + 360.0) % 360.0;
                    lightningPitch = 6.0 + rnd.NextDouble() * 22.0;
                    lightningTicksUntilNext = 90 + rnd.Next(130);
                }
            }


            if (stormFlashLight != null)
            {
                byte flashVal = (byte)(lightningFlashAlpha * 235);
                byte flashBlue = (byte)(lightningFlashAlpha * 255);
                stormFlashLight.Color = System.Windows.Media.Color.FromRgb(flashVal, (byte)(flashVal * 0.96), flashBlue);
                double rad = lightningYaw * Math.PI / 180.0;
                stormFlashLight.Direction = new System.Windows.Media.Media3D.Vector3D(-Math.Sin(rad), -1.2, -Math.Cos(rad));
            }
        }

        if (advanceMovement)
        {
            RenderBackgroundDecor();
            if (lightPreset == 1) RenderPassThroughClouds();
            else if (lightPreset == 3) Step3DRain();
        }

    }

    private void RenderPassThroughClouds()
    {
        if (fgCanvas == null) return;
        fgCanvas.Children.Clear();
        if (lightPreset != 1 || passClouds.Count == 0) return;

        double w = fgCanvas.ActualWidth > 0 ? fgCanvas.ActualWidth : 840;
        double h = fgCanvas.ActualHeight > 0 ? fgCanvas.ActualHeight : 600;

        for (int i = 0; i < passClouds.Count; i++)
        {
            PassThroughCloud pc = passClouds[i];
            pc.Progress += pc.Speed;
            if (pc.Progress > 1.0)
            {
                // Случайная пауза ~20-40 секунд до следующего пролёта
                pc.Progress = -1.4 - ((DateTime.UtcNow.Ticks % 150) / 100.0);
                pc.OffsetX = ((DateTime.UtcNow.Millisecond % 180) - 90);
                pc.OffsetY = ((DateTime.UtcNow.Second % 90) - 45);
                continue;
            }
            if (pc.Progress < 0.0) continue;

            double t = pc.Progress;
            double perspScale = 0.65 + t * 0.95;

            // Лёгкая, ненавязчивая полупрозрачность (0.38 вместо 0.85), чтобы самолёт всегда хорошо читался
            double opacity = Math.Sin(t * Math.PI);
            opacity = Math.Pow(Math.Max(0, opacity), 0.85) * 0.38;


            double cx = w * 0.5 + pc.OffsetX + (0.5 - t) * (w * 0.65);
            double cy = h * 0.5 + pc.OffsetY + (t - 0.5) * (h * 0.35);

            pc.Visual.Opacity = opacity;
            pc.Visual.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            pc.Visual.RenderTransform = new System.Windows.Media.ScaleTransform(perspScale, perspScale);

            System.Windows.Controls.Canvas.SetLeft(pc.Visual, cx - pc.Visual.Width * 0.5);
            System.Windows.Controls.Canvas.SetTop(pc.Visual, cy - pc.Visual.Height * 0.5);
            fgCanvas.Children.Add(pc.Visual);
        }
    }

    private double WrapAngle180(double deg)
    {
        deg = (deg + 180.0) % 360.0;
        if (deg < 0) deg += 360.0;
        return deg - 180.0;
    }

    private bool ProjectSkyObject(double objYaw, double objPitch, double w, double h, out double sx, out double sy)
    {
        double camLookYaw = orbitYaw + 180.0;
        double camLookPitch = -orbitPitch;

        double dYaw = WrapAngle180(objYaw - camLookYaw);
        double dPitch = objPitch - camLookPitch;

        double pxPerDeg = h / 52.0;
        sx = w * 0.5 - dYaw * pxPerDeg;
        sy = h * 0.5 - dPitch * pxPerDeg;

        return (sx > -750 && sx < w + 750 && sy > -550 && sy < h + 550);
    }


    private void RenderBackgroundDecor()
    {
        if (bgCanvas == null) return;
        bgCanvas.Children.Clear();
        double w = bgCanvas.ActualWidth > 0 ? bgCanvas.ActualWidth : 840;
        double h = bgCanvas.ActualHeight > 0 ? bgCanvas.ActualHeight : 600;

        if (lightPreset == 0)
        {
            System.Windows.Media.Brush gridBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(24, 0, 255, 140));
            double step = 48.0;
            double offX = ((orbitYaw * 4.5) % step + step) % step;
            double offY = ((orbitPitch * 4.5) % step + step) % step;


            for (double x = offX; x < w; x += step)
            {
                System.Windows.Shapes.Line ln = new System.Windows.Shapes.Line() { X1 = x, Y1 = 0, X2 = x, Y2 = h, Stroke = gridBrush, StrokeThickness = 1 };
                bgCanvas.Children.Add(ln);
            }
            for (double y = offY; y < h; y += step)
            {
                System.Windows.Shapes.Line ln = new System.Windows.Shapes.Line() { X1 = 0, Y1 = y, X2 = w, Y2 = y, Stroke = gridBrush, StrokeThickness = 1 };
                bgCanvas.Children.Add(ln);
            }

            double cx = w / 2.0 + WrapAngle180(-orbitYaw) * 0.35;
            double cy = h / 2.0 + orbitPitch * 0.35;
            System.Windows.Media.Brush ringBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(38, 0, 255, 140));
            double[] radii = new double[] { 145, 245 };
            foreach (double r in radii)
            {
                System.Windows.Shapes.Ellipse el = new System.Windows.Shapes.Ellipse()
                {
                    Width = r * 2,
                    Height = r * 2,
                    Stroke = ringBrush,
                    StrokeThickness = 1
                };
                System.Windows.Controls.Canvas.SetLeft(el, cx - r);
                System.Windows.Controls.Canvas.SetTop(el, cy - r);
                bgCanvas.Children.Add(el);
            }
        }
        else if (lightPreset == 1)
        {
            // 1. Атмосферная дымка и нижняя облачная/морская пелена ниже линии горизонта
            double hx, hy;
            ProjectSkyObject(orbitYaw + 180.0, -3.0, w, h, out hx, out hy);
            if (hy < h + 120)
            {
                double deckTop = Math.Max(-100, hy);
                System.Windows.Media.LinearGradientBrush haze = new System.Windows.Media.LinearGradientBrush();
                haze.StartPoint = new System.Windows.Point(0.5, 0);
                haze.EndPoint = new System.Windows.Point(0.5, 1);
                haze.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0, 195, 215, 230), 0.0));
                haze.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(145, 185, 206, 222), 0.25));
                haze.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(215, 130, 158, 184), 1.0));

                System.Windows.Shapes.Rectangle groundDeck = new System.Windows.Shapes.Rectangle()
                {
                    Width = w + 4,
                    Height = Math.Max(0, h - deckTop + 100),
                    Fill = haze
                };
                System.Windows.Controls.Canvas.SetLeft(groundDeck, -2);
                System.Windows.Controls.Canvas.SetTop(groundDeck, deckTop);
                bgCanvas.Children.Add(groundDeck);
            }

            // 2. Всегда считаем экранную проекцию солнца (210°, 14°) без жёсткой обрезки по краям окна
            double sx, sy;
            ProjectSkyObject(210.0, 14.0, w, h, out sx, out sy);
            double sunAngleFromView = Math.Abs(WrapAngle180(210.0 - (orbitYaw + 180.0)));
            double sunVisibility = Math.Max(0.0, Math.Min(1.0, (140.0 - sunAngleFromView) / 45.0));

            double sunOcclusion = 0.0;
            for (int i = 0; i < activeCloudBanks.Count; i++)
            {
                CloudBank3D cb = activeCloudBanks[i];
                double dy = WrapAngle180(cb.Yaw - 210.0);
                double dp = cb.Pitch - 14.0;
                double d = Math.Sqrt(dy * dy + dp * dp);
                if (d < 22.0)
                    sunOcclusion = Math.Max(sunOcclusion, 1.0 - (d / 22.0));
            }

            if (sunVisibility > 0.01 && sx > -1200 && sx < w + 1200 && sy > -1200 && sy < h + 1200)
            {
                byte atmoAlpha = (byte)((140 + sunOcclusion * 55) * sunVisibility);
                System.Windows.Media.RadialGradientBrush atmoGlow = new System.Windows.Media.RadialGradientBrush();
                atmoGlow.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(atmoAlpha, 255, 246, 225), 0.0));
                atmoGlow.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb((byte)((60 + sunOcclusion * 45) * sunVisibility), 255, 232, 190), 0.35));
                atmoGlow.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb((byte)(18 * sunVisibility), 220, 235, 255), 0.70));
                atmoGlow.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0, 220, 235, 255), 1.0));

                System.Windows.Shapes.Ellipse bigGlow = new System.Windows.Shapes.Ellipse() { Width = 640, Height = 640, Fill = atmoGlow };
                System.Windows.Controls.Canvas.SetLeft(bigGlow, sx - 320);
                System.Windows.Controls.Canvas.SetTop(bigGlow, sy - 320);
                bgCanvas.Children.Add(bigGlow);

                byte coreAlpha = (byte)((255 - sunOcclusion * 90) * sunVisibility);
                System.Windows.Media.RadialGradientBrush coreBrush = new System.Windows.Media.RadialGradientBrush();
                coreBrush.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(coreAlpha, 255, 255, 255), 0.0));
                coreBrush.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb((byte)(coreAlpha * 0.85), 255, 252, 238), 0.35));
                coreBrush.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0, 255, 245, 210), 1.0));

                System.Windows.Shapes.Ellipse sunCore = new System.Windows.Shapes.Ellipse() { Width = 58, Height = 58, Fill = coreBrush };
                System.Windows.Controls.Canvas.SetLeft(sunCore, sx - 29);
                System.Windows.Controls.Canvas.SetTop(sunCore, sy - 29);
                bgCanvas.Children.Add(sunCore);
            }

            // 3. Облака + подповерхностное рассеивание (Silver Lining)
            List<System.Windows.Rect> cloudScreenBounds = new List<System.Windows.Rect>();

            for (int i = 0; i < activeCloudBanks.Count; i++)
            {
                CloudBank3D cb = activeCloudBanks[i];
                double cx, cy;
                if (ProjectSkyObject(cb.Yaw, cb.Pitch, w, h, out cx, out cy))
                {
                    // Сохраняем экранный контур плотного ядра облака для расчёта щелей
                    double coreW = cb.Visual.Width * 0.56;
                    double coreH = cb.Visual.Height * 0.44;
                    cloudScreenBounds.Add(new System.Windows.Rect(cx - coreW * 0.5, cy - coreH * 0.5, coreW, coreH));

                    double dYawSun = WrapAngle180(cb.Yaw - 210.0);
                    double dPitchSun = cb.Pitch - 14.0;
                    double angDist = Math.Sqrt(dYawSun * dYawSun + dPitchSun * dPitchSun);

                    if (angDist < 65.0)
                    {
                        double scatter = 1.0 - (angDist / 65.0);
                        byte glowAlpha = (byte)(scatter * scatter * 155);

                        System.Windows.Media.RadialGradientBrush scatterBrush = new System.Windows.Media.RadialGradientBrush();
                        scatterBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                            System.Windows.Media.Color.FromArgb(glowAlpha, 255, 249, 226), 0.0));
                        scatterBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                            System.Windows.Media.Color.FromArgb((byte)(glowAlpha * 0.55), 255, 236, 185), 0.50));
                        scatterBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                            System.Windows.Media.Color.FromArgb(0, 255, 236, 185), 1.0));

                        double sw = cb.Visual.Width * 1.18;
                        double sh = cb.Visual.Height * 1.18;
                        System.Windows.Shapes.Ellipse scatterGlow = new System.Windows.Shapes.Ellipse()
                        {
                            Width = sw,
                            Height = sh,
                            Fill = scatterBrush
                        };
                        System.Windows.Controls.Canvas.SetLeft(scatterGlow, cx - sw * 0.5);
                        System.Windows.Controls.Canvas.SetTop(scatterGlow, cy - sh * 0.5);
                        bgCanvas.Children.Add(scatterGlow);
                    }

                    System.Windows.Controls.Canvas.SetLeft(cb.Visual, cx - cb.Visual.Width * 0.5);
                    System.Windows.Controls.Canvas.SetTop(cb.Visual, cy - cb.Visual.Height * 0.5);
                    bgCanvas.Children.Add(cb.Visual);
                }
            }

            // 4. Плавные переливающиеся саншафты (мягкие световые конусы с рассеиванием вдоль и поперёк луча)
            if (sunVisibility > 0.02 && cloudScreenBounds.Count > 0)
            {
                System.Windows.Controls.Canvas shaftsLayer = new System.Windows.Controls.Canvas();
                shaftsLayer.Width = w;
                shaftsLayer.Height = h;
                shaftsLayer.IsHitTestVisible = false;
                shaftsLayer.Effect = new System.Windows.Media.Effects.BlurEffect() { Radius = 28.0 };

                int numRays = 72;
                double maxRayLen = Math.Sqrt(w * w + h * h) * 1.25;

                for (int r = 0; r < numRays; r++)
                {
                    double theta = (2.0 * Math.PI * r) / numRays;
                    double dirX = Math.Cos(theta);
                    double dirY = Math.Sin(theta);

                    double blockScore = 0.0;
                    double edgeProximity = 0.0;

                    for (int step = 1; step <= 7; step++)
                    {
                        double dist = step * 55.0;
                        double px = sx + dirX * dist;
                        double py = sy + dirY * dist;

                        for (int c = 0; c < cloudScreenBounds.Count; c++)
                        {
                            System.Windows.Rect rect = cloudScreenBounds[c];
                            double rcx = rect.X + rect.Width * 0.5;
                            double rcy = rect.Y + rect.Height * 0.5;
                            double nx = (px - rcx) / (rect.Width * 0.5);
                            double ny = (py - rcy) / (rect.Height * 0.5);
                            double normDist = Math.Sqrt(nx * nx + ny * ny);

                            if (normDist < 0.72)
                            {
                                blockScore += (0.72 - normDist) * 1.6;
                            }
                            else if (normDist >= 0.72 && normDist < 1.45)
                            {
                                double edgeFactor = 1.0 - Math.Abs(normDist - 1.02) / 0.45;
                                if (edgeFactor > 0) edgeProximity = Math.Max(edgeProximity, edgeFactor);
                            }
                        }
                    }

                    double intensity = edgeProximity * Math.Max(0.0, 1.0 - blockScore) * sunVisibility;
                    if (intensity > 0.14)
                    {
                        double angleDeg = theta * 180.0 / Math.PI;
                        double beamLen = maxRayLen * (0.78 + 0.22 * intensity);
                        double beamHeight = 76.0 + intensity * 115.0;

                        System.Windows.Media.RadialGradientBrush softBeamBrush = new System.Windows.Media.RadialGradientBrush();
                        softBeamBrush.GradientOrigin = new System.Windows.Point(0.04, 0.5);
                        softBeamBrush.Center = new System.Windows.Point(0.18, 0.5);
                        softBeamBrush.RadiusX = 0.85;
                        softBeamBrush.RadiusY = 0.50;

                        byte aCore = (byte)Math.Min(46, intensity * 44);
                        byte aMid = (byte)(aCore * 0.48);
                        byte aEdge = (byte)(aCore * 0.16);

                        softBeamBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                            System.Windows.Media.Color.FromArgb(aCore, 255, 250, 226), 0.0));
                        softBeamBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                            System.Windows.Media.Color.FromArgb(aMid, 255, 243, 204), 0.38));
                        softBeamBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                            System.Windows.Media.Color.FromArgb(aEdge, 240, 244, 255), 0.72));
                        softBeamBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                            System.Windows.Media.Color.FromArgb(0, 240, 244, 255), 1.0));

                        System.Windows.Shapes.Ellipse beamEllipse = new System.Windows.Shapes.Ellipse()
                        {
                            Width = beamLen,
                            Height = beamHeight,
                            Fill = softBeamBrush,
                            RenderTransformOrigin = new System.Windows.Point(0.0, 0.5),
                            RenderTransform = new System.Windows.Media.RotateTransform(angleDeg)
                        };

                        System.Windows.Controls.Canvas.SetLeft(beamEllipse, sx);
                        System.Windows.Controls.Canvas.SetTop(beamEllipse, sy - beamHeight * 0.5);
                        shaftsLayer.Children.Add(beamEllipse);
                    }
                }

                if (shaftsLayer.Children.Count > 0)
                    bgCanvas.Children.Add(shaftsLayer);
            }
        }

        else if (lightPreset == 2)
        {
            Random rnd = new Random(42667);

            for (int i = 0; i < 260; i++)
            {
                double starYaw = rnd.NextDouble() * 360.0;
                double starPitch = (rnd.NextDouble() * 170.0) - 85.0;
                double sz = (i % 9 == 0) ? 2.7 : ((i % 3 == 0) ? 1.8 : 1.1);
                byte alpha = (byte)(120 + rnd.Next(135));
                byte blueTint = (byte)(i % 4 == 0 ? 255 : 225);

                double sx, sy;
                if (ProjectSkyObject(starYaw, starPitch, w, h, out sx, out sy))
                {
                    System.Windows.Shapes.Ellipse star = new System.Windows.Shapes.Ellipse()
                    {
                        Width = sz,
                        Height = sz,
                        Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, 225, 235, blueTint))
                    };
                    System.Windows.Controls.Canvas.SetLeft(star, sx);
                    System.Windows.Controls.Canvas.SetTop(star, sy);
                    bgCanvas.Children.Add(star);
                }
            }

            double mx, my;
            if (ProjectSkyObject(242.0, 15.0, w, h, out mx, out my))
            {
                System.Windows.Media.RadialGradientBrush moonGlow = new System.Windows.Media.RadialGradientBrush();
                moonGlow.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(95, 170, 205, 255), 0.0));
                moonGlow.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0, 170, 205, 255), 1.0));
                System.Windows.Shapes.Ellipse glow = new System.Windows.Shapes.Ellipse() { Width = 170, Height = 170, Fill = moonGlow };
                System.Windows.Controls.Canvas.SetLeft(glow, mx - 85);
                System.Windows.Controls.Canvas.SetTop(glow, my - 85);
                bgCanvas.Children.Add(glow);

                System.Windows.Shapes.Ellipse moon = new System.Windows.Shapes.Ellipse()
                {
                    Width = 44,
                    Height = 44,
                    Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 232, 250))
                };
                System.Windows.Controls.Canvas.SetLeft(moon, mx - 22);
                System.Windows.Controls.Canvas.SetTop(moon, my - 22);
                bgCanvas.Children.Add(moon);
            }
        }
        else
        {
            double lx = 0, ly = 0;
            bool flashVisible = (lightningFlashAlpha > 0.02) && ProjectSkyObject(lightningYaw, lightningPitch, w, h, out lx, out ly);

            // 1. Глубокое сине-фиолетовое свечение внутри туч (как на фотореференсе)
            if (flashVisible)
            {
                System.Windows.Media.RadialGradientBrush deepBlueGlow = new System.Windows.Media.RadialGradientBrush();
                deepBlueGlow.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb((byte)(lightningFlashAlpha * 235), 165, 195, 255), 0.0));
                deepBlueGlow.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb((byte)(lightningFlashAlpha * 185), 55, 105, 245), 0.30));
                deepBlueGlow.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb((byte)(lightningFlashAlpha * 95), 25, 55, 175), 0.65));
                deepBlueGlow.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(0, 15, 30, 110), 1.0));

                System.Windows.Shapes.Ellipse flashHalo = new System.Windows.Shapes.Ellipse() { Width = 1150, Height = 820, Fill = deepBlueGlow };
                System.Windows.Controls.Canvas.SetLeft(flashHalo, lx - 575);
                System.Windows.Controls.Canvas.SetTop(flashHalo, ly - 380);
                bgCanvas.Children.Add(flashHalo);
            }

            // 2. Грозовые тучи
            for (int i = 0; i < activeCloudBanks.Count; i++)
            {
                CloudBank3D cb = activeCloudBanks[i];
                double cx, cy;
                if (ProjectSkyObject(cb.Yaw, cb.Pitch, w, h, out cx, out cy))
                {
                    System.Windows.Controls.Canvas.SetLeft(cb.Visual, cx - cb.Visual.Width * 0.5);
                    System.Windows.Controls.Canvas.SetTop(cb.Visual, cy - cb.Visual.Height * 0.5);
                    bgCanvas.Children.Add(cb.Visual);
                }
            }

            // 3. Тонкий, дёрганый микро-сегментный канал молнии + подсвеченное облачное ядро
            if (flashVisible)
            {
                // Яркое локальное пятно пробоя в облаке
                System.Windows.Media.RadialGradientBrush breakGlow = new System.Windows.Media.RadialGradientBrush();
                breakGlow.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb((byte)(lightningFlashAlpha * 210), 190, 215, 255), 0.0));
                breakGlow.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb((byte)(lightningFlashAlpha * 110), 65, 115, 250), 0.45));
                breakGlow.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(0, 30, 60, 180), 1.0));

                System.Windows.Shapes.Ellipse cloudBreak = new System.Windows.Shapes.Ellipse() { Width = 460, Height = 340, Fill = breakGlow };
                System.Windows.Controls.Canvas.SetLeft(cloudBreak, lx - 230);
                System.Windows.Controls.Canvas.SetTop(cloudBreak, ly - 150);
                bgCanvas.Children.Add(cloudBreak);

                Random boltRnd = new Random(lightningSeed);
                System.Windows.Media.PointCollection mainPts = new System.Windows.Media.PointCollection();
                double curX = lx, curY = Math.Max(-10, ly - 75);
                mainPts.Add(new System.Windows.Point(curX, curY));

                double drift = (boltRnd.NextDouble() - 0.5) * 3.5;
                int segments = 46;
                for (int s = 0; s < segments; s++)
                {
                    curX += drift + (boltRnd.NextDouble() - 0.5) * 18.0;
                    curY += 8.0 + boltRnd.NextDouble() * 8.5;
                    mainPts.Add(new System.Windows.Point(curX, curY));

                    // Тонкие капиллярные ответвления
                    if (s == 9 || s == 19 || s == 31)
                    {
                        System.Windows.Media.PointCollection branchPts = new System.Windows.Media.PointCollection();
                        double bx = curX, by = curY;
                        branchPts.Add(new System.Windows.Point(bx, by));
                        double side = (s % 2 == 1) ? -1.0 : 1.0;
                        for (int b = 0; b < 14; b++)
                        {
                            bx += side * (4.0 + boltRnd.NextDouble() * 9.0) + (boltRnd.NextDouble() - 0.5) * 8.0;
                            by += 5.0 + boltRnd.NextDouble() * 7.0;
                            branchPts.Add(new System.Windows.Point(bx, by));
                        }

                        System.Windows.Shapes.Polyline branchGlow = new System.Windows.Shapes.Polyline()
                        {
                            Points = branchPts,
                            Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)(lightningFlashAlpha * 110), 80, 130, 255)),
                            StrokeThickness = 2.4
                        };
                        bgCanvas.Children.Add(branchGlow);

                        System.Windows.Shapes.Polyline branchCore = new System.Windows.Shapes.Polyline()
                        {
                            Points = branchPts,
                            Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)(lightningFlashAlpha * 215), 225, 238, 255)),
                            StrokeThickness = 0.85
                        };
                        bgCanvas.Children.Add(branchCore);
                    }
                }

                // Сине-фиолетовый электрический ореол
                System.Windows.Shapes.Polyline outerGlow = new System.Windows.Shapes.Polyline()
                {
                    Points = mainPts,
                    Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)(lightningFlashAlpha * 125), 65, 115, 255)),
                    StrokeThickness = 6.5,
                    StrokeLineJoin = System.Windows.Media.PenLineJoin.Round
                };
                bgCanvas.Children.Add(outerGlow);

                // Тонкое ослепительно-белое ядро молнии (1.5 px)
                System.Windows.Shapes.Polyline coreBolt = new System.Windows.Shapes.Polyline()
                {
                    Points = mainPts,
                    Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)(lightningFlashAlpha * 255), 248, 252, 255)),
                    StrokeThickness = 1.5,
                    StrokeLineJoin = System.Windows.Media.PenLineJoin.Round
                };
                bgCanvas.Children.Add(coreBolt);
            }
        }
    }

    private void Build3DRainVolume()
    {
        System.Windows.Media.Media3D.MeshGeometry3D mesh = new System.Windows.Media.Media3D.MeshGeometry3D();
        double spanY = 7.0;

        for (int copy = 0; copy < 2; copy++)
        {
            double baseOffsetY = copy * spanY;
            Random layerRnd = new Random(8842);

            for (int i = 0; i < 110; i++)
            {
                double x = (layerRnd.NextDouble() - 0.5) * 9.0;
                double y = (layerRnd.NextDouble() - 0.5) * spanY + baseOffsetY;
                double z = (layerRnd.NextDouble() - 0.5) * 9.0;

                if (x * x + y * y + z * z < 1.2) continue;

                double len = 0.28 + layerRnd.NextDouble() * 0.24;
                double tiltX = -0.055;
                double r = 0.0032;

                int idx = mesh.Positions.Count;
                mesh.Positions.Add(new System.Windows.Media.Media3D.Point3D(x - r, y, z));
                mesh.Positions.Add(new System.Windows.Media.Media3D.Point3D(x + r, y, z));
                mesh.Positions.Add(new System.Windows.Media.Media3D.Point3D(x - tiltX, y + len, z));

                mesh.Positions.Add(new System.Windows.Media.Media3D.Point3D(x, y, z - r));
                mesh.Positions.Add(new System.Windows.Media.Media3D.Point3D(x, y, z + r));
                mesh.Positions.Add(new System.Windows.Media.Media3D.Point3D(x - tiltX, y + len, z));

                mesh.TriangleIndices.Add(idx);
                mesh.TriangleIndices.Add(idx + 1);
                mesh.TriangleIndices.Add(idx + 2);

                mesh.TriangleIndices.Add(idx + 3);
                mesh.TriangleIndices.Add(idx + 4);
                mesh.TriangleIndices.Add(idx + 5);
            }
        }
        mesh.Freeze();

        rainMatBrush = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromArgb(65, 175, 205, 240));
        System.Windows.Media.Media3D.EmissiveMaterial rainMat = new System.Windows.Media.Media3D.EmissiveMaterial(rainMatBrush);

        System.Windows.Media.Media3D.GeometryModel3D rainModel = new System.Windows.Media.Media3D.GeometryModel3D(mesh, rainMat);
        rainModel.BackMaterial = rainMat;

        rainDropTransform = new System.Windows.Media.Media3D.TranslateTransform3D(0, 0, 0);
        rainModel.Transform = rainDropTransform;

        rainVisual = new System.Windows.Media.Media3D.ModelVisual3D();
        rainVisual.Content = rainModel;
        viewport.Children.Add(rainVisual);
    }

    private void Step3DRain()
    {
        if (rainDropTransform == null || rainMatBrush == null || lightPreset != 3) return;
        double spanY = 7.0;
        double offset = (rainPhase * 0.018) % spanY;
        rainDropTransform.OffsetY = -offset;
        rainDropTransform.OffsetX = -offset * 0.14;


        byte alpha = (byte)Math.Min(195, 58 + lightningFlashAlpha * 115);
        rainMatBrush.Color = System.Windows.Media.Color.FromArgb(alpha, 185, 212, 248);
    }


    private void UpdateCamera()


    {
        double yawRad = orbitYaw * Math.PI / 180.0;
        double pitchRad = orbitPitch * Math.PI / 180.0;
        double x = zoomDist * Math.Cos(pitchRad) * Math.Sin(yawRad);
        double y = zoomDist * Math.Sin(pitchRad);
        double z = zoomDist * Math.Cos(pitchRad) * Math.Cos(yawRad);
        camera.Position = new System.Windows.Media.Media3D.Point3D(x, y, z);
        System.Windows.Media.Media3D.Vector3D look = new System.Windows.Media.Media3D.Vector3D(-x, -y, -z);
        look.Normalize();
        camera.LookDirection = look;
    camera.UpDirection = new System.Windows.Media.Media3D.Vector3D(0, 1, 0);
    SortAndSyncClouds(false);
    RenderBackgroundDecor();
}


private void LoadModelAndTexture()
        {
            System.Windows.Controls.Grid grid = (System.Windows.Controls.Grid)host.Child;
            if (lblMissingModel != null) { grid.Children.Remove(lblMissingModel); lblMissingModel = null; }
            if (modelVisual != null) { viewport.Children.Remove(modelVisual); modelVisual = null; }

            string fbxPath = FindReferenceModel(entry.Aircraft);
            if (string.IsNullOrEmpty(fbxPath) || !File.Exists(fbxPath))
            {
                lblMissingModel = new System.Windows.Controls.TextBlock();
                lblMissingModel.Text = "Референс-модель для борта \"" + entry.Aircraft + "\" не найдена.\n" +
                          "Ожидаемый путь: %LOCALAPPDATA%\\VTOL_QoL\\models\\" + entry.Aircraft + ".fbx\n" +
                          "Положите туда экспортированную модель самолета (FBX), чтобы включить 3D-просмотр.";
                lblMissingModel.Foreground = System.Windows.Media.Brushes.OrangeRed;
                lblMissingModel.FontSize = 16;
                lblMissingModel.TextWrapping = System.Windows.TextWrapping.Wrap;
                lblMissingModel.Margin = new System.Windows.Thickness(20);
                grid.Children.Add(lblMissingModel);
                return;
            }

        FbxMeshData meshData = FbxLoader.Load(fbxPath);
        if (meshData == null)
        {
            MessageBox.Show("Не удалось прочитать FBX-файл модели: " + fbxPath, "Ошибка 3D-просмотра", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        NormalizeToUnitCube(meshData);

        System.Windows.Media.Brush matBrush;
        string texPath = !string.IsNullOrEmpty(entry.TexturePath) && File.Exists(entry.TexturePath) ? entry.TexturePath : null;
        if (texPath != null)
        {
            try
            {
                System.Windows.Media.Imaging.BitmapImage bmp = LiveryParser.LoadTextureBitmap(texPath);
                textureBrush = new System.Windows.Media.ImageBrush(bmp);
                textureBrush.ViewportUnits = System.Windows.Media.BrushMappingMode.Absolute;
                textureBrush.Viewport = new System.Windows.Rect(0, 0, 1, 1);
                textureBrush.TileMode = System.Windows.Media.TileMode.Tile;
                matBrush = textureBrush;
            }
            catch
            {
                matBrush = System.Windows.Media.Brushes.LightGray;
            }
        }
        else
        {
            matBrush = System.Windows.Media.Brushes.LightGray;
        }

        System.Windows.Media.Media3D.Model3DGroup group = new System.Windows.Media.Media3D.Model3DGroup();

        System.Windows.Media.Media3D.MeshGeometry3D bodyGeom = BuildWpfMesh(meshData.Body);
        System.Windows.Media.Media3D.MaterialGroup bodyMatGroup = new System.Windows.Media.Media3D.MaterialGroup();
        bodyMatGroup.Children.Add(new System.Windows.Media.Media3D.DiffuseMaterial(matBrush));
        bodyMatGroup.Children.Add(new System.Windows.Media.Media3D.SpecularMaterial(
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(65, 70, 75)), 22));
        model = new System.Windows.Media.Media3D.GeometryModel3D(bodyGeom, bodyMatGroup);
        model.BackMaterial = bodyMatGroup;
        group.Children.Add(model);


        if (meshData.Details.Vertices.Count > 0)
        {
            System.Windows.Media.Media3D.MeshGeometry3D detailsGeom = BuildWpfMesh(meshData.Details);
            System.Windows.Media.Media3D.MaterialGroup detailsMat = new System.Windows.Media.Media3D.MaterialGroup();
            detailsMat.Children.Add(new System.Windows.Media.Media3D.DiffuseMaterial(
                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(42, 46, 51))));
            detailsMat.Children.Add(new System.Windows.Media.Media3D.SpecularMaterial(
                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(90, 95, 100)), 25));
            group.Children.Add(new System.Windows.Media.Media3D.GeometryModel3D(detailsGeom, detailsMat));
        }

        if (meshData.Glass.Vertices.Count > 0)
        {
            System.Windows.Media.Media3D.MeshGeometry3D glassGeom = BuildWpfMesh(meshData.Glass);
            System.Windows.Media.Media3D.MaterialGroup glassMat = new System.Windows.Media.Media3D.MaterialGroup();
            glassMat.Children.Add(new System.Windows.Media.Media3D.DiffuseMaterial(
                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(115, 35, 65, 85))));
            glassMat.Children.Add(new System.Windows.Media.Media3D.SpecularMaterial(
                System.Windows.Media.Brushes.White, 60));
            group.Children.Add(new System.Windows.Media.Media3D.GeometryModel3D(glassGeom, glassMat));
        }

        modelVisual = new System.Windows.Media.Media3D.ModelVisual3D();
        modelVisual.Content = group;
        viewport.Children.Add(modelVisual);

        BuildAircraftLights(meshData);
    }

    private FbxMeshData lastLoadedMeshData;


    private void BuildAircraftLights(FbxMeshData meshData)
    {
        lastLoadedMeshData = meshData;
        if (navLightTimer != null)
        {
            navLightTimer.Stop();
            navLightTimer.Dispose();
            navLightTimer = null;
        }
        strobeBrushes.Clear();
        strobePointLights.Clear();

        if (viewport == null) return;
        if (aircraftLightsVisual != null)
        {
            viewport.Children.Remove(aircraftLightsVisual);
            aircraftLightsVisual = null;
        }

        if (meshData == null || meshData.Lights.Count == 0) return;

        bool isNightOrStorm = (lightPreset == 2 || lightPreset == 3);
        bool isDay = (lightPreset == 1);

        System.Windows.Media.Media3D.Model3DGroup lightsGroup = new System.Windows.Media.Media3D.Model3DGroup();
        System.Windows.Media.Media3D.MeshGeometry3D bulbMesh = BuildLightSphereMesh(10, 8);

        for (int i = 0; i < meshData.Lights.Count; i++)
        {
            FbxLightMarker lm = meshData.Lights[i];
            System.Windows.Media.Media3D.Point3D rawPos = lm.Position;
            System.Windows.Media.Media3D.Point3D pos = SnapToWingSurface(meshData.Body.Vertices, rawPos, lm.Kind);
            AppLogger.Log("LIGHT_SNAP", string.Format("[{0}] {1}: Raw=({2:F3},{3:F3},{4:F3}) -> Snapped=({5:F3},{6:F3},{7:F3})",
                entry != null ? entry.Aircraft : "?", lm.Kind,
                rawPos.X, rawPos.Y, rawPos.Z, pos.X, pos.Y, pos.Z));



            if ((lm.Kind == "NavRed" || lm.Kind == "NavGreen" || lm.Kind == "NavWhite") && navLightsEnabled)
            {
                System.Windows.Media.Color coreCol = lm.Kind == "NavRed"
                    ? System.Windows.Media.Color.FromRgb(255, 35, 35)
                    : (lm.Kind == "NavGreen"
                        ? System.Windows.Media.Color.FromRgb(30, 255, 95)
                        : System.Windows.Media.Color.FromRgb(255, 248, 230));

                byte haloAlpha = isNightOrStorm ? (byte)115 : (isDay ? (byte)35 : (byte)75);
                System.Windows.Media.Color haloCol = System.Windows.Media.Color.FromArgb(haloAlpha, coreCol.R, coreCol.G, coreCol.B);

                AddLightBulb(lightsGroup, bulbMesh, pos, 0.013, coreCol);
                AddLightBulb(lightsGroup, bulbMesh, pos, isNightOrStorm ? 0.042 : 0.026, haloCol);

                if (isNightOrStorm)
                {
                    System.Windows.Media.Color ptCol = lm.Kind == "NavRed"
                        ? System.Windows.Media.Color.FromRgb(160, 20, 20)
                        : (lm.Kind == "NavGreen"
                            ? System.Windows.Media.Color.FromRgb(20, 160, 55)
                            : System.Windows.Media.Color.FromRgb(140, 135, 120));
                    System.Windows.Media.Media3D.PointLight pt = new System.Windows.Media.Media3D.PointLight(ptCol, pos);
                    pt.Range = 0.65;
                    pt.ConstantAttenuation = 0.6;
                    pt.LinearAttenuation = 2.2;
                    lightsGroup.Children.Add(pt);
                }
            }
            else if ((lm.Kind == "Strobe" || lm.Kind == "RedStrobe") && strobeLightsEnabled)
            {
                bool isRed = (lm.Kind == "RedStrobe");
                System.Windows.Media.Media3D.Point3D stPos = pos;

                // Если строб на крыле — ставим его строго по середине торца законцовки относительно БАНО этого крыла
                if (!isRed && Math.Abs(pos.X) > 0.35)
                {
                    for (int n = 0; n < meshData.Lights.Count; n++)
                    {
                        FbxLightMarker nav = meshData.Lights[n];
                        if ((nav.Kind == "NavRed" || nav.Kind == "NavGreen") && Math.Sign(nav.Position.X) == Math.Sign(pos.X))
                        {
                            System.Windows.Media.Media3D.Point3D navSnapped = SnapToWingSurface(meshData.Body.Vertices, nav.Position, nav.Kind);
                            stPos = new System.Windows.Media.Media3D.Point3D(navSnapped.X, navSnapped.Y, navSnapped.Z - 0.028);
                            break;
                        }
                    }
                }



                System.Windows.Media.SolidColorBrush coreBr = new System.Windows.Media.SolidColorBrush(
                    isRed ? System.Windows.Media.Color.FromArgb(0, 255, 45, 45) : System.Windows.Media.Color.FromArgb(0, 255, 255, 255));
                System.Windows.Media.SolidColorBrush haloBr = new System.Windows.Media.SolidColorBrush(
                    isRed ? System.Windows.Media.Color.FromArgb(0, 255, 30, 30) : System.Windows.Media.Color.FromArgb(0, 200, 230, 255));

                strobeBrushes.Add(coreBr);
                strobeBrushes.Add(haloBr);

                AddLightBulbBrush(lightsGroup, bulbMesh, stPos, 0.011, coreBr);
                AddLightBulbBrush(lightsGroup, bulbMesh, stPos, 0.036, haloBr);

                System.Windows.Media.Media3D.Point3D lightOrigin = new System.Windows.Media.Media3D.Point3D(
                    stPos.X * 0.92, stPos.Y + 0.04, stPos.Z);
                System.Windows.Media.Media3D.PointLight stPt = new System.Windows.Media.Media3D.PointLight(
                    System.Windows.Media.Color.FromRgb(0, 0, 0), lightOrigin);
                stPt.Range = 0.85;
                stPt.ConstantAttenuation = 0.6;
                stPt.LinearAttenuation = 2.0;
                lightsGroup.Children.Add(stPt);
                strobePointLights.Add(stPt);
            }
            else if (lm.Kind == "Landing" && landingLightEnabled)
            {
                if (!isDay)
                {
                    System.Windows.Media.Color lampCol = System.Windows.Media.Color.FromRgb(255, 250, 232);
                    AddLightBulb(lightsGroup, bulbMesh, pos, 0.014, lampCol);
                    AddLightBulb(lightsGroup, bulbMesh, pos, 0.034,
                        System.Windows.Media.Color.FromArgb(isNightOrStorm ? (byte)95 : (byte)45, 255, 244, 205));

                    if (isNightOrStorm)
                    {
                        System.Windows.Media.Media3D.PointLight nosePt = new System.Windows.Media.Media3D.PointLight(
                            System.Windows.Media.Color.FromRgb(210, 198, 165),
                            new System.Windows.Media.Media3D.Point3D(pos.X, pos.Y - 0.04, pos.Z + 0.12));
                        nosePt.Range = 0.85;
                        nosePt.ConstantAttenuation = 0.5;
                        nosePt.LinearAttenuation = 1.8;
                        lightsGroup.Children.Add(nosePt);
                    }
                }
            }
        }


        aircraftLightsVisual = new System.Windows.Media.Media3D.ModelVisual3D();
        aircraftLightsVisual.Content = lightsGroup;
        viewport.Children.Add(aircraftLightsVisual);

        if (strobeBrushes.Count > 0)
        {
            navLightTimer = new System.Windows.Forms.Timer();
            navLightTimer.Interval = 40;
            navLightTimer.Tick += (s, e) => StepAircraftLights();
            navLightTimer.Start();
        }
    }

    private System.Windows.Media.Media3D.Point3D SnapToWingSurface(
        List<System.Windows.Media.Media3D.Point3D> verts,
        System.Windows.Media.Media3D.Point3D pt,
        string kind)
    {
        if (verts == null || verts.Count == 0 || kind == "Landing") return pt;
        if (Math.Abs(pt.X) < 0.25) return pt;

        double bestDistSq = double.MaxValue;
        System.Windows.Media.Media3D.Point3D bestVert = pt;

        for (int i = 0; i < verts.Count; i++)
        {
            System.Windows.Media.Media3D.Point3D v = verts[i];
            double dx = v.X - pt.X;
            double dy = v.Y - pt.Y;
            double dz = v.Z - pt.Z;
            double dSq = dx * dx + dy * dy + dz * dz;
            if (dSq < bestDistSq)
            {
                bestDistSq = dSq;
                bestVert = v;
            }
        }

        if (bestDistSq < 0.032)
        {
            double outOffset = pt.X > 0 ? 0.006 : -0.006;
            return new System.Windows.Media.Media3D.Point3D(bestVert.X + outOffset, bestVert.Y, bestVert.Z);
        }
        return pt;
    }

    private void StepAircraftLights()
    {
        if (strobeBrushes.Count == 0) return;
        strobePhaseTick = (strobePhaseTick + 1) % 38;

        bool isFlashOn = (strobePhaseTick == 0 || strobePhaseTick == 1 || strobePhaseTick == 4 || strobePhaseTick == 5);
        byte coreA = isFlashOn ? (byte)255 : (byte)0;
        byte haloA = isFlashOn ? (byte)(lightPreset == 1 ? 75 : 155) : (byte)0;

    for (int i = 0; i < strobeBrushes.Count; i += 2)
    {
        System.Windows.Media.Color baseC = strobeBrushes[i].Color;
        bool isRed = (baseC.G < 100);
        strobeBrushes[i].Color = isRed
            ? System.Windows.Media.Color.FromArgb(coreA, 255, 45, 45)
            : System.Windows.Media.Color.FromArgb(coreA, 255, 255, 255);
        if (i + 1 < strobeBrushes.Count)
        {
            strobeBrushes[i + 1].Color = isRed
                ? System.Windows.Media.Color.FromArgb(haloA, 255, 30, 30)
                : System.Windows.Media.Color.FromArgb(haloA, 200, 230, 255);
        }

        int ptIdx = i / 2;
        if (ptIdx < strobePointLights.Count)
        {
            byte ptVal = (isFlashOn && (lightPreset == 2 || lightPreset == 3)) ? (byte)210 : (byte)0;
            strobePointLights[ptIdx].Color = isRed
                ? System.Windows.Media.Color.FromRgb(ptVal, (byte)(ptVal * 0.12), (byte)(ptVal * 0.12))
                : System.Windows.Media.Color.FromRgb(ptVal, ptVal, (byte)Math.Min(255, ptVal + 25));
        }
    }
}

private void AddLightBulb(System.Windows.Media.Media3D.Model3DGroup group, System.Windows.Media.Media3D.MeshGeometry3D sphere, System.Windows.Media.Media3D.Point3D pos, double radius, System.Windows.Media.Color color)
    {
        AddLightBulbBrush(group, sphere, pos, radius, new System.Windows.Media.SolidColorBrush(color));
    }

    private void AddLightBulbBrush(System.Windows.Media.Media3D.Model3DGroup group, System.Windows.Media.Media3D.MeshGeometry3D sphere, System.Windows.Media.Media3D.Point3D pos, double radius, System.Windows.Media.SolidColorBrush brush)
    {
        System.Windows.Media.Media3D.EmissiveMaterial mat = new System.Windows.Media.Media3D.EmissiveMaterial(brush);
        System.Windows.Media.Media3D.GeometryModel3D gm = new System.Windows.Media.Media3D.GeometryModel3D(sphere, mat);
        System.Windows.Media.Media3D.Transform3DGroup tg = new System.Windows.Media.Media3D.Transform3DGroup();
        tg.Children.Add(new System.Windows.Media.Media3D.ScaleTransform3D(radius, radius, radius));
        tg.Children.Add(new System.Windows.Media.Media3D.TranslateTransform3D(pos.X, pos.Y, pos.Z));
        gm.Transform = tg;
        group.Children.Add(gm);
    }

    private System.Windows.Media.Media3D.MeshGeometry3D BuildLightSphereMesh(int slices, int stacks)
    {
        System.Windows.Media.Media3D.MeshGeometry3D m = new System.Windows.Media.Media3D.MeshGeometry3D();
        for (int st = 0; st <= stacks; st++)
        {
            double phi = Math.PI * st / stacks;
            double y = Math.Cos(phi);
            double r = Math.Sin(phi);
            for (int sl = 0; sl <= slices; sl++)
            {
                double th = 2.0 * Math.PI * sl / slices;
                m.Positions.Add(new System.Windows.Media.Media3D.Point3D(r * Math.Cos(th), y, r * Math.Sin(th)));
            }
        }
        int stride = slices + 1;
        for (int st = 0; st < stacks; st++)
        {
            for (int sl = 0; sl < slices; sl++)
            {
                int a = st * stride + sl, b = a + stride, c = a + 1, d = b + 1;
                m.TriangleIndices.Add(a); m.TriangleIndices.Add(b); m.TriangleIndices.Add(c);
                m.TriangleIndices.Add(c); m.TriangleIndices.Add(b); m.TriangleIndices.Add(d);
            }
        }
        m.Freeze();
        return m;
    }

    private System.Windows.Media.Media3D.MeshGeometry3D BuildSpotlightConeMesh(System.Windows.Media.Media3D.Point3D origin, double r0, double r1, double length, int sides)
    {
        System.Windows.Media.Media3D.MeshGeometry3D m = new System.Windows.Media.Media3D.MeshGeometry3D();
        for (int i = 0; i <= sides; i++)
        {
            double a = 2.0 * Math.PI * i / sides;
            double ca = Math.Cos(a), sa = Math.Sin(a);
            m.Positions.Add(new System.Windows.Media.Media3D.Point3D(origin.X + r0 * ca, origin.Y + r0 * sa, origin.Z));
            m.Positions.Add(new System.Windows.Media.Media3D.Point3D(origin.X + r1 * ca, origin.Y + r1 * sa - 0.14, origin.Z + length));
        }
        for (int i = 0; i < sides; i++)
        {
            int b = i * 2;
            m.TriangleIndices.Add(b); m.TriangleIndices.Add(b + 1); m.TriangleIndices.Add(b + 2);
            m.TriangleIndices.Add(b + 2); m.TriangleIndices.Add(b + 1); m.TriangleIndices.Add(b + 3);
        }
        m.Freeze();
        return m;
    }

    private System.Windows.Media.Media3D.MeshGeometry3D BuildWpfMesh(FbxSubMesh sub)
    {
        System.Windows.Media.Media3D.MeshGeometry3D geom = new System.Windows.Media.Media3D.MeshGeometry3D();
        foreach (System.Windows.Media.Media3D.Point3D p in sub.Vertices) geom.Positions.Add(p);
        foreach (int idx in sub.Indices) geom.TriangleIndices.Add(idx);
        if (sub.UVs.Count == sub.Vertices.Count)
            foreach (System.Windows.Point uv in sub.UVs) geom.TextureCoordinates.Add(uv);
        return geom;
    }


    private void NormalizeToUnitCube(FbxMeshData mesh)
    {
        if (mesh.TotalVertices == 0) return;
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

        FbxSubMesh[] subs = new FbxSubMesh[] { mesh.Body, mesh.Details, mesh.Glass };
        foreach (FbxSubMesh s in subs)
        {
            foreach (System.Windows.Media.Media3D.Point3D p in s.Vertices)
            {
                if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
                if (p.Z < minZ) minZ = p.Z; if (p.Z > maxZ) maxZ = p.Z;
            }
        }

        double cx = (minX + maxX) / 2.0, cy = (minY + maxY) / 2.0, cz = (minZ + maxZ) / 2.0;
        double sizeX = maxX - minX, sizeY = maxY - minY, sizeZ = maxZ - minZ;
        double maxSize = Math.Max(sizeX, Math.Max(sizeY, sizeZ));
        if (maxSize < 0.0001) maxSize = 1;
        double scale = 2.0 / maxSize;

        foreach (FbxSubMesh s in subs)
        {
            for (int i = 0; i < s.Vertices.Count; i++)
            {
                System.Windows.Media.Media3D.Point3D p = s.Vertices[i];
                s.Vertices[i] = new System.Windows.Media.Media3D.Point3D((p.X - cx) * scale, (p.Y - cy) * scale, (p.Z - cz) * scale);
            }
        }

        for (int i = 0; i < mesh.Lights.Count; i++)
        {
            System.Windows.Media.Media3D.Point3D p = mesh.Lights[i].Position;
            mesh.Lights[i].Position = new System.Windows.Media.Media3D.Point3D((p.X - cx) * scale, (p.Y - cy) * scale, (p.Z - cz) * scale);
            if (mesh.Lights[i].Kind == "NavAuto")
            {
                if (Math.Abs(mesh.Lights[i].Position.X) < 0.08) mesh.Lights[i].Kind = "NavWhite";
                else mesh.Lights[i].Kind = mesh.Lights[i].Position.X > 0 ? "NavRed" : "NavGreen";
            }
            AppLogger.Log("LIGHTS_NORM", string.Format("[{0}] Kind={1} -> Pos=({2:F3}, {3:F3}, {4:F3})",
                entry != null ? entry.Aircraft : "?", mesh.Lights[i].Kind,
                mesh.Lights[i].Position.X, mesh.Lights[i].Position.Y, mesh.Lights[i].Position.Z));
        }
    }

    private string FindReferenceModel(string aircraft)
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL", "models");
        try { Directory.CreateDirectory(dir); } catch { }
        string candidate = Path.Combine(dir, aircraft + ".fbx");
        if (!File.Exists(candidate))
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(aircraft + ".fbx"))
            {
                if (s != null)
                {
                    using (FileStream fs = new FileStream(candidate, FileMode.Create, FileAccess.Write))
                        s.CopyTo(fs);
                }
            }
        }
        if (File.Exists(candidate)) return candidate;
        return null;
    }

    private void OpenCustomTexture()
    {
        using (OpenFileDialog ofd = new OpenFileDialog() { Filter = "Изображения (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg" })
        {
            if (ofd.ShowDialog() == DialogResult.OK && model != null)
            {
                try
                {
                    System.Windows.Media.Imaging.BitmapImage bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(ofd.FileName, UriKind.Absolute);
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    System.Windows.Media.ImageBrush br = new System.Windows.Media.ImageBrush(bmp);
                    br.ViewportUnits = System.Windows.Media.BrushMappingMode.Absolute;
                    br.Viewport = new System.Windows.Rect(0, 0, 1, 1);
                    br.TileMode = System.Windows.Media.TileMode.Tile;
                    System.Windows.Media.Media3D.DiffuseMaterial mat = new System.Windows.Media.Media3D.DiffuseMaterial(br);
                    model.Material = mat;
                    model.BackMaterial = mat;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Не удалось загрузить текстуру: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}

// =========================================================================
// === МЕНЕДЖЕР ЛИВРЕЙ (НОВЫЙ, с 3D-кнопкой, ZIP, Workshop) ================
// =========================================================================
public class LiveryManagerForm : Form
{
    private string gameRoot;
    private bool isCatalogMode = false;
    private int catalogPage = 1;

    private Button btnTabMy;
    private Button btnTabCatalog;
    private ComboBox cmbAircraft;
    private TextBox txtSearch;
    private Button btnSearchOrRefresh;
    private CheckBox chkWorkshop;
    private Button btnPrevPage;
    private Button btnNextPage;
    private Label lblPage;

    private ListBox lstLiveries;
    private PictureBox picPreview;
    private Label lblPreviewInfo;
    private Label lblStatus;

    private Button btn3D;
    private Button btnDownloadWs;
    private Button btnInstallFolder;
    private Button btnInstallZip;
    private Button btnDelete;
    private Button btnOpenDir;
    private Button btnOpenItemDir;

    private List<LiveryEntry> currentEntries = new List<LiveryEntry>();
    private List<LiveryEntry> filteredEntries = new List<LiveryEntry>();
    private List<WorkshopCatalogItem> catalogItems = new List<WorkshopCatalogItem>();
    private readonly string[] aircrafts = new string[] { "Все самолеты", "Не определен", "FA-26B", "F-45A", "AV-42C", "AH-94", "T-55", "EF-24G" };

    public LiveryManagerForm(string root)
    {
        this.gameRoot = root;
        this.Text = "VTOL QoL — Менеджер ливрей, 3D-просмотр и Steam Workshop";
        DarkWindowHelper.Apply(this);
        this.Size = new Size(920, 585);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.BackColor = Color.FromArgb(18, 22, 20);
        this.ForeColor = Color.FromArgb(0, 255, 102);
        this.Font = new Font("Consolas", 9.5f, FontStyle.Regular);

        // Вкладки сверху
        btnTabMy = new Button() { Text = "📂 МОИ ЛИВРЕИ (LOCAL / STEAM)", Left = 20, Top = 12, Width = 230, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = new Font("Consolas", 9f, FontStyle.Bold) };
        btnTabMy.Click += (s, e) => SwitchMode(false);
        this.Controls.Add(btnTabMy);

        btnTabCatalog = new Button() { Text = "🌐 КАТАЛОГ STEAM WORKSHOP", Left = 255, Top = 12, Width = 230, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(32, 40, 36), ForeColor = Color.LightGray, Font = new Font("Consolas", 9f, FontStyle.Bold) };
        btnTabCatalog.Click += (s, e) => SwitchMode(true);
        this.Controls.Add(btnTabCatalog);

        cmbAircraft = new ComboBox();
        cmbAircraft.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbAircraft.Location = new Point(20, 50);
        cmbAircraft.Size = new Size(165, 26);
        cmbAircraft.BackColor = Color.FromArgb(30, 36, 33);
        cmbAircraft.ForeColor = Color.Cyan;
        foreach (string ac in aircrafts) cmbAircraft.Items.Add(ac);
        cmbAircraft.SelectedIndex = 0;
        cmbAircraft.SelectedIndexChanged += (s, e) => { if (isCatalogMode) { catalogPage = 1; SearchCatalogAsync(); } else LoadLiveries(); };
        this.Controls.Add(cmbAircraft);

        txtSearch = new TextBox() { Left = 192, Top = 50, Width = 185, Height = 25, BackColor = Color.FromArgb(30, 36, 33), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        txtSearch.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; if (isCatalogMode) { catalogPage = 1; SearchCatalogAsync(); } else LoadLiveries(); } };
        this.Controls.Add(txtSearch);

        btnSearchOrRefresh = new Button() { Text = "Поиск / Обн.", Left = 383, Top = 49, Width = 102, Height = 27, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(40, 55, 48), ForeColor = Color.White };
        btnSearchOrRefresh.Click += (s, e) => { if (isCatalogMode) { catalogPage = 1; SearchCatalogAsync(); } else LoadLiveries(); };
        this.Controls.Add(btnSearchOrRefresh);

        chkWorkshop = new CheckBox() { Text = "Показывать скачанный Workshop", Left = 20, Top = 82, AutoSize = true, ForeColor = Color.FromArgb(210, 240, 220), Checked = true };
        chkWorkshop.CheckedChanged += (s, e) => LoadLiveries();
        this.Controls.Add(chkWorkshop);

        btnPrevPage = new Button() { Text = "< Пред.", Left = 20, Top = 80, Width = 80, Height = 24, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(35, 45, 40), ForeColor = Color.White, Visible = false };
        btnPrevPage.Click += (s, e) => { if (catalogPage > 1) { catalogPage--; SearchCatalogAsync(); } };
        this.Controls.Add(btnPrevPage);

        lblPage = new Label() { Text = "Стр. 1", Left = 108, Top = 84, Width = 70, ForeColor = Color.Cyan, Visible = false };
        this.Controls.Add(lblPage);

        btnNextPage = new Button() { Text = "След. >", Left = 180, Top = 80, Width = 80, Height = 24, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(35, 45, 40), ForeColor = Color.White, Visible = false };
        btnNextPage.Click += (s, e) => { catalogPage++; SearchCatalogAsync(); };
        this.Controls.Add(btnNextPage);

        lstLiveries = new ListBox();
        lstLiveries.Location = new Point(20, 110);
        lstLiveries.Size = new Size(465, 375);
        lstLiveries.BackColor = Color.FromArgb(24, 28, 26);
        lstLiveries.ForeColor = Color.White;
        lstLiveries.BorderStyle = BorderStyle.FixedSingle;
        lstLiveries.SelectedIndexChanged += (s, e) => UpdatePreview();
        this.Controls.Add(lstLiveries);
        lstLiveries.DrawMode = DrawMode.OwnerDrawFixed;
        lstLiveries.ItemHeight = 18;
        lstLiveries.DrawItem += (s, e) => {
            if (e.Index < 0 || e.Index >= lstLiveries.Items.Count) return;
            string text = lstLiveries.Items[e.Index].ToString();
            bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            bool isInstalled = text.StartsWith("✔ ");

            Color bg = isSelected ? Color.FromArgb(0, 95, 135) : Color.FromArgb(24, 28, 26);
            Color fg = isInstalled ? Color.FromArgb(0, 255, 102) : Color.White;

            using (SolidBrush bgBrush = new SolidBrush(bg))
                e.Graphics.FillRectangle(bgBrush, e.Bounds);
            using (SolidBrush fgBrush = new SolidBrush(fg))
                e.Graphics.DrawString(text, e.Font, fgBrush, e.Bounds.Left + 4, e.Bounds.Top + 1);
        };

        ContextMenuStrip ctxLivery = new ContextMenuStrip();
        ToolStripMenuItem item3D = new ToolStripMenuItem("🎮 Открыть в 3D-просмотре");
        item3D.Click += Btn3D_Click;
        ctxLivery.Items.Add(item3D);

        ToolStripMenuItem itemAssignMenu = new ToolStripMenuItem("✈ Назначить борт вручную");
        string[] assignPlanes = new string[] { "FA-26B", "F-45A", "AV-42C", "AH-94", "T-55", "EF-24G" };
        foreach (string pl in assignPlanes)
        {
            string targetPlane = pl;
            ToolStripMenuItem sub = new ToolStripMenuItem(targetPlane);
            sub.Click += (s, e) => {
                if (isCatalogMode) return;
                LiveryEntry entry = GetSelectedEntry();
                if (entry == null) return;
                entry.Aircraft = targetPlane;
                LiveryParser.SaveUserAircraftMapping(entry.FolderPath, entry.Aircraft);
                int idx = lstLiveries.SelectedIndex;
                if (idx >= 0 && idx < filteredEntries.Count)
                {
                    lstLiveries.Items[idx] = string.Format("[{0}] {1}  ({2})", entry.Source, entry.Name, entry.Aircraft);
                    UpdatePreview();
                }
            };
            itemAssignMenu.DropDownItems.Add(sub);
        }
        ctxLivery.Items.Add(itemAssignMenu);

        ToolStripMenuItem itemFolder = new ToolStripMenuItem("📁 Открыть папку мода");
        itemFolder.Click += (s, e) => {
            LiveryEntry entry = GetSelectedEntry();
            if (entry != null && Directory.Exists(entry.FolderPath)) Process.Start("explorer.exe", entry.FolderPath);
        };
        ctxLivery.Items.Add(itemFolder);

        lstLiveries.MouseDown += (s, e) => {
            if (e.Button == MouseButtons.Right)
            {
                int idx = lstLiveries.IndexFromPoint(e.Location);
                if (idx >= 0) lstLiveries.SelectedIndex = idx;
            }
        };
        lstLiveries.ContextMenuStrip = ctxLivery;

        Label lblPrevTitle = new Label() { Text = "ПРЕВЬЮ ЛИВРЕИ:", Left = 500, Top = 15, AutoSize = true, Font = new Font("Consolas", 9.5f, FontStyle.Bold) };
        this.Controls.Add(lblPrevTitle);

        picPreview = new PictureBox();
        picPreview.Location = new Point(500, 38);
        picPreview.Size = new Size(390, 230);
        picPreview.BackColor = Color.FromArgb(24, 28, 26);
        picPreview.BorderStyle = BorderStyle.FixedSingle;
        picPreview.SizeMode = PictureBoxSizeMode.Zoom;
        this.Controls.Add(picPreview);

        lblPreviewInfo = new Label() { Left = 500, Top = 274, Width = 390, Height = 20, ForeColor = Color.Cyan, Text = "Выберите ливрею из списка" };
        this.Controls.Add(lblPreviewInfo);

        int top = 302, btnW = 190, btnH = 36, gap = 10;
        btn3D = CreateBtn("🎮 3D-просмотр", 500, ref top, btnW, btnH, gap, Color.FromArgb(70, 50, 110));
        btn3D.Click += Btn3D_Click;

        btnDownloadWs = new Button() { Text = "⬇ Скачать ливрею", Location = new Point(500, 302), Size = new Size(390, 42), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 130, 80), ForeColor = Color.White, Font = new Font("Consolas", 10f, FontStyle.Bold), Visible = false };

        btnDownloadWs.Click += BtnDownloadWs_Click;
        this.Controls.Add(btnDownloadWs);

        btnInstallFolder = CreateBtn("+ Добавить скин", 500, ref top, btnW, btnH, gap, Color.FromArgb(0, 110, 140));
        btnInstallFolder.Click += BtnInstallFolder_Click;

        btnInstallZip = CreateBtn("+ Установить .zip", 500, ref top, btnW, btnH, gap, Color.FromArgb(0, 130, 90));
        btnInstallZip.Click += BtnInstallZip_Click;

        int topRight = 302;
        btnDelete = CreateBtn("Удалить ливрею", 700, ref topRight, btnW, btnH, gap, Color.FromArgb(140, 35, 35));
        btnDelete.Click += BtnDelete_Click;

        btnOpenDir = CreateBtn("Папка CustomLiveries", 700, ref topRight, btnW, btnH, gap, Color.FromArgb(45, 55, 50));
        btnOpenDir.Click += (s, e) => {
            string dir = GetLiveriesRootDir();
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            Process.Start("explorer.exe", dir);
        };

        btnOpenItemDir = CreateBtn("Открыть папку мода", 700, ref topRight, btnW, btnH, gap, Color.FromArgb(45, 55, 50));
        btnOpenItemDir.Click += (s, e) => {
            LiveryEntry entry = GetSelectedEntry();
            if (entry != null && Directory.Exists(entry.FolderPath)) Process.Start("explorer.exe", entry.FolderPath);
        };

        lblStatus = new Label() { Left = 20, Top = 496, Width = 870, Height = 22, Text = "", ForeColor = Color.Silver };
        this.Controls.Add(lblStatus);

        LoadLiveries();
    }

    private Button CreateBtn(string text, int left, ref int top, int width, int height, int gap, Color backColor)
    {
        Button btn = new Button() { Text = text, Location = new Point(left, top), Size = new Size(width, height), FlatStyle = FlatStyle.Flat, BackColor = backColor, ForeColor = Color.White };
        this.Controls.Add(btn);
        top += height + gap;
        return btn;
    }

    private void SwitchMode(bool catalog)
    {
        catalogSearchGen++;
        ShowCatalogLoader(false);
        isCatalogMode = catalog;
        btnTabMy.BackColor = !catalog ? Color.FromArgb(0, 120, 80) : Color.FromArgb(32, 40, 36);
        btnTabMy.ForeColor = !catalog ? Color.White : Color.LightGray;
        btnTabCatalog.BackColor = catalog ? Color.FromArgb(0, 110, 150) : Color.FromArgb(32, 40, 36);
        btnTabCatalog.ForeColor = catalog ? Color.White : Color.LightGray;

        chkWorkshop.Visible = !catalog;
        btnPrevPage.Visible = catalog;
        lblPage.Visible = catalog;
        btnNextPage.Visible = catalog;

        btn3D.Visible = !catalog;
        btnInstallFolder.Visible = !catalog;
        btnInstallZip.Visible = !catalog;
        btnDelete.Visible = !catalog;
        btnOpenDir.Visible = !catalog;
        btnOpenItemDir.Visible = !catalog;
        btnDownloadWs.Visible = catalog;

        if (catalog) { catalogPage = 1; SearchCatalogAsync(); }
        else LoadLiveries();
    }
    private Panel pnlCatalogLoader = null;
    private System.Windows.Forms.Timer catalogAnimTimer = null;
    private int catalogSearchGen = 0;

    private void ShowCatalogLoader(bool show)
    {
        if (catalogAnimTimer != null)
        {
            catalogAnimTimer.Stop();
            catalogAnimTimer.Dispose();
            catalogAnimTimer = null;
        }
        if (pnlCatalogLoader != null)
        {
            if (pnlCatalogLoader.Parent != null) this.Controls.Remove(pnlCatalogLoader);
            pnlCatalogLoader.Dispose();
            pnlCatalogLoader = null;
        }
        if (!show) return;

        pnlCatalogLoader = new Panel();
        pnlCatalogLoader.Size = new Size(340, 78);
        pnlCatalogLoader.Location = new Point(lstLiveries.Left + (lstLiveries.Width - 340) / 2, lstLiveries.Top + (lstLiveries.Height - 78) / 2);
        pnlCatalogLoader.BackColor = Color.FromArgb(28, 36, 33);
        pnlCatalogLoader.BorderStyle = BorderStyle.FixedSingle;

        Label lblLoader = new Label();
        lblLoader.Text = "⏳ Загрузка каталога Steam Workshop...";
        lblLoader.Location = new Point(12, 14);
        lblLoader.Width = 316;
        lblLoader.Height = 22;
        lblLoader.TextAlign = ContentAlignment.MiddleCenter;
        lblLoader.ForeColor = Color.Cyan;
        lblLoader.Font = new Font("Consolas", 9.5f, FontStyle.Bold);
        pnlCatalogLoader.Controls.Add(lblLoader);

        ProgressBar pb = new ProgressBar();
        pb.Style = ProgressBarStyle.Marquee;
        pb.MarqueeAnimationSpeed = 25;
        pb.Location = new Point(20, 44);
        pb.Size = new Size(300, 16);
        pnlCatalogLoader.Controls.Add(pb);

        this.Controls.Add(pnlCatalogLoader);
        pnlCatalogLoader.BringToFront();

        int elapsedSec = 0;
        catalogAnimTimer = new System.Windows.Forms.Timer();
        catalogAnimTimer.Interval = 1000;
        catalogAnimTimer.Tick += (s, e) => {
            elapsedSec++;
            lblLoader.Text = string.Format("⏳ Загрузка Steam Workshop... ({0} сек)", elapsedSec);
        };
        catalogAnimTimer.Start();
    }

    private void SearchCatalogAsync()
    {
        int currentGen = ++catalogSearchGen;
        lstLiveries.Items.Clear();
        catalogItems.Clear();
        if (picPreview.Image != null) { picPreview.Image.Dispose(); picPreview.Image = null; }
        lblPage.Text = "Стр. " + catalogPage;
        string ac = cmbAircraft.SelectedItem != null ? cmbAircraft.SelectedItem.ToString() : "Все самолеты";
        string q = txtSearch.Text.Trim();
        lblStatus.Text = "Загрузка каталога Steam Workshop...";
        ShowCatalogLoader(true);

        ThreadPool.QueueUserWorkItem(delegate {
            try
            {
                List<WorkshopCatalogItem> found = WorkshopBrowser.SearchLiveries(q, ac, catalogPage);
                this.Invoke(new Action(delegate {
                    if (currentGen != catalogSearchGen || !isCatalogMode) return;
                    ShowCatalogLoader(false);
                    catalogItems = found;
                    lstLiveries.Items.Clear();
                    string wsContentDir = !string.IsNullOrEmpty(gameRoot)
    ? Path.Combine(Path.GetFullPath(Path.Combine(gameRoot, @"..\..\..")), @"steamapps\workshop\content\667970")
    : "";

                    foreach (WorkshopCatalogItem it in catalogItems)
                    {
                        bool alreadyInstalled = !string.IsNullOrEmpty(wsContentDir) && Directory.Exists(Path.Combine(wsContentDir, it.PublishedFileId));
                        string prefix = alreadyInstalled ? "✔ " : "";
                        string suffix = alreadyInstalled ? "  [УСТАНОВЛЕНО]" : "";
                        lstLiveries.Items.Add(string.Format("{0}[WS #{1}] {2}{3}", prefix, it.PublishedFileId, it.Title, suffix));
                    }
                    lblStatus.Text = "Найдено в Steam Workshop на странице " + catalogPage + ": " + catalogItems.Count;
                }));
            }
            catch (Exception ex)
            {
                AppLogger.Error("WORKSHOP", "SearchCatalogAsync", ex);
                try
                {
                    this.Invoke(new Action(delegate {
                        if (currentGen != catalogSearchGen) return;
                        ShowCatalogLoader(false);
                        lblStatus.Text = "Ошибка загрузки каталога: " + ex.Message;
                    }));
                }
                catch { }
            }
        });
    }


    private bool isWaitingDownload = false;

    private void BtnDownloadWs_Click(object sender, EventArgs e)
    {
        if (isWaitingDownload)
        {
            WorkshopBrowser.CancelWait = true;
            return;
        }

        if (lstLiveries.SelectedIndex < 0 || lstLiveries.SelectedIndex >= catalogItems.Count)
        {
            MessageBox.Show("Выберите ливрею из каталога для скачивания.", "Внимание", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        WorkshopCatalogItem item = catalogItems[lstLiveries.SelectedIndex];
        isWaitingDownload = true;
        btnDownloadWs.BackColor = Color.FromArgb(140, 95, 20);
        btnDownloadWs.Text = "⏳ Ожидание Steam... (нажмите для отмены)";

        ProgressBar pbWs = new ProgressBar();
        pbWs.Style = ProgressBarStyle.Marquee;
        pbWs.MarqueeAnimationSpeed = 25;
        pbWs.Location = new Point(500, 352);
        pbWs.Size = new Size(390, 16);
        this.Controls.Add(pbWs);

        ThreadPool.QueueUserWorkItem(delegate {
            bool downloaded = false;
            string errMsg = null;
            try
            {
                downloaded = WorkshopBrowser.SubscribeAndDownloadWorkshop(item, gameRoot, msg => {
                    try
                    {
                        this.Invoke(new Action(delegate {
                            if (isWaitingDownload)
                            {
                                lblStatus.Text = msg;
                                btnDownloadWs.Text = "⏳ " + msg;
                            }
                        }));
                    }
                    catch { }
                });
            }
            catch (Exception ex)
            {
                errMsg = ex.Message;
                AppLogger.Error("WORKSHOP", "BtnDownloadWs_Click", ex);
            }

            try
            {
                this.Invoke(new Action(delegate {
                    isWaitingDownload = false;
                    if (pbWs.Parent != null) this.Controls.Remove(pbWs);
                    pbWs.Dispose();
                    btnDownloadWs.BackColor = Color.FromArgb(0, 130, 80);
                    btnDownloadWs.Text = "⬇ Скачать ливрею";

                    if (downloaded)
                    {
                        this.Activate();
                        this.BringToFront();
                        lblStatus.Text = "✔ Ливрея \"" + item.Title + "\" установлена и готова к использованию!";
                        int selIdx = lstLiveries.SelectedIndex;
                        if (selIdx >= 0 && selIdx < lstLiveries.Items.Count)
                        {
                            string curText = lstLiveries.Items[selIdx].ToString();
                            if (!curText.StartsWith("✔ "))
                                lstLiveries.Items[selIdx] = "✔ " + curText + " [УСТАНОВЛЕНО]";
                        }
                    }

                    else if (!string.IsNullOrEmpty(errMsg))
                    {
                        lblStatus.Text = "Ошибка: " + errMsg;
                    }
                    else
                    {
                        lblStatus.Text = "Ожидание завершено. Нажмите «Обн.» во вкладке «Мои ливреи», если подписка уже оформлена.";
                    }
                }));
            }
            catch { }
        });
    }

    private Label lblPreviewLoader = null;
    private int previewLoadGen = 0;

    private void SetPreviewLoaderText(string text)
    {
        if (lblPreviewLoader == null)
        {
            lblPreviewLoader = new Label();
            lblPreviewLoader.Size = new Size(picPreview.Width - 20, 32);
            lblPreviewLoader.Location = new Point(picPreview.Left + 10, picPreview.Top + (picPreview.Height - 32) / 2);
            lblPreviewLoader.BackColor = Color.FromArgb(24, 28, 26);
            lblPreviewLoader.ForeColor = Color.Cyan;
            lblPreviewLoader.TextAlign = ContentAlignment.MiddleCenter;
            lblPreviewLoader.Font = new Font("Consolas", 9.5f, FontStyle.Bold);
            this.Controls.Add(lblPreviewLoader);
        }

        if (string.IsNullOrEmpty(text))
        {
            lblPreviewLoader.Visible = false;
        }
        else
        {
            lblPreviewLoader.Text = text;
            lblPreviewLoader.Visible = true;
            lblPreviewLoader.BringToFront();
        }
    }

    private void UpdatePreview()
    {
        int currentPrevGen = ++previewLoadGen;
        if (isWaitingDownload) WorkshopBrowser.CancelWait = true;
        if (picPreview.Image != null) { picPreview.Image.Dispose(); picPreview.Image = null; }
        SetPreviewLoaderText(null);

        if (isCatalogMode)
        {
            if (lstLiveries.SelectedIndex < 0 || lstLiveries.SelectedIndex >= catalogItems.Count)
            {
                lblPreviewInfo.Text = "Выберите скин из каталога";
                return;
            }
            WorkshopCatalogItem item = catalogItems[lstLiveries.SelectedIndex];
            lblPreviewInfo.Text = item.Title + " (ID: " + item.PublishedFileId + ")";

            if (string.IsNullOrEmpty(item.PreviewUrl))
            {
                SetPreviewLoaderText("Превью отсутствует");
                return;
            }

            SetPreviewLoaderText("⏳ Загрузка превью...");
            string url = item.PreviewUrl;
            ThreadPool.QueueUserWorkItem(delegate {
                try
                {
                    WorkshopBrowser.EnableTls12();
                    using (System.Net.WebClient wc = new System.Net.WebClient())
                    {
                        byte[] data = wc.DownloadData(url);
                        this.Invoke(new Action(delegate {
                            if (currentPrevGen != previewLoadGen || !isCatalogMode) return;
                            SetPreviewLoaderText(null);
                            using (MemoryStream ms = new MemoryStream(data))
                                picPreview.Image = Image.FromStream(ms);
                        }));
                    }
                }
                catch
                {
                    try
                    {
                        this.Invoke(new Action(delegate {
                            if (currentPrevGen == previewLoadGen && isCatalogMode)
                                SetPreviewLoaderText("Не удалось загрузить превью");
                        }));
                    }
                    catch { }
                }
            });
            return;
        }

        LiveryEntry entry = GetSelectedEntry();
        if (entry == null) { lblPreviewInfo.Text = "Выберите ливрею (ПКМ — назначить борт)"; return; }

        lblPreviewInfo.Text = entry.Name + " (" + entry.Aircraft + ")";
        if (!string.IsNullOrEmpty(entry.PreviewPath) && File.Exists(entry.PreviewPath))
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(entry.PreviewPath);
                using (MemoryStream ms = new MemoryStream(bytes))
                    picPreview.Image = Image.FromStream(ms);
            }
            catch { SetPreviewLoaderText("Ошибка чтения превью"); }
        }
        else
        {
            SetPreviewLoaderText("Превью отсутствует");
        }
    }


    private string GetLiveriesRootDir()
    {
        if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL", "CustomLiveries");
        return Path.Combine(gameRoot, "CustomLiveries");
    }

    private void LoadLiveries()
    {
        lstLiveries.Items.Clear();
        currentEntries.Clear();
        string dir = GetLiveriesRootDir();
        string filterAc = cmbAircraft.SelectedItem != null ? cmbAircraft.SelectedItem.ToString() : "Все самолеты";
        string searchQ = txtSearch != null ? txtSearch.Text.Trim() : "";

        List<LiveryEntry> local = LiveryParser.ScanLocal(dir);
        currentEntries.AddRange(local);

        if (chkWorkshop.Checked && !string.IsNullOrEmpty(gameRoot))
        {
            string steamLibRoot = Path.GetFullPath(Path.Combine(gameRoot, @"..\..\.."));
            List<LiveryEntry> ws = LiveryParser.ScanWorkshop(steamLibRoot);
            currentEntries.AddRange(ws);
        }

        filteredEntries.Clear();
        foreach (LiveryEntry e in currentEntries)
        {
            bool matchAc = (filterAc == "Все самолеты" || e.Aircraft.Equals(filterAc, StringComparison.OrdinalIgnoreCase));
            bool matchText = (string.IsNullOrEmpty(searchQ) || e.Name.IndexOf(searchQ, StringComparison.OrdinalIgnoreCase) >= 0);
            if (matchAc && matchText)
            {
                filteredEntries.Add(e);
                string mark = e.Valid ? "" : " [!невалидна]";
                lstLiveries.Items.Add(string.Format("[{0}] {1}  ({2}){3}", e.Source, e.Name, e.Aircraft, mark));
            }
        }
        lblStatus.Text = string.Format("Путь: {0} | Найдено: {1}", dir, lstLiveries.Items.Count);
        UpdatePreview();
    }

    private LiveryEntry GetSelectedEntry()
    {
        if (lstLiveries.SelectedIndex < 0 || lstLiveries.SelectedIndex >= filteredEntries.Count) return null;
        return filteredEntries[lstLiveries.SelectedIndex];
    }

    private void Btn3D_Click(object sender, EventArgs e)
    {
        LiveryEntry entry = GetSelectedEntry();
        if (entry == null) { MessageBox.Show("Выберите ливрею для просмотра.", "Внимание", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        try
        {
            using (Livery3DViewerForm viewer = new Livery3DViewerForm(entry)) { viewer.ShowDialog(this); }
            int idx = lstLiveries.SelectedIndex;
            if (idx >= 0 && idx < filteredEntries.Count)
            {
                string mark = entry.Valid ? "" : " [!невалидна]";
                lstLiveries.Items[idx] = string.Format("[{0}] {1}  ({2}){3}", entry.Source, entry.Name, entry.Aircraft, mark);
                UpdatePreview();
            }
        }
        catch (Exception ex) { MessageBox.Show("Ошибка открытия 3D-просмотра: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void BtnInstallFolder_Click(object sender, EventArgs e)
    {
        using (FolderBrowserDialog fbd = new FolderBrowserDialog())
        {
            fbd.Description = "Выберите папку со скином (livery.cfg/*.vtl и текстуры)";
            if (fbd.ShowDialog() == DialogResult.OK)
            {
                string src = fbd.SelectedPath;
                string folderName = Path.GetFileName(src);
                string dst = Path.Combine(GetLiveriesRootDir(), folderName);
                try
                {
                    Directory.CreateDirectory(dst);
                    foreach (string f in Directory.GetFiles(src, "*.*", SearchOption.AllDirectories))
                    {
                        string rel = f.Substring(src.Length).TrimStart('\\', '/');
                        string target = Path.Combine(dst, rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        File.Copy(f, target, true);
                    }
                    LoadLiveries();
                    MessageBox.Show("Ливрея \"" + folderName + "\" добавлена!", "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { MessageBox.Show("Ошибка установки скина: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }
    }
    private void BtnInstallZip_Click(object sender, EventArgs e)
    {
        using (OpenFileDialog ofd = new OpenFileDialog() { Filter = "ZIP-архив (.zip)|.zip" })
        {
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                if (LiveryParser.InstallZip(ofd.FileName, GetLiveriesRootDir()))
                {
                    LoadLiveries();
                    MessageBox.Show("Архив успешно распакован и установлен!", "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else MessageBox.Show("Не удалось распаковать архив.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
    private void BtnDelete_Click(object sender, EventArgs e)
    {
        LiveryEntry entry = GetSelectedEntry();
        if (entry == null) { MessageBox.Show("Выберите ливрею для удаления.", "Внимание", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        string msg = entry.Source == "Workshop"
            ? "Отписаться и удалить Workshop-ливрею \"" + entry.Name + "\"?"
            : "Удалить камуфляж \"" + entry.Name + "\"?";

        if (MessageBox.Show(msg, "Подтверждение удаления", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            try
            {
                if (entry.Source == "Workshop")
                    WorkshopBrowser.UnsubscribeAndDeleteWorkshop(entry.FolderPath, gameRoot);
                else if (Directory.Exists(entry.FolderPath))
                    Directory.Delete(entry.FolderPath, true);

                LoadLiveries();
                lblStatus.Text = "Ливрея \"" + entry.Name + "\" удалена.";
            }
            catch (Exception ex) { MessageBox.Show("Ошибка удаления: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }

}

// =========================================================================
// === ТАКТИЧЕСКИЙ ЖУРНАЛ ВЫЛЕТОВ (без изменений логики) ===================
// =========================================================================
public class FlightLogbookForm : Form
{
    private ListView lvLog;
    private Label lblSummary;
    private Label lblSubStats;

    public FlightLogbookForm()
    {
        this.Text = "VTOL QoL — Журнал боевых вылетов";
        DarkWindowHelper.Apply(this);
        this.Size = new Size(1060, 620);
        this.MinimumSize = new Size(880, 460);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.Sizable;
        this.MaximizeBox = true;
        this.BackColor = Color.FromArgb(18, 22, 20);
        this.ForeColor = Color.FromArgb(0, 255, 102);
        this.Font = new Font("Consolas", 9.5f);

        Label lblTitle = new Label() { Text = "/// ТАКТИЧЕСКИЙ ЖУРНАЛ БОЕВЫХ ВЫЛЕТОВ ///", Left = 20, Top = 14, AutoSize = true, Font = new Font("Consolas", 11f, FontStyle.Bold) };
        this.Controls.Add(lblTitle);

        Panel pnlStats = new Panel() { Left = 20, Top = 42, Width = 1004, Height = 52, BackColor = Color.FromArgb(25, 32, 28), BorderStyle = BorderStyle.FixedSingle };
        pnlStats.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        this.Controls.Add(pnlStats);

        lblSummary = new Label() { Left = 12, Top = 6, Width = 975, Height = 20, ForeColor = Color.Cyan, Font = new Font("Consolas", 9.5f, FontStyle.Bold) };
        lblSummary.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        pnlStats.Controls.Add(lblSummary);

        lblSubStats = new Label() { Left = 12, Top = 27, Width = 975, Height = 20, ForeColor = Color.FromArgb(170, 230, 190) };
        lblSubStats.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        pnlStats.Controls.Add(lblSubStats);

        lvLog = new ListView();
        lvLog.Location = new Point(20, 104);
        lvLog.Size = new Size(1004, 420);
        lvLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        lvLog.View = View.Details;
        lvLog.FullRowSelect = true;
        lvLog.OwnerDraw = true;
        lvLog.BackColor = Color.FromArgb(20, 24, 22);
        lvLog.ForeColor = Color.White;
        lvLog.BorderStyle = BorderStyle.FixedSingle;

        lvLog.ColumnWidthChanging += (s, e) => {
            e.Cancel = true;
            e.NewWidth = lvLog.Columns[e.ColumnIndex].Width;
        };

        lvLog.DrawColumnHeader += (s, e) => {
            Rectangle fillRect = e.Bounds;
            if (e.ColumnIndex == lvLog.Columns.Count - 1)
                fillRect.Width += 200; // перекрываем пустой белый хвост заголовка справа
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(28, 36, 32)))
                e.Graphics.FillRectangle(bg, fillRect);
            using (Pen borderPen = new Pen(Color.FromArgb(45, 65, 55)))
                e.Graphics.DrawRectangle(borderPen, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, new Font("Consolas", 9.5f, FontStyle.Bold),
                new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height),
                Color.FromArgb(0, 255, 102), TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        };

        lvLog.DrawSubItem += (s, e) => {
            bool isSelected = e.Item.Selected;
            Color rowBg = isSelected
                ? Color.FromArgb(35, 65, 52)
                : (e.ItemIndex % 2 == 0 ? Color.FromArgb(20, 24, 22) : Color.FromArgb(24, 29, 26));
            using (SolidBrush bg = new SolidBrush(rowBg))
                e.Graphics.FillRectangle(bg, e.Bounds);
            using (Pen linePen = new Pen(Color.FromArgb(32, 40, 36)))
                e.Graphics.DrawLine(linePen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, lvLog.Font,
                new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height),
                e.Item.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };
        lvLog.Columns.Add("Дата и время", 145);
        lvLog.Columns.Add("Статус", 72);
        lvLog.Columns.Add("Борт", 195);
        lvLog.Columns.Add("Позывной", 145);
        lvLog.Columns.Add("Налет", 58);
        lvLog.Columns.Add("Сбито", 55);
        lvLog.Columns.Add("Детали события (цель, противник, оружие)", 310);
        this.Controls.Add(lvLog);

        Button btnRefresh = new Button() { Text = "Обновить", Left = 20, Top = 536, Width = 120, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(35, 45, 40), ForeColor = Color.FromArgb(0, 255, 102) };
        btnRefresh.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnRefresh.Click += (s, e) => LoadLog();
        this.Controls.Add(btnRefresh);

        Button btnExport = new Button() { Text = "Экспорт отчета", Left = 150, Top = 536, Width = 145, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 110, 140), ForeColor = Color.White };
        btnExport.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnExport.Click += BtnExport_Click;
        this.Controls.Add(btnExport);

        Button btnClear = new Button() { Text = "Очистить журнал", Left = 305, Top = 536, Width = 145, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(120, 30, 30), ForeColor = Color.White };
        btnClear.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnClear.Click += BtnClear_Click;
        this.Controls.Add(btnClear);

        Button btnClose = new Button() { Text = "Закрыть", Left = 894, Top = 536, Width = 130, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(40, 50, 45), ForeColor = Color.White };
        btnClose.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        btnClose.Click += (s, e) => this.Close();
        this.Controls.Add(btnClose);

        Action fitLastColumn = () => {
            if (lvLog.Columns.Count >= 7)
            {
                int fixedW = 0;
                for (int i = 0; i < 6; i++) fixedW += lvLog.Columns[i].Width;
                int rem = lvLog.ClientSize.Width - fixedW;
                if (rem > 220) lvLog.Columns[6].Width = rem;
            }
        };

        this.Shown += (s, e) => fitLastColumn();
        this.SizeChanged += (s, e) => fitLastColumn();

        LoadLog();
    }


    private string GetLogFilePath()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL", "flight_logbook.txt");
    }

    private void LoadLog()
    {
        lvLog.Items.Clear();
        string path = GetLogFilePath();
        int totalKills = 0, totalSorties = 0, totalLosses = 0;
        Dictionary<string, int> planeUsage = new Dictionary<string, int>();

        if (File.Exists(path))
        {
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string line = lines[i];
                if (string.IsNullOrEmpty(line.Trim())) continue;
                string[] p = line.Split('|');
                string time = p.Length > 0 ? p[0] : "";
                string ev = p.Length > 1 ? p[1] : "";
                string plane = p.Length > 2 ? p[2] : "";
                string cs = p.Length > 3 ? p[3] : "";
                string dur = p.Length > 4 ? p[4] : "—";
                string killsStr = p.Length > 5 ? p[5] : "0";
                string note = p.Length > 6 ? p[6] : "";

                ListViewItem item = new ListViewItem(time);
                item.SubItems.Add(ev); item.SubItems.Add(plane); item.SubItems.Add(cs);
                item.SubItems.Add(dur); item.SubItems.Add(killsStr); item.SubItems.Add(note);

                if (ev == "KILL") { item.ForeColor = Color.Cyan; totalKills++; }
                else if (ev == "EJECT" || ev == "KIA") { item.ForeColor = Color.OrangeRed; totalLosses++; }
                else if (ev == "SORTIE")
                {
                    item.ForeColor = Color.LightGreen;
                    totalSorties++;
                    if (!string.IsNullOrEmpty(plane) && plane.IndexOf("ангар", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        if (!planeUsage.ContainsKey(plane)) planeUsage[plane] = 0;
                        planeUsage[plane]++;
                    }
                }
                else item.ForeColor = Color.Silver;
                lvLog.Items.Add(item);
            }
        }

        string favPlane = "Нет данных"; int maxCount = 0;
        foreach (KeyValuePair<string, int> kv in planeUsage) if (kv.Value > maxCount) { maxCount = kv.Value; favPlane = kv.Key; }
        double kd = totalLosses > 0 ? Math.Round((double)totalKills / totalLosses, 2) : totalKills;
        lblSummary.Text = string.Format("ВЫЛЕТОВ: {0}  |  СБИТО: {1}  |  ПОТЕРЬ: {2}  |  K/D: {3}", totalSorties, totalKills, totalLosses, kd);

        string weapSummary = LoadWeaponStatsSummary();
        lblSubStats.Text = string.Format("Борт: {0} ({1})  |  {2}", favPlane, maxCount, weapSummary);
    }

    private string LoadWeaponStatsSummary()
    {
        try
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL", "weapon_stats.ini");
            if (!File.Exists(path)) return "Статистика оружия: ожидание пусков";
            List<string> parts = new List<string>();
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                string[] p = line.Split('|');
                if (p.Length >= 3)
                {
                    int fired, hits;
                    int.TryParse(p[1], out fired);
                    int.TryParse(p[2], out hits);
                    if (fired > 0)
                    {
                        int acc = Math.Min(100, (int)Math.Round((hits * 100.0) / fired));
                        parts.Add(string.Format("{0}: {1}/{2} ({3}%)", p[0], hits, fired, acc));
                    }
                    else if (hits > 0)
                    {
                        parts.Add(string.Format("{0}: {1} сбито", p[0], hits));
                    }
                }
            }
            return parts.Count > 0 ? ("Оружие: " + string.Join(" | ", parts.ToArray())) : "Статистика оружия: пусто";
        }
        catch { return ""; }
    }

    private void BtnClear_Click(object sender, EventArgs e)
    {
        if (MessageBox.Show("Очистить историю вылетов и статистику оружия?", "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            try
            {
                string path = GetLogFilePath();
                if (File.Exists(path)) File.Delete(path);

                string weapPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL", "weapon_stats.ini");
                if (File.Exists(weapPath)) File.Delete(weapPath);

                LoadLog();
            }
            catch (Exception ex) { MessageBox.Show("Ошибка: " + ex.Message); }
        }
    }


    private void BtnExport_Click(object sender, EventArgs e)
    {
        using (SaveFileDialog sfd = new SaveFileDialog() { Filter = "Текстовый отчет (*.txt)|*.txt", FileName = "VTOL_Flight_Report.txt" })
        {
            if (sfd.ShowDialog() == DialogResult.OK)
            {
                string path = GetLogFilePath();
                if (File.Exists(path)) File.Copy(path, sfd.FileName, true);
                MessageBox.Show("Отчет сохранен: " + sfd.FileName, "Экспорт завершен", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}

// =========================================================================
// === БЭКАП И ЗАЩИТА СОХРАНЕНИЙ (без изменений логики) ====================
// =========================================================================
public class SaveVaultForm : Form
{
    private ListBox lstBackups;
    private Label lblSaveStatus;

    public SaveVaultForm()
    {
        this.Text = "VTOL QoL — Резервные копии сохранений";
        DarkWindowHelper.Apply(this);
        this.Size = new Size(620, 480);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.BackColor = Color.FromArgb(18, 22, 20);
        this.ForeColor = Color.FromArgb(0, 255, 102);
        this.Font = new Font("Consolas", 9.5f);

        Label lbl = new Label() { Text = "АРХИВ СНАПШОТОВ СОХРАНЕНИЙ:", Left = 20, Top = 15, AutoSize = true, Font = new Font("Consolas", 10f, FontStyle.Bold) };
        this.Controls.Add(lbl);

        lblSaveStatus = new Label() { Left = 20, Top = 40, Width = 560, ForeColor = Color.Silver };
        this.Controls.Add(lblSaveStatus);

        lstBackups = new ListBox();
        lstBackups.Location = new Point(20, 68);
        lstBackups.Size = new Size(410, 310);
        lstBackups.BackColor = Color.FromArgb(24, 28, 26);
        lstBackups.ForeColor = Color.White;
        lstBackups.BorderStyle = BorderStyle.FixedSingle;
        this.Controls.Add(lstBackups);

        int top = 68, btnW = 155, btnH = 38, gap = 10;
        Button btnCreate = CreateBtn("+ Создать бэкап", ref top, btnW, btnH, gap, Color.FromArgb(0, 140, 60));
        btnCreate.Click += (s, e) => CreateSnapshot();
        Button btnRestore = CreateBtn("Восстановить", ref top, btnW, btnH, gap, Color.FromArgb(160, 100, 0));
        btnRestore.Click += BtnRestore_Click;
        Button btnDelete = CreateBtn("Удалить архив", ref top, btnW, btnH, gap, Color.FromArgb(140, 35, 35));
        btnDelete.Click += BtnDelete_Click;
        Button btnOpenDir = CreateBtn("Папка бэкапов", ref top, btnW, btnH, gap, Color.FromArgb(40, 50, 45));
        btnOpenDir.Click += (s, e) => { string dir = GetBackupsDir(); if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); Process.Start("explorer.exe", dir); };

        Button btnClose = new Button() { Text = "Закрыть", Left = 440, Top = 390, Width = 155, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(30, 36, 33), ForeColor = Color.White };
        btnClose.Click += (s, e) => this.Close();
        this.Controls.Add(btnClose);

        RefreshBackups();
    }

    private Button CreateBtn(string text, ref int top, int width, int height, int gap, Color backColor)
    {
        Button btn = new Button() { Text = text, Location = new Point(440, top), Size = new Size(width, height), FlatStyle = FlatStyle.Flat, BackColor = backColor, ForeColor = Color.White };
        this.Controls.Add(btn);
        top += height + gap;
        return btn;
    }

    private string GetSaveDataDir() { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Boundless Dynamics, LLC", "VTOLVR", "SaveData"); }
    private string GetBackupsDir() { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL", "SaveBackups"); }

    private void RefreshBackups()
    {
        lstBackups.Items.Clear();
        string bDir = GetBackupsDir();
        if (Directory.Exists(bDir))
        {
            string[] dirs = Directory.GetDirectories(bDir);
            Array.Sort(dirs); Array.Reverse(dirs);
            foreach (string d in dirs) lstBackups.Items.Add(Path.GetFileName(d));
        }
        string sDir = GetSaveDataDir();
        lblSaveStatus.Text = Directory.Exists(sDir) ? "Папка сохранений: " + sDir : "Папка сохранений игры пока не создана.";
    }

    private void CreateSnapshot()
    {
        string sDir = GetSaveDataDir();
        if (!Directory.Exists(sDir)) { MessageBox.Show("Папка сохранений не найдена:\n" + sDir, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        string targetDir = Path.Combine(GetBackupsDir(), "Backup_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        try
        {
            Directory.CreateDirectory(targetDir);
            foreach (string f in Directory.GetFiles(sDir, "*.*", SearchOption.AllDirectories))
            {
                string rel = f.Substring(sDir.Length).TrimStart('\\', '/');
                string dst = Path.Combine(targetDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.Copy(f, dst, true);
            }
            RefreshBackups();
            MessageBox.Show("Бэкап создан: " + Path.GetFileName(targetDir), "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show("Ошибка: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void BtnRestore_Click(object sender, EventArgs e)
    {
        if (lstBackups.SelectedItem == null) { MessageBox.Show("Выберите бэкап.", "Внимание", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        string bSource = Path.Combine(GetBackupsDir(), lstBackups.SelectedItem.ToString());
        string sDir = GetSaveDataDir();
        if (MessageBox.Show("Восстановить сохранения? Текущий прогресс будет перезаписан.", "Внимание", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
        {
            try
            {
                Directory.CreateDirectory(sDir);
                foreach (string f in Directory.GetFiles(bSource, "*.*", SearchOption.AllDirectories))
                {
                    string rel = f.Substring(bSource.Length).TrimStart('\\', '/');
                    string dst = Path.Combine(sDir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    File.Copy(f, dst, true);
                }
                MessageBox.Show("Сохранения восстановлены!", "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show("Ошибка: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }

    private void BtnDelete_Click(object sender, EventArgs e)
    {
        if (lstBackups.SelectedItem == null) return;
        string target = Path.Combine(GetBackupsDir(), lstBackups.SelectedItem.ToString());
        if (MessageBox.Show("Удалить бэкап?", "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            try { if (Directory.Exists(target)) Directory.Delete(target, true); RefreshBackups(); }
            catch (Exception ex) { MessageBox.Show("Ошибка: " + ex.Message); }
        }
    }
}
// =========================================================================
// === СКАНЕР ПУБЛИЧНЫХ ЛОББИ VTOL VR (STEAMWORKS MATCHMAKING + SDR TEST) ===
// =========================================================================
public class VtolLobbyInfo
{
    public ulong LobbyId;
    public string Name;
    public string Mission;
    public string State;
    public int Members;
    public int MaxMembers;
    public bool HasPassword;
    public string ConnectHint;
    public bool IsReachableWarning;
    public Dictionary<string, string> RawData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public static class SteamLobbyScanner
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate bool SteamAPI_InitDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SteamAPI_RunCallbacksDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr SteamMatchmakingDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong Flat_RequestLobbyList(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Flat_AddRequestLobbyListDistanceFilter(IntPtr self, int eLobbyDistanceFilter);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Flat_AddRequestLobbyListResultCountFilter(IntPtr self, int cMaxResults);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong Flat_GetLobbyByIndex(IntPtr self, int iLobby);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Flat_GetNumLobbyMembers(IntPtr self, ulong steamIDLobby);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Flat_GetLobbyMemberLimit(IntPtr self, ulong steamIDLobby);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Flat_GetLobbyDataCount(IntPtr self, ulong steamIDLobby);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate bool Flat_GetLobbyDataByIndex(IntPtr self, ulong steamIDLobby, int iLobbyData,
    IntPtr pchKey, int cchKeyBufferSize, IntPtr pchValue, int cchValueBufferSize);

    private static T GetExport<T>(IntPtr hMod, string procName) where T : class
    {
        IntPtr p = GetProcAddress(hMod, procName);
        if (p == IntPtr.Zero) throw new Exception("Не найден экспорт Flat API: " + procName);
        return (T)(object)Marshal.GetDelegateForFunctionPointer(p, typeof(T));
    }

    private static bool rawKeysDumped = false;

    public static string DetectActiveVpnOrTunnel()
    {
        try
        {
            foreach (System.Net.NetworkInformation.NetworkInterface ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                string desc = (ni.Description + " " + ni.Name).ToLowerInvariant();
                if (desc.Contains("teredo") || desc.Contains("isatap") || desc.Contains("6to4") || desc.Contains("loopback"))
                    continue;

                if (desc.Contains("wireguard") || desc.Contains("openvpn") || desc.Contains("tap-windows") ||
                    desc.Contains("wintun") || desc.Contains("vpn") ||
                    desc.Contains("radmin") || desc.Contains("zerotier") || desc.Contains("hamachi") ||
                    desc.Contains("nekoray") || desc.Contains("v2ray") || desc.Contains("clash") || desc.Contains("amnezia"))
                {
                    return ni.Name;
                }
            }
        }
        catch { }
        return "";
    }

    public static bool CheckSteamRelayReachable(out int pingMs)
    {
        pingMs = -1;
        try
        {
            WorkshopBrowser.EnableTls12();
            Stopwatch sw = Stopwatch.StartNew();
            System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create("https://steampowered.com");
            req.Method = "GET";
            req.Timeout = 2500;
            using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)req.GetResponse())
            {
                sw.Stop();
                pingMs = (int)sw.ElapsedMilliseconds;
                return ((int)resp.StatusCode == 200);
            }
        }
        catch { return false; }
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SteamAPI_InitFlatDelegate(IntPtr pOutErrMsg);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate void AddRequestLobbyListDistanceFilterDelegate(IntPtr self, int eLobbyDistanceFilter);

    public static List<VtolLobbyInfo> FetchPublicLobbies(string gameRoot, out string networkDiagnostic)
    {
        AppLogger.Log("LOBBY", "=== Старт сканирования публичных лобби VTOL VR (Flat API) ===");
        List<VtolLobbyInfo> result = new List<VtolLobbyInfo>();
        string vpnName = DetectActiveVpnOrTunnel();
        int relayPing;
        bool sdrOk = CheckSteamRelayReachable(out relayPing);

        if (!string.IsNullOrEmpty(vpnName))
            networkDiagnostic = string.Format("⚠ Активен VPN/туннель [{0}] | Steam SDR: {1}", vpnName, sdrOk ? (relayPing + " мс") : "НЕДОСТУПЕН");
        else
            networkDiagnostic = string.Format("Прямое подключение | Steam SDR Relay: {0}", sdrOk ? ("OK (" + relayPing + " мс)") : "ОШИБКА СВЯЗИ");

        if (Process.GetProcessesByName("steam").Length == 0)
            throw new Exception("Клиент Steam не запущен. Запустите Steam для получения списка лобби.");

        if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot))
            throw new Exception("Не найдена папка игры VTOL VR.");

        string dllPath = Path.Combine(gameRoot, @"VTOLVR_Data\Plugins\x86_64\steam_api64.dll");
        if (!File.Exists(dllPath))
            dllPath = Path.Combine(gameRoot, @"VTOLVR_Data\Plugins\steam_api64.dll");
        if (!File.Exists(dllPath))
            throw new Exception("Библиотека steam_api64.dll не найдена.");

        Environment.SetEnvironmentVariable("SteamAppId", "667970");
        Environment.SetEnvironmentVariable("SteamGameId", "667970");

        IntPtr hMod = LoadLibrary(dllPath);
        if (hMod == IntPtr.Zero) throw new Exception("Не удалось загрузить steam_api64.dll.");

        SteamAPI_InitDelegate fnInit = GetExport<SteamAPI_InitDelegate>(hMod, "SteamAPI_Init");
        if (!fnInit()) throw new Exception("SteamAPI_Init вернул false.");

        SteamAPI_RunCallbacksDelegate fnCallbacks = GetExport<SteamAPI_RunCallbacksDelegate>(hMod, "SteamAPI_RunCallbacks");
        SteamMatchmakingDelegate fnMm = GetExport<SteamMatchmakingDelegate>(hMod, "SteamAPI_SteamMatchmaking_v009");
        IntPtr mmPtr = fnMm();
        if (mmPtr == IntPtr.Zero) throw new Exception("ISteamMatchmaking вернул null.");

        Flat_RequestLobbyList fnReqList = GetExport<Flat_RequestLobbyList>(hMod, "SteamAPI_ISteamMatchmaking_RequestLobbyList");
        Flat_GetLobbyByIndex fnGetLobby = GetExport<Flat_GetLobbyByIndex>(hMod, "SteamAPI_ISteamMatchmaking_GetLobbyByIndex");
        Flat_GetNumLobbyMembers fnNumMembers = GetExport<Flat_GetNumLobbyMembers>(hMod, "SteamAPI_ISteamMatchmaking_GetNumLobbyMembers");
        Flat_GetLobbyMemberLimit fnMemberLimit = GetExport<Flat_GetLobbyMemberLimit>(hMod, "SteamAPI_ISteamMatchmaking_GetLobbyMemberLimit");
        Flat_GetLobbyDataCount fnDataCount = GetExport<Flat_GetLobbyDataCount>(hMod, "SteamAPI_ISteamMatchmaking_GetLobbyDataCount");
        Flat_GetLobbyDataByIndex fnDataByIdx = GetExport<Flat_GetLobbyDataByIndex>(hMod, "SteamAPI_ISteamMatchmaking_GetLobbyDataByIndex");

        try
        {
            Flat_AddRequestLobbyListDistanceFilter fnDist = GetExport<Flat_AddRequestLobbyListDistanceFilter>(hMod, "SteamAPI_ISteamMatchmaking_AddRequestLobbyListDistanceFilter");
            Flat_AddRequestLobbyListResultCountFilter fnCount = GetExport<Flat_AddRequestLobbyListResultCountFilter>(hMod, "SteamAPI_ISteamMatchmaking_AddRequestLobbyListResultCountFilter");
            fnDist(mmPtr, 3); // 3 = k_ELobbyDistanceFilterWorldwide (все серверы мира)
            fnCount(mmPtr, 100);
        }
        catch (Exception exF) { AppLogger.Log("LOBBY", "Фильтр дистанции пропущен: " + exF.Message); }

        ulong callHandle = fnReqList(mmPtr);
        AppLogger.Log("LOBBY", "Flat RequestLobbyList() -> SteamAPICall_t=" + callHandle);

        for (int t = 0; t < 35; t++)
        {
            fnCallbacks();
            Thread.Sleep(100);
        }

        int foundCount = 0;
        for (int i = 0; i < 100; i++)
        {
            ulong lobbyId = fnGetLobby(mmPtr, i);
            if (lobbyId == 0) break;
            foundCount++;

            VtolLobbyInfo info = new VtolLobbyInfo();
            info.LobbyId = lobbyId;
            info.Members = fnNumMembers(mmPtr, lobbyId);
            info.MaxMembers = fnMemberLimit(mmPtr, lobbyId);

            int dCount = fnDataCount(mmPtr, lobbyId);
            IntPtr keyBuf = Marshal.AllocHGlobal(512);
            IntPtr valBuf = Marshal.AllocHGlobal(2048);
            try
            {
                for (int k = 0; k < dCount; k++)
                {
                    Marshal.WriteByte(keyBuf, 0, 0);
                    Marshal.WriteByte(valBuf, 0, 0);
                    if (fnDataByIdx(mmPtr, lobbyId, k, keyBuf, 512, valBuf, 2048))
                    {
                        string kStr = Marshal.PtrToStringAnsi(keyBuf) ?? "";
                        string vStr = Marshal.PtrToStringAnsi(valBuf) ?? "";
                        // Если строка в UTF-8 (например, кириллица в названии сервера)
                        byte[] rawValBytes = new byte[2048];
                        Marshal.Copy(valBuf, rawValBytes, 0, 2048);
                        int zeroIdx = Array.IndexOf(rawValBytes, (byte)0);
                        if (zeroIdx > 0) vStr = Encoding.UTF8.GetString(rawValBytes, 0, zeroIdx);

                        if (!string.IsNullOrEmpty(kStr))
                            info.RawData[kStr] = vStr;
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(keyBuf);
                Marshal.FreeHGlobal(valBuf);
            }

            if (!rawKeysDumped)
            {
                AppLogger.Log("LOBBY_META", string.Format("Лобби #{0}: dCount={1}, RawData.Count={2}", lobbyId, dCount, info.RawData.Count));
                if (info.RawData.Count > 0)
                {
                    rawKeysDumped = true;
                    List<string> kvLog = new List<string>();
                    foreach (KeyValuePair<string, string> kv in info.RawData) kvLog.Add(kv.Key + "=" + kv.Value);
                    AppLogger.Log("LOBBY_META", "Ключи метаданных: " + string.Join(" | ", kvLog.ToArray()));
                }
            }

            info.Name = PickLobbyField(info.RawData, "lName", "name", "lobbyName", "serverName");
            if (string.IsNullOrEmpty(info.Name)) info.Name = "Lobby #" + lobbyId;

            info.Mission = PickLobbyField(info.RawData, "scn", "mission", "scenario", "campaign", "map");
            if (string.IsNullOrEmpty(info.Mission)) info.Mission = "—";

            string rawState = PickLobbyField(info.RawData, "gState", "state", "status", "missionState");
            info.State = NormalizeLobbyState(rawState);

            // В VTOL VR pwh="1" или "" означает открытое лобби без пароля; при наличии пароля в pwh лежит его хеш (длина > 4)
            string pwHash = PickLobbyField(info.RawData, "pwh", "pw", "password", "hasPassword");
            info.HasPassword = (!string.IsNullOrEmpty(pwHash) && pwHash != "1" && pwHash != "0" &&
                                !pwHash.Equals("false", StringComparison.OrdinalIgnoreCase) && pwHash.Length > 3);

            // Пропускаем неинициализированные комнаты (если lReady == "0")
            string lReady = PickLobbyField(info.RawData, "lReady");
            if (lReady == "0") continue;


            string maxPStr = PickLobbyField(info.RawData, "maxP", "spaces");
            int parsedMaxP;
            if (info.MaxMembers <= 0 && int.TryParse(maxPStr, out parsedMaxP))
                info.MaxMembers = parsedMaxP;


            if (!sdrOk) { info.IsReachableWarning = true; info.ConnectHint = "✖ Нет связи со Steam Relay"; }
            else if (!string.IsNullOrEmpty(vpnName)) { info.IsReachableWarning = true; info.ConnectHint = "⚠ Активен VPN (возможен таймаут P2P)"; }
            else if (info.MaxMembers > 0 && info.Members >= info.MaxMembers) { info.IsReachableWarning = true; info.ConnectHint = "Сервер заполнен"; }
            else { info.IsReachableWarning = false; info.ConnectHint = "✔ Готов к подключению"; }

            result.Add(info);
        }

        AppLogger.Log("LOBBY", "Итого получено настоящих лобби: " + foundCount);
        return result;
    }

    private static string PickLobbyField(Dictionary<string, string> dict, params string[] keys)
    {
        foreach (string k in keys) { string v; if (dict.TryGetValue(k, out v) && !string.IsNullOrEmpty(v)) return v.Trim(); }
        return "";
    }

    private static string NormalizeLobbyState(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "Брифинг / Лобби";
        string s = raw.ToLowerInvariant();
        if (s == "1" || s == "true" || s.Contains("mission") || s.Contains("flight") || s.Contains("playing") || s.Contains("ingame") || s.Contains("active")) return "В миссии";
        if (s.Contains("debrief") || s.Contains("end") || s.Contains("result")) return "Дебрифинг";
        if (s == "0" || s == "false" || s.Contains("brief") || s.Contains("lobby") || s.Contains("wait")) return "Брифинг";
        return raw;
    }
}
// =========================================================================
// === ОКНО БРАУЗЕРА ПУБЛИЧНЫХ СЕРВЕРОВ VTOL VR (LOBBIES + VPN DIAGNOSTICS) =
// =========================================================================
public class ServerBrowserForm : Form
{
    private string gameRoot;
    private ListView lvServers;
    private Label lblNetBanner;
    private Label lblSummary;
    private TextBox txtFilter;
    private CheckBox chkHideFull;
    private Button btnRefresh;
    private List<VtolLobbyInfo> allLobbies = new List<VtolLobbyInfo>();
    private List<VtolLobbyInfo> shownLobbies = new List<VtolLobbyInfo>();

    public ServerBrowserForm(string root)
    {
        this.gameRoot = root;
        this.Text = "VTOL QoL — Публичные серверы (Steam Matchmaking)";
        DarkWindowHelper.Apply(this);
        this.Size = new Size(1020, 600);
        this.MinimumSize = new Size(860, 440);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.Sizable;
        this.MaximizeBox = true;
        this.BackColor = Color.FromArgb(18, 22, 20);
        this.ForeColor = Color.FromArgb(0, 255, 102);
        this.Font = new Font("Consolas", 9.5f);

        Label lblTitle = new Label() { Text = "/// МОНИТОРИНГ ПУБЛИЧНЫХ СЕРВЕРОВ VTOL VR ///", Left = 20, Top = 12, AutoSize = true, Font = new Font("Consolas", 11f, FontStyle.Bold) };
        this.Controls.Add(lblTitle);

        Panel pnlTop = new Panel() { Left = 20, Top = 38, Width = 964, Height = 54, BackColor = Color.FromArgb(24, 30, 27), BorderStyle = BorderStyle.FixedSingle };
        pnlTop.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        this.Controls.Add(pnlTop);

        lblNetBanner = new Label() { Left = 10, Top = 6, Width = 940, Height = 18, ForeColor = Color.Cyan, Font = new Font("Consolas", 9f, FontStyle.Bold), Text = "Проверка сетевого маршрута и Steam Relay..." };
        lblNetBanner.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        pnlTop.Controls.Add(lblNetBanner);

        Label lblSearch = new Label() { Text = "Поиск:", Left = 10, Top = 30, AutoSize = true, ForeColor = Color.LightGray };
        pnlTop.Controls.Add(lblSearch);

        txtFilter = new TextBox() { Left = 64, Top = 27, Width = 230, Height = 22, BackColor = Color.FromArgb(30, 38, 34), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        txtFilter.TextChanged += (s, e) => ApplyFilter();
        pnlTop.Controls.Add(txtFilter);

        chkHideFull = new CheckBox() { Text = "Скрыть полные", Left = 310, Top = 28, AutoSize = true, ForeColor = Color.FromArgb(200, 235, 215), Checked = false };
        chkHideFull.CheckedChanged += (s, e) => ApplyFilter();
        pnlTop.Controls.Add(chkHideFull);

        lblSummary = new Label() { Left = 460, Top = 30, Width = 490, Height = 18, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.FromArgb(160, 225, 185), Text = "Загрузка..." };
        lblSummary.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        pnlTop.Controls.Add(lblSummary);

        lvServers = new ListView();
        lvServers.Location = new Point(20, 100);
        lvServers.Size = new Size(964, 405);
        lvServers.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        lvServers.View = View.Details;
        lvServers.FullRowSelect = true;
        lvServers.OwnerDraw = true;
        lvServers.BackColor = Color.FromArgb(20, 24, 22);
        lvServers.ForeColor = Color.White;
        lvServers.BorderStyle = BorderStyle.FixedSingle;

        lvServers.ColumnWidthChanging += (s, e) => { e.Cancel = true; e.NewWidth = lvServers.Columns[e.ColumnIndex].Width; };

        lvServers.DrawColumnHeader += (s, e) => {
            Rectangle fillRect = e.Bounds;
            if (e.ColumnIndex == lvServers.Columns.Count - 1) fillRect.Width += 200;
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(28, 36, 32))) e.Graphics.FillRectangle(bg, fillRect);
            using (Pen borderPen = new Pen(Color.FromArgb(45, 65, 55))) e.Graphics.DrawRectangle(borderPen, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, new Font("Consolas", 9.5f, FontStyle.Bold),
                new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height),
                Color.FromArgb(0, 255, 102), TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        };

        lvServers.DrawSubItem += (s, e) => {
            Color rowBg = e.Item.Selected ? Color.FromArgb(35, 65, 52) : (e.ItemIndex % 2 == 0 ? Color.FromArgb(20, 24, 22) : Color.FromArgb(24, 29, 26));
            using (SolidBrush bg = new SolidBrush(rowBg)) e.Graphics.FillRectangle(bg, e.Bounds);
            using (Pen linePen = new Pen(Color.FromArgb(32, 40, 36))) e.Graphics.DrawLine(linePen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, lvServers.Font,
                new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height),
                e.Item.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };

        lvServers.Columns.Add("Название сервера", 285);
        lvServers.Columns.Add("Миссия / Сценарий", 245);
        lvServers.Columns.Add("Статус", 130);
        lvServers.Columns.Add("Игроки", 85);
        lvServers.Columns.Add("Совместимость / Сеть", 205);
        this.Controls.Add(lvServers);

        btnRefresh = new Button() { Text = "🔄 Обновить список", Left = 20, Top = 516, Width = 175, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(35, 45, 40), ForeColor = Color.FromArgb(0, 255, 102) };
        btnRefresh.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnRefresh.Click += (s, e) => LoadServersAsync();
        this.Controls.Add(btnRefresh);

        Button btnRelayTest = new Button() { Text = "🔍 Тест P2P / VPN", Left = 205, Top = 516, Width = 175, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 65, 80), ForeColor = Color.Cyan };
        btnRelayTest.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnRelayTest.Click += (s, e) => RunRelayDiagnostics();
        this.Controls.Add(btnRelayTest);

        Button btnClose = new Button() { Text = "Закрыть", Left = 854, Top = 516, Width = 130, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(40, 50, 45), ForeColor = Color.White };
        btnClose.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        btnClose.Click += (s, e) => this.Close();
        this.Controls.Add(btnClose);

        Action fitLastCol = () => {
            if (lvServers.Columns.Count >= 5)
            {
                int fixedW = 0;
                for (int i = 0; i < 4; i++) fixedW += lvServers.Columns[i].Width;
                int rem = lvServers.ClientSize.Width - fixedW;
                if (rem > 160) lvServers.Columns[4].Width = rem;
            }
        };
        this.Shown += (s, e) => { fitLastCol(); LoadServersAsync(); };
        this.SizeChanged += (s, e) => fitLastCol();
    }
    private void RunRelayDiagnostics()
    {
        string vpn = SteamLobbyScanner.DetectActiveVpnOrTunnel();
        int ping;
        bool ok = SteamLobbyScanner.CheckSteamRelayReachable(out ping);
        string msg = string.Format(
            "Активный VPN / туннельный адаптер: {0}\n" +
            "Связь с кластером Steam Datagram Relay (Valve P2P): {1}\n\n" +
            "Совет при ошибках подключения:\n" +
            "• Если включён VPN или DPI-обход, лобби могут отображаться в списке, но при входе зависать на подключении из-за блокировки UDP-трафика.\n" +
            "• Попробуйте отключить VPN или добавить VTOLVR.exe и steam.exe в исключения (Split Tunneling).",
            string.IsNullOrEmpty(vpn) ? "Не обнаружен (прямой маршрут)" : vpn,
            ok ? ("Доступен (" + ping + " мс)") : "Не отвечает (блокировка или нет сети)"
        );
        MessageBox.Show(msg, "Диагностика мультиплеера VTOL VR", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private void LoadServersAsync()
    {
        btnRefresh.Enabled = false;
        lblSummary.Text = "Сканирование лобби Steam...";
        lvServers.Items.Clear();

        ThreadPool.QueueUserWorkItem(delegate {
            try
            {
                string diag;
                List<VtolLobbyInfo> list = SteamLobbyScanner.FetchPublicLobbies(gameRoot, out diag);
                this.Invoke(new Action(delegate {
                    lblNetBanner.Text = diag;
                    lblNetBanner.ForeColor = diag.StartsWith("⚠") ? Color.Gold : Color.Cyan;
                    allLobbies = list;
                    ApplyFilter();
                    btnRefresh.Enabled = true;
                }));
            }
            catch (Exception ex)
            {
                try
                {
                    this.Invoke(new Action(delegate {
                        lblNetBanner.Text = "Ошибка сканирования: " + ex.Message;
                        lblNetBanner.ForeColor = Color.OrangeRed;
                        lblSummary.Text = "0 серверов";
                        btnRefresh.Enabled = true;
                    }));
                }
                catch { }
            }
        });
    }

    private void ApplyFilter()
    {
        lvServers.Items.Clear();
        shownLobbies.Clear();
        string q = txtFilter.Text.Trim();
        int totalPlayers = 0;

        foreach (VtolLobbyInfo l in allLobbies)
        {
            totalPlayers += l.Members;
            bool isFull = (l.MaxMembers > 0 && l.Members >= l.MaxMembers);
            if (chkHideFull.Checked && isFull) continue;
            if (!string.IsNullOrEmpty(q) &&
                l.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 &&
                l.Mission.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;

            shownLobbies.Add(l);
            ListViewItem item = new ListViewItem(l.Name);
            item.SubItems.Add(l.Mission);
            item.SubItems.Add(l.State);
            item.SubItems.Add(l.MaxMembers > 0 ? (l.Members + " / " + l.MaxMembers) : l.Members.ToString());
            item.SubItems.Add(l.ConnectHint);

            if (l.State == "Дебрифинг") item.ForeColor = Color.OrangeRed;
            else if (isFull) item.ForeColor = Color.Orange;
            else if (l.IsReachableWarning) item.ForeColor = Color.Gold;
            else if (l.State == "В миссии") item.ForeColor = Color.Cyan;
            else item.ForeColor = Color.FromArgb(0, 255, 102);

            lvServers.Items.Add(item);
        }

        lblSummary.Text = string.Format("Серверов: {0} (показано {1}) | Пилотов в сети: {2}", allLobbies.Count, shownLobbies.Count, totalPlayers);
    }
}


// =========================================================================
// === ГЛАВНАЯ ФОРМА ========================================================
// =========================================================================
public class SettingsDialogForm : Form
{
    public CheckBox ChkShuffle;
    public CheckBox ChkLoudNorm;
    public CheckBox ChkDiscord;
    public CheckBox ChkDucking;
    public CheckBox ChkAudioSwitch;
    public CheckBox ChkDiagnostics;
    public CheckBox ChkKillSound;

    public SettingsDialogForm()
    {
        this.Text = "VTOL QoL — Опции";
        DarkWindowHelper.Apply(this);
        this.Size = new Size(490, 345);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.BackColor = Color.FromArgb(18, 22, 20);
        this.ForeColor = Color.FromArgb(0, 255, 102);
        this.Font = new Font("Consolas", 9.5f);

        Label lblTitle = new Label() { Text = "/// КОНФИГУРАЦИЯ МОДУЛЕЙ ///", Left = 20, Top = 14, AutoSize = true, Font = new Font("Consolas", 10.5f, FontStyle.Bold) };
        this.Controls.Add(lblTitle);

        ChkShuffle = AddStyledCheck("Умный шаффл музыки между вылетами", 48, SettingsManager.GetBool("Shuffle", true));
        ChkLoudNorm = AddStyledCheck("Выравнивание громкости (-14 LUFS)", 78, SettingsManager.GetBool("LoudNorm", true));
        ChkDiscord = AddStyledCheck("Discord Rich Presence (статус полёта)", 108, SettingsManager.GetBool("Discord", true));
        ChkDucking = AddStyledCheck("Авто-приглушение радио при разговоре", 138, SettingsManager.GetBool("Ducking", true));
        ChkAudioSwitch = AddStyledCheck("Авто-переключение аудио на VR-шлем", 168, SettingsManager.GetBool("AudioSwitch", true));
        ChkDiagnostics = AddStyledCheck("Диагностика лога игры (Player.log)", 198, SettingsManager.GetBool("DiagnosticsEnabled", false));
        ChkKillSound = AddStyledCheck("Озвучка серий сбитий в наушниках", 228, SettingsManager.GetBool("KillSoundEnabled", true));

        Button btnBackup = new Button() { Text = "🛡 Бэкап сохранений", Left = 22, Top = 266, Width = 195, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(130, 80, 0), ForeColor = Color.White };
        btnBackup.Click += (s, e) => { using (SaveVaultForm vault = new SaveVaultForm()) vault.ShowDialog(this); };
        this.Controls.Add(btnBackup);

        Button btnClose = new Button() { Text = "Готово", Left = 335, Top = 266, Width = 120, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 130, 65), ForeColor = Color.White };
        btnClose.Click += (s, e) => this.Close();
        this.Controls.Add(btnClose);
    }


    private CheckBox AddStyledCheck(string text, int top, bool isChecked)
    {
        CheckBox cb = new CheckBox();
        cb.Text = text;
        cb.Location = new Point(22, top);
        cb.Size = new Size(435, 24);
        cb.Checked = isChecked;
        cb.ForeColor = Color.FromArgb(210, 240, 220);
        cb.Cursor = Cursors.Hand;

        cb.Paint += (s, e) => {
            e.Graphics.Clear(this.BackColor);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            int boxSize = 15;
            int boxY = (cb.Height - boxSize) / 2;
            Rectangle boxRect = new Rectangle(1, boxY, boxSize, boxSize);

            Color fill = cb.Checked ? Color.FromArgb(0, 75, 42) : Color.FromArgb(24, 30, 27);
            Color border = cb.Checked ? Color.FromArgb(0, 255, 102) : Color.FromArgb(65, 95, 80);

            using (SolidBrush br = new SolidBrush(fill))
                e.Graphics.FillRectangle(br, boxRect);
            using (Pen pen = new Pen(border, 1.4f))
                e.Graphics.DrawRectangle(pen, boxRect);

            if (cb.Checked)
            {
                using (Pen checkPen = new Pen(Color.FromArgb(0, 255, 102), 2.1f))
                {
                    checkPen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    checkPen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    e.Graphics.DrawLines(checkPen, new Point[] {
                        new Point(4, boxY + 8),
                        new Point(7, boxY + 11),
                        new Point(13, boxY + 4)
                    });
                }
            }

            TextRenderer.DrawText(e.Graphics, cb.Text, cb.Font,
                new Rectangle(23, 0, cb.Width - 23, cb.Height),
                cb.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        };

        this.Controls.Add(cb);
        return cb;
    }
}

public class MainForm : Form
{
    [DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    private TextBox txtPath;
    private Button btnStopDownload;
    private Button btnManageMusic;
    private Panel pnlDropZone;
    private Label lblDropText;
    private bool optShuffle;
    private bool optLoudNorm;
    private bool optDiscord;
    private bool optDucking;
    private bool optAudioSwitch;
    private bool optDiagnostics;
    private bool optKillSound;
    private Button btnInstall;
    private Label lblStatus;
    private ProgressBar progressBar;
    private Button btnLiveryManager;
    private Button btnLogbook;
    private Button btnServers;
    private volatile Process currentWorkerProcess = null;
    private volatile bool isCancelRequested = false;
    private GameTracker tracker = new GameTracker();
    private Label lblIndDiscord;
    private Label lblIndMusic;
    private Label lblIndAudio;
    private Label lblIndPilot;
    private Label lblIndSteam;
    private Label lblIndNet;
    private volatile int netState = 0; // 0 = проверка, 1 = всё ок, 2 = частично, 3 = нет сети
    private volatile bool netYtOk = false;
    private volatile bool netDcOk = false;
    private volatile bool netStmOk = false;
    private System.Windows.Forms.Timer uiStatusTimer;

    public MainForm()
    {
        this.Text = "VTOL QoL";
        this.Size = new Size(568, 386);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.None;
        this.MaximizeBox = false;
        this.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(this.Handle, 0xA1, 0x2, 0); } };
        this.KeyPreview = true;
        try
        {
            string localIco = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
            if (File.Exists(localIco))
                this.Icon = new Icon(localIco);
            else
            {
                Stream icoStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("icon.ico");
                if (icoStream != null)
                    this.Icon = new Icon(icoStream);
                else
                    this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
        }
        catch { }
        this.BackColor = Color.FromArgb(18, 22, 20);
        this.ForeColor = Color.FromArgb(0, 255, 102);
        this.Font = new Font("Consolas", 9.5f, FontStyle.Regular);

        optShuffle = SettingsManager.GetBool("Shuffle", true);
        optLoudNorm = SettingsManager.GetBool("LoudNorm", true);
        optDiscord = SettingsManager.GetBool("Discord", true);
        optDucking = SettingsManager.GetBool("Ducking", true);
        optAudioSwitch = SettingsManager.GetBool("AudioSwitch", true);
        optDiagnostics = SettingsManager.GetBool("DiagnosticsEnabled", false);
        optKillSound = SettingsManager.GetBool("KillSoundEnabled", true);

        this.DoubleBuffered = true;
        this.Paint += (s, e) => {
            Graphics g = e.Graphics;
            Rectangle rc = this.ClientRectangle;

            using (System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                path.AddRectangle(rc);
                using (System.Drawing.Drawing2D.PathGradientBrush pgb = new System.Drawing.Drawing2D.PathGradientBrush(path))
                {
                    pgb.CenterColor = Color.FromArgb(24, 32, 28);
                    pgb.SurroundColors = new Color[] { Color.FromArgb(14, 18, 16) };
                    g.FillRectangle(pgb, rc);
                }
            }

            using (Pen gridPen = new Pen(Color.FromArgb(24, 34, 29), 1))
            {
                for (int x = 24; x < rc.Width; x += 28)
                    g.DrawLine(gridPen, x, 0, x, rc.Height);
                for (int y = 24; y < rc.Height; y += 28)
                    g.DrawLine(gridPen, 0, y, rc.Width, y);
            }

            using (Pen hudPen = new Pen(Color.FromArgb(45, 85, 65), 1))
            {
                g.DrawRectangle(hudPen, 10, 8, rc.Width - 21, rc.Height - 17);
            }
            using (Pen cornerPen = new Pen(Color.FromArgb(0, 190, 85), 2))
            {
                int l = 10, t = 8, r = rc.Width - 11, b = rc.Height - 9, len = 14;
                g.DrawLine(cornerPen, l, t, l + len, t); g.DrawLine(cornerPen, l, t, l, t + len);
                g.DrawLine(cornerPen, r, t, r - len, t); g.DrawLine(cornerPen, r, t, r, t + len);
                g.DrawLine(cornerPen, l, b, l + len, b); g.DrawLine(cornerPen, l, b, l, b - len);
                g.DrawLine(cornerPen, r, b, r - len, b); g.DrawLine(cornerPen, r, b, r, b - len);
            }
        };

        Label lblTitle = new Label();
        lblTitle.Text = "/// VTOL QoL ///";
        lblTitle.Font = new Font("Consolas", 12f, FontStyle.Bold);
        lblTitle.BackColor = Color.Transparent;
        lblTitle.Location = new Point(22, 14);
        lblTitle.AutoSize = true;
        lblTitle.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(this.Handle, 0xA1, 0x2, 0); } };
        this.Controls.Add(lblTitle);

        Button btnWinMin = new Button();
        btnWinMin.Text = "—";
        btnWinMin.Font = new Font("Consolas", 9.5f, FontStyle.Bold);
        btnWinMin.Location = new Point(482, 12);
        btnWinMin.Size = new Size(30, 24);
        btnWinMin.FlatStyle = FlatStyle.Flat;
        btnWinMin.FlatAppearance.BorderColor = Color.FromArgb(45, 85, 65);
        btnWinMin.BackColor = Color.FromArgb(24, 32, 28);
        btnWinMin.ForeColor = Color.FromArgb(0, 255, 102);
        btnWinMin.Cursor = Cursors.Hand;
        btnWinMin.Click += (s, e) => { this.WindowState = FormWindowState.Minimized; };
        this.Controls.Add(btnWinMin);

        Button btnWinClose = new Button();
        btnWinClose.Text = "✕";
        btnWinClose.Font = new Font("Consolas", 9.5f, FontStyle.Bold);
        btnWinClose.Location = new Point(515, 12);
        btnWinClose.Size = new Size(30, 24);
        btnWinClose.FlatStyle = FlatStyle.Flat;
        btnWinClose.FlatAppearance.BorderColor = Color.FromArgb(45, 85, 65);
        btnWinClose.BackColor = Color.FromArgb(24, 32, 28);
        btnWinClose.ForeColor = Color.FromArgb(0, 255, 102);
        btnWinClose.Cursor = Cursors.Hand;
        btnWinClose.MouseEnter += (s, e) => { btnWinClose.BackColor = Color.FromArgb(165, 35, 35); btnWinClose.ForeColor = Color.White; };
        btnWinClose.MouseLeave += (s, e) => { btnWinClose.BackColor = Color.FromArgb(24, 32, 28); btnWinClose.ForeColor = Color.FromArgb(0, 255, 102); };
        btnWinClose.Click += (s, e) => { this.Close(); };
        this.Controls.Add(btnWinClose);

        txtPath = new TextBox();
        txtPath.Visible = false;
        txtPath.Text = SettingsManager.Get("RadioMusicDir", FindVtolMusicDir());
        this.Controls.Add(txtPath);


        pnlDropZone = new Panel();
        pnlDropZone.Location = new Point(24, 44);
        pnlDropZone.Size = new Size(520, 86);
        pnlDropZone.BackColor = Color.FromArgb(26, 34, 30);
        pnlDropZone.BorderStyle = BorderStyle.FixedSingle;
        pnlDropZone.AllowDrop = true;
        pnlDropZone.Cursor = Cursors.Hand;
        pnlDropZone.DragEnter += new DragEventHandler(PnlDropZone_DragEnter);
        pnlDropZone.DragLeave += new EventHandler(PnlDropZone_DragLeave);
        pnlDropZone.DragDrop += new DragEventHandler(PnlDropZone_DragDrop);
        pnlDropZone.Click += (s, e) => TryPasteLinkFromClipboard(true);
        this.Controls.Add(pnlDropZone);

        lblDropText = new Label();
        lblDropText.Text = "[ ПЕРЕТАЩИТЕ ТРЕКИ, ПАПКИ ИЛИ ССЫЛКУ СЮДА ]\nНажмите Ctrl+V или кликните для вставки ссылки\n(MP3, FLAC, WAV, MP4, YouTube, SoundCloud, Радио)";
        lblDropText.TextAlign = ContentAlignment.MiddleCenter;
        lblDropText.Dock = DockStyle.Fill;
        lblDropText.ForeColor = Color.FromArgb(160, 230, 185);
        lblDropText.AllowDrop = true;
        lblDropText.Cursor = Cursors.Hand;
        lblDropText.DragEnter += new DragEventHandler(PnlDropZone_DragEnter);
        lblDropText.DragDrop += new DragEventHandler(PnlDropZone_DragDrop);
        lblDropText.Click += (s, e) => TryPasteLinkFromClipboard(true);
        pnlDropZone.Controls.Add(lblDropText);

        this.KeyDown += (s, e) => {
            if (e.Control && e.KeyCode == Keys.V)
            {
                e.SuppressKeyPress = true;
                TryPasteLinkFromClipboard(true);
            }
        };

        progressBar = new ProgressBar();
        progressBar.Location = new Point(24, 138);
        progressBar.Size = new Size(430, 16);
        progressBar.Visible = false;
        this.Controls.Add(progressBar);

        btnStopDownload = new Button();
        btnStopDownload.Text = "⏹ СТОП";
        btnStopDownload.Location = new Point(460, 135);
        btnStopDownload.Size = new Size(84, 22);
        btnStopDownload.FlatStyle = FlatStyle.Flat;
        btnStopDownload.BackColor = Color.FromArgb(140, 35, 35);
        btnStopDownload.ForeColor = Color.White;
        btnStopDownload.Visible = false;
        btnStopDownload.Click += new EventHandler(BtnStopDownload_Click);
        this.Controls.Add(btnStopDownload);

        lblStatus = new Label();
        lblStatus.Text = "Готов к работе.";
        lblStatus.Location = new Point(22, 160);
        lblStatus.Size = new Size(520, 20);
        lblStatus.BackColor = Color.Transparent;
        lblStatus.ForeColor = Color.Silver;
        this.Controls.Add(lblStatus);

        btnInstall = new Button();
        btnInstall.Text = "▶ СТАРТ VTOL VR";
        btnInstall.Font = new Font("Consolas", 10f, FontStyle.Bold);
        btnInstall.Location = new Point(24, 188);
        btnInstall.Size = new Size(255, 42);
        btnInstall.FlatStyle = FlatStyle.Flat;
        btnInstall.BackColor = Color.FromArgb(0, 140, 60);
        btnInstall.ForeColor = Color.White;
        btnInstall.Click += new EventHandler(BtnInstall_Click);
        this.Controls.Add(btnInstall);

        btnLiveryManager = new Button();
        btnLiveryManager.Text = "✈ МЕНЕДЖЕР ЛИВРЕЙ";
        btnLiveryManager.Font = new Font("Consolas", 9.5f, FontStyle.Bold);
        btnLiveryManager.Location = new Point(290, 188);
        btnLiveryManager.Size = new Size(254, 42);
        btnLiveryManager.FlatStyle = FlatStyle.Flat;
        btnLiveryManager.BackColor = Color.FromArgb(70, 50, 110);
        btnLiveryManager.ForeColor = Color.White;
        btnLiveryManager.Click += new EventHandler(BtnLiveryManager_Click);
        this.Controls.Add(btnLiveryManager);

        btnManageMusic = new Button();
        btnManageMusic.Text = "♫ ТРЕКИ";
        btnManageMusic.Location = new Point(24, 238);
        btnManageMusic.Size = new Size(124, 38);
        btnManageMusic.FlatStyle = FlatStyle.Flat;
        btnManageMusic.BackColor = Color.FromArgb(0, 95, 125);
        btnManageMusic.ForeColor = Color.White;
        btnManageMusic.Click += new EventHandler(BtnManageMusic_Click);
        this.Controls.Add(btnManageMusic);

        btnServers = new Button();
        btnServers.Text = "🌐 СЕРВЕРЫ";
        btnServers.Location = new Point(156, 238);
        btnServers.Size = new Size(124, 38);
        btnServers.FlatStyle = FlatStyle.Flat;
        btnServers.BackColor = Color.FromArgb(30, 85, 115);
        btnServers.ForeColor = Color.White;
        btnServers.Click += new EventHandler(BtnServers_Click);
        this.Controls.Add(btnServers);

        btnLogbook = new Button();
        btnLogbook.Text = "📋 ЛОГБУК";
        btnLogbook.Location = new Point(288, 238);
        btnLogbook.Size = new Size(124, 38);
        btnLogbook.FlatStyle = FlatStyle.Flat;
        btnLogbook.BackColor = Color.FromArgb(40, 85, 70);
        btnLogbook.ForeColor = Color.White;
        btnLogbook.Click += new EventHandler(BtnLogbook_Click);
        this.Controls.Add(btnLogbook);

        Button btnSettings = new Button();
        btnSettings.Text = "⚙ ОПЦИИ";
        btnSettings.Location = new Point(420, 238);
        btnSettings.Size = new Size(124, 38);
        btnSettings.FlatStyle = FlatStyle.Flat;
        btnSettings.BackColor = Color.FromArgb(52, 62, 58);
        btnSettings.ForeColor = Color.FromArgb(0, 255, 102);
        btnSettings.Click += (s, e) => OpenSettingsDialog();
        this.Controls.Add(btnSettings);

        Panel pnlIndicators = new Panel();
        pnlIndicators.Location = new Point(24, 286);
        pnlIndicators.Size = new Size(520, 62);
        pnlIndicators.BackColor = Color.FromArgb(22, 28, 25);
        pnlIndicators.BorderStyle = BorderStyle.FixedSingle;
        this.Controls.Add(pnlIndicators);

        ToolTip indTip = new ToolTip();

        lblIndDiscord = new Label() { Left = 8, Top = 5, Width = 245, Height = 16, Font = new Font("Consolas", 8.5f, FontStyle.Bold), ForeColor = Color.Silver, Text = "● Discord: проверка..." };
        lblIndDiscord.Click += (s, e) => {
            if (tracker.IsDiscordAccessDenied)
            {
                if (MessageBox.Show("Discord запущен от имени администратора.\nПерезапустить VTOL QoL с правами администратора для подключения?",
                    "Требуются права администратора", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath);
                        psi.Verb = "runas";
                        psi.UseShellExecute = true;
                        Process.Start(psi);
                        Application.Exit();
                    }
                    catch { }
                }
            }
            else if (Process.GetProcessesByName("Discord").Length == 0)
            {
                try
                {
                    string dcUpd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Discord\Update.exe");
                    if (File.Exists(dcUpd))
                    {
                        lblIndDiscord.Text = "● Discord: запуск...";
                        lblIndDiscord.ForeColor = Color.Gold;
                        Process.Start(dcUpd, "--processStart Discord.exe");
                    }
                }
                catch { }
            }
        };

        lblIndMusic = new Label() { Left = 260, Top = 5, Width = 250, Height = 16, Font = new Font("Consolas", 8.5f, FontStyle.Bold), ForeColor = Color.Silver, Text = "● Треки: проверка... ⚙", Cursor = Cursors.Hand };
        lblIndMusic.Click += new EventHandler(BtnBrowse_Click);
        lblIndMusic.MouseEnter += (s, e) => { lblIndMusic.Font = new Font("Consolas", 8.5f, FontStyle.Bold | FontStyle.Underline); };
        lblIndMusic.MouseLeave += (s, e) => { lblIndMusic.Font = new Font("Consolas", 8.5f, FontStyle.Bold); };
        indTip.SetToolTip(lblIndMusic, "Нажмите, чтобы изменить путь к папке RadioMusic");

        lblIndAudio = new Label() { Left = 8, Top = 23, Width = 245, Height = 16, Font = new Font("Consolas", 8.5f, FontStyle.Bold), ForeColor = Color.Silver, Text = "● VR Аудио: проверка... ⚙", Cursor = Cursors.Hand };
        lblIndAudio.Click += new EventHandler(BtnAudioSettings_Click);
        lblIndAudio.MouseEnter += (s, e) => { lblIndAudio.Font = new Font("Consolas", 8.5f, FontStyle.Bold | FontStyle.Underline); };
        lblIndAudio.MouseLeave += (s, e) => { lblIndAudio.Font = new Font("Consolas", 8.5f, FontStyle.Bold); };
        indTip.SetToolTip(lblIndAudio, "Нажмите, чтобы выбрать наушники и микрофон VR-шлема");

        lblIndPilot = new Label() { Left = 260, Top = 23, Width = 250, Height = 16, Font = new Font("Consolas", 8.5f, FontStyle.Bold), ForeColor = Color.Cyan, Text = "● Пилот: —" };

        lblIndSteam = new Label() { Left = 8, Top = 41, Width = 245, Height = 16, Font = new Font("Consolas", 8.5f, FontStyle.Bold), ForeColor = Color.Silver, Text = "● Steam: проверка... ⚙", Cursor = Cursors.Hand };
        lblIndSteam.Click += (s, e) => {
            if (Process.GetProcessesByName("steam").Length == 0)
            {
                try
                {
                    string sp = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamExe", "") as string;
                    if (string.IsNullOrEmpty(sp) || !File.Exists(sp))
                    {
                        string sDir = (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", "") as string ?? "").Replace('/', '\\');
                        sp = Path.Combine(sDir, "steam.exe");
                    }
                    if (File.Exists(sp))
                    {
                        lblIndSteam.Text = "● Steam: запуск клиента...";
                        lblIndSteam.ForeColor = Color.Gold;
                        Process.Start(sp);
                        return;
                    }
                    Process.Start(new ProcessStartInfo("steam://open/main") { UseShellExecute = true });
                }
                catch { }
                return;
            }
            ThreadPool.QueueUserWorkItem(delegate { SilentSync(true); });
        };
        lblIndSteam.MouseEnter += (s, e) => { lblIndSteam.Font = new Font("Consolas", 8.5f, FontStyle.Bold | FontStyle.Underline); };
        lblIndSteam.MouseLeave += (s, e) => { lblIndSteam.Font = new Font("Consolas", 8.5f, FontStyle.Bold); };
        indTip.SetToolTip(lblIndSteam, "Нажмите, чтобы запустить Steam или проверить интеграцию");

        lblIndNet = new Label() { Left = 260, Top = 41, Width = 250, Height = 16, Font = new Font("Consolas", 8.5f, FontStyle.Bold), ForeColor = Color.Silver, Text = "● Сеть: проверка... ↻", Cursor = Cursors.Hand };
        lblIndNet.Click += (s, e) => { netState = 0; TriggerNetworkCheck(); };
        lblIndNet.MouseEnter += (s, e) => { lblIndNet.Font = new Font("Consolas", 8.5f, FontStyle.Bold | FontStyle.Underline); };
        lblIndNet.MouseLeave += (s, e) => { lblIndNet.Font = new Font("Consolas", 8.5f, FontStyle.Bold); };
        indTip.SetToolTip(lblIndNet, "Проверка доступа к YouTube, Discord и Steam (нажмите для повтора)");

        pnlIndicators.Controls.Add(lblIndDiscord);
        pnlIndicators.Controls.Add(lblIndMusic);
        pnlIndicators.Controls.Add(lblIndAudio);
        pnlIndicators.Controls.Add(lblIndPilot);
        pnlIndicators.Controls.Add(lblIndSteam);
        pnlIndicators.Controls.Add(lblIndNet);

        Label lblAppLog = new Label();
        lblAppLog.Text = "🐞 лог проги";
        lblAppLog.Font = new Font("Consolas", 8f, FontStyle.Regular);
        lblAppLog.ForeColor = Color.FromArgb(75, 130, 100);
        lblAppLog.BackColor = Color.Transparent;
        lblAppLog.Location = new Point(22, 355);
        lblAppLog.AutoSize = true;
        lblAppLog.Cursor = Cursors.Hand;
        lblAppLog.MouseEnter += (s, e) => { lblAppLog.ForeColor = Color.FromArgb(0, 255, 102); };
        lblAppLog.MouseLeave += (s, e) => { lblAppLog.ForeColor = Color.FromArgb(75, 130, 100); };
        lblAppLog.Click += (s, e) => { try { Process.Start("notepad.exe", AppLogger.GetLogPath()); } catch { } };
        this.Controls.Add(lblAppLog);

        Label lblGameLog = new Label();
        lblGameLog.Text = "🔧 лог игры";
        lblGameLog.Font = new Font("Consolas", 8f, FontStyle.Regular);
        lblGameLog.ForeColor = Color.FromArgb(75, 130, 100);
        lblGameLog.BackColor = Color.Transparent;
        lblGameLog.Location = new Point(122, 355);
        lblGameLog.AutoSize = true;
        lblGameLog.Cursor = Cursors.Hand;
        lblGameLog.MouseEnter += (s, e) => { lblGameLog.ForeColor = Color.FromArgb(0, 255, 102); };
        lblGameLog.MouseLeave += (s, e) => { lblGameLog.ForeColor = Color.FromArgb(75, 130, 100); };
        lblGameLog.Click += new EventHandler(BtnDiagnostics_Click);
        this.Controls.Add(lblGameLog);

        Label lblAuthor = new Label();
        lblAuthor.Text = "by L.Shutze";
        lblAuthor.Font = new Font("Consolas", 8f, FontStyle.Italic);
        lblAuthor.ForeColor = Color.FromArgb(85, 140, 110);
        lblAuthor.BackColor = Color.Transparent;
        lblAuthor.Location = new Point(468, 355);
        lblAuthor.AutoSize = true;
        this.Controls.Add(lblAuthor);
        uiStatusTimer = new System.Windows.Forms.Timer();
        uiStatusTimer.Interval = 1500;
        uiStatusTimer.Tick += (s, e) => RefreshStatusIndicators();
        uiStatusTimer.Start();
        this.FormClosing += new FormClosingEventHandler(MainForm_FormClosing);
        this.Shown += new EventHandler(MainForm_Shown);
    }

    private void OpenSettingsDialog()
    {
        using (SettingsDialogForm dlg = new SettingsDialogForm())
        {
            dlg.ShowDialog(this);
            optShuffle = dlg.ChkShuffle.Checked;
            optLoudNorm = dlg.ChkLoudNorm.Checked;
            optDiscord = dlg.ChkDiscord.Checked;
            optDucking = dlg.ChkDucking.Checked;
            optAudioSwitch = dlg.ChkAudioSwitch.Checked;
            optDiagnostics = dlg.ChkDiagnostics.Checked;
            optKillSound = dlg.ChkKillSound.Checked;

            tracker.DiscordEnabled = optDiscord;
            tracker.DuckingEnabled = optDucking;
            tracker.AudioSwitchEnabled = optAudioSwitch;
            tracker.DiagnosticsEnabled = optDiagnostics;
            SaveCheckboxSettings();
            RefreshStatusIndicators();
        }
    }

    private void MainForm_Shown(object sender, EventArgs e)
    {
        ThreadPool.QueueUserWorkItem(delegate { SilentSync(true); });
        TriggerNetworkCheck();
        tracker.DiscordEnabled = optDiscord;
        tracker.DuckingEnabled = optDucking;
        tracker.AudioSwitchEnabled = optAudioSwitch;
        tracker.DiagnosticsEnabled = optDiagnostics;
        tracker.Start();
        RefreshStatusIndicators();
    }

    private DateTime lastNetCheckUtc = DateTime.MinValue;
    private volatile bool netCheckBusy = false;

    private void TriggerNetworkCheck()
    {
        if (netCheckBusy) return;
        netCheckBusy = true;
        lastNetCheckUtc = DateTime.UtcNow;

        ThreadPool.QueueUserWorkItem(delegate {
            try
            {
                bool hasInet = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
                if (!hasInet)
                {
                    netYtOk = false; netDcOk = false; netStmOk = false;
                    netState = 3;
                    return;
                }

                netYtOk = CheckHostReachable("https://youtube.com");
                netDcOk = CheckHostReachable("https://discord.com");
                netStmOk = CheckHostReachable("https://steamcommunity.com");

                if (netYtOk && netDcOk && netStmOk) netState = 1;
                else if (!netYtOk && !netDcOk && !netStmOk) netState = 3;
                else netState = 2;
            }
            catch
            {
                netState = 3;
            }
            finally
            {
                netCheckBusy = false;
            }
        });
    }

    private bool CheckHostReachable(string url)
    {
        try
        {
            WorkshopBrowser.EnableTls12();
            System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
            req.Method = "HEAD";
            req.Timeout = 2800;
            req.ReadWriteTimeout = 2800;
            req.AllowAutoRedirect = true;
            req.UserAgent = "Mozilla/5.0";
            using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)req.GetResponse())
                return ((int)resp.StatusCode >= 200 && (int)resp.StatusCode < 500);
        }
        catch (System.Net.WebException wex)
        {
            if (wex.Response != null)
            {
                try { wex.Response.Close(); } catch { }
                return true;
            }
            return false;
        }
        catch { return false; }
    }

    private void RefreshStatusIndicators()
    {
        if (lblIndDiscord == null) return;

        if ((DateTime.UtcNow - lastNetCheckUtc).TotalSeconds >= 20)
            TriggerNetworkCheck();

        bool discordProcRunning = Process.GetProcessesByName("Discord").Length > 0;

        if (!optDiscord)
        {
            lblIndDiscord.Text = "● Discord: выключен";
            lblIndDiscord.ForeColor = Color.Gray;
            lblIndDiscord.Cursor = Cursors.Default;
        }
        else if (tracker.IsDiscordAccessDenied)
        {
            lblIndDiscord.Text = "● Discord: права админа ⚙";
            lblIndDiscord.ForeColor = Color.OrangeRed;
            lblIndDiscord.Cursor = Cursors.Hand;
        }
        else if (!discordProcRunning)
        {
            lblIndDiscord.Text = "● Discord: не запущен ⚙";
            lblIndDiscord.ForeColor = Color.OrangeRed;
            lblIndDiscord.Cursor = Cursors.Hand;
        }
        else if (tracker.IsDiscordConnected && tracker.IsDiscordLoggedIn)
        {
            string u = !string.IsNullOrEmpty(tracker.DiscordUsername) ? (" (" + tracker.DiscordUsername + ")") : "";
            lblIndDiscord.Text = tracker.IsGameActive ? ("● Discord: активен" + u) : ("● Discord: вход выполнен" + u);
            lblIndDiscord.ForeColor = Color.FromArgb(0, 255, 102);
            lblIndDiscord.Cursor = Cursors.Default;
        }
        else if (tracker.IsDiscordConnected && !tracker.IsDiscordLoggedIn)
        {
            lblIndDiscord.Text = "● Discord: не выполнен вход";
            lblIndDiscord.ForeColor = Color.Gold;
            lblIndDiscord.Cursor = Cursors.Default;
        }
        else
        {
            lblIndDiscord.Text = "● Discord: запущен (вход/ожидание)";
            lblIndDiscord.ForeColor = Color.FromArgb(140, 220, 175);
            lblIndDiscord.Cursor = Cursors.Default;
        }


    string mDir = txtPath.Text.Trim('"');
        if (!string.IsNullOrEmpty(mDir) && Directory.Exists(mDir))
        {
            int count = 0;
            try { count = Directory.GetFiles(mDir, "*.mp3", SearchOption.TopDirectoryOnly).Length; } catch { }
            lblIndMusic.Text = "● RadioMusic: " + count + " тр. ⚙";
            lblIndMusic.ForeColor = count > 0 ? Color.FromArgb(0, 255, 102) : Color.Gold;
        }
        else
        {
            lblIndMusic.Text = "● RadioMusic: задать путь ⚙";
            lblIndMusic.ForeColor = Color.OrangeRed;
        }

        string vrPb = SettingsManager.Get("VrPlaybackId", "");
        string vrCp = SettingsManager.Get("VrCaptureId", "");
        if (!optAudioSwitch)
        {
            lblIndAudio.Text = "● VR Аудио: выключено ⚙";
            lblIndAudio.ForeColor = Color.Gray;
        }
        else if (!string.IsNullOrEmpty(vrPb) || !string.IsNullOrEmpty(vrCp))
        {
            lblIndAudio.Text = "● VR Аудио: настроено ⚙";
            lblIndAudio.ForeColor = Color.FromArgb(0, 255, 102);
        }
        else
        {
            lblIndAudio.Text = "● VR Аудио: не выбрано ⚙";
            lblIndAudio.ForeColor = Color.Gold;
        }

        string pilot = tracker.ActivePilotName;
        if (tracker.IsGameActive)
        {
            lblIndPilot.Text = "● " + (pilot != "—" ? pilot + ": " : "") + tracker.ActiveFlightSummary;
            lblIndPilot.ForeColor = Color.Cyan;
        }
        else
        {
            lblIndPilot.Text = "● Игра: не запущена";
            lblIndPilot.ForeColor = Color.Silver;
        }

        if (lblIndSteam != null)
        {
            bool steamRunning = Process.GetProcessesByName("steam").Length > 0;
            bool batReady = !string.IsNullOrEmpty(mDir) && File.Exists(Path.Combine(mDir, "Shuffle.bat"));

            if (steamRunning && batReady)
            {
                lblIndSteam.Text = "● Steam: подключён ✔";
                lblIndSteam.ForeColor = Color.FromArgb(0, 255, 102);
            }
            else if (steamRunning && !batReady)
            {
                lblIndSteam.Text = "● Steam: настроить автозапуск ⚙";
                lblIndSteam.ForeColor = Color.Gold;
            }
            else
            {
                lblIndSteam.Text = "● Steam: не запущен ⚙";
                lblIndSteam.ForeColor = Color.OrangeRed;
            }
        }

        if (lblIndNet != null)
        {
            if (netState == 0)
            {
                lblIndNet.Text = "● Сеть: проверка... ↻";
                lblIndNet.ForeColor = Color.Silver;
            }
            else if (netState == 1)
            {
                lblIndNet.Text = "● Сеть: YT ✔ | DC ✔ | STM ✔";
                lblIndNet.ForeColor = Color.FromArgb(0, 255, 102);
            }
            else if (netState == 2)
            {
                lblIndNet.Text = string.Format("● Сеть: YT {0} | DC {1} | STM {2}",
                    netYtOk ? "✔" : "✖", netDcOk ? "✔" : "✖", netStmOk ? "✔" : "✖");
                lblIndNet.ForeColor = Color.Gold;
            }
            else
            {
                lblIndNet.Text = "● Сеть: нет интернета ✖";
                lblIndNet.ForeColor = Color.OrangeRed;
            }
        }
    }


    private void SaveCheckboxSettings()
    {
        SettingsManager.SetBool("Shuffle", optShuffle);
        SettingsManager.SetBool("LoudNorm", optLoudNorm);
        SettingsManager.SetBool("Discord", optDiscord);
        SettingsManager.SetBool("Ducking", optDucking);
        SettingsManager.SetBool("AudioSwitch", optAudioSwitch);
        SettingsManager.SetBool("DiagnosticsEnabled", optDiagnostics);
        SettingsManager.SetBool("KillSoundEnabled", optKillSound);
        SettingsManager.Set("RadioMusicDir", txtPath.Text);
        SettingsManager.Save();
    }


    private void BtnBrowse_Click(object sender, EventArgs e)
    {
        using (FolderBrowserDialog fbd = new FolderBrowserDialog())
        {
            fbd.Description = "Выберите папку RadioMusic в директории VTOL VR (текущая: " + txtPath.Text + ")";
            if (!string.IsNullOrEmpty(txtPath.Text) && Directory.Exists(txtPath.Text))
                fbd.SelectedPath = txtPath.Text;
            if (fbd.ShowDialog() == DialogResult.OK)
            {
                txtPath.Text = fbd.SelectedPath;
                SaveCheckboxSettings();
                RefreshStatusIndicators();
            }
        }
    }

    private void BtnInstall_Click(object sender, EventArgs e)
    {
        SaveCheckboxSettings();
        SilentSync(false);
        Process.Start("steam://rungameid/667970");
    }

    private void BtnServers_Click(object sender, EventArgs e)
    {
        string dir = txtPath.Text.Trim('"');
        string gameRoot = Directory.Exists(dir) ? Path.GetFullPath(Path.Combine(dir, "..")) : "";
        using (ServerBrowserForm sb = new ServerBrowserForm(gameRoot)) sb.ShowDialog(this);
    }

    private void BtnManageMusic_Click(object sender, EventArgs e)
    {
        string dir = txtPath.Text.Trim('"');
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            MessageBox.Show("Сначала укажите корректную папку RadioMusic!", "Внимание", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using (MusicManagerForm manager = new MusicManagerForm(dir)) manager.ShowDialog(this);
    }

    private void BtnLiveryManager_Click(object sender, EventArgs e)
    {
        string dir = txtPath.Text.Trim('"');
        string gameRoot = Directory.Exists(dir) ? Path.GetFullPath(Path.Combine(dir, "..")) : "";
        using (LiveryManagerForm livery = new LiveryManagerForm(gameRoot)) livery.ShowDialog(this);
    }

    private void BtnLogbook_Click(object sender, EventArgs e)
    {
        using (FlightLogbookForm logbook = new FlightLogbookForm()) logbook.ShowDialog(this);
    }

    private void BtnAudioSettings_Click(object sender, EventArgs e)
    {
        using (AudioSettingsForm f = new AudioSettingsForm()) f.ShowDialog(this);
    }

    private void BtnDiagnostics_Click(object sender, EventArgs e)
    {
        using (LogDiagnosticsForm f = new LogDiagnosticsForm())
        {
            f.ShowDialog(this);
            optDiagnostics = SettingsManager.GetBool("DiagnosticsEnabled", false);
            tracker.DiagnosticsEnabled = optDiagnostics;
        }
    }

    private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
    {
        SaveCheckboxSettings();
        if (uiStatusTimer != null)
        {
            uiStatusTimer.Stop();
            uiStatusTimer.Dispose();
        }
        Environment.Exit(0);
    }

    private string FindVtolMusicDir()
    {
        string steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", "") as string;
        if (steamPath == null) steamPath = "";
        steamPath = steamPath.Replace('/', '\\');

        List<string> searchDirs = new List<string>();
        if (!string.IsNullOrEmpty(steamPath))
        {
            searchDirs.Add(Path.Combine(steamPath, @"steamapps\common\VTOL VR\RadioMusic"));
            string libVdf = Path.Combine(steamPath, @"steamapps\libraryfolders.vdf");
            if (File.Exists(libVdf))
            {
                string libText = File.ReadAllText(libVdf, Encoding.UTF8);
                foreach (Match m in Regex.Matches(libText, "\"path\"\\s+\"([^\"]+)\""))
                {
                    string libPath = m.Groups[1].Value.Replace("\\\\", "\\");
                    searchDirs.Add(Path.Combine(libPath, @"steamapps\common\VTOL VR\RadioMusic"));
                }
            }
        }

        foreach (char drive in "CDEFGH")
        {
            searchDirs.Add(drive + @":\SteamLibrary\steamapps\common\VTOL VR\RadioMusic");
            searchDirs.Add(drive + @":\Steam\steamapps\common\VTOL VR\RadioMusic");
            searchDirs.Add(drive + @":\Games\Steam\steamapps\common\VTOL VR\RadioMusic");
        }

        foreach (string dir in searchDirs) if (Directory.Exists(dir)) return dir;
        return "";
    }

    private void ExtractResource(string resName, string targetPath)
    {
        if (File.Exists(targetPath)) return;
        using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resName))
        {
            if (stream != null)
            {
                using (FileStream fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write))
                    stream.CopyTo(fs);
            }
        }
    }

    private void UpdateStatus(string text, int progress)
    {
        if (this.InvokeRequired) { try { this.Invoke(new Action<string, int>(UpdateStatus), text, progress); } catch { } return; }
        lblStatus.Text = text;
        if (progress >= 0 && progress <= 100) progressBar.Value = progress;
    }

    private void SetControlsEnabled(bool enabled)
    {
        if (this.InvokeRequired) { try { this.Invoke(new Action<bool>(SetControlsEnabled), enabled); } catch { } return; }
        btnInstall.Enabled = enabled;
        btnManageMusic.Enabled = enabled;
        btnLiveryManager.Enabled = enabled;
        btnLogbook.Enabled = enabled;
        btnServers.Enabled = enabled;
        pnlDropZone.AllowDrop = enabled;
        btnStopDownload.Visible = !enabled;
        btnStopDownload.Enabled = !enabled;
        progressBar.Visible = !enabled;
        if (enabled) { progressBar.Value = 0; currentWorkerProcess = null; }
    }

    private void BtnStopDownload_Click(object sender, EventArgs e)
    {
        isCancelRequested = true;
        try { if (currentWorkerProcess != null && !currentWorkerProcess.HasExited) currentWorkerProcess.Kill(); } catch { }
        UpdateStatus("Остановка процесса...", 0);
    }

    private void TryPasteLinkFromClipboard(bool notifyIfEmpty)
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                string clip = Clipboard.GetText().Trim();
                if (clip.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || clip.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    StartUrlDownload(clip);
                    return;
                }
            }
            if (notifyIfEmpty)
                UpdateStatus("Скопируйте ссылку (http/https) в буфер обмена или перетащите файлы.", 0);
        }
        catch { }
    }

    private void PnlDropZone_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.UnicodeText) || e.Data.GetDataPresent(DataFormats.Text))
        {
            e.Effect = DragDropEffects.Copy;
            pnlDropZone.BackColor = Color.FromArgb(0, 80, 45);
        }
    }

    private void PnlDropZone_DragLeave(object sender, EventArgs e) { pnlDropZone.BackColor = Color.FromArgb(26, 34, 30); }

    private void PnlDropZone_DragDrop(object sender, DragEventArgs e)
    {
        pnlDropZone.BackColor = Color.FromArgb(26, 34, 30);

        if (!e.Data.GetDataPresent(DataFormats.FileDrop) && (e.Data.GetDataPresent(DataFormats.UnicodeText) || e.Data.GetDataPresent(DataFormats.Text)))
        {
            string droppedUrl = (e.Data.GetData(DataFormats.UnicodeText) as string ?? e.Data.GetData(DataFormats.Text) as string ?? "").Trim();
            if (droppedUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || droppedUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                StartUrlDownload(droppedUrl);
                return;
            }
        }

        string gameDir = txtPath.Text.Trim('"');
        if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
        {
            MessageBox.Show("Сначала укажите папку RadioMusic!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string[] droppedItems = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (droppedItems == null || droppedItems.Length == 0) return;

        SetControlsEnabled(false);
        ThreadPool.QueueUserWorkItem(delegate {
            try
            {
                string toolsDir = GetToolsDir();
                string ffmpegPath = Path.Combine(toolsDir, "ffmpeg.exe");
                ExtractResource("ffmpeg.exe", ffmpegPath);

                List<string> filesToProcess = new List<string>();
                string validExts = ".mp3.flac.wav.ogg.m4a.opus.aac.wma.mp4.mkv.webm.avi";

                foreach (string item in droppedItems)
                {
                    if (Directory.Exists(item))
                    {
                        foreach (string f in Directory.GetFiles(item, "*.*", SearchOption.AllDirectories))
                            if (validExts.Contains(Path.GetExtension(f).ToLower())) filesToProcess.Add(f);
                    }
                    else if (File.Exists(item) && validExts.Contains(Path.GetExtension(item).ToLower()))
                        filesToProcess.Add(item);
                }

                if (filesToProcess.Count == 0) { UpdateStatus("Не найдено поддерживаемых файлов.", 0); return; }

                isCancelRequested = false;
                for (int i = 0; i < filesToProcess.Count; i++)
                {
                    if (isCancelRequested) { UpdateStatus("Обработка отменена.", 0); return; }
                    string src = filesToProcess[i];
                    string baseName = Path.GetFileNameWithoutExtension(src);
                    string outMp3 = Path.Combine(gameDir, baseName + ".mp3");
                    int pct = (int)(((i + 1) / (double)filesToProcess.Count) * 100);
                    UpdateStatus(string.Format("Обработка [{0}/{1}]: {2}...", i + 1, filesToProcess.Count, baseName), pct);

                    if (File.Exists(ffmpegPath))
                    {
                        string afFilter = optLoudNorm ? "-af loudnorm=I=-14:TP=-1.5:LRA=11 " : "";
                        string args = string.Format("-y -i \"{0}\" -vn {1}-codec:a libmp3lame -qscale:a 2 \"{2}\"", src, afFilter, outMp3);
                        Process p = Process.Start(new ProcessStartInfo(ffmpegPath, args) { WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true });
                        currentWorkerProcess = p;
                        if (p != null) p.WaitForExit();
                    }
                    else File.Copy(src, outMp3, true);
                }

                if (!isCancelRequested) UpdateStatus(string.Format("Готово! Обработано треков: {0}", filesToProcess.Count), 100);
            }
            catch (Exception ex) { UpdateStatus("Ошибка: " + ex.Message, 0); }
            finally { SetControlsEnabled(true); }
        });
    }


    private void StartUrlDownload(string url)
    {
        string gameDir = txtPath.Text.Trim('"');
        if (string.IsNullOrEmpty(url)) return;
        if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
        {
            MessageBox.Show("Сначала укажите папку RadioMusic!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SetControlsEnabled(false);
        UpdateStatus("Скачивание по ссылке...", 30);

        ThreadPool.QueueUserWorkItem(delegate {
            try
            {
                string toolsDir = GetToolsDir();
                string ffmpegPath = Path.Combine(toolsDir, "ffmpeg.exe");
                string ytdlpPath = Path.Combine(toolsDir, "yt-dlp.exe");
                ExtractResource("ffmpeg.exe", ffmpegPath);
                ExtractResource("yt-dlp.exe", ytdlpPath);

                File.AppendAllText(Path.Combine(gameDir, "links.txt"), url + "\r\n", Encoding.UTF8);
                bool isLiveStream = url.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) || url.EndsWith(".pls", StringComparison.OrdinalIgnoreCase) || url.Contains("stream") || url.Contains("radio");

                isCancelRequested = false;
                if (isLiveStream && File.Exists(ffmpegPath))
                {
                    UpdateStatus("Запись эфира (15 мин)...", 60);
                    string outRadio = Path.Combine(gameDir, "RadioStream_" + DateTime.Now.ToString("HHmmss") + ".mp3");
                    string args = string.Format("-y -i \"{0}\" -t 900 -af loudnorm=I=-14:TP=-1.5:LRA=11 -codec:a libmp3lame -qscale:a 2 \"{1}\"", url, outRadio);
                    Process p = Process.Start(new ProcessStartInfo(ffmpegPath, args) { WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true });
                    currentWorkerProcess = p;
                    if (p != null) p.WaitForExit();
                }
                else if (File.Exists(ytdlpPath))
                {
                    UpdateStatus("yt-dlp: скачивание (СТОП для отмены)...", 60);
                    string archiveFile = Path.Combine(gameDir, "downloaded.txt");
                    string postArgs = optLoudNorm ? "--postprocessor-args \"ffmpeg:-af loudnorm=I=-14:TP=-1.5:LRA=11\"" : "";
                    string args = string.Format("-x --audio-format mp3 --ffmpeg-location \"{0}\" {1} --download-archive \"{2}\" -o \"{3}\\%(title)s.%(ext)s\" \"{4}\"", toolsDir, postArgs, archiveFile, gameDir, url);
                    Process p = Process.Start(new ProcessStartInfo(ytdlpPath, args) { WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true });
                    currentWorkerProcess = p;
                    if (p != null) p.WaitForExit();
                }

                if (isCancelRequested) UpdateStatus("Загрузка отменена.", 0);
                else UpdateStatus("Загрузка завершена!", 100);
            }
            catch (Exception ex) { UpdateStatus("Ошибка загрузки: " + ex.Message, 0); }
            finally { SetControlsEnabled(true); }
        });
    }

    private string GetToolsDir()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL", "tools");
        try { Directory.CreateDirectory(dir); } catch { }
        return dir;
    }

    private void SilentSync(bool firstRun)
    {
        string gameDir = "";
        try { this.Invoke(new Action(delegate { gameDir = txtPath.Text.Trim('"'); })); } catch { }
        if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir)) { UpdateStatus("Укажите папку RadioMusic через Обзор...", 0); return; }

        try
        {
            UpdateStatus("Синхронизация модулей VTOL QoL...", 30);
            string toolsDir = GetToolsDir();
            ExtractResource("ffmpeg.exe", Path.Combine(toolsDir, "ffmpeg.exe"));
            ExtractResource("yt-dlp.exe", Path.Combine(toolsDir, "yt-dlp.exe"));

            string appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL");
            Directory.CreateDirectory(appFolder);
            string installedExe = Path.Combine(appFolder, "VTOL_QoL.exe");
            string currentExe = Process.GetCurrentProcess().MainModule.FileName;
            if (!string.Equals(currentExe, installedExe, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Copy(currentExe, installedExe, true); } catch { }
            }

            string batPath = Path.Combine(gameDir, "Shuffle.bat");
            File.WriteAllText(batPath,
                "@echo off\r\n\"" + installedExe + "\" --shuffle \"" + gameDir + "\"\r\n",
                Encoding.ASCII);

            ConfigureSteam(batPath);

            UpdateStatus("VTOL QoL готов к работе.", 100);
        }
        catch (Exception ex) { UpdateStatus("Ошибка синхронизации: " + ex.Message, 0); }
    }

    private void ConfigureSteam(string batPath)
    {
        string steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", "") as string;
        if (steamPath == null) steamPath = "";
        steamPath = steamPath.Replace('/', '\\');
        if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(Path.Combine(steamPath, "userdata"))) return;

        string escapedLaunch = ("cmd /c \"\"" + batPath + "\" & %command%\"").Replace("\\", "\\\\").Replace("\"", "\\\"");
        string[] vdfFiles = Directory.GetFiles(Path.Combine(steamPath, "userdata"), "localconfig.vdf", SearchOption.AllDirectories);

        bool needsUpdate = false;
        foreach (string vdf in vdfFiles)
        {
            string text = File.ReadAllText(vdf, Encoding.UTF8);
            if (Regex.IsMatch(text, "\"667970\"\\s*\\{") && !text.Contains(escapedLaunch)) { needsUpdate = true; break; }
        }
        if (!needsUpdate) return;

        DialogResult confirm = MessageBox.Show(
            "Для автозапуска шафла и трекера нужно один раз перезапустить Steam и прописать параметры запуска VTOL VR.\nЗакрыть Steam сейчас и внести изменения?",
            "Настройка интеграции со Steam", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        bool steamWasRunning = false;
        Process[] steamProcs = Process.GetProcessesByName("steam");
        if (steamProcs.Length > 0)
        {
            steamWasRunning = true;
            try { Process.Start(new ProcessStartInfo("steam://exit") { UseShellExecute = true }); } catch { }
            DateTime waitDeadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < waitDeadline)
            {
                if (Process.GetProcessesByName("steam").Length == 0) break;
                Thread.Sleep(500);
            }
            foreach (Process p in Process.GetProcessesByName("steam")) { try { p.Kill(); } catch { } }
            Thread.Sleep(1000);
        }

        foreach (string vdf in vdfFiles)
        {
            string text = File.ReadAllText(vdf, Encoding.UTF8);
            Match appMatch = Regex.Match(text, "\"667970\"\\s*\\{");
            if (appMatch.Success)
            {
                File.Copy(vdf, vdf + ".bak", true);
                int startIdx = appMatch.Index;
                int braceCount = 0, endIdx = -1;
                for (int i = startIdx; i < text.Length; i++)
                {
                    if (text[i] == '{') braceCount++;
                    else if (text[i] == '}') { braceCount--; if (braceCount == 0) { endIdx = i; break; } }
                }
                if (endIdx > startIdx)
                {
                    string appBlock = text.Substring(startIdx, endIdx - startIdx + 1);
                    if (Regex.IsMatch(appBlock, "\"LaunchOptions\"\\s*\""))
                        appBlock = Regex.Replace(appBlock, "(\"LaunchOptions\"\\s*\")[^\"]*(\")", "${1}" + escapedLaunch + "${2}");
                    else
                        appBlock = Regex.Replace(appBlock, "(\"667970\"\\s*\\{)", "\"667970\"\n\t\t\t\t\t{\n\t\t\t\t\t\t\"LaunchOptions\"\t\t\"" + escapedLaunch + "\"");
                    text = text.Substring(0, startIdx) + appBlock + text.Substring(endIdx + 1);
                    File.WriteAllText(vdf, text, new UTF8Encoding(false));
                }
            }
        }

        if (steamWasRunning && File.Exists(Path.Combine(steamPath, "steam.exe")))
            Process.Start(Path.Combine(steamPath, "steam.exe"));
    }

    [STAThread]
    static void Main(string[] args)
    {
        bool isShuffleMode = (args != null && args.Length >= 1 && args[0] == "--shuffle");
        if (!isShuffleMode)
            AppLogger.InitSession();

        AppLogger.Log("CORE", "Запуск приложения. Аргументы: " + (args != null ? string.Join(" ", args) : "нет"));

        if (isShuffleMode)
        {
            string dir = args.Length >= 2 ? args[1] : "";
            if (!string.IsNullOrEmpty(dir))
            {
                DateTime deadline = DateTime.UtcNow.AddSeconds(40);
                while (DateTime.UtcNow < deadline)
                {
                    if (!MusicShuffler.IsGameRunning()) break;
                    Thread.Sleep(500);
                }
                bool smart = SettingsManagerSafeGetShuffleFlag();
                MusicShuffler.ShuffleNow(dir, smart);
            }

            // Если основной экземпляр VTOL_QoL.exe не открыт — запускаем трекер в фоне на время этой игровой сессии
            bool createdNewBg;
            using (Mutex bgMutex = new Mutex(true, @"Local\VTOL_QoL_Tracker_SingleInstance", out createdNewBg))
            {
                if (createdNewBg)
                {
                    SettingsManager.Load();
                    GameTracker bgTracker = new GameTracker();
                    bgTracker.DiscordEnabled = SettingsManager.GetBool("Discord", true);
                    bgTracker.DuckingEnabled = SettingsManager.GetBool("Ducking", true);
                    bgTracker.AudioSwitchEnabled = SettingsManager.GetBool("AudioSwitch", true);
                    bgTracker.DiagnosticsEnabled = SettingsManager.GetBool("DiagnosticsEnabled", false);
                    bgTracker.Start();

                    // Ждём старта VTOLVR.exe (до 30 сек), а затем работаем, пока игра открыта
                    DateTime startWait = DateTime.UtcNow.AddSeconds(30);
                    while (DateTime.UtcNow < startWait && !MusicShuffler.IsGameRunning())
                        Thread.Sleep(500);

                    while (MusicShuffler.IsGameRunning())
                        Thread.Sleep(2000);

                    Thread.Sleep(2500); // даём трекеру завершить очистку Discord и вернуть звук
                }
                else
                {
                    AppLogger.Log("CORE", "Основной экземпляр VTOL_QoL уже активен, фоновый трекер пропущен.");
                }
            }
            return;
        }

        bool createdNewMain;
        using (Mutex mainMutex = new Mutex(true, @"Local\VTOL_QoL_Tracker_SingleInstance", out createdNewMain))
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            SettingsManager.Load();
            Application.Run(new MainForm());
        }

    }


    private static bool SettingsManagerSafeGetShuffleFlag()
    {
        try { SettingsManager.Load(); return SettingsManager.GetBool("Shuffle", true); } catch { return true; }
    }
}
