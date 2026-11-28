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

    // Запасной путь: рядом с exe. Нужен, если запись в AppData недоступна —
    // иначе лог молча не пишется и мы не видим, что делает программа.
    public static string GetFallbackLogPath()
    {
        try { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_debug.log"); }
        catch { return null; }
    }

    private static bool TryWrite(string path, string text, bool append)
    {
        if (string.IsNullOrEmpty(path)) return false;
        try
        {
            if (append) File.AppendAllText(path, text, Encoding.UTF8);
            else File.WriteAllText(path, text, Encoding.UTF8);
            return true;
        }
        catch { return false; }
    }

    // Пишем в основной лог, при неудаче — в запасной. Возвращает путь, куда записалось.
    private static string WriteWithFallback(string text, bool append)
    {
        string primary = GetLogPath();
        if (TryWrite(primary, text, append)) return primary;
        string fb = GetFallbackLogPath();
        if (!string.Equals(fb, primary, StringComparison.OrdinalIgnoreCase) && TryWrite(fb, text, append))
        {
            // Дальше пишем сразу туда, чтобы не тратить попытки на недоступный путь.
            logPath = fb;
            return fb;
        }
        return null;
    }

    public static void InitSession()
    {
        WriteWithFallback("=== VTOL_QoL Debug Session: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===" + Environment.NewLine, false);
    }

    public static void Log(string module, string message)
    {
        lock (logLock)
        {
            string line = string.Format("[{0}] [{1}] {2}", DateTime.Now.ToString("HH:mm:ss.fff"), module, message);
            WriteWithFallback(line + Environment.NewLine, true);
        }
    }

    public static void Error(string module, string context, Exception ex)
    {
        Log(module, "ERROR (" + context + "): " + ex.GetType().Name + " — " + ex.Message);
    }
}

