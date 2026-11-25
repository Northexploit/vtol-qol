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
    int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr ppFormat);
    int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, bool bDefault, IntPtr ppFormat);
    int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
    int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr pEndpointFormat, IntPtr mixFormat);
    int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, bool bDefault, out long defaultPeriod, out long minimumPeriod);
    int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ref long period);
    int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr pMode);
    int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
    int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ref PROPERTYKEY key, IntPtr pv);
    int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ref PROPERTYKEY key, IntPtr pv);
    int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
    int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, bool bVisible);
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

[ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioMeterInformation
{
    int GetPeakValue(out float pfPeak);
    int GetMeteringChannelCount(out int pnChannels);
    int GetChannelsPeakValues(int u32ChannelCount, [Out] float[] afPeakValues);
    int QueryHardwareSupport(out int pdwHardwareSupportMask);
}

public static class AudioSessionDucking
{
    private static float? originalVolume = null;
    private static bool isCurrentlyDucked = false;
    private static DateTime lastVoiceUtc = DateTime.MinValue;
    private static readonly object dlock = new object();

    public static void SetDuck(bool duck, string processName)
    {
        lock (dlock)
        {
            // При первом вызове проверяем, не осталось ли зависшее приглушение с прошлой сессии
            if (!originalVolume.HasValue)
            {
                string saved = SettingsManager.Get("PreDuckVolume", "");
                float parsed;
                if (!string.IsNullOrEmpty(saved) && float.TryParse(saved, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed) && parsed > 0.35f)
                {
                    originalVolume = parsed;
                }
            }

            if (!duck && !isCurrentlyDucked && !originalVolume.HasValue)
                return;

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
                            if (proc != null)
                            {
                                string pName = proc.ProcessName;
                                proc.Dispose();
                                if (pName.Equals(processName, StringComparison.OrdinalIgnoreCase))
                                {
                                    ISimpleAudioVolume vol = (ISimpleAudioVolume)sc;
                                    Guid ctx = Guid.Empty;
                                    if (duck)
                                    {
                                        float cur;
                                        vol.GetMasterVolume(out cur);
                                        if (!originalVolume.HasValue)
                                        {
                                            // Если громкость уже была занижена прошлым сбоем (< 0.35), считаем нормой 1.0f
                                            originalVolume = cur > 0.35f ? cur : 1.0f;
                                            SettingsManager.Set("PreDuckVolume", originalVolume.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                                            SettingsManager.Save();
                                        }
                                        vol.SetMasterVolume(0.25f, ref ctx);
                                        isCurrentlyDucked = true;
                                    }
                                    else
                                    {
                                        float restore = originalVolume.HasValue ? originalVolume.Value : 1.0f;
                                        if (restore < 0.35f) restore = 1.0f;
                                        vol.SetMasterVolume(restore, ref ctx);
                                        originalVolume = null;
                                        isCurrentlyDucked = false;
                                        if (!string.IsNullOrEmpty(SettingsManager.Get("PreDuckVolume", "")))
                                        {
                                            SettingsManager.Set("PreDuckVolume", "");
                                            SettingsManager.Save();
                                        }
                                    }
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
        IMMDeviceEnumerator enumerator = null;
        IMMDevice micDev = null;
        IAudioMeterInformation meter = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eCommunications, out micDev);
            if (hr != 0 || micDev == null) return false;

            Guid iidMeter = typeof(IAudioMeterInformation).GUID;
            object objMeter;
            hr = micDev.Activate(ref iidMeter, 23, IntPtr.Zero, out objMeter);
            if (hr != 0 || objMeter == null) return false;

            meter = (IAudioMeterInformation)objMeter;
            float peak = 0f;
            meter.GetPeakValue(out peak);

            // Порог реального голоса с микрофона (игнорирует тишину и простой Discord в канале)
            if (peak >= 0.035f)
            {
                lastVoiceUtc = DateTime.UtcNow;
                return true;
            }
            // Удерживаем плавность 1.2 секунды между словами
            if ((DateTime.UtcNow - lastVoiceUtc).TotalSeconds <= 1.2)
                return true;
        }
        catch { }
        finally
        {
            if (meter != null) Marshal.ReleaseComObject(meter);
            if (micDev != null) Marshal.ReleaseComObject(micDev);
            if (enumerator != null) Marshal.ReleaseComObject(enumerator);
        }
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

