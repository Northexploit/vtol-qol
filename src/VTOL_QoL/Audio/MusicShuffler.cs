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

