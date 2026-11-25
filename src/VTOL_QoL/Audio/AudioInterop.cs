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

