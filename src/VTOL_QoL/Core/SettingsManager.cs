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

    private static readonly object saveLock = new object();

    public static void Save()
    {
        if (string.IsNullOrEmpty(filePath)) return;
        lock (saveLock)
        {
            string tmpPath = filePath + ".tmp";
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("; VTOL_QoL settings");
                foreach (KeyValuePair<string, string> kv in data)
                    sb.AppendLine(kv.Key + "=" + kv.Value);

                File.WriteAllText(tmpPath, sb.ToString(), Encoding.UTF8);
                if (File.Exists(filePath))
                    File.Replace(tmpPath, filePath, null);
                else
                    File.Move(tmpPath, filePath);
            }
            catch (Exception ex)
            {
                AppLogger.Error("SETTINGS", "Save", ex);
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
            }
        }
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

