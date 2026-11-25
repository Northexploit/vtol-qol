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

        SaveVaultForm openVault = null;
        Button btnBackup = new Button() { Text = "🛡 Бэкап сохранений", Left = 22, Top = 266, Width = 195, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(130, 80, 0), ForeColor = Color.White };
        btnBackup.Click += (s, e) => {
            if (openVault != null && !openVault.IsDisposed) { openVault.Activate(); openVault.BringToFront(); return; }
            openVault = new SaveVaultForm();
            openVault.FormClosed += (s2, e2) => { openVault = null; };
            openVault.Show(this);
        };
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

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

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
    private volatile string netVpnName = "";
    private ToolTip indTipRef = null;
    private System.Windows.Forms.Timer uiStatusTimer;
    private Font indFontNormal = new Font("Consolas", 8.5f, FontStyle.Bold);
    private Font indFontUnderline = new Font("Consolas", 8.5f, FontStyle.Bold | FontStyle.Underline);
    private volatile bool bgDiscordRunning = false;
    private volatile bool bgSteamRunning = false;
    private volatile int bgTrackCount = -1;
    private volatile bool bgProbeBusy = false;
    private SettingsDialogForm openSettingsForm = null;
    private ServerBrowserForm openServersForm = null;
    private MusicManagerForm openMusicForm = null;
    private LiveryManagerForm openLiveryForm = null;
    private FlightLogbookForm openLogbookForm = null;
    private AudioSettingsForm openAudioForm = null;
    private LogDiagnosticsForm openDiagForm = null;

    private bool BringExistingToFront(Form f)
    {
        if (f != null && !f.IsDisposed)
        {
            if (f.WindowState == FormWindowState.Minimized)
                f.WindowState = FormWindowState.Normal;
            f.Activate();
            f.BringToFront();
            return true;
        }
        return false;
    }

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
        indTipRef = indTip;
        System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += (s, e) => { netState = 0; TriggerNetworkCheck(true); };
        System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += (s, e) => { netState = 0; TriggerNetworkCheck(true); };

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

        lblIndMusic = new Label() { Left = 260, Top = 5, Width = 250, Height = 16, Font = indFontNormal, ForeColor = Color.Silver, Text = "● Треки: проверка... ⚙", Cursor = Cursors.Hand };
        lblIndMusic.Click += new EventHandler(BtnBrowse_Click);
        lblIndMusic.MouseEnter += (s, e) => { lblIndMusic.Font = indFontUnderline; };
        lblIndMusic.MouseLeave += (s, e) => { lblIndMusic.Font = indFontNormal; };
        indTip.SetToolTip(lblIndMusic, "Нажмите, чтобы изменить путь к папке RadioMusic");

        lblIndAudio = new Label() { Left = 8, Top = 23, Width = 245, Height = 16, Font = indFontNormal, ForeColor = Color.Silver, Text = "● VR Аудио: проверка... ⚙", Cursor = Cursors.Hand };
        lblIndAudio.Click += new EventHandler(BtnAudioSettings_Click);
        lblIndAudio.MouseEnter += (s, e) => { lblIndAudio.Font = indFontUnderline; };
        lblIndAudio.MouseLeave += (s, e) => { lblIndAudio.Font = indFontNormal; };
        indTip.SetToolTip(lblIndAudio, "Нажмите, чтобы выбрать наушники и микрофон VR-шлема");

        lblIndPilot = new Label() { Left = 260, Top = 23, Width = 250, Height = 16, Font = indFontNormal, ForeColor = Color.Cyan, Text = "● Пилот: —" };

        lblIndSteam = new Label() { Left = 8, Top = 41, Width = 245, Height = 16, Font = indFontNormal, ForeColor = Color.Silver, Text = "● Steam: проверка... ⚙", Cursor = Cursors.Hand };

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
        lblIndSteam.MouseEnter += (s, e) => { lblIndSteam.Font = indFontUnderline; };
        lblIndSteam.MouseLeave += (s, e) => { lblIndSteam.Font = indFontNormal; };
        indTip.SetToolTip(lblIndSteam, "Нажмите, чтобы запустить Steam или проверить интеграцию");

        lblIndNet = new Label() { Left = 260, Top = 41, Width = 250, Height = 16, Font = indFontNormal, ForeColor = Color.Silver, Text = "● Сеть: проверка... ↻", Cursor = Cursors.Hand };
        lblIndNet.Click += (s, e) => {
            netState = 0;
            RefreshStatusIndicators();
            TriggerNetworkCheck(true);
        };
        lblIndNet.MouseEnter += (s, e) => { lblIndNet.Font = indFontUnderline; };
        lblIndNet.MouseLeave += (s, e) => { lblIndNet.Font = indFontNormal; };
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
        if (BringExistingToFront(openSettingsForm)) return;
        openSettingsForm = new SettingsDialogForm();
        openSettingsForm.FormClosed += (s, e) => {
            SettingsDialogForm dlg = openSettingsForm;
            openSettingsForm = null;
            if (dlg == null) return;
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
        };
        openSettingsForm.Show(this);
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

    private void TriggerNetworkCheck(bool force = false)
    {
        if (netCheckBusy && !force) return;
        netCheckBusy = true;
        lastNetCheckUtc = DateTime.UtcNow;

        ThreadPool.QueueUserWorkItem(delegate {
            try
            {
                netVpnName = SteamLobbyScanner.DetectActiveVpnOrTunnel();
                bool hasInet = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
                if (!hasInet)
                {
                    netYtOk = false; netDcOk = false; netStmOk = false;
                    netState = 3;
                }
                else
                {
                    // Проверяем YouTube, Discord и Steam параллельно без преждевременного Dispose() по таймауту
                    bool yt = false, dc = false, stm = false;
                    CountdownEvent latch = new CountdownEvent(3);
                    ThreadPool.QueueUserWorkItem(delegate { try { yt = CheckHostReachable("https://youtube.com"); } finally { try { if (!latch.IsSet) latch.Signal(); } catch { } } });
                    ThreadPool.QueueUserWorkItem(delegate { try { dc = CheckHostReachable("https://discord.com"); } finally { try { if (!latch.IsSet) latch.Signal(); } catch { } } });
                    ThreadPool.QueueUserWorkItem(delegate { try { stm = CheckHostReachable("https://steamcommunity.com"); } finally { try { if (!latch.IsSet) latch.Signal(); } catch { } } });
                    latch.Wait(2000);

                    netYtOk = yt;
                    netDcOk = dc;
                    netStmOk = stm;

                    if (netYtOk && netDcOk && netStmOk) netState = 1;
                    else if (!netYtOk && !netDcOk && !netStmOk) netState = 3;
                    else netState = 2;
                }

                try { this.BeginInvoke(new Action(RefreshStatusIndicators)); } catch { }
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
            req.Method = "GET";
            req.KeepAlive = false;
            req.ServicePoint.ConnectionLeaseTimeout = 0;
            req.ServicePoint.MaxIdleTime = 0;
            req.Timeout = 1800;
            req.ReadWriteTimeout = 1800;
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

    private void ProbeEnvironmentAsync()
    {
        if (bgProbeBusy) return;
        bgProbeBusy = true;
        string mDir = "";
        try { mDir = txtPath.Text.Trim('"'); } catch { }

        ThreadPool.QueueUserWorkItem(delegate {
            try
            {
                Process[] dc = Process.GetProcessesByName("Discord");
                bgDiscordRunning = dc.Length > 0;
                foreach (Process p in dc) p.Dispose();

                Process[] st = Process.GetProcessesByName("steam");
                bgSteamRunning = st.Length > 0;
                foreach (Process p in st) p.Dispose();

                if (!string.IsNullOrEmpty(mDir) && Directory.Exists(mDir))
                {
                    int cnt = 0;
                    foreach (string f in Directory.EnumerateFiles(mDir, "*.mp3", SearchOption.TopDirectoryOnly))
                        cnt++;
                    bgTrackCount = cnt;
                }
                else
                {
                    bgTrackCount = -1;
                }
            }
            catch { }
            finally { bgProbeBusy = false; }
        });
    }

    private void RefreshStatusIndicators()
    {
        if (lblIndDiscord == null) return;

        if ((DateTime.UtcNow - lastNetCheckUtc).TotalSeconds >= 6)
            TriggerNetworkCheck();

        ProbeEnvironmentAsync();
        bool discordProcRunning = bgDiscordRunning;

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
            int count = Math.Max(0, bgTrackCount);
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
            bool steamRunning = bgSteamRunning;
            string appBat = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL", "Shuffle.bat");
            bool batReady = File.Exists(appBat);

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
            bool hasVpn = !string.IsNullOrEmpty(netVpnName);

            if (netState == 0)
            {
                lblIndNet.Text = "● Сеть: проверка... ↻";
                lblIndNet.ForeColor = Color.Silver;
            }
            else if (netState == 3)
            {
                lblIndNet.Text = "● Сеть: нет интернета ✖";
                lblIndNet.ForeColor = Color.OrangeRed;
            }
            else if (hasVpn)
            {
                // При включённом VPN всегда показываем состояние каждого сервиса (YT, DC, STM)
                lblIndNet.Text = string.Format("● VPN: YT{0} DC{1} STM{2}",
                    netYtOk ? "✔" : "✖", netDcOk ? "✔" : "✖", netStmOk ? "✔" : "✖");
                lblIndNet.ForeColor = (netState == 1) ? Color.Cyan : Color.Gold;
            }
            else if (netState == 1)
            {
                lblIndNet.Text = "● Сеть: Прямое соединение ✔";
                lblIndNet.ForeColor = Color.FromArgb(0, 255, 102);
            }
            else
            {
                lblIndNet.Text = string.Format("● Прямое: YT{0} DC{1} STM{2}",
                    netYtOk ? "✔" : "✖", netDcOk ? "✔" : "✖", netStmOk ? "✔" : "✖");
                lblIndNet.ForeColor = Color.Gold;
            }

            if (indTipRef != null)
            {
                string tipText = string.Format(
                    "Маршрут: {0}\nYouTube: {1} | Discord: {2} | Steam: {3}\n(Нажмите для принудительной проверки)",
                    hasVpn ? ("VPN / туннель (" + netVpnName + ")") : "Прямое подключение (без VPN)",
                    netYtOk ? "доступен" : "недоступен",
                    netDcOk ? "доступен" : "недоступен",
                    netStmOk ? "доступен" : "недоступен");
                if (indTipRef.GetToolTip(lblIndNet) != tipText)
                    indTipRef.SetToolTip(lblIndNet, tipText);
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
        if (BringExistingToFront(openServersForm)) return;
        string dir = txtPath.Text.Trim('"');
        string gameRoot = Directory.Exists(dir) ? Path.GetFullPath(Path.Combine(dir, "..")) : "";
        openServersForm = new ServerBrowserForm(gameRoot);
        openServersForm.FormClosed += (s, ev) => { openServersForm = null; };
        openServersForm.Show(this);
    }

    private void BtnManageMusic_Click(object sender, EventArgs e)
    {
        if (BringExistingToFront(openMusicForm)) return;
        string dir = txtPath.Text.Trim('"');
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            MessageBox.Show(this, "Сначала укажите корректную папку RadioMusic!", "Внимание", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        openMusicForm = new MusicManagerForm(dir);
        openMusicForm.FormClosed += (s, ev) => { openMusicForm = null; };
        openMusicForm.Show(this);
    }

    private void BtnLiveryManager_Click(object sender, EventArgs e)
    {
        if (BringExistingToFront(openLiveryForm)) return;
        string dir = txtPath.Text.Trim('"');
        string gameRoot = Directory.Exists(dir) ? Path.GetFullPath(Path.Combine(dir, "..")) : "";
        openLiveryForm = new LiveryManagerForm(gameRoot);
        openLiveryForm.FormClosed += (s, ev) => { openLiveryForm = null; };
        openLiveryForm.Show(this);
    }

    private void BtnLogbook_Click(object sender, EventArgs e)
    {
        if (BringExistingToFront(openLogbookForm)) return;
        openLogbookForm = new FlightLogbookForm();
        openLogbookForm.FormClosed += (s, ev) => { openLogbookForm = null; };
        openLogbookForm.Show(this);
    }

    private void BtnAudioSettings_Click(object sender, EventArgs e)
    {
        if (BringExistingToFront(openAudioForm)) return;
        openAudioForm = new AudioSettingsForm();
        openAudioForm.FormClosed += (s, ev) => { openAudioForm = null; };
        openAudioForm.Show(this);
    }

    private void BtnDiagnostics_Click(object sender, EventArgs e)
    {
        if (BringExistingToFront(openDiagForm)) return;
        openDiagForm = new LogDiagnosticsForm();
        openDiagForm.FormClosed += (s, ev) => {
            openDiagForm = null;
            optDiagnostics = SettingsManager.GetBool("DiagnosticsEnabled", false);
            tracker.DiagnosticsEnabled = optDiagnostics;
        };
        openDiagForm.Show(this);
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

    private static readonly object extractLock = new object();

    private void ExtractResource(string resName, string targetPath, bool forceOverwrite = false)
    {
        lock (extractLock)
        {
            if (!forceOverwrite && File.Exists(targetPath) && new FileInfo(targetPath).Length > 1024)
                return;

            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resName))
            {
                if (stream == null) return;
                string tmpPath = targetPath + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
                try
                {
                    using (FileStream fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        stream.CopyTo(fs);

                    if (File.Exists(targetPath))
                    {
                        try { File.Delete(targetPath); }
                        catch { File.Delete(tmpPath); return; }
                    }
                    File.Move(tmpPath, targetPath);
                }
                catch
                {
                    try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
                }
            }
        }
    }

    private void EnsureLatestTools(string toolsDir, out string ffmpegPath, out string ytdlpPath)
    {
        ffmpegPath = Path.Combine(toolsDir, "ffmpeg.exe");
        ytdlpPath = Path.Combine(toolsDir, "yt-dlp.exe");
        ExtractResource("ffmpeg.exe", ffmpegPath, false);

        string curVer = Assembly.GetExecutingAssembly().GetName().Version.ToString();
        string savedVer = SettingsManager.Get("BundledToolsVersion", "");
        bool versionChanged = !string.Equals(curVer, savedVer, StringComparison.OrdinalIgnoreCase);
        ExtractResource("yt-dlp.exe", ytdlpPath, versionChanged);
        if (versionChanged && File.Exists(ytdlpPath))
        {
            SettingsManager.Set("BundledToolsVersion", curVer);
            SettingsManager.Save();
        }

        string lastUpdateStr = SettingsManager.Get("LastYtDlpSelfUpdate", "");
        DateTime lastUpd;
        if (File.Exists(ytdlpPath) && (!DateTime.TryParse(lastUpdateStr, out lastUpd) || (DateTime.UtcNow - lastUpd).TotalDays >= 7))
        {
            try
            {
                Process pUpd = Process.Start(new ProcessStartInfo(ytdlpPath, "-U") { WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true });
                if (pUpd != null) { pUpd.WaitForExit(15000); pUpd.Dispose(); }
                SettingsManager.Set("LastYtDlpSelfUpdate", DateTime.UtcNow.ToString("o"));
                SettingsManager.Save();
            }
            catch { }
        }
    }

    private string SanitizeHttpUrl(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string trimmed = raw.Trim().Trim('"', '\'', '<', '>');
        if (trimmed.IndexOfAny(new char[] { '"', '\r', '\n', '\0', '|', '&', ';', '`' }) >= 0)
            return null;

        Uri uri;
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out uri)) return null;
        if (!uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) &&
            !uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            return null;

        return uri.AbsoluteUri;
    }

    private string GetUniqueOutputPath(string dir, string baseName, string ext, string sourcePath)
    {
        string cleanBase = Regex.Replace(baseName, @"^\d{4}_", "");
        if (string.IsNullOrWhiteSpace(cleanBase)) cleanBase = "Track";
        string candidate = Path.Combine(dir, cleanBase + ext);

        if (!string.IsNullOrEmpty(sourcePath) &&
            string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(candidate), StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(dir, "__reencode_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ext);
        }

        int counter = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(dir, string.Format("{0} ({1}){2}", cleanBase, counter, ext));
            counter++;
        }
        return candidate;
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
                string safeUrl = SanitizeHttpUrl(Clipboard.GetText());
                if (!string.IsNullOrEmpty(safeUrl))
                {
                    StartUrlDownload(safeUrl);
                    return;
                }
            }
            if (notifyIfEmpty)
                UpdateStatus("Скопируйте корректную ссылку (http/https) или перетащите файлы.", 0);
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
            string rawDrop = e.Data.GetData(DataFormats.UnicodeText) as string ?? e.Data.GetData(DataFormats.Text) as string ?? "";
            string safeUrl = SanitizeHttpUrl(rawDrop);
            if (!string.IsNullOrEmpty(safeUrl))
            {
                StartUrlDownload(safeUrl);
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
                ExtractResource("ffmpeg.exe", ffmpegPath, false);

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
                    bool isSameDir = string.Equals(Path.GetFullPath(Path.GetDirectoryName(src)), Path.GetFullPath(gameDir), StringComparison.OrdinalIgnoreCase);

                    if (isSameDir && Path.GetExtension(src).Equals(".mp3", StringComparison.OrdinalIgnoreCase) && !optLoudNorm)
                        continue;

                    string outMp3 = GetUniqueOutputPath(gameDir, baseName, ".mp3", src);
                    bool isInPlaceReencode = Path.GetFileName(outMp3).StartsWith("__reencode_");
                    int pct = (int)(((i + 1) / (double)filesToProcess.Count) * 100);
                    UpdateStatus(string.Format("Обработка [{0}/{1}]: {2}...", i + 1, filesToProcess.Count, baseName), pct);

                    if (File.Exists(ffmpegPath))
                    {
                        string afFilter = optLoudNorm ? "-af loudnorm=I=-14:TP=-1.5:LRA=11 " : "";
                        string args = string.Format("-y -i \"{0}\" -vn {1}-codec:a libmp3lame -qscale:a 2 \"{2}\"", src, afFilter, outMp3);
                        Process p = Process.Start(new ProcessStartInfo(ffmpegPath, args) { WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true });
                        currentWorkerProcess = p;
                        if (p != null) { p.WaitForExit(); p.Dispose(); }

                        if (isInPlaceReencode)
                        {
                            if (!isCancelRequested && File.Exists(outMp3) && new FileInfo(outMp3).Length > 1024)
                            {
                                File.Delete(src);
                                File.Move(outMp3, src);
                            }
                            else
                            {
                                try { if (File.Exists(outMp3)) File.Delete(outMp3); } catch { }
                            }
                        }
                    }
                    else if (!isSameDir)
                    {
                        File.Copy(src, outMp3, false);
                    }
                }

                if (!isCancelRequested) UpdateStatus(string.Format("Готово! Обработано треков: {0}", filesToProcess.Count), 100);
            }
            catch (Exception ex) { UpdateStatus("Ошибка: " + ex.Message, 0); }
            finally { SetControlsEnabled(true); }
        });
    }


    private bool IsDirectAudioStreamUrl(string url)
    {
        Uri u;
        if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return false;
        string host = u.Host.ToLowerInvariant();
        if (host.Contains("youtube.com") || host.Contains("youtu.be") ||
            host.Contains("soundcloud.com") || host.Contains("twitch.tv") ||
            host.Contains("bandcamp.com") || host.Contains("vimeo.com"))
            return false;

        string path = u.AbsolutePath.ToLowerInvariant();
        return path.EndsWith(".m3u8") || path.EndsWith(".pls") || path.EndsWith(".aac") ||
               path.EndsWith(".ogg") || path.EndsWith(".mp3") || path.EndsWith(".opus") ||
               path.Contains("/stream") || path.Contains("/radio") || path.Contains("/live");
    }

    private void StartUrlDownload(string rawUrl)
    {
        string url = SanitizeHttpUrl(rawUrl);
        string gameDir = txtPath.Text.Trim('"');
        if (string.IsNullOrEmpty(url))
        {
            UpdateStatus("Некорректная или небезопасная ссылка.", 0);
            return;
        }
        if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
        {
            MessageBox.Show("Сначала укажите папку RadioMusic!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SetControlsEnabled(false);
        UpdateStatus("Подготовка загрузчика...", 20);

        ThreadPool.QueueUserWorkItem(delegate {
            try
            {
                string toolsDir = GetToolsDir();
                string ffmpegPath, ytdlpPath;
                EnsureLatestTools(toolsDir, out ffmpegPath, out ytdlpPath);

                string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL");
                Directory.CreateDirectory(appDataDir);
                File.AppendAllText(Path.Combine(appDataDir, "links.txt"), url + "\r\n", Encoding.UTF8);
                bool isLiveStream = IsDirectAudioStreamUrl(url);

                isCancelRequested = false;
                if (isLiveStream && File.Exists(ffmpegPath))
                {
                    UpdateStatus("Запись прямого радиоэфира (15 мин)...", 60);
                    string outRadio = Path.Combine(gameDir, "RadioStream_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mp3");
                    string afFilter = optLoudNorm ? "-af loudnorm=I=-14:TP=-1.5:LRA=11 " : "";
                    string args = string.Format("-y -i \"{0}\" -t 900 {1}-codec:a libmp3lame -qscale:a 2 \"{2}\"", url, afFilter, outRadio);
                    Process p = Process.Start(new ProcessStartInfo(ffmpegPath, args) { WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true });
                    currentWorkerProcess = p;
                    if (p != null) { p.WaitForExit(); p.Dispose(); }
                }
                else if (File.Exists(ytdlpPath))
                {
                    UpdateStatus("yt-dlp: скачивание (СТОП для отмены)...", 60);
                    string archiveFile = Path.Combine(appDataDir, "downloaded.txt");
                    string legacyArchive = Path.Combine(gameDir, "downloaded.txt");
                    if (!File.Exists(archiveFile) && File.Exists(legacyArchive))
                    {
                        try { File.Move(legacyArchive, archiveFile); } catch { }
                    }
                    string postArgs = optLoudNorm ? "--postprocessor-args \"ffmpeg:-af loudnorm=I=-14:TP=-1.5:LRA=11\"" : "";
                    string args = string.Format("--no-exec -x --audio-format mp3 --ffmpeg-location \"{0}\" {1} --download-archive \"{2}\" -o \"{3}\\%(title)s.%(ext)s\" -- \"{4}\"", toolsDir, postArgs, archiveFile, gameDir, url);
                    Process p = Process.Start(new ProcessStartInfo(ytdlpPath, args) { WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true });
                    currentWorkerProcess = p;
                    if (p != null) { p.WaitForExit(); p.Dispose(); }
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
            ExtractResource("ffmpeg.exe", Path.Combine(toolsDir, "ffmpeg.exe"), false);
            ExtractResource("yt-dlp.exe", Path.Combine(toolsDir, "yt-dlp.exe"), false);

            string appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL");
            Directory.CreateDirectory(appFolder);
            string installedExe = Path.Combine(appFolder, "VTOL_QoL.exe");
            string currentExe = Process.GetCurrentProcess().MainModule.FileName;
            string targetExeForBat = currentExe;

            if (!string.Equals(currentExe, installedExe, StringComparison.OrdinalIgnoreCase))
            {
                bool copied = false;
                try
                {
                    File.Copy(currentExe, installedExe, true);
                    copied = true;
                }
                catch
                {
                    try
                    {
                        using (EventWaitHandle stopEv = new EventWaitHandle(false, EventResetMode.ManualReset, @"Local\VTOL_QoL_StopBgTracker"))
                            stopEv.Set();
                    }
                    catch { }
                    Thread.Sleep(600);
                    try
                    {
                        File.Copy(currentExe, installedExe, true);
                        copied = true;
                    }
                    catch (Exception exCopy)
                    {
                        AppLogger.Error("SYNC", "Не удалось обновить копию exe в AppData, используем текущий путь", exCopy);
                    }
                }
                if (copied) targetExeForBat = installedExe;
            }

            string legacyBat = Path.Combine(gameDir, "Shuffle.bat");
            if (File.Exists(legacyBat))
            {
                try { File.Delete(legacyBat); } catch { }
            }

            string batPath = Path.Combine(appFolder, "Shuffle.bat");
            File.WriteAllText(batPath,
                "@echo off\r\nstart \"\" /b \"" + targetExeForBat + "\" --shuffle \"" + gameDir + "\"\r\n",
                Encoding.ASCII);

            ConfigureSteam(batPath);

            UpdateStatus("VTOL QoL готов к работе.", 100);
        }
        catch (Exception ex) { UpdateStatus("Ошибка синхронизации: " + ex.Message, 0); }
    }

    private string UnescapeVdfString(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        StringBuilder sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                char next = s[i + 1];
                if (next == '\\' || next == '"') { sb.Append(next); i++; continue; }
                if (next == 'n') { sb.Append('\n'); i++; continue; }
                if (next == 't') { sb.Append('\t'); i++; continue; }
            }
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    private string EscapeVdfString(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private bool FindVdfBlockBounds(string text, string keyName, out int blockStart, out int blockEnd)
    {
        blockStart = -1;
        blockEnd = -1;
        string pattern = "\"" + keyName + "\"";
        int idx = 0;
        bool inQuotes = false;

        while (idx < text.Length)
        {
            char c = text[idx];
            if (c == '\\' && inQuotes) { idx += 2; continue; }
            if (c == '"')
            {
                if (!inQuotes && idx + pattern.Length <= text.Length &&
                    string.Compare(text, idx, pattern, 0, pattern.Length, StringComparison.Ordinal) == 0)
                {
                    int afterKey = idx + pattern.Length;
                    while (afterKey < text.Length && char.IsWhiteSpace(text[afterKey])) afterKey++;
                    if (afterKey < text.Length && text[afterKey] == '{')
                    {
                        blockStart = idx;
                        int depth = 0;
                        bool innerQuotes = false;
                        for (int j = afterKey; j < text.Length; j++)
                        {
                            char cj = text[j];
                            if (cj == '\\' && innerQuotes) { j++; continue; }
                            if (cj == '"') { innerQuotes = !innerQuotes; continue; }
                            if (!innerQuotes)
                            {
                                if (cj == '{') depth++;
                                else if (cj == '}')
                                {
                                    depth--;
                                    if (depth == 0) { blockEnd = j; return true; }
                                }
                            }
                        }
                        return false;
                    }
                }
                inQuotes = !inQuotes;
            }
            idx++;
        }
        return false;
    }

    private string MergeLaunchOptions(string existingRaw, string batPath)
    {
        string unescaped = UnescapeVdfString(existingRaw).Trim();
        string ourPrefix = "cmd /c \"\"" + batPath + "\" & ";

        // Если наши параметры уже прописаны — ничего не меняем
        if (unescaped.IndexOf(batPath, StringComparison.OrdinalIgnoreCase) >= 0)
            return null;

        if (string.IsNullOrEmpty(unescaped))
            return ourPrefix + "%command%\"";

        if (unescaped.IndexOf("%command%", StringComparison.OrdinalIgnoreCase) >= 0)
            return Regex.Replace(unescaped, "%command%", "cmd /c \"\"" + batPath + "\" & %command%\"", RegexOptions.IgnoreCase);

        // Если у пользователя были только флаги (например "-vrmode oculus"), сохраняем их после %command%
        return ourPrefix + "%command% " + unescaped + "\"";
    }

    private void CreateVersionedBackup(string vdfPath)
    {
        try
        {
            string bakPath = vdfPath + ".bak_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            File.Copy(vdfPath, bakPath, true);
            string dir = Path.GetDirectoryName(vdfPath);
            string basePattern = Path.GetFileName(vdfPath) + ".bak_*";
            string[] allBaks = Directory.GetFiles(dir, basePattern);
            if (allBaks.Length > 5)
            {
                Array.Sort(allBaks);
                for (int i = 0; i < allBaks.Length - 5; i++)
                    try { File.Delete(allBaks[i]); } catch { }
            }
        }
        catch { }
    }

    private void ConfigureSteam(string batPath)
    {
        string steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", "") as string;
        if (steamPath == null) steamPath = "";
        steamPath = steamPath.Replace('/', '\\');
        if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(Path.Combine(steamPath, "userdata"))) return;

        string[] vdfFiles = Directory.GetFiles(Path.Combine(steamPath, "userdata"), "localconfig.vdf", SearchOption.AllDirectories);

        bool needsUpdate = false;
        foreach (string vdf in vdfFiles)
        {
            string text = File.ReadAllText(vdf, Encoding.UTF8);
            int bStart, bEnd;
            if (FindVdfBlockBounds(text, "667970", out bStart, out bEnd))
            {
                string block = text.Substring(bStart, bEnd - bStart + 1);
                Match mLo = Regex.Match(block, "\"LaunchOptions\"\\s+\"((?:\\\\.|[^\"\\\\])*)\"");
                string curLo = mLo.Success ? mLo.Groups[1].Value : "";
                if (MergeLaunchOptions(curLo, batPath) != null)
                {
                    needsUpdate = true;
                    break;
                }
            }
        }
        if (!needsUpdate) return;

        DialogResult confirm = DialogResult.No;
        try
        {
            this.Invoke(new Action(delegate {
                confirm = MessageBox.Show(this,
                    "Для автозапуска шафла и трекера нужно один раз перезапустить Steam и добавить параметры запуска VTOL VR (ваши текущие параметры будут сохранены).\nЗакрыть Steam сейчас и внести изменения?",
                    "Настройка интеграции со Steam", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            }));
        }
        catch { return; }
        if (confirm != DialogResult.Yes) return;

        bool steamWasRunning = false;
        Process[] steamProcs = Process.GetProcessesByName("steam");
        if (steamProcs.Length > 0)
        {
            steamWasRunning = true;
            foreach (Process sp in steamProcs) sp.Dispose();
            try { Process.Start(new ProcessStartInfo("steam://exit") { UseShellExecute = true }); } catch { }

            DateTime waitDeadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < waitDeadline)
            {
                Process[] cur = Process.GetProcessesByName("steam");
                int cnt = cur.Length;
                foreach (Process p in cur) p.Dispose();
                if (cnt == 0) break;
                Thread.Sleep(500);
            }

            Process[] stillRunning = Process.GetProcessesByName("steam");
            if (stillRunning.Length > 0)
            {
                foreach (Process p in stillRunning) p.Dispose();
                UpdateStatus("Steam не успел закрыться за 20 сек. Закройте Steam вручную и повторите.", 0);
                return;
            }
            Thread.Sleep(800);
        }

        foreach (string vdf in vdfFiles)
        {
            string text = File.ReadAllText(vdf, Encoding.UTF8);
            int startIdx, endIdx;
            if (FindVdfBlockBounds(text, "667970", out startIdx, out endIdx))
            {
                string appBlock = text.Substring(startIdx, endIdx - startIdx + 1);
                Match mLo = Regex.Match(appBlock, "\"LaunchOptions\"\\s+\"((?:\\\\.|[^\"\\\\])*)\"");
                string curLo = mLo.Success ? mLo.Groups[1].Value : "";
                string merged = MergeLaunchOptions(curLo, batPath);
                if (merged == null) continue;

                CreateVersionedBackup(vdf);
                string escapedNew = EscapeVdfString(merged);

                if (mLo.Success)
                    appBlock = appBlock.Substring(0, mLo.Index) + "\"LaunchOptions\"\t\t\"" + escapedNew + "\"" + appBlock.Substring(mLo.Index + mLo.Length);
                else
                    appBlock = Regex.Replace(appBlock, "^(\"667970\"\\s*\\{)", "\"667970\"\n\t\t\t\t\t{\n\t\t\t\t\t\t\"LaunchOptions\"\t\t\"" + escapedNew + "\"");
                text = text.Substring(0, startIdx) + appBlock + text.Substring(endIdx + 1);
                File.WriteAllText(vdf, text, new UTF8Encoding(false));
            }
        }
        if (steamWasRunning && File.Exists(Path.Combine(steamPath, "steam.exe")))
            Process.Start(Path.Combine(steamPath, "steam.exe"));
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [STAThread]
    static void Main(string[] args)
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (s, e) =>
        {
            try { File.WriteAllText("crash_last.log", e.Exception.ToString()); } catch { }
        };
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            try { File.WriteAllText("crash_last.log", e.ExceptionObject.ToString()); } catch { }
        };
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

                    DateTime startWait = DateTime.UtcNow.AddSeconds(30);
                    while (DateTime.UtcNow < startWait && !MusicShuffler.IsGameRunning())
                    {
                        if (FindWindow(null, "VTOL QoL") != IntPtr.Zero) return;
                        Thread.Sleep(500);
                    }

                    while (MusicShuffler.IsGameRunning())
                    {
                        if (FindWindow(null, "VTOL QoL") != IntPtr.Zero) return;
                        Thread.Sleep(2000);
                    }

                    Thread.Sleep(2500);
                }
                else
                {
                    AppLogger.Log("CORE", "Трекер VTOL_QoL уже активен, фоновый дубль пропущен.");
                }
            }
            return;
        }

        bool createdNewMain;
        using (Mutex mainMutex = new Mutex(true, @"Local\VTOL_QoL_MainUI_SingleInstance", out createdNewMain))
        {
            if (!createdNewMain)
            {
                IntPtr existingHwnd = FindWindow(null, "VTOL QoL");
                if (existingHwnd != IntPtr.Zero)
                {
                    ShowWindowAsync(existingHwnd, 9); // SW_RESTORE
                    SetForegroundWindow(existingHwnd);
                }
                return;
            }

            bool createdNewTracker;
            using (Mutex trackerMutex = new Mutex(true, @"Local\VTOL_QoL_Tracker_SingleInstance", out createdNewTracker))
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                SettingsManager.Load();
                Application.Run(new MainForm());
            }
        }
    }

    private static bool SettingsManagerSafeGetShuffleFlag()
    {
        try { SettingsManager.Load(); return SettingsManager.GetBool("Shuffle", true); } catch { return true; }
    }
}

