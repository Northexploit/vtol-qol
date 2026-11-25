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

