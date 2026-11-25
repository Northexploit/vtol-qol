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
    private string lastSentActivityKey = "";
    private DateTime lastActivitySentUtc = DateTime.MinValue;
    private static bool elevationAttempted = false;
    private LogPatterns patterns;

    private string originalPlaybackId = null;
    private string originalCaptureId = null;
    private bool switchedAudio = false;
    private string lastFiredPlayerMissile = "";
    private Dictionary<string, string> targetIdToMissile = new Dictionary<string, string>();
    private string activeReplayFile = null;
    private DateTime activeReplayStartUtc = DateTime.UtcNow;

    private string activeCampaignId = "";
    private string activeScenarioId = "";
    private string activeVtsPath = "";

    private void StartReplayRecording(string pilot, string aircraft)
    {
        try
        {
            string dir = DebriefEngine.GetReplaysDir();
            activeReplayStartUtc = DateTime.UtcNow;
            string safeAc = Regex.Replace(aircraft ?? "VTOL", @"[^A-Za-z0-9\-]", "_");
            activeReplayFile = Path.Combine(dir, string.Format("Sortie_{0}_{1}.vtolrep", DateTime.Now.ToString("yyyyMMdd_HHmmss"), safeAc));
            StringBuilder hdr = new StringBuilder();
            hdr.AppendFormat("0.0|META|{0}|{1}\r\n", pilot ?? "Pilot", aircraft ?? "VTOL");
            if (!string.IsNullOrEmpty(activeVtsPath))
                hdr.AppendFormat("0.0|VTS|{0}|\r\n", activeVtsPath);
            if (!string.IsNullOrEmpty(activeCampaignId) || !string.IsNullOrEmpty(activeScenarioId))
                hdr.AppendFormat("0.0|SCENARIO|{0}|{1}\r\n", activeCampaignId, activeScenarioId);
            File.WriteAllText(activeReplayFile, hdr.ToString(), System.Text.Encoding.UTF8);
        }
        catch { activeReplayFile = null; }
    }

    private void RecordReplayEvent(string kind, string arg1, string arg2 = "")
    {
        if (string.IsNullOrEmpty(activeReplayFile)) return;
        try
        {
            double sec = Math.Max(0.0, (DateTime.UtcNow - activeReplayStartUtc).TotalSeconds);
            string clean1 = (arg1 ?? "").Replace("|", "/").Replace("\r", "").Replace("\n", " ");
            string clean2 = (arg2 ?? "").Replace("|", "/").Replace("\r", "").Replace("\n", " ");
            string line = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F1}|{1}|{2}|{3}\r\n", sec, kind, clean1, clean2);
            File.AppendAllText(activeReplayFile, line, System.Text.Encoding.UTF8);
        }
        catch { }
    }

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
                    AudioSessionDucking.SetDuck(false, "VTOLVR");
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

                                // 1.5. Определяем загруженную кампанию, сценарий и путь к .vts файлу
                                Match mCamp = Regex.Match(chunk, @"LoadCampaign\((?<camp>[^,\)\r\n]+),\s*(?<scen>[^\)\r\n]+)\)", RegexOptions.IgnoreCase);
                                if (mCamp.Success)
                                {
                                    activeCampaignId = mCamp.Groups["camp"].Value.Trim();
                                    activeScenarioId = mCamp.Groups["scen"].Value.Trim();
                                    activeVtsPath = "";
                                    currentMission = activeCampaignId + " / " + activeScenarioId;
                                    AppLogger.Log("TRACKER", "Обнаружена кампания/миссия: " + currentMission);
                                }

                                Match mVts = Regex.Match(chunk, @"scenario\s+(?<path>[A-Za-z]:\\[^\r\n]+?\.vts)\b", RegexOptions.IgnoreCase);
                                if (mVts.Success)
                                {
                                    string pVts = mVts.Groups["path"].Value.Trim();
                                    if (File.Exists(pVts))
                                    {
                                        activeVtsPath = pVts;
                                        currentMission = Path.GetFileNameWithoutExtension(pVts);
                                        AppLogger.Log("TRACKER", "Обнаружен файл сценария .vts: " + activeVtsPath);
                                    }
                                }

                                Match mScen = Regex.Match(chunk, @"(?:Loading campaign mission|Loading workshop scenario|Starting mission|Selected scenario|Scenario name)[:\s]+(?<val>[^\r\n\(\[]+)", RegexOptions.IgnoreCase);
                                if (mScen.Success)
                                {
                                    string sc = mScen.Groups["val"].Value.Trim().Trim('"', '\'', '.', ' ');
                                    if (sc.Length > 2 && sc.Length < 60 &&
                                        !Regex.IsMatch(sc, @"^(?:none|async|true|false|null|scene|level)$", RegexOptions.IgnoreCase))
                                    {
                                        currentMission = sc;
                                        if (string.IsNullOrEmpty(activeScenarioId)) activeScenarioId = sc;
                                    }
                                }

                                // 2. Сброс при выходе из миссии (сингл или МП)
                                if (inSortie && Regex.IsMatch(chunk, @"(?:PlayerSpawn destroyed\.|selectedSlot\s*=>\s*-1)", RegexOptions.IgnoreCase))
                                {
                                    string acWithRole = FormatAircraftWithRole(currentAircraft, currentCrewRole);
                                    WriteFlightLog("LANDED", acWithRole, currentCallsign, GetDurStr(sortieStart), kills, "Завершение вылета / выход в брифинг");
                                    RecordReplayEvent("END", "Выход в брифинг");
                                    activeReplayFile = null;
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

                                // 4. Старт реального вылета
                                bool mpSpawned = !string.IsNullOrEmpty(myNickEsc) && Regex.IsMatch(chunk, @"(?:FlightLogger:\s*)?" + myNickEsc + @"\s+has spawned", RegexOptions.IgnoreCase);
                                bool spSpawned = Regex.IsMatch(chunk, @"PlayerSpawn:OnSpawnUnit\(\)|Registering actor Player \[\d+\]", RegexOptions.IgnoreCase);

                                if (mpSpawned || spSpawned)
                                {
                                    string veh = SettingsManager.Get("LastSelectedVehicle", "В ангаре / Меню");
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
                                        SettingsManager.Set("LastPilotNick", myNick);
                                        StartReplayRecording(myNick, acDisplay);
                                        AppLogger.Log("TRACKER", string.Format("Старт вылета ({0}): Борт={1}, Позывной={2}", mpSpawned ? "MP" : "SP", acDisplay, currentCallsign));
                                        WriteFlightLog("SORTIE", acDisplay, currentCallsign, "00:00", 0, mpSpawned ? "Вылет (мультиплеер)" : "Вылет (одиночная миссия)");
                                    }
                                }

                                // 5. ПОЛНАЯ БОЕВАЯ ТЕЛЕМЕТРИЯ ИЗ PLAYER.LOG (АКТОРЫ, РАДАРЫ, RWR, РАКЕТЫ, ПОВРЕЖДЕНИЯ, УБИЙСТВА)
                                if (!string.IsNullOrEmpty(myNickEsc))
                                {
                                    // 5.1 Взлёт / Посадка / Задачи миссии
                                    Match mTakeoff = Regex.Match(chunk, @"FlightLogger:\s*" + myNickEsc + @"\s+took off from\s+(?<base>[^\r\n\.]+)", RegexOptions.IgnoreCase);
                                    if (mTakeoff.Success)
                                        RecordReplayEvent("TAKEOFF", mTakeoff.Groups["base"].Value.Trim());

                                    Match mLanded = Regex.Match(chunk, @"FlightLogger:\s*" + myNickEsc + @"\s+landed at\s+(?<base>[^\r\n\.]+)", RegexOptions.IgnoreCase);
                                    if (mLanded.Success)
                                        RecordReplayEvent("LANDED", mLanded.Groups["base"].Value.Trim());

                                    foreach (Match mObj in Regex.Matches(chunk, @"FlightLogger:\s*(?<st>Completed|Failed)\s+objective:\s*(?<obj>[^\r\n]+)", RegexOptions.IgnoreCase))
                                        RecordReplayEvent("OBJECTIVE", mObj.Groups["st"].Value, mObj.Groups["obj"].Value.Trim());

                                    // 5.2 Регистрация всех юнитов (союзников, врагов, ДРЛО, танкеров, ЗРК) и их начальных векторов скорости
                                    foreach (Match mActor in Regex.Matches(chunk, @"Registering actor\s+(?<unit>[^\r\n\(\)]+?)\s*\((?<type>[^\r\n\(\)]+)\)", RegexOptions.IgnoreCase))
                                    {
                                        string uName = mActor.Groups["unit"].Value.Trim();
                                        string uType = mActor.Groups["type"].Value.Trim();
                                        if (uName.StartsWith("Player", StringComparison.OrdinalIgnoreCase) ||
                                            uName.IndexOf("Clone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            uName == "null" || uName.StartsWith("elevator", StringComparison.OrdinalIgnoreCase))
                                            continue;

                                        // Отсекаем ракеты — для них есть отдельный блок пусков
                                        if (Regex.IsMatch(uType, @"\b(?:AIM-|ASF-MRM|ASF-SRM|AIRS-T|AGM-|R-77|R-27|SA-|MIM-)\b", RegexOptions.IgnoreCase) ||
                                            uName.IndexOf("(" + myNick + ")", StringComparison.OrdinalIgnoreCase) >= 0)
                                            continue;

                                        RecordReplayEvent("ACTOR_REG", uName, uType);
                                    }

                                    foreach (Match mFly in Regex.Matches(chunk, @"(?<unit>[^\r\n\(\)]+?)\s*\([^\)]+\)\s*set rb vel from InitialFlyRoutine\s*\(\((?<vx>[-\d,]+),\s*(?<vy>[-\d,]+),\s*(?<vz>[-\d,]+)\)\)", RegexOptions.IgnoreCase))
                                    {
                                        string uName = mFly.Groups["unit"].Value.Trim();
                                        string vel = string.Format("{0};{1};{2}",
                                            mFly.Groups["vx"].Value.Replace(',', '.'),
                                            mFly.Groups["vy"].Value.Replace(',', '.'),
                                            mFly.Groups["vz"].Value.Replace(',', '.'));
                                        RecordReplayEvent("UNIT_SPAWN", uName, vel);
                                    }

                                    // 5.3 Радарная обстановка: обнаружения игроком, ДРЛО (AWACS), союзниками и облучение RWR (СПО)
                                    foreach (Match mDet in Regex.Matches(chunk, @"(?<observer>[^\r\n]+?)\s+detected\s+(?<target>[^\r\n]+?)\s+via radar\.", RegexOptions.IgnoreCase))
                                    {
                                        string obs = mDet.Groups["observer"].Value.Trim();
                                        string tgt = mDet.Groups["target"].Value.Trim();

                                        if (tgt.Equals(myNick, StringComparison.OrdinalIgnoreCase))
                                        {
                                            // Кто-то засветил игрока своим радаром (RWR!)
                                            RecordReplayEvent("RWR_DETECT", obs, "Радарное облучение");
                                        }
                                        else if (obs.Equals(myNick, StringComparison.OrdinalIgnoreCase))
                                        {
                                            // Игрок обнаружил цель на свой радар
                                            RecordReplayEvent("RADAR_DETECT", "PLAYER", tgt);
                                        }
                                        else if (obs.IndexOf("Overlord", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                 obs.IndexOf("AWACS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                 obs.IndexOf("Magic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                 obs.IndexOf("Wizard", StringComparison.OrdinalIgnoreCase) >= 0)
                                        {
                                            // ДРЛО / АВАКС передал контакт по даталинку
                                            RecordReplayEvent("RADAR_DETECT", obs, tgt);
                                        }
                                    }

                                    // 5.4 Захват цели игроком (TWS / STT) и управление режимами радара
                                    foreach (Match mSoft in Regex.Matches(chunk, @"SoftLockActor\((?<tgt>[^\r\n\)]+\))", RegexOptions.IgnoreCase))
                                        RecordReplayEvent("RADAR_LOCK", CleanUnitDisplayName(mSoft.Groups["tgt"].Value));

                                    foreach (Match mRdrPwr in Regex.Matches(chunk, @"MFDRadarUI\.SetPower\(\d+,\s*(?<st>True|False)\)", RegexOptions.IgnoreCase))
                                        RecordReplayEvent("RADAR_PWR", mRdrPwr.Groups["st"].Value);

                                    // 5.5 Все пуски ракет (игрока, союзных ботов и противников) и их захваты целей
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
                                            RecordReplayEvent("LAUNCH", mslName);
                                        }
                                    }

                                    // Все захваты ракет (любого стрелка по любой цели)
                                    MatchCollection mcAllMslLock = Regex.Matches(
                                        chunk,
                                        @"(?<msl>[A-Za-z0-9\-]+)\s*\((?<shooter>[^\)]+)\)\s+acquired\s+(?:radar\s+|optical\s+|heat\s+)?target:\s*(?<tfull>[^\r\n]+)",
                                        RegexOptions.IgnoreCase
                                    );
                                    foreach (Match mLock in mcAllMslLock)
                                    {
                                        string lockMsl = mLock.Groups["msl"].Value.Trim();
                                        string shooter = mLock.Groups["shooter"].Value.Trim();
                                        string targetFull = mLock.Groups["tfull"].Value.Trim();

                                        if (shooter.Equals(myNick, StringComparison.OrdinalIgnoreCase))
                                        {
                                            if (!string.IsNullOrEmpty(lastFiredPlayerMissile) &&
                                                lastFiredPlayerMissile.StartsWith(lockMsl, StringComparison.OrdinalIgnoreCase))
                                                lockMsl = lastFiredPlayerMissile;

                                            Match mTid = Regex.Match(targetFull, @"\[(?<tid>\d+)\]");
                                            if (mTid.Success)
                                                targetIdToMissile[mTid.Groups["tid"].Value] = lockMsl;

                                            RecordReplayEvent("MSL_ACTIVE", lockMsl, CleanUnitDisplayName(targetFull));
                                        }
                                        else if (targetFull.IndexOf("(" + myNick + ")", StringComparison.OrdinalIgnoreCase) >= 0)
                                        {
                                            // Ракета противника вышла на активное самонаведение по игроку!
                                            RecordReplayEvent("THREAT_LOCK", lockMsl, shooter);
                                        }
                                        else
                                        {
                                            // Пуск/наведение ракеты ИИ (союзника или врага) по другой цели
                                            RecordReplayEvent("AI_MSL_LOCK", lockMsl + " (" + shooter + ")", CleanUnitDisplayName(targetFull));
                                        }
                                    }
                                    // 5.6 Повреждения (отказы двигателей и отрыв пилонов/подвесок при попаданиях)
                                    foreach (Match mEng in Regex.Matches(chunk, @"Failed engine\s+(?<part>[^\r\n]+)", RegexOptions.IgnoreCase))
                                    {
                                        string part = mEng.Groups["part"].Value.Trim();
                                        if (part.IndexOf("(Clone)", StringComparison.OrdinalIgnoreCase) >= 0)
                                            RecordReplayEvent("DAMAGE", "PLAYER", "Отказ двигателя: " + part);
                                        else
                                            RecordReplayEvent("DAMAGE", "UNIT", "Повреждён двигатель: " + part);
                                    }
                                    foreach (Match mJet in Regex.Matches(chunk, @"jettisoning by part destruction:\s*(?<item>[^\r\n]+)", RegexOptions.IgnoreCase))
                                    {
                                        RecordReplayEvent("PART_LOSS", mJet.Groups["item"].Value.Trim());
                                    }
                                    // 5.7 Все сбития и потери в миссии (игрока, союзников и врагов)
                                    int newKills = 0;
                                    string lastTarget = "Цель";
                                    string acDisplay = FormatAircraftWithRole(currentAircraft, currentCrewRole);
                                    MatchCollection mcAllKills = Regex.Matches(
                                    chunk,
                                    @"FlightLogger:\s*(?<target>[^\r\n]+?)\s+was killed by\s+(?<killer>[^\r\n.]+?)(?:\s+with\s+(?<weapon>[^\r\n.]+?))?(?=\s*(?:\.|$|\r|\n))",
                                    RegexOptions.IgnoreCase
                                    );
                                    foreach (Match mk in mcAllKills)
                                    {
                                        string rawTarget = mk.Groups["target"].Value.Trim();
                                        string rawKiller = mk.Groups["killer"].Value.Trim();
                                        // Гибель самого игрока обрабатывается в блоке 6
                                        if (rawTarget.IndexOf("(" + myNick + ")", StringComparison.OrdinalIgnoreCase) >= 0)
                                            continue;
                                        string targetName = CleanUnitDisplayName(rawTarget);
                                        string killerName = CleanUnitDisplayName(rawKiller);
                                        string weapon = mk.Groups["weapon"].Success ? CleanUnitDisplayName(mk.Groups["weapon"].Value) : "";
                                        bool isPlayerKill = rawKiller.IndexOf("(" + myNick + ")", StringComparison.OrdinalIgnoreCase) >= 0;
                                        if (isPlayerKill)
                                        {
                                            Match mTid = Regex.Match(rawTarget, @"(?<tid>\d+)");
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
                                            RecordReplayEvent("KILL", targetName, weapon);
                                        }
                                        else
                                        {
                                            // Сбитие союзником, ЗРК или врагом (нужно для удаления цели с карты в нужный момент)
                                            RecordReplayEvent("WORLD_KILL", targetName, killerName);
                                        }
                                    }
                                    if (newKills > 0)
                                    {
                                        AppLogger.Log("TRACKER", string.Format("Подтверждено сбитие ({0}): всего за сессию = {1}", lastTarget, kills));
                                        if (SettingsManager.GetBool("KillSoundEnabled", true))
                                            KillSoundPlayer.RegisterKills(newKills);
                                    }
                                }
                                // 6. Катапультирование / гибель борта с точным определением причины (ракета, земля, перегрузка)
                                bool hit = false;
                                string deathEventType = "KIA";
                                string deathDetails = "Борт потерян";
                                string deathCauseCode = "UNKNOWN";
                                if (!string.IsNullOrEmpty(myNickEsc))
                                {
                                    Match mKilledBy = Regex.Match(
                                    chunk,
                                    @"FlightLogger:\s*[^\r\n]?\(" + myNickEsc + @"\)[^\r\n]?\bwas killed by\s+(?<killer>[^\r\n.]+?)(?:\s+with\s+(?<weapon>[^\r\n.]+?))?(?=\s*(?:\.|$|\r|\n))",
                                    RegexOptions.IgnoreCase
                                    );
                                    if (mKilledBy.Success)
                                    {
                                        hit = true;
                                        deathEventType = "KIA";
                                        string killer = CleanUnitDisplayName(mKilledBy.Groups["killer"].Value);
                                        string weap = mKilledBy.Groups["weapon"].Success ? CleanUnitDisplayName(mKilledBy.Groups["weapon"].Value) : "";
                                        if (killer.Equals("Environment", StringComparison.OrdinalIgnoreCase) ||
                                        chunk.IndexOf("TerrainCollider", StringComparison.OrdinalIgnoreCase) >= 0)
                                        {
                                            deathCauseCode = "CFIT";
                                            deathDetails = "Столкновение с землёй (CFIT)";
                                        }
                                        else
                                        {
                                            deathCauseCode = "ENEMY";
                                            deathDetails = string.IsNullOrEmpty(weap)
                                            ? ("Сбит: " + killer)
                                            : ("Сбит: " + killer + "  |  Оружие: " + weap);
                                        }
                                    }
                                    if (!hit && Regex.IsMatch(chunk, @"Died by instantaneous G-force\s*(?<g>[\d,]+)", RegexOptions.IgnoreCase))
                                    {
                                        Match mG = Regex.Match(chunk, @"Died by instantaneous G-force\s*(?<g>[\d,]+)", RegexOptions.IgnoreCase);
                                        hit = true;
                                        deathEventType = "KIA";
                                        deathCauseCode = "GFORCE";
                                        deathDetails = "Разрушение от перегрузки (" + mG.Groups["g"].Value + " G)";
                                    }
                                    if (!hit && Regex.IsMatch(chunk, @"(?:FlightLogger:\s*[^\r\n]?\(" + myNickEsc + @"\)|(?:$|\b)" + myNickEsc + @"$?)[^\r\n]?\bejected\b", RegexOptions.IgnoreCase))
                                    {
                                        hit = true;
                                        deathEventType = "EJECT";
                                        deathCauseCode = "EJECT";
                                        deathDetails = "Катапультирование пилота";
                                    }
                                    if (!hit && Regex.IsMatch(chunk, @"(?:FlightLogger:\s*[^\r\n]?\(" + myNickEsc + @"\)|(?:$|\b)" + myNickEsc + @"$?)[^\r\n]?\b(?:was killed|died|crashed)\b", RegexOptions.IgnoreCase))
                                    {
                                        hit = true;
                                        deathEventType = "KIA";
                                        if (chunk.IndexOf("crashed", StringComparison.OrdinalIgnoreCase) >= 0 || chunk.IndexOf("terrain", StringComparison.OrdinalIgnoreCase) >= 0)
                                        {
                                            deathCauseCode = "CFIT";
                                            deathDetails = "Столкновение с землёй / крушение";
                                        }
                                        else
                                        {
                                            deathDetails = "Борт уничтожен (авария / столкновение)";
                                        }
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
                                    RecordReplayEvent(deathEventType, deathDetails, deathCauseCode);
                                    activeReplayFile = null;
                                    currentAircraft = "В ангаре / Меню";
                                    currentCallsign = "В ангаре / Меню";
                                    inSortie = false;
                                    AppLogger.Log("TRACKER", "Пилот " + myNick + " погиб/катапультировался (" + deathEventType + ": " + deathDetails + ").");
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
                else
                {
                    AudioSessionDucking.SetDuck(false, "VTOLVR");
                }

                // Discord IPC (строго с соблюдением Rate Limit 15 сек и без конфликта нескольких окон)
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
                                    AppLogger.Log("DISCORD", string.Format("Пайп discord-ipc-{0}: ok={1}, op={2}", i, ok, op));
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
                                        lastSentActivityKey = "";
                                        lastActivitySentUtc = DateTime.MinValue;
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
                                    if (err == 5 && !elevationAttempted)
                                    {
                                        elevationAttempted = true;
                                        bool isAdmin = new System.Security.Principal.WindowsPrincipal(
                                            System.Security.Principal.WindowsIdentity.GetCurrent()
                                        ).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

                                        if (!isAdmin)
                                        {
                                            try
                                            {
                                                ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath);
                                                psi.Verb = "runas";
                                                psi.UseShellExecute = true;
                                                Process.Start(psi);
                                                Environment.Exit(0);
                                            }
                                            catch { }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    IsGameActive = true;
                    string acFullDisplay = FormatAircraftWithRole(currentAircraft, currentCrewRole);
                    ActiveFlightSummary = inSortie ? (acFullDisplay + " (" + currentCallsign + ")") : "В ангаре / Меню";
                    if (!string.IsNullOrEmpty(myNick)) ActivePilotName = myNick;

                    if (pipe != null)
                    {
                        string det = inSortie
                            ? ("Борт: " + acFullDisplay + (kills > 0 ? " | Сбито: " + kills : ""))
                            : "В ангаре / Меню";

                        string st = inSortie
                            ? ("Позывной: " + currentCallsign + (!string.IsNullOrEmpty(currentMission) ? " | " + currentMission : ""))
                            : (!string.IsNullOrEmpty(currentMission) ? "Брифинг: " + currentMission : "Ожидание вылета");

                        string activityKey = det + "|" + st + "|" + currentAircraft;
                        double secSinceLastSend = (DateTime.UtcNow - lastActivitySentUtc).TotalSeconds;

                        // Шлём SET_ACTIVITY только при изменении статуса или раз в 15 сек (защита от Rate Limit Discord!)
                        if (activityKey != lastSentActivityKey || secSinceLastSend >= 15.0)
                        {
                            int targetPid = Process.GetCurrentProcess().Id;
                            try
                            {
                                Process[] vProcs = Process.GetProcessesByName("VTOLVR");
                                if (vProcs.Length > 0)
                                {
                                    targetPid = vProcs[0].Id;
                                    for (int vp = 0; vp < vProcs.Length; vp++) vProcs[vp].Dispose();
                                }
                            }
                            catch { }

                            string acAssetKey = GetAircraftAssetKey(currentAircraft);
                            string assetsJson = string.IsNullOrEmpty(acAssetKey)
                                ? "\"large_image\":\"vtol_logo\",\"large_text\":\"VTOL QoL by L.Shutze\""
                                : "\"large_image\":\"vtol_logo\",\"large_text\":\"VTOL QoL by L.Shutze\",\"small_image\":\"" + acAssetKey + "\",\"small_text\":\"" + JsonEscape(currentAircraft) + "\"";

                            string json = "{\"cmd\":\"SET_ACTIVITY\",\"args\":{\"pid\":" + targetPid +
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
                                        IsDiscordConnected = false;
                                    }
                                    else
                                    {
                                        lastSentActivityKey = activityKey;
                                        lastActivitySentUtc = DateTime.UtcNow;
                                    }
                                }
                                else
                                {
                                    AppLogger.Log("DISCORD", "ReadPacket вернул false после SET_ACTIVITY");
                                    try { pipe.Dispose(); } catch { }
                                    pipe = null;
                                    IsDiscordConnected = false;
                                }
                            }
                            catch (Exception exSend)
                            {
                                AppLogger.Error("DISCORD", "SET_ACTIVITY", exSend);
                                try { pipe.Dispose(); } catch { }
                                pipe = null;
                                IsDiscordConnected = false;
                            }
                        }
                    }
                }
                else if (pipe != null)
                {
                    try { pipe.Dispose(); } catch { }
                    pipe = null;
                    IsDiscordConnected = false;
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

