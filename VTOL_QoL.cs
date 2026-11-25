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
// === ТРЕКЕР ИГРЫ: DISCORD RPC + АУДИО VR + DUCKING + ЛОГБУК (ЕДИНСТВЕННЫЙ) =
// =========================================================================
public class GameTracker
{
    // ВНИМАНИЕ: впишите сюда Application ID, созданный на https://discord.com/developers/applications
    // (кнопка "New Application" -> General Information -> Application ID).
    public const string DiscordClientId = "1573385169807872222";

    public volatile bool DiscordEnabled = true;
    public volatile bool DuckingEnabled = true;
    public volatile bool AudioSwitchEnabled = true;
    public volatile bool DiagnosticsEnabled = false;

    private bool running = false;
    private LogPatterns patterns;

    private string originalPlaybackId = null;
    private string originalCaptureId = null;
    private bool switchedAudio = false;

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

    private void SendPacket(NamedPipeClientStream pipe, int op, string json)
    {
        byte[] jb = Encoding.UTF8.GetBytes(json);
        byte[] header = new byte[8];
        BitConverter.GetBytes(op).CopyTo(header, 0);
        BitConverter.GetBytes(jb.Length).CopyTo(header, 4);
        pipe.Write(header, 0, 8);
        pipe.Write(jb, 0, jb.Length);
        pipe.Flush();
    }

    private byte[] ReadExact(NamedPipeClientStream pipe, int n)
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

    private bool ReadPacket(NamedPipeClientStream pipe, out int op, out string body)
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
        int kills = 0;
        bool inSortie = false;
        DateTime sortieStart = DateTime.Now;
        long startEpoch = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
        long lastSeekPos = 0;
        bool wasRunning = false;

        NamedPipeClientStream pipe = null;
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
                        if (DiscordEnabled && pipe != null && pipe.IsConnected)
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
                    lastSeekPos = File.Exists(logPath) ? new FileInfo(logPath).Length : 0;
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

                                if (DiscordEnabled || true) // логика статуса/логбука нужна и без дискорда для логбука
                                {
                                    string csPat = patterns.CallsignPatterns.Count > 0 ? patterns.CallsignPatterns[0] : @"((?:Alpha|Bravo|Charlie|Delta|Echo|Foxtrot|Golf|Hotel|India|Juliet|Kilo|Lima|Mike|November|Oscar|Papa|Quebec|Romeo|Sierra|Tango|Uniform|Victor|Whiskey|X-?ray|Yankee|Zulu)\s+\d+-\d+)";

                                    Match mCs = !string.IsNullOrEmpty(myNickEsc)
                                        ? Regex.Match(chunk, myNickEsc + @"[^\r\n]*?" + csPat)
                                        : Regex.Match(chunk, csPat);
                                    if (mCs.Success) currentCallsign = mCs.Groups[1].Value;

                                    if (currentCallsign != "В ангаре / Меню")
                                    {
                                        Match mPl = Regex.Match(chunk, aircraftRegexStr, RegexOptions.IgnoreCase);
                                        if (mPl.Success)
                                        {
                                            string normalized = patterns.NormalizeAircraft(mPl.Groups[1].Value);
                                            if (!inSortie || currentAircraft != normalized)
                                            {
                                                currentAircraft = normalized;
                                                inSortie = true;
                                                sortieStart = DateTime.Now;
                                                WriteFlightLog("SORTIE", currentAircraft, currentCallsign, "00:00", 0, "Занятие слота и вылет");
                                            }
                                        }
                                    }

                                    if (!string.IsNullOrEmpty(myNickEsc))
                                    {
                                        foreach (string killTpl in patterns.KillTemplates)
                                        {
                                            string pat = killTpl.Replace("{NICK}", myNickEsc);
                                            if (Regex.IsMatch(chunk, "(?i)" + pat))
                                            {
                                                kills++;
                                                WriteFlightLog("KILL", currentAircraft, currentCallsign, GetDurStr(sortieStart), kills, "Цель уничтожена");
                                                break;
                                            }
                                        }
                                    }

                                    bool hit = false;
                                    foreach (string ejTpl in patterns.EjectTemplates)
                                    {
                                        string pat = !string.IsNullOrEmpty(myNickEsc) ? ejTpl.Replace("{NICK}", myNickEsc) : ejTpl.Replace("{NICK}", ".+?");
                                        if (Regex.IsMatch(chunk, pat)) { hit = true; break; }
                                    }
                                    if (hit)
                                    {
                                        string evType = chunk.IndexOf("ejected", StringComparison.OrdinalIgnoreCase) >= 0 ? "EJECT" : "KIA";
                                        WriteFlightLog(evType, currentAircraft, currentCallsign, GetDurStr(sortieStart), kills, evType == "EJECT" ? "Катапультирование" : "Борт сбит / потерян");
                                        currentAircraft = "В ангаре / Меню";
                                        currentCallsign = "В ангаре / Меню";
                                        inSortie = false;
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
                    if (pipe == null || !pipe.IsConnected)
                    {
                        for (int i = 0; i < 10; i++)
                        {
                            try
                            {
                                NamedPipeClientStream p = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut);
                                p.Connect(150);
                                if (p.IsConnected)
                                {
                                    pipe = p;
                                    string hs = "{\"v\":1,\"client_id\":\"" + DiscordClientId + "\"}";
                                    SendPacket(pipe, 0, hs);
                                    int op; string body;
                                    ReadPacket(pipe, out op, out body);
                                    break;
                                }
                            }
                            catch { }
                        }
                    }

                    if (pipe != null && pipe.IsConnected)
                    {
                        string det = "Борт: " + currentAircraft + (kills > 0 ? " | Сбито: " + kills : "");
                        string st = "Позывной: " + currentCallsign;
                        string json = "{\"cmd\":\"SET_ACTIVITY\",\"args\":{\"pid\":" + Process.GetCurrentProcess().Id +
                                      ",\"activity\":{\"details\":\"" + JsonEscape(det) +
                                      "\",\"state\":\"" + JsonEscape(st) +
                                      "\",\"timestamps\":{\"start\":" + startEpoch +
                                      "},\"assets\":{\"large_text\":\"VTOL QoL by L.Shutze\"}}},\"nonce\":\"" + Guid.NewGuid() + "\"}";
                        try
                        {
                            SendPacket(pipe, 1, json);
                            int op; string body;
                            if (ReadPacket(pipe, out op, out body))
                            {
                                if (op == 3) // ERROR
                                {
                                    try { pipe.Dispose(); } catch { }
                                    pipe = null;
                                }
                            }
                            else
                            {
                                try { pipe.Dispose(); } catch { }
                                pipe = null;
                            }
                        }
                        catch
                        {
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

    private string GetDurStr(DateTime start)
    {
        TimeSpan span = DateTime.Now - start;
        return string.Format("{0:D2}:{1:D2}", (int)span.TotalMinutes, span.Seconds);
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

    public static bool ShuffleNow(string musicDir, bool smart)
    {
        if (!Directory.Exists(musicDir)) return false;
        if (IsGameRunning()) return false; // никогда не трогаем файлы во время игры

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
            if (MusicShuffler.ShuffleNow(musicDir, true)) { LoadTracks(); MessageBox.Show("Плейлист перемешан!", "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information); }
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

        Button btnSave = new Button() { Text = "Сохранить и закрыть", Left = 585, Top = 478, Width = 140, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 140, 60), ForeColor = Color.White, DialogResult = DialogResult.OK };
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
// === ЗАГРУЗЧИК FBX (БИНАРНЫЙ + УПРОЩЕННЫЙ ASCII) =========================
// =========================================================================
public class FbxMeshData
{
    public List<System.Windows.Media.Media3D.Point3D> Vertices = new List<System.Windows.Media.Media3D.Point3D>();
    public List<int> Indices = new List<int>();
    public List<System.Windows.Point> UVs = new List<System.Windows.Point>();
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


    public static FbxMeshData Load(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        if (data.Length > 23 && Encoding.ASCII.GetString(data, 0, 18) == "Kaydara FBX Binary")
        {
            try { return LoadBinary(data); } catch { return null; }
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
        Dictionary<long, long> geomToModel = new Dictionary<long, long>();

        foreach (FbxNode child in objects.Children)
        {
            if (child.Name == "Geometry" && child.Properties.Count > 0)
            {
                long id = Convert.ToInt64(child.Properties[0]);
                FbxGeomPart part = ParseGeometryNode(child, id);
                if (part != null && part.RawVerts.Count > 0) geoms[id] = part;
            }
            else if (child.Name == "Model" && child.Properties.Count > 0)
            {
                long id = Convert.ToInt64(child.Properties[0]);
                models[id] = ParseModelNode(child, id);
            }
        }

        FbxNode conns = FindChild(topNodes, "Connections");
        if (conns != null)
        {
            foreach (FbxNode c in conns.Children)
            {
                if (c.Name == "C" && c.Properties.Count >= 3 && (c.Properties[0] as string) == "OO")
                {
                    long childId = Convert.ToInt64(c.Properties[1]);
                    long parentId = Convert.ToInt64(c.Properties[2]);
                    if (geoms.ContainsKey(childId) && models.ContainsKey(parentId))
                        geomToModel[childId] = parentId;
                    else if (models.ContainsKey(childId) && models.ContainsKey(parentId))
                        models[childId].ParentId = parentId;
                }
            }
        }

        return AssembleFullMesh(geoms, models, geomToModel);
    }
    private static FbxMeshData AssembleFullMesh(
    Dictionary<long, FbxGeomPart> geoms,
    Dictionary<long, FbxModelTransform> models,
    Dictionary<long, long> geomToModel)
    {
        bool hasDetailedParts = false;
        foreach (var kv in geoms) if (kv.Value.RawVerts.Count > 30) { hasDetailedParts = true; break; }

        FbxMeshData result = new FbxMeshData();

        foreach (var kv in geoms)
        {
            FbxGeomPart part = kv.Value;
            if (hasDetailedParts && !(part.RawVerts.Count > 24)) continue;

            System.Windows.Media.Media3D.Matrix3D worldMat = System.Windows.Media.Media3D.Matrix3D.Identity;
            long modelId;
            if (geomToModel.TryGetValue(part.Id, out modelId))
            {
                FbxModelTransform m;
                if (models.TryGetValue(modelId, out m) && m.Name != null &&
     (m.Name.IndexOf("Collider", StringComparison.OrdinalIgnoreCase) >= 0 ||
      m.Name.IndexOf("Hitbox", StringComparison.OrdinalIgnoreCase) >= 0 ||
      m.Name.IndexOf("LOD1", StringComparison.OrdinalIgnoreCase) >= 0 ||
      m.Name.IndexOf("LOD2", StringComparison.OrdinalIgnoreCase) >= 0 ||
      m.Name.IndexOf("LOD3", StringComparison.OrdinalIgnoreCase) >= 0 ||
      m.Name.IndexOf("Shadow", StringComparison.OrdinalIgnoreCase) >= 0 ||
      m.Name.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0 ||
      m.Name.IndexOf("Cockpit", StringComparison.OrdinalIgnoreCase) >= 0 ||
      m.Name.IndexOf("Interior", StringComparison.OrdinalIgnoreCase) >= 0 ||
      m.Name.IndexOf("Seat", StringComparison.OrdinalIgnoreCase) >= 0 ||
      m.Name.IndexOf("Damage", StringComparison.OrdinalIgnoreCase) >= 0 ||
      m.Name.IndexOf("Dmg", StringComparison.OrdinalIgnoreCase) >= 0 ||
      m.Name.IndexOf("Break", StringComparison.OrdinalIgnoreCase) >= 0 ||
      m.Name.IndexOf("Broken", StringComparison.OrdinalIgnoreCase) >= 0 ||
      m.Name.IndexOf("Debris", StringComparison.OrdinalIgnoreCase) >= 0 ||
      m.Name.IndexOf("Stub", StringComparison.OrdinalIgnoreCase) >= 0 ||
      m.Name.IndexOf("Stump", StringComparison.OrdinalIgnoreCase) >= 0 ||
      m.Name.IndexOf("Cap", StringComparison.OrdinalIgnoreCase) >= 0 ||
      m.Name.IndexOf("Decal", StringComparison.OrdinalIgnoreCase) >= 0 ||
      m.Name.IndexOf("Emblem", StringComparison.OrdinalIgnoreCase) >= 0))
                    continue;


                worldMat = BuildWorldMatrix(modelId, models);
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
                    int[] triV = new int[] { polyVerts[0], polyVerts[k], polyVerts[k + 1] };
                    int[] triC = new int[] { polyCorners[0], polyCorners[k], polyCorners[k + 1] };

                    for (int t = 0; t < 3; t++)
                    {
                        int vi = triV[t];
                        if (vi < 0 || vi >= part.RawVerts.Count) continue;

                        System.Windows.Media.Media3D.Point3D pt = worldMat.Transform(part.RawVerts[vi]);
                        result.Indices.Add(result.Vertices.Count);
                        result.Vertices.Add(pt);

                        System.Windows.Point uv = ResolveUV(part, vi, triC[t]);
                            result.UVs.Add(new System.Windows.Point(uv.X, FlipUVY ? (1.0 - uv.Y) : uv.Y));
                        }
                    }
                polyVerts.Clear();
                polyCorners.Clear();
            }
        }
    }

        return result.Vertices.Count > 0 ? result : null;
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

        public Livery3DViewerForm(LiveryEntry liveryEntry)
        {
            this.entry = liveryEntry;
            this.Text = "VTOL QoL — 3D просмотр ливреи: " + liveryEntry.Name;
            this.Size = new Size(860, 680);
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = Color.FromArgb(18, 22, 20);

            host = new System.Windows.Forms.Integration.ElementHost();
            host.Dock = DockStyle.Fill;
            this.Controls.Add(host);

            System.Windows.Controls.Grid grid = new System.Windows.Controls.Grid();
            grid.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(28, 32, 34));
            viewport = new System.Windows.Controls.Viewport3D();
            viewport.IsHitTestVisible = false;
            grid.Children.Add(viewport);
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

        Label lblHint = new Label() { Text = "ЛКМ — вращение", Left = 650, Top = 13, AutoSize = true, ForeColor = Color.Silver };
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
            System.Windows.Media.Media3D.Model3DGroup lightGroup = new System.Windows.Media.Media3D.Model3DGroup();
            lightGroup.Children.Add(new System.Windows.Media.Media3D.AmbientLight(System.Windows.Media.Color.FromRgb(145, 145, 145)));
            lightGroup.Children.Add(new System.Windows.Media.Media3D.DirectionalLight(System.Windows.Media.Colors.White, new System.Windows.Media.Media3D.Vector3D(-1, -2, -2)));
            lightGroup.Children.Add(new System.Windows.Media.Media3D.DirectionalLight(System.Windows.Media.Color.FromRgb(190, 190, 190), new System.Windows.Media.Media3D.Vector3D(2, 1, 2)));
            lightVisual.Content = lightGroup;
            viewport.Children.Add(lightVisual);

            UpdateCamera();
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

        System.Windows.Media.Media3D.MeshGeometry3D geom = new System.Windows.Media.Media3D.MeshGeometry3D();
        foreach (System.Windows.Media.Media3D.Point3D p in meshData.Vertices) geom.Positions.Add(p);
        foreach (int idx in meshData.Indices) geom.TriangleIndices.Add(idx);
        if (meshData.UVs.Count == meshData.Vertices.Count)
            foreach (System.Windows.Point uv in meshData.UVs) geom.TextureCoordinates.Add(uv);

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

        System.Windows.Media.Media3D.DiffuseMaterial mat = new System.Windows.Media.Media3D.DiffuseMaterial(matBrush);
        model = new System.Windows.Media.Media3D.GeometryModel3D(geom, mat);

            modelVisual = new System.Windows.Media.Media3D.ModelVisual3D();
            modelVisual.Content = model;
            viewport.Children.Add(modelVisual);

        }

        private void NormalizeToUnitCube(FbxMeshData mesh)
    {
        if (mesh.Vertices.Count == 0) return;
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        foreach (System.Windows.Media.Media3D.Point3D p in mesh.Vertices)
        {
            if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
            if (p.Z < minZ) minZ = p.Z; if (p.Z > maxZ) maxZ = p.Z;
        }
        double cx = (minX + maxX) / 2.0, cy = (minY + maxY) / 2.0, cz = (minZ + maxZ) / 2.0;
        double sizeX = maxX - minX, sizeY = maxY - minY, sizeZ = maxZ - minZ;
        double maxSize = Math.Max(sizeX, Math.Max(sizeY, sizeZ));
        if (maxSize < 0.0001) maxSize = 1;
        double scale = 2.0 / maxSize;

        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            System.Windows.Media.Media3D.Point3D p = mesh.Vertices[i];
            mesh.Vertices[i] = new System.Windows.Media.Media3D.Point3D((p.X - cx) * scale, (p.Y - cy) * scale, (p.Z - cz) * scale);
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
        private ComboBox cmbAircraft;
        private ListBox lstLiveries;
        private PictureBox picPreview;
        private Label lblPreviewInfo;
        private Label lblStatus;
        private List<LiveryEntry> currentEntries = new List<LiveryEntry>();
    private List<LiveryEntry> filteredEntries = new List<LiveryEntry>();
    private readonly string[] aircrafts = new string[] { "Все самолеты", "Не определен", "FA-26B", "F-45A", "AV-42C", "AH-94", "T-55", "EF-24G" };

    public LiveryManagerForm(string root)
        {
            this.gameRoot = root;
            this.Text = "VTOL QoL — Менеджер ливрей и 3D-просмотр";
            this.Size = new Size(920, 560);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.BackColor = Color.FromArgb(18, 22, 20);
            this.ForeColor = Color.FromArgb(0, 255, 102);
            this.Font = new Font("Consolas", 9.5f, FontStyle.Regular);

            Label lblAc = new Label() { Text = "Фильтр по летательному аппарату:", Left = 20, Top = 15, AutoSize = true, ForeColor = Color.LightGray };
            this.Controls.Add(lblAc);

            cmbAircraft = new ComboBox();
            cmbAircraft.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbAircraft.Location = new Point(20, 38);
            cmbAircraft.Size = new Size(340, 26);
            cmbAircraft.BackColor = Color.FromArgb(30, 36, 33);
            cmbAircraft.ForeColor = Color.Cyan;
            foreach (string ac in aircrafts) cmbAircraft.Items.Add(ac);
            cmbAircraft.SelectedIndex = 0;
            cmbAircraft.SelectedIndexChanged += (s, e) => LoadLiveries();
            this.Controls.Add(cmbAircraft);

            Button btnRefresh = new Button() { Text = "Обновить", Left = 370, Top = 37, Width = 115, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(40, 50, 45), ForeColor = Color.White };
            btnRefresh.Click += (s, e) => LoadLiveries();
            this.Controls.Add(btnRefresh);

            CheckBox chkWorkshop = new CheckBox() { Text = "Показывать Workshop (только просмотр)", Left = 20, Top = 70, AutoSize = true, ForeColor = Color.FromArgb(210, 240, 220), Checked = true };
            chkWorkshop.Tag = "workshop";
            chkWorkshop.CheckedChanged += (s, e) => LoadLiveries();
            this.Controls.Add(chkWorkshop);

        lstLiveries = new ListBox();
        lstLiveries.Location = new Point(20, 100);
        lstLiveries.Size = new Size(465, 360);
        lstLiveries.BackColor = Color.FromArgb(24, 28, 26);
        lstLiveries.ForeColor = Color.White;
        lstLiveries.BorderStyle = BorderStyle.FixedSingle;
        lstLiveries.SelectedIndexChanged += (s, e) => UpdatePreview();
        this.Controls.Add(lstLiveries);

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
        picPreview.Size = new Size(390, 220);
        picPreview.BackColor = Color.FromArgb(24, 28, 26);
        picPreview.BorderStyle = BorderStyle.FixedSingle;
        picPreview.SizeMode = PictureBoxSizeMode.Zoom;
        this.Controls.Add(picPreview);

        lblPreviewInfo = new Label() { Left = 500, Top = 264, Width = 390, Height = 20, ForeColor = Color.Cyan, Text = "Выберите ливрею (ПКМ — назначить борт)" };
        this.Controls.Add(lblPreviewInfo);

        int top = 292, btnW = 190, btnH = 36, gap = 10;
        Button btn3D = CreateBtn("🎮 3D-просмотр", 500, ref top, btnW, btnH, gap, Color.FromArgb(70, 50, 110));
        btn3D.Click += Btn3D_Click;

        Button btnInstallFolder = CreateBtn("+ Добавить скин", 500, ref top, btnW, btnH, gap, Color.FromArgb(0, 110, 140));
        btnInstallFolder.Click += BtnInstallFolder_Click;

        Button btnInstallZip = CreateBtn("+ Установить .zip", 500, ref top, btnW, btnH, gap, Color.FromArgb(0, 130, 90));
        btnInstallZip.Click += BtnInstallZip_Click;

        int topRight = 292;


        Button btnDelete = CreateBtn("Удалить ливрею", 700, ref topRight, btnW, btnH, gap, Color.FromArgb(140, 35, 35));
            btnDelete.Click += BtnDelete_Click;

            Button btnOpenDir = CreateBtn("Папка CustomLiveries", 700, ref topRight, btnW, btnH, gap, Color.FromArgb(45, 55, 50));
            btnOpenDir.Click += (s, e) => {
                string dir = GetLiveriesRootDir();
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                Process.Start("explorer.exe", dir);
            };

            Button btnOpenItemDir = CreateBtn("Открыть папку мода", 700, ref topRight, btnW, btnH, gap, Color.FromArgb(45, 55, 50));
            btnOpenItemDir.Click += (s, e) => {
                LiveryEntry entry = GetSelectedEntry();
                if (entry != null && Directory.Exists(entry.FolderPath)) Process.Start("explorer.exe", entry.FolderPath);
            };

            lblStatus = new Label() { Left = 20, Top = 472, Width = 870, Text = "", ForeColor = Color.Silver };
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

        private void UpdatePreview()
        {
            if (picPreview.Image != null) { picPreview.Image.Dispose(); picPreview.Image = null; }
            LiveryEntry entry = GetSelectedEntry();
            if (entry == null) { lblPreviewInfo.Text = "Выберите ливрею из списка"; return; }

            lblPreviewInfo.Text = entry.Name + " (" + entry.Aircraft + ")";
            if (!string.IsNullOrEmpty(entry.PreviewPath) && File.Exists(entry.PreviewPath))
            {
                try
                {
                    byte[] bytes = File.ReadAllBytes(entry.PreviewPath);
                    using (MemoryStream ms = new MemoryStream(bytes))
                    {
                        picPreview.Image = Image.FromStream(ms);
                    }
                }
                catch { }
            }
        }

        private string GetLiveriesRootDir()
        {
            if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL", "CustomLiveries");
            return Path.Combine(gameRoot, "CustomLiveries");
        }

        private bool ShowWorkshop()
        {
            foreach (Control c in this.Controls)
                if (c is CheckBox && ((CheckBox)c).Tag as string == "workshop") return ((CheckBox)c).Checked;
            return true;
        }

        private void LoadLiveries()
        {
            lstLiveries.Items.Clear();
            currentEntries.Clear();
            string dir = GetLiveriesRootDir();
            string filterAc = cmbAircraft.SelectedItem as string != null ? cmbAircraft.SelectedItem.ToString() : "Все самолеты";

            List<LiveryEntry> local = LiveryParser.ScanLocal(dir);
            currentEntries.AddRange(local);

            if (ShowWorkshop() && !string.IsNullOrEmpty(gameRoot))
            {
                string steamLibRoot = Path.GetFullPath(Path.Combine(gameRoot, @"..\..\.."));
                List<LiveryEntry> ws = LiveryParser.ScanWorkshop(steamLibRoot);
                currentEntries.AddRange(ws);
            }

        filteredEntries.Clear();
        foreach (LiveryEntry e in currentEntries)
        {
            if (filterAc == "Все самолеты" || e.Aircraft.Equals(filterAc, StringComparison.OrdinalIgnoreCase))
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
            using (OpenFileDialog ofd = new OpenFileDialog() { Filter = "ZIP-архив (*.zip)|*.zip" })
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
            if (entry.Source == "Workshop") { MessageBox.Show("Ливреи из Workshop удаляются только через Steam.", "Внимание", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

            if (MessageBox.Show("Удалить камуфляж \"" + entry.Name + "\"?", "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                try
                {
                    if (Directory.Exists(entry.FolderPath)) Directory.Delete(entry.FolderPath, true);
                    LoadLiveries();
                }
                catch (Exception ex) { MessageBox.Show("Ошибка: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error); }
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
        this.Size = new Size(840, 560);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.BackColor = Color.FromArgb(18, 22, 20);
        this.ForeColor = Color.FromArgb(0, 255, 102);
        this.Font = new Font("Consolas", 9.5f);

        Label lblTitle = new Label() { Text = "/// ТАКТИЧЕСКИЙ ЖУРНАЛ БОЕВЫХ ВЫЛЕТОВ ///", Left = 20, Top = 14, AutoSize = true, Font = new Font("Consolas", 11f, FontStyle.Bold) };
        this.Controls.Add(lblTitle);

        Panel pnlStats = new Panel() { Left = 20, Top = 42, Width = 785, Height = 52, BackColor = Color.FromArgb(25, 32, 28), BorderStyle = BorderStyle.FixedSingle };
        this.Controls.Add(pnlStats);

        lblSummary = new Label() { Left = 12, Top = 6, Width = 760, Height = 20, ForeColor = Color.Cyan, Font = new Font("Consolas", 9.5f, FontStyle.Bold) };
        pnlStats.Controls.Add(lblSummary);

        lblSubStats = new Label() { Left = 12, Top = 27, Width = 760, Height = 20, ForeColor = Color.FromArgb(170, 230, 190) };
        pnlStats.Controls.Add(lblSubStats);

        lvLog = new ListView();
        lvLog.Location = new Point(20, 104);
        lvLog.Size = new Size(785, 365);
        lvLog.View = View.Details;
        lvLog.FullRowSelect = true;
        lvLog.BackColor = Color.FromArgb(22, 26, 24);
        lvLog.ForeColor = Color.White;
        lvLog.BorderStyle = BorderStyle.FixedSingle;
        lvLog.Columns.Add("Дата и время", 145);
        lvLog.Columns.Add("Статус", 85);
        lvLog.Columns.Add("Борт", 115);
        lvLog.Columns.Add("Позывной", 115);
        lvLog.Columns.Add("Налет", 65);
        lvLog.Columns.Add("Сбито", 60);
        lvLog.Columns.Add("Детали события", 195);
        this.Controls.Add(lvLog);

        Button btnRefresh = new Button() { Text = "Обновить", Left = 20, Top = 480, Width = 120, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(35, 45, 40), ForeColor = Color.FromArgb(0, 255, 102) };
        btnRefresh.Click += (s, e) => LoadLog();
        this.Controls.Add(btnRefresh);

        Button btnExport = new Button() { Text = "Экспорт отчета", Left = 150, Top = 480, Width = 145, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 110, 140), ForeColor = Color.White };
        btnExport.Click += BtnExport_Click;
        this.Controls.Add(btnExport);

        Button btnClear = new Button() { Text = "Очистить журнал", Left = 305, Top = 480, Width = 145, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(120, 30, 30), ForeColor = Color.White };
        btnClear.Click += BtnClear_Click;
        this.Controls.Add(btnClear);

        Button btnClose = new Button() { Text = "Закрыть", Left = 675, Top = 480, Width = 130, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(40, 50, 45), ForeColor = Color.White };
        btnClose.Click += (s, e) => this.Close();
        this.Controls.Add(btnClose);

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
        lblSubStats.Text = string.Format("Основной борт: {0} ({1} вылетов)  |  Записей: {2}", favPlane, maxCount, lvLog.Items.Count);
    }

    private void BtnClear_Click(object sender, EventArgs e)
    {
        if (MessageBox.Show("Очистить историю вылетов?", "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            try { string path = GetLogFilePath(); if (File.Exists(path)) File.Delete(path); LoadLog(); }
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
// === ГЛАВНАЯ ФОРМА ========================================================
// =========================================================================
public class MainForm : Form
{
    private TextBox txtPath;
    private TextBox txtUrl;
    private Button btnDownloadUrl;
    private Button btnStopDownload;
    private Button btnManageMusic;
    private Panel pnlDropZone;
    private Label lblDropText;
    private CheckBox chkShuffle;
    private CheckBox chkLoudNorm;
    private CheckBox chkDiscord;
    private CheckBox chkDucking;
    private CheckBox chkAudioSwitch;
    private CheckBox chkDiagnostics;
    private Button btnInstall;
    private Button btnOpenFolder;
    private Label lblStatus;
    private ProgressBar progressBar;
    private Button btnLiveryManager;
    private Button btnLogbook;
    private Button btnSaveVault;
    private Button btnAudioSettings;
    private Button btnDiagnostics;
    private NotifyIcon trayIcon;
    private ContextMenuStrip trayMenu;
    private bool isExplicitExit = false;
    private volatile Process currentWorkerProcess = null;
    private volatile bool isCancelRequested = false;
    private GameTracker tracker = new GameTracker();

    public MainForm()
    {
        this.Text = "VTOL QoL — Smart Radio & Media Hub | by L.Shutze";
        this.Size = new Size(580, 710);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.FixedSingle;
        this.MaximizeBox = false;
        try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        this.BackColor = Color.FromArgb(18, 22, 20);
        this.ForeColor = Color.FromArgb(0, 255, 102);
        this.Font = new Font("Consolas", 9.5f, FontStyle.Regular);

        Label lblTitle = new Label();
        lblTitle.Text = "/// VTOL QoL — FLIGHT AUDIO && DISCORD SUITE ///";
        lblTitle.Font = new Font("Consolas", 12f, FontStyle.Bold);
        lblTitle.Location = new Point(20, 15);
        lblTitle.AutoSize = true;
        this.Controls.Add(lblTitle);

        Label lblAuthor = new Label();
        lblAuthor.Text = "Created by L.Shutze";
        lblAuthor.Font = new Font("Consolas", 8.5f, FontStyle.Italic);
        lblAuthor.ForeColor = Color.FromArgb(130, 180, 150);
        lblAuthor.Location = new Point(405, 38);
        lblAuthor.AutoSize = true;
        this.Controls.Add(lblAuthor);

        Label lblDir = new Label();
        lblDir.Text = "Директория RadioMusic:";
        lblDir.Location = new Point(22, 48);
        lblDir.AutoSize = true;
        lblDir.ForeColor = Color.LightGray;
        this.Controls.Add(lblDir);

        txtPath = new TextBox();
        txtPath.Location = new Point(24, 68);
        txtPath.Size = new Size(415, 24);
        txtPath.BackColor = Color.FromArgb(30, 36, 33);
        txtPath.ForeColor = Color.White;
        txtPath.BorderStyle = BorderStyle.FixedSingle;
        txtPath.Text = SettingsManager.Get("RadioMusicDir", FindVtolMusicDir());
        this.Controls.Add(txtPath);

        Button btnBrowse = new Button();
        btnBrowse.Text = "Обзор...";
        btnBrowse.Location = new Point(445, 67);
        btnBrowse.Size = new Size(80, 26);
        btnBrowse.FlatStyle = FlatStyle.Flat;
        btnBrowse.BackColor = Color.FromArgb(35, 45, 40);
        btnBrowse.Click += new EventHandler(BtnBrowse_Click);
        this.Controls.Add(btnBrowse);

        pnlDropZone = new Panel();
        pnlDropZone.Location = new Point(24, 105);
        pnlDropZone.Size = new Size(501, 90);
        pnlDropZone.BackColor = Color.FromArgb(26, 34, 30);
        pnlDropZone.BorderStyle = BorderStyle.FixedSingle;
        pnlDropZone.AllowDrop = true;
        pnlDropZone.DragEnter += new DragEventHandler(PnlDropZone_DragEnter);
        pnlDropZone.DragLeave += new EventHandler(PnlDropZone_DragLeave);
        pnlDropZone.DragDrop += new DragEventHandler(PnlDropZone_DragDrop);
        this.Controls.Add(pnlDropZone);

        lblDropText = new Label();
        lblDropText.Text = "[ ПЕРЕТАЩИТЕ СЮДА МУЗЫКУ, ВИДЕО ИЛИ ПАПКИ ]\nMP3, FLAC, WAV, OGG, M4A, OPUS, MP4, MKV\nАвтоматическая конвертация и нормализация громкости";
        lblDropText.TextAlign = ContentAlignment.MiddleCenter;
        lblDropText.Dock = DockStyle.Fill;
        lblDropText.ForeColor = Color.FromArgb(160, 230, 185);
        lblDropText.AllowDrop = true;
        lblDropText.DragEnter += new DragEventHandler(PnlDropZone_DragEnter);
        lblDropText.DragDrop += new DragEventHandler(PnlDropZone_DragDrop);
        pnlDropZone.Controls.Add(lblDropText);

        Label lblUrl = new Label();
        lblUrl.Text = "Загрузить по ссылке (YouTube / SoundCloud / Радио):";
        lblUrl.Location = new Point(22, 205);
        lblUrl.AutoSize = true;
        lblUrl.ForeColor = Color.LightGray;
        this.Controls.Add(lblUrl);

        txtUrl = new TextBox();
        txtUrl.Location = new Point(24, 225);
        txtUrl.Size = new Size(310, 24);
        txtUrl.BackColor = Color.FromArgb(30, 36, 33);
        txtUrl.ForeColor = Color.Cyan;
        txtUrl.BorderStyle = BorderStyle.FixedSingle;
        this.Controls.Add(txtUrl);

        btnDownloadUrl = new Button();
        btnDownloadUrl.Text = "СКАЧАТЬ";
        btnDownloadUrl.Location = new Point(340, 224);
        btnDownloadUrl.Size = new Size(95, 26);
        btnDownloadUrl.FlatStyle = FlatStyle.Flat;
        btnDownloadUrl.BackColor = Color.FromArgb(0, 110, 140);
        btnDownloadUrl.ForeColor = Color.White;
        btnDownloadUrl.Click += new EventHandler(BtnDownloadUrl_Click);
        this.Controls.Add(btnDownloadUrl);

        btnStopDownload = new Button();
        btnStopDownload.Text = "СТОП";
        btnStopDownload.Location = new Point(440, 224);
        btnStopDownload.Size = new Size(85, 26);
        btnStopDownload.FlatStyle = FlatStyle.Flat;
        btnStopDownload.BackColor = Color.FromArgb(140, 35, 35);
        btnStopDownload.ForeColor = Color.White;
        btnStopDownload.Enabled = false;
        btnStopDownload.Click += new EventHandler(BtnStopDownload_Click);
        this.Controls.Add(btnStopDownload);

        chkShuffle = CreateCheckBox("Умный шаффл музыки (между игровыми сессиями)", 262, SettingsManager.GetBool("Shuffle", true));
        chkLoudNorm = CreateCheckBox("Выравнивание громкости треков (LoudNorm -14 LUFS)", 288, SettingsManager.GetBool("LoudNorm", true));
        chkDiscord = CreateCheckBox("Discord Rich Presence (борт, позывной, фраги, таймер)", 314, SettingsManager.GetBool("Discord", true));
        chkDucking = CreateCheckBox("Авто-затихание радио при голосовой связи", 340, SettingsManager.GetBool("Ducking", true));
        chkAudioSwitch = CreateCheckBox("Авто-переключение звука/микрофона на VR-шлем", 366, SettingsManager.GetBool("AudioSwitch", true));
        chkDiagnostics = CreateCheckBox("Диагностика лога (для подбора паттернов распознавания)", 392, SettingsManager.GetBool("DiagnosticsEnabled", false));

        progressBar = new ProgressBar();
        progressBar.Location = new Point(24, 428);
        progressBar.Size = new Size(520, 14);
        progressBar.Visible = false;
        this.Controls.Add(progressBar);

        lblStatus = new Label();
        lblStatus.Text = "Запуск...";
        lblStatus.Location = new Point(22, 446);
        lblStatus.Size = new Size(520, 22);
        lblStatus.ForeColor = Color.Silver;
        this.Controls.Add(lblStatus);

        btnInstall = new Button();
        btnInstall.Text = "▶ СТАРТ VTOL VR";
        btnInstall.Font = new Font("Consolas", 10f, FontStyle.Bold);
        btnInstall.Location = new Point(24, 476);
        btnInstall.Size = new Size(255, 42);
        btnInstall.FlatStyle = FlatStyle.Flat;
        btnInstall.BackColor = Color.FromArgb(0, 140, 60);
        btnInstall.ForeColor = Color.White;
        btnInstall.Click += new EventHandler(BtnInstall_Click);
        this.Controls.Add(btnInstall);

        btnSaveVault = new Button();
        btnSaveVault.Text = "🛡 БЭКАП СОХРАНЕНИЙ";
        btnSaveVault.Font = new Font("Consolas", 9.5f, FontStyle.Bold);
        btnSaveVault.Location = new Point(290, 476);
        btnSaveVault.Size = new Size(254, 42);
        btnSaveVault.FlatStyle = FlatStyle.Flat;
        btnSaveVault.BackColor = Color.FromArgb(130, 80, 0);
        btnSaveVault.ForeColor = Color.White;
        btnSaveVault.Click += new EventHandler(BtnSaveVault_Click);
        this.Controls.Add(btnSaveVault);

        btnManageMusic = new Button();
        btnManageMusic.Text = "♫ ТРЕКИ";
        btnManageMusic.Location = new Point(24, 526);
        btnManageMusic.Size = new Size(125, 38);
        btnManageMusic.FlatStyle = FlatStyle.Flat;
        btnManageMusic.BackColor = Color.FromArgb(0, 95, 125);
        btnManageMusic.ForeColor = Color.White;
        btnManageMusic.Click += new EventHandler(BtnManageMusic_Click);
        this.Controls.Add(btnManageMusic);

        btnLiveryManager = new Button();
        btnLiveryManager.Text = "✈ ЛИВРЕИ";
        btnLiveryManager.Location = new Point(157, 526);
        btnLiveryManager.Size = new Size(125, 38);
        btnLiveryManager.FlatStyle = FlatStyle.Flat;
        btnLiveryManager.BackColor = Color.FromArgb(70, 50, 110);
        btnLiveryManager.ForeColor = Color.White;
        btnLiveryManager.Click += new EventHandler(BtnLiveryManager_Click);
        this.Controls.Add(btnLiveryManager);

        btnLogbook = new Button();
        btnLogbook.Text = "📋 ЛОГБУК";
        btnLogbook.Location = new Point(290, 526);
        btnLogbook.Size = new Size(125, 38);
        btnLogbook.FlatStyle = FlatStyle.Flat;
        btnLogbook.BackColor = Color.FromArgb(40, 85, 70);
        btnLogbook.ForeColor = Color.White;
        btnLogbook.Click += new EventHandler(BtnLogbook_Click);
        this.Controls.Add(btnLogbook);

        btnAudioSettings = new Button();
        btnAudioSettings.Text = "🎧 АУДИО VR";
        btnAudioSettings.Location = new Point(423, 526);
        btnAudioSettings.Size = new Size(122, 38);
        btnAudioSettings.FlatStyle = FlatStyle.Flat;
        btnAudioSettings.BackColor = Color.FromArgb(80, 60, 20);
        btnAudioSettings.ForeColor = Color.White;
        btnAudioSettings.Click += new EventHandler(BtnAudioSettings_Click);
        this.Controls.Add(btnAudioSettings);

        btnDiagnostics = new Button();
        btnDiagnostics.Text = "🔧 ДИАГНОСТИКА ЛОГА";
        btnDiagnostics.Location = new Point(24, 572);
        btnDiagnostics.Size = new Size(255, 32);
        btnDiagnostics.FlatStyle = FlatStyle.Flat;
        btnDiagnostics.BackColor = Color.FromArgb(30, 38, 34);
        btnDiagnostics.ForeColor = Color.FromArgb(0, 255, 102);
        btnDiagnostics.Click += new EventHandler(BtnDiagnostics_Click);
        this.Controls.Add(btnDiagnostics);

        btnOpenFolder = new Button();
        btnOpenFolder.Text = "📁 ПАПКА RADIOMUSIC";
        btnOpenFolder.Location = new Point(290, 572);
        btnOpenFolder.Size = new Size(254, 32);
        btnOpenFolder.FlatStyle = FlatStyle.Flat;
        btnOpenFolder.BackColor = Color.FromArgb(30, 38, 34);
        btnOpenFolder.ForeColor = Color.FromArgb(0, 255, 102);
        btnOpenFolder.Click += new EventHandler(BtnOpenFolder_Click);
        this.Controls.Add(btnOpenFolder);

        InitSystemTray();
        this.FormClosing += new FormClosingEventHandler(MainForm_FormClosing);
        this.Shown += new EventHandler(MainForm_Shown);
    }

    private void MainForm_Shown(object sender, EventArgs e)
    {
        ThreadPool.QueueUserWorkItem(delegate { SilentSync(true); });
        tracker.DiscordEnabled = chkDiscord.Checked;
        tracker.DuckingEnabled = chkDucking.Checked;
        tracker.AudioSwitchEnabled = chkAudioSwitch.Checked;
        tracker.DiagnosticsEnabled = chkDiagnostics.Checked;
        tracker.Start();

        chkDiscord.CheckedChanged += delegate { tracker.DiscordEnabled = chkDiscord.Checked; SaveCheckboxSettings(); };
        chkDucking.CheckedChanged += delegate { tracker.DuckingEnabled = chkDucking.Checked; SaveCheckboxSettings(); };
        chkAudioSwitch.CheckedChanged += delegate { tracker.AudioSwitchEnabled = chkAudioSwitch.Checked; SaveCheckboxSettings(); };
        chkDiagnostics.CheckedChanged += delegate { tracker.DiagnosticsEnabled = chkDiagnostics.Checked; SaveCheckboxSettings(); };
        chkShuffle.CheckedChanged += delegate { SaveCheckboxSettings(); };
        chkLoudNorm.CheckedChanged += delegate { SaveCheckboxSettings(); };
    }

    private void SaveCheckboxSettings()
    {
        SettingsManager.SetBool("Shuffle", chkShuffle.Checked);
        SettingsManager.SetBool("LoudNorm", chkLoudNorm.Checked);
        SettingsManager.SetBool("Discord", chkDiscord.Checked);
        SettingsManager.SetBool("Ducking", chkDucking.Checked);
        SettingsManager.SetBool("AudioSwitch", chkAudioSwitch.Checked);
        SettingsManager.SetBool("DiagnosticsEnabled", chkDiagnostics.Checked);
        SettingsManager.Set("RadioMusicDir", txtPath.Text);
        SettingsManager.Save();
    }

    private void BtnBrowse_Click(object sender, EventArgs e)
    {
        using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            if (fbd.ShowDialog() == DialogResult.OK) { txtPath.Text = fbd.SelectedPath; SaveCheckboxSettings(); }
    }

    private void BtnInstall_Click(object sender, EventArgs e)
    {
        SaveCheckboxSettings();
        SilentSync(false);
        Process.Start("steam://rungameid/667970");
    }

    private void BtnSaveVault_Click(object sender, EventArgs e)
    {
        using (SaveVaultForm vault = new SaveVaultForm()) vault.ShowDialog(this);
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
            chkDiagnostics.Checked = SettingsManager.GetBool("DiagnosticsEnabled", false);
            tracker.DiagnosticsEnabled = chkDiagnostics.Checked;
        }
    }

    private void BtnOpenFolder_Click(object sender, EventArgs e)
    {
        if (Directory.Exists(txtPath.Text)) Process.Start("explorer.exe", txtPath.Text);
    }

    private void InitSystemTray()
    {
        trayMenu = new ContextMenuStrip();
        ToolStripMenuItem itemRestore = new ToolStripMenuItem("Показать главное окно");
        itemRestore.Click += delegate { RestoreFromTray(); };
        trayMenu.Items.Add(itemRestore);

        ToolStripMenuItem itemShuffle = new ToolStripMenuItem("Перемешать треки сейчас");
        itemShuffle.Click += delegate {
            string dir = txtPath.Text.Trim('"');
            if (Directory.Exists(dir))
            {
                bool ok = MusicShuffler.ShuffleNow(dir, chkShuffle.Checked);
                if (trayIcon != null)
                    trayIcon.ShowBalloonTip(2000, "VTOL QoL", ok ? "Плейлист перемешан!" : "Шафл невозможен (игра запущена или папка пуста).", ToolTipIcon.Info);
            }
        };
        trayMenu.Items.Add(itemShuffle);

        ToolStripMenuItem itemOpenFolder = new ToolStripMenuItem("Открыть папку RadioMusic");
        itemOpenFolder.Click += delegate {
            string dir = txtPath.Text.Trim('"');
            if (Directory.Exists(dir)) Process.Start("explorer.exe", dir);
        };
        trayMenu.Items.Add(itemOpenFolder);

        trayMenu.Items.Add(new ToolStripSeparator());

        ToolStripMenuItem itemExit = new ToolStripMenuItem("Выход из VTOL QoL");
        itemExit.Click += delegate {
            isExplicitExit = true;
            if (trayIcon != null) trayIcon.Visible = false;
            Application.Exit();
        };
        trayMenu.Items.Add(itemExit);

        trayIcon = new NotifyIcon();
        trayIcon.Text = "VTOL QoL — Flight Audio Hub";
        trayIcon.Icon = this.Icon;
        trayIcon.ContextMenuStrip = trayMenu;
        trayIcon.Visible = true;
        trayIcon.DoubleClick += delegate { RestoreFromTray(); };
    }

    private void RestoreFromTray()
    {
        this.Show();
        this.WindowState = FormWindowState.Normal;
        this.BringToFront();
        this.Activate();
    }

    private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
    {
        if (!isExplicitExit && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            this.Hide();
            if (trayIcon != null)
                trayIcon.ShowBalloonTip(2500, "VTOL QoL", "Утилита свернута в трей и продолжает работу в фоне.", ToolTipIcon.Info);
        }
        else
        {
            SaveCheckboxSettings();
        }
    }

    private CheckBox CreateCheckBox(string text, int top, bool isChecked)
    {
        CheckBox cb = new CheckBox();
        cb.Text = text;
        cb.Location = new Point(24, top);
        cb.Size = new Size(505, 24);
        cb.Checked = isChecked;
        cb.ForeColor = Color.FromArgb(210, 240, 220);
        this.Controls.Add(cb);
        return cb;
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
        btnDownloadUrl.Enabled = enabled;
        btnManageMusic.Enabled = enabled;
        btnLiveryManager.Enabled = enabled;
        btnLogbook.Enabled = enabled;
        btnSaveVault.Enabled = enabled;
        pnlDropZone.AllowDrop = enabled;
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

    private void PnlDropZone_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effect = DragDropEffects.Copy; pnlDropZone.BackColor = Color.FromArgb(0, 80, 45); }
    }

    private void PnlDropZone_DragLeave(object sender, EventArgs e) { pnlDropZone.BackColor = Color.FromArgb(26, 34, 30); }

    private void PnlDropZone_DragDrop(object sender, DragEventArgs e)
    {
        pnlDropZone.BackColor = Color.FromArgb(26, 34, 30);
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
                        string afFilter = chkLoudNorm.Checked ? "-af loudnorm=I=-14:TP=-1.5:LRA=11 " : "";
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

    private void BtnDownloadUrl_Click(object sender, EventArgs e)
    {
        string url = txtUrl.Text.Trim();
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
                string modelsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL", "models");
                try { Directory.CreateDirectory(modelsDir); } catch { }
                string[] planes = new string[] { "FA-26B.fbx", "F-45A.fbx", "AV-42C.fbx", "AH-94.fbx", "T-55.fbx", "EF-24G.fbx" };
                foreach (string pl in planes)
                    ExtractResource(pl, Path.Combine(modelsDir, pl));


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
                    string postArgs = chkLoudNorm.Checked ? "--postprocessor-args \"ffmpeg:-af loudnorm=I=-14:TP=-1.5:LRA=11\"" : "";
                    string args = string.Format("-x --audio-format mp3 --ffmpeg-location \"{0}\" {1} --download-archive \"{2}\" -o \"{3}\\%(title)s.%(ext)s\" \"{4}\"", toolsDir, postArgs, archiveFile, gameDir, url);
                    Process p = Process.Start(new ProcessStartInfo(ytdlpPath, args) { WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true });
                    currentWorkerProcess = p;
                    if (p != null) p.WaitForExit();
                }

                if (isCancelRequested) UpdateStatus("Загрузка отменена.", 0);
                else { try { this.Invoke(new Action(delegate { txtUrl.Text = ""; })); } catch { } UpdateStatus("Загрузка завершена!", 100); }
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

            if (firstRun) ConfigureSteam(batPath);

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
        if (args != null && args.Length >= 1 && args[0] == "--shuffle")
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
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        SettingsManager.Load();
        Application.Run(new MainForm());
    }

    private static bool SettingsManagerSafeGetShuffleFlag()
    {
        try { SettingsManager.Load(); return SettingsManager.GetBool("Shuffle", true); } catch { return true; }
    }
}
