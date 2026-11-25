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
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using OpenFileDialog = System.Windows.Forms.OpenFileDialog;
using SaveFileDialog = System.Windows.Forms.SaveFileDialog;


public static class SteamLobbyScanner
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate bool SteamAPI_InitDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SteamAPI_ShutdownDelegate();

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
        // 1. Проверка только реально поднятых (Up) VPN/TUN/TAP адаптеров с рабочим IPv4-адресом
        try
        {
            foreach (System.Net.NetworkInformation.NetworkInterface ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                string desc = (ni.Description + " " + ni.Name).ToLowerInvariant();
                if (desc.Contains("teredo") || desc.Contains("isatap") || desc.Contains("6to4") || desc.Contains("loopback"))
                    continue;

                bool looksLikeVpn =
                    ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Tunnel ||
                    ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Ppp ||
                    desc.Contains("wireguard") || desc.Contains("openvpn") || desc.Contains("tap-") ||
                    desc.Contains("wintun") || desc.Contains("tun") || desc.Contains("vpn") ||
                    desc.Contains("radmin") || desc.Contains("zerotier") || desc.Contains("hamachi") ||
                    desc.Contains("nekoray") || desc.Contains("nekobox") || desc.Contains("v2ray") ||
                    desc.Contains("clash") || desc.Contains("amnezia") || desc.Contains("sing-box") ||
                    desc.Contains("outline") || desc.Contains("warp") || desc.Contains("proton") ||
                    desc.Contains("windscribe") || desc.Contains("mullvad") || desc.Contains("nordlynx");

                if (!looksLikeVpn) continue;

                // Убеждаемся, что интерфейсу выдан реальный IPv4-адрес (когда VPN отключён, адреса нет или он 169.254.x.x)
                foreach (System.Net.NetworkInformation.UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        byte[] b = ip.Address.GetAddressBytes();
                        if (b[0] != 127 && !(b[0] == 169 && b[1] == 254))
                            return ni.Name;
                    }
                }
            }
        }
        catch { }

        // 2. Проверка включённого системного прокси Windows (для SOCKS5/HTTP VPN без TUN-адаптера)
        try
        {
            object proxyEnabled = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Internet Settings", "ProxyEnable", 0);
            if (proxyEnabled != null && Convert.ToInt32(proxyEnabled) == 1)
            {
                string proxySrv = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Internet Settings", "ProxyServer", "") as string;
                if (!string.IsNullOrWhiteSpace(proxySrv))
                    return "Прокси (" + proxySrv + ")";
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
        if (hMod == IntPtr.Zero)
        {
            Environment.SetEnvironmentVariable("SteamAppId", null);
            Environment.SetEnvironmentVariable("SteamGameId", null);
            throw new Exception("Не удалось загрузить steam_api64.dll.");
        }

        SteamAPI_InitDelegate fnInit = GetExport<SteamAPI_InitDelegate>(hMod, "SteamAPI_Init");
        if (!fnInit())
        {
            Environment.SetEnvironmentVariable("SteamAppId", null);
            Environment.SetEnvironmentVariable("SteamGameId", null);
            throw new Exception("SteamAPI_Init вернул false.");
        }

        try
        {
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
        finally
        {
            try
            {
                IntPtr pShut = GetProcAddress(hMod, "SteamAPI_Shutdown");
                if (pShut != IntPtr.Zero)
                {
                    SteamAPI_ShutdownDelegate fnShut = (SteamAPI_ShutdownDelegate)Marshal.GetDelegateForFunctionPointer(pShut, typeof(SteamAPI_ShutdownDelegate));
                    fnShut();
                }
            }
            catch { }
            Environment.SetEnvironmentVariable("SteamAppId", null);
            Environment.SetEnvironmentVariable("SteamGameId", null);
        }
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

