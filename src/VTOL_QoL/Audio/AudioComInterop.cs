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


[ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioMeterInformation
{
    int GetPeakValue(out float pfPeak);
    int GetMeteringChannelCount(out int pnChannels);
    int GetChannelsPeakValues(int u32ChannelCount, [Out] float[] afPeakValues);
    int QueryHardwareSupport(out int pdwHardwareSupportMask);
}

