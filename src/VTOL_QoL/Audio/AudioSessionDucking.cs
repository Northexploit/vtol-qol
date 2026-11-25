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

