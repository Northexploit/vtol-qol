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
            Directory.CreateDirectory(dest);

            string fullDestRoot = Path.GetFullPath(dest);
            if (!fullDestRoot.EndsWith(Path.DirectorySeparatorChar.ToString()))
                fullDestRoot += Path.DirectorySeparatorChar;

            using (System.IO.Compression.ZipArchive archive = System.IO.Compression.ZipFile.OpenRead(zipPath))
            {
                foreach (System.IO.Compression.ZipArchiveEntry entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue; // пропускаем пустые записи папок
                    string targetFile = Path.GetFullPath(Path.Combine(dest, entry.FullName));
                    if (!targetFile.StartsWith(fullDestRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        AppLogger.Log("SECURITY", "Заблокирована попытка Zip Slip в архиве: " + entry.FullName);
                        throw new InvalidOperationException("Небезопасный путь внутри архива: " + entry.FullName);
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(targetFile));
                    using (Stream entryStream = entry.Open())
                    using (FileStream outFs = new FileStream(targetFile, FileMode.Create, FileAccess.Write))
                        entryStream.CopyTo(outFs);
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("LIVERY", "InstallZip", ex);
            return false;
        }
    }
}

