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
// =========================================================================
// === ОНЛАЙН-КАТАЛОГ И ЗАГРУЗЧИК STEAM WORKSHOP (ЧЕРЕЗ STEAMCMD) ==========
// =========================================================================
public class WorkshopCatalogItem
{
    public string PublishedFileId;
    public string Title;
    public string PreviewUrl;
    public string AircraftTag;
}

public static class WorkshopBrowser
{
    public static void EnableTls12()
    {
        try { System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072; } catch { }
    }

    public static List<WorkshopCatalogItem> SearchLiveries(string query, string aircraftTag, int page)
    {
        EnableTls12();
        List<WorkshopCatalogItem> list = new List<WorkshopCatalogItem>();

        string qTrim = !string.IsNullOrEmpty(query) ? query.Trim() : "";
        AppLogger.Log("WORKSHOP", string.Format("Поиск: query='{0}', tag='{1}', page={2}", qTrim, aircraftTag, page));

        Match mDirectId = Regex.Match(qTrim, @"(?:id=|^)(?<val>\d{7,12})(?:&|$)");
        if (mDirectId.Success)
        {
            string directId = mDirectId.Groups["val"].Value;
            List<WorkshopCatalogItem> singleList = FetchDetailsBatch(new List<string> { directId }, aircraftTag);
            if (singleList.Count > 0) return singleList;
            WorkshopCatalogItem single = FetchSingleWorkshopItem(directId, aircraftTag);
            if (single != null) list.Add(single);
            return list;
        }

        string host = "steamcommunity.com";
        string url = "https://" + host + "/workshop/browse/?appid=667970&browsesort=trend&section=readytouseitems&requiredtags%5B0%5D=Liveries&p=" + page;

        if (!string.IsNullOrEmpty(aircraftTag) && aircraftTag != "Все самолеты" && aircraftTag != "Не определен")
        {
            string steamVehicleTag = aircraftTag == "FA-26B" ? "F/A-26B" : aircraftTag;
            url += "&requiredtags%5B1%5D=" + Uri.EscapeDataString(steamVehicleTag);
        }
        if (!string.IsNullOrEmpty(qTrim))
            url += "&searchtext=" + Uri.EscapeDataString(qTrim);

        AppLogger.Log("WORKSHOP", "GET URL: " + url);

        System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
        req.Method = "GET";
        req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36";
        req.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
        req.CookieContainer = new System.Net.CookieContainer();
        req.CookieContainer.Add(new Uri("https://" + host), new System.Net.Cookie("birthtime", "283993201"));
        req.CookieContainer.Add(new Uri("https://" + host), new System.Net.Cookie("wants_mature_content", "1"));
        req.AllowAutoRedirect = true;
        req.AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate;

        string html = "";
        using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)req.GetResponse())
        using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            html = sr.ReadToEnd();

        List<string> ids = new List<string>();
        MatchCollection idMatches = Regex.Matches(html, @"sharedfiles/filedetails/\?id=(?<val>\d{7,12})", RegexOptions.IgnoreCase);
        foreach (Match mId in idMatches)
        {
            string id = mId.Groups["val"].Value;
            if (!ids.Contains(id)) ids.Add(id);
        }

        AppLogger.Log("WORKSHOP", "Найдено уникальных ID на странице: " + ids.Count);
        if (ids.Count == 0) return list;

        List<WorkshopCatalogItem> rawList = FetchDetailsBatch(ids, "Не определен");
        if (string.IsNullOrEmpty(aircraftTag) || aircraftTag == "Все самолеты" || aircraftTag == "Не определен")
            return rawList;

        foreach (WorkshopCatalogItem it in rawList)
        {
            string ac = it.AircraftTag;
            // Если в тегах Steam борт не был указан, пробуем определить его по названию мода
            if (ac == "Не определен")
            {
                string t = it.Title;
                if (Regex.IsMatch(t, @"(?i)(EF-?24|F-?14|Tomcat|Mischief)")) ac = "EF-24G";
                else if (Regex.IsMatch(t, @"(?i)(F-?45|F-?22|F-?35|Ghost)")) ac = "F-45A";
                else if (Regex.IsMatch(t, @"(?i)(AV-?42|Kestrel)")) ac = "AV-42C";
                else if (Regex.IsMatch(t, @"(?i)(AH-?94|AH-?1|Cobra|Viper|Dragonfly)")) ac = "AH-94";
                else if (Regex.IsMatch(t, @"(?i)(T-?55|Tyro|Trainer)")) ac = "T-55";
                else if (Regex.IsMatch(t, @"(?i)(FA-?26|F/?A-?26|Wasp|F-?15|F-?16|F-?18|Su-?57|Felon)")) ac = "FA-26B";
                it.AircraftTag = ac;
            }

            if (it.AircraftTag.Equals(aircraftTag, StringComparison.OrdinalIgnoreCase))
                list.Add(it);
        }

        AppLogger.Log("WORKSHOP", string.Format("После строгого фильтра для {0} осталось: {1} из {2}", aircraftTag, list.Count, rawList.Count));
        return list;
    }


    private static List<WorkshopCatalogItem> FetchDetailsBatch(List<string> ids, string defaultAircraftTag)
    {
        List<WorkshopCatalogItem> result = new List<WorkshopCatalogItem>();
        try
        {
            StringBuilder postData = new StringBuilder();
            postData.Append("itemcount=" + ids.Count);
            for (int i = 0; i < ids.Count; i++)
                postData.Append("&publishedfileids[" + i + "]=" + ids[i]);

            byte[] bytes = Encoding.UTF8.GetBytes(postData.ToString());

            string apiHost = "api." + "steampowered" + ".com";
            string apiUrl = "https://" + apiHost + "/ISteamRemoteStorage/GetPublishedFileDetails/v1/";
            System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(apiUrl);

            req.Method = "POST";
            req.Timeout = 8000;
            req.ContentType = "application/x-www-form-urlencoded";
            req.ContentLength = bytes.Length;
            req.AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate;

            using (Stream reqStream = req.GetRequestStream())
                reqStream.Write(bytes, 0, bytes.Length);

            string json = "";
            using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)req.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                json = sr.ReadToEnd();

            string[] blocks = Regex.Split(json, @"""publishedfileid""\s*:\s*""");
            Dictionary<string, WorkshopCatalogItem> map = new Dictionary<string, WorkshopCatalogItem>();

            for (int i = 1; i < blocks.Length; i++)
            {
                string b = blocks[i];
                int qEnd = b.IndexOf('"');
                if (qEnd <= 0) continue;
                string id = b.Substring(0, qEnd);

                Match mTitle = Regex.Match(b, @"""title""\s*:\s*""(?<val>(?:\\.|[^""\\])*)""");
                string title = mTitle.Success ? Regex.Unescape(mTitle.Groups["val"].Value).Trim() : ("Workshop_" + id);
                if (string.IsNullOrEmpty(title)) title = "Workshop_" + id;

                Match mPrev = Regex.Match(b, @"""preview_url""\s*:\s*""(?<val>(?:\\.|[^""\\])*)""");
                string prevUrl = mPrev.Success ? Regex.Unescape(mPrev.Groups["val"].Value).Replace(@"\/", "/") : "";

                string detectedAc = (!string.IsNullOrEmpty(defaultAircraftTag) && defaultAircraftTag != "Все самолеты") ? defaultAircraftTag : "Не определен";
                foreach (Match mTag in Regex.Matches(b, @"""tag""\s*:\s*""(?<val>[^""]+)"""))
                {
                    string norm = LiveryParser.NormalizeAircraftName(mTag.Groups["val"].Value.Trim());
                    if (norm == "FA-26B" || norm == "F-45A" || norm == "AV-42C" || norm == "AH-94" || norm == "T-55" || norm == "EF-24G")
                    {
                        detectedAc = norm;
                        break;
                    }
                }

                map[id] = new WorkshopCatalogItem
                {
                    PublishedFileId = id,
                    Title = title,
                    PreviewUrl = prevUrl,
                    AircraftTag = detectedAc
                };
            }

            foreach (string id in ids)
            {
                WorkshopCatalogItem item;
                if (map.TryGetValue(id, out item)) result.Add(item);
            }
            AppLogger.Log("WORKSHOP", "GetPublishedFileDetails вернул метаданные для " + result.Count + " модов.");
        }
        catch (Exception ex)
        {
            AppLogger.Error("WORKSHOP", "FetchDetailsBatch", ex);
        }
        return result;
    }

    private static WorkshopCatalogItem FetchSingleWorkshopItem(string id, string aircraftTag)
    {
        try
        {
            string host = "steamcommunity.com";
            string url = "https://" + host + "/sharedfiles/filedetails/?id=" + id;
            AppLogger.Log("WORKSHOP", "Загрузка одиночного предмета: " + url);
            System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
            req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
            req.AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate;
            string html = "";
            using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)req.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                html = sr.ReadToEnd();

            Match mTitle = Regex.Match(html, @"class=""workshopItemTitle""[^>]*>(?<val>.*?)</div>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            string title = mTitle.Success ? System.Net.WebUtility.HtmlDecode(Regex.Replace(mTitle.Groups["val"].Value, "<.*?>", "")).Trim() : ("Workshop_" + id);

            Match mImg = Regex.Match(html, @"id=""previewImageMain""[^>]+src=""(?<val>https?://[^""]+)""", RegexOptions.IgnoreCase);
            if (!mImg.Success) mImg = Regex.Match(html, @"<link rel=""image_src"" href=""(?<val>https?://[^""]+)""", RegexOptions.IgnoreCase);
            string prevUrl = mImg.Success ? mImg.Groups["val"].Value : "";

            return new WorkshopCatalogItem
            {
                PublishedFileId = id,
                Title = title,
                PreviewUrl = prevUrl,
                AircraftTag = (!string.IsNullOrEmpty(aircraftTag) && aircraftTag != "Все самолеты") ? aircraftTag : "Не определен"
            };
        }
        catch (Exception ex)
        {
            AppLogger.Error("WORKSHOP", "FetchSingleWorkshopItem", ex);
            return new WorkshopCatalogItem { PublishedFileId = id, Title = "Workshop_" + id, PreviewUrl = "", AircraftTag = "Не определен" };
        }
    }
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public static void MinimizeSteamWindows()
    {
        try
        {
            string[] names = new string[] { "steam", "steamwebhelper" };
            foreach (string name in names)
            {
                foreach (Process p in Process.GetProcessesByName(name))
                {
                    if (p.MainWindowHandle != IntPtr.Zero)
                        ShowWindow(p.MainWindowHandle, 6);
                }
            }
        }
        catch { }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate bool SteamAPI_InitDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SteamAPI_ShutdownDelegate();

    private static void ShutdownSteamApi(IntPtr hMod)
    {
        try
        {
            if (hMod != IntPtr.Zero)
            {
                SteamAPI_ShutdownDelegate fnShut = GetUgcExport<SteamAPI_ShutdownDelegate>(hMod, "SteamAPI_Shutdown");
                if (fnShut != null) fnShut();
            }
        }
        catch { }
        finally
        {
            if (hMod != IntPtr.Zero)
            {
                try { FreeLibrary(hMod); } catch { }
            }
            Environment.SetEnvironmentVariable("SteamAppId", null);
            Environment.SetEnvironmentVariable("SteamGameId", null);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SteamAPI_RunCallbacksDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr SteamUGCDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong Flat_UGC_SubscribeItem(IntPtr self, ulong nPublishedFileID);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate bool Flat_UGC_DownloadItem(IntPtr self, ulong nPublishedFileID, [MarshalAs(UnmanagedType.I1)] bool bHighPriority);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate bool Flat_UGC_GetItemDownloadInfo(IntPtr self, ulong nPublishedFileID, out ulong punBytesDownloaded, out ulong punBytesTotal);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint Flat_UGC_GetItemState(IntPtr self, ulong nPublishedFileID);

    private static T GetUgcExport<T>(IntPtr hMod, string procName) where T : class
    {
        IntPtr p = GetProcAddress(hMod, procName);
        if (p == IntPtr.Zero) return null;
        return (T)(object)Marshal.GetDelegateForFunctionPointer(p, typeof(T));
    }

    private static IntPtr GetNativeUgcInterface(string gameRoot, out IntPtr hModOut)
    {
        hModOut = IntPtr.Zero;
        if (Process.GetProcessesByName("steam").Length == 0) return IntPtr.Zero;

        string dllPath = Path.Combine(gameRoot, @"VTOLVR_Data\Plugins\x86_64\steam_api64.dll");
        if (!File.Exists(dllPath))
            dllPath = Path.Combine(gameRoot, @"VTOLVR_Data\Plugins\steam_api64.dll");
        if (!File.Exists(dllPath)) return IntPtr.Zero;

        Environment.SetEnvironmentVariable("SteamAppId", "667970");
        Environment.SetEnvironmentVariable("SteamGameId", "667970");

        IntPtr hMod = LoadLibrary(dllPath);
        if (hMod == IntPtr.Zero) return IntPtr.Zero;

        SteamAPI_InitDelegate fnInit = GetUgcExport<SteamAPI_InitDelegate>(hMod, "SteamAPI_Init");
        if (fnInit == null || !fnInit()) return IntPtr.Zero;

        string[] ugcExports = new string[] {
            "SteamAPI_SteamUGC_v021", "SteamAPI_SteamUGC_v020", "SteamAPI_SteamUGC_v018",
            "SteamAPI_SteamUGC_v017", "SteamAPI_SteamUGC_v016", "SteamAPI_SteamUGC_v015", "SteamUGC"
        };
        foreach (string exp in ugcExports)
        {
            SteamUGCDelegate fnUgc = GetUgcExport<SteamUGCDelegate>(hMod, exp);
            if (fnUgc != null)
            {
                IntPtr ptr = fnUgc();
                if (ptr != IntPtr.Zero)
                {
                    hModOut = hMod;
                    return ptr;
                }
            }
        }
        return IntPtr.Zero;
    }

    public static volatile bool CancelWait = false;

    public static bool SubscribeAndDownloadWorkshop(WorkshopCatalogItem item, string gameRoot, Action<int, string> onProgress)
    {
        CancelWait = false;
        AppLogger.Log("WORKSHOP", "Авто-подписка и загрузка ID=" + item.PublishedFileId + " (" + item.Title + ")");
        if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot))
            throw new Exception("Не определена папка игры VTOL VR.");

        if (Process.GetProcessesByName("steam").Length == 0)
            throw new Exception("Клиент Steam не запущен. Запустите Steam для скачивания ливреи.");
        if (Process.GetProcessesByName("VTOLVR").Length > 0)
            throw new Exception("Закройте VTOL VR перед скачиванием ливреи из Steam Workshop, чтобы не блокировать загрузку в игре.");

        string steamLibRoot = Path.GetFullPath(Path.Combine(gameRoot, @"..\..\.."));
        string gameWorkshopContent = Path.Combine(steamLibRoot, @"steamapps\workshop\content\667970");
        Directory.CreateDirectory(gameWorkshopContent);
        string targetFolder = Path.Combine(gameWorkshopContent, item.PublishedFileId);

        ulong fileIdNum;
        if (!ulong.TryParse(item.PublishedFileId, out fileIdNum) || fileIdNum == 0)
            throw new Exception("Некорректный ID ливреи.");

        IntPtr hMod;
        IntPtr ugcPtr = GetNativeUgcInterface(gameRoot, out hMod);
        if (ugcPtr == IntPtr.Zero)
        {
            ShutdownSteamApi(hMod);
            throw new Exception("Не удалось подключиться к Steamworks UGC. Проверьте, что вы вошли в аккаунт Steam.");
        }

        try
        {
            SteamAPI_RunCallbacksDelegate fnCallbacks = GetUgcExport<SteamAPI_RunCallbacksDelegate>(hMod, "SteamAPI_RunCallbacks");
            Flat_UGC_SubscribeItem fnSub = GetUgcExport<Flat_UGC_SubscribeItem>(hMod, "SteamAPI_ISteamUGC_SubscribeItem");
        Flat_UGC_DownloadItem fnDownload = GetUgcExport<Flat_UGC_DownloadItem>(hMod, "SteamAPI_ISteamUGC_DownloadItem");
        Flat_UGC_GetItemDownloadInfo fnGetInfo = GetUgcExport<Flat_UGC_GetItemDownloadInfo>(hMod, "SteamAPI_ISteamUGC_GetItemDownloadInfo");
        Flat_UGC_GetItemState fnGetState = GetUgcExport<Flat_UGC_GetItemState>(hMod, "SteamAPI_ISteamUGC_GetItemState");

        if (onProgress != null) onProgress(5, "Оформление подписки в Steam...");
        if (fnSub != null)
        {
            ulong subCall = fnSub(ugcPtr, fileIdNum);
            AppLogger.Log("WORKSHOP", "Flat SubscribeItem -> callHandle=" + subCall);
        }

        if (fnCallbacks != null) fnCallbacks();
        Thread.Sleep(400);

        if (fnDownload != null)
        {
            bool dlStarted = fnDownload(ugcPtr, fileIdNum, true);
            AppLogger.Log("WORKSHOP", "Flat DownloadItem(highPriority=true) -> " + dlStarted);
        }

        DateTime deadline = DateTime.UtcNow.AddSeconds(45);
        int lastPct = 10;

        while (DateTime.UtcNow < deadline)
        {
            if (CancelWait) return false;
            if (fnCallbacks != null) fnCallbacks();

            uint state = fnGetState != null ? fnGetState(ugcPtr, fileIdNum) : 0;
            // k_EItemStateInstalled = 4, k_EItemStateDownloading = 16, k_EItemStateDownloadPending = 32
            bool isInstalledFlag = (state & 4) != 0 && (state & 16) == 0 && (state & 32) == 0;

            ulong bytesDone = 0, bytesTotal = 0;
            if (fnGetInfo != null && fnGetInfo(ugcPtr, fileIdNum, out bytesDone, out bytesTotal) && bytesTotal > 0)
            {
                int pct = Math.Min(99, Math.Max(10, (int)((bytesDone * 100.0) / bytesTotal)));
                lastPct = pct;
                double mbDone = bytesDone / (1024.0 * 1024.0);
                double mbTotal = bytesTotal / (1024.0 * 1024.0);
                if (onProgress != null)
                    onProgress(pct, string.Format("Скачивание: {0}% ({1:F1} / {2:F1} МБ)", pct, mbDone, mbTotal));
            }
            else if ((state & 32) != 0)
            {
                if (onProgress != null) onProgress(lastPct, "В очереди загрузки Steam...");
            }
            else if ((state & 16) != 0)
            {
                if (onProgress != null) onProgress(Math.Max(lastPct, 25), "Загрузка файлов из Steam...");
            }

            if ((isInstalledFlag || (state == 0 && lastPct > 50)) &&
                Directory.Exists(targetFolder) && Directory.GetFiles(targetFolder, "*.*", SearchOption.AllDirectories).Length > 0)
            {
                if (onProgress != null) onProgress(100, "Завершение установки...");
                Thread.Sleep(600);
                WriteWorkshopMetadata(targetFolder, item);
                AppLogger.Log("WORKSHOP", "Мод автоматически скачан без открытия окон Steam: " + targetFolder);
                return true;
            }
                Thread.Sleep(350);
            }

            return false;
        }
        finally
        {
            ShutdownSteamApi(hMod);
        }
    }


    private static void WriteWorkshopMetadata(string targetFolder, WorkshopCatalogItem item)
    {
        // Не записываем посторонние файлы внутрь папки Steam Workshop, чтобы не вызывать перепроверку хешей в Steam!
        if (!string.IsNullOrEmpty(item.AircraftTag) && item.AircraftTag != "Не определен")
            LiveryParser.SaveUserAircraftMapping(targetFolder, item.AircraftTag);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong Flat_UGC_UnsubscribeItem(IntPtr self, ulong nPublishedFileID);

    private static bool TryNativeSteamUnsubscribe(string gameRoot, ulong fileId)
    {
        if (Process.GetProcessesByName("VTOLVR").Length > 0)
            throw new Exception("Закройте VTOL VR перед удалением ливреи из Steam Workshop.");

        IntPtr hMod = IntPtr.Zero;
        try
        {
            IntPtr ugcPtr = GetNativeUgcInterface(gameRoot, out hMod);
            if (ugcPtr == IntPtr.Zero) return false;

            Flat_UGC_UnsubscribeItem fnUnsub = GetUgcExport<Flat_UGC_UnsubscribeItem>(hMod, "SteamAPI_ISteamUGC_UnsubscribeItem");
            SteamAPI_RunCallbacksDelegate fnCallbacks = GetUgcExport<SteamAPI_RunCallbacksDelegate>(hMod, "SteamAPI_RunCallbacks");
            if (fnUnsub == null) return false;

            ulong callHandle = fnUnsub(ugcPtr, fileId);
            if (fnCallbacks != null)
            {
                for (int i = 0; i < 6; i++) { fnCallbacks(); Thread.Sleep(100); }
            }
            AppLogger.Log("WORKSHOP", "Flat SteamUGC UnsubscribeItem вызван, handle=" + callHandle);
            return callHandle != 0;
        }
        catch (Exception ex)
        {
            AppLogger.Error("WORKSHOP", "TryNativeSteamUnsubscribe", ex);
            return false;
        }
        finally
        {
            ShutdownSteamApi(hMod);
        }
    }

    public static void UnsubscribeAndDeleteWorkshop(string folderPath, string gameRoot)
    {
        string itemId = Path.GetFileName(folderPath);
        AppLogger.Log("WORKSHOP", "Удаление Workshop-ливреи ID=" + itemId);

        ulong fileIdNum;
        if (IntPtr.Size == 8 && ulong.TryParse(itemId, out fileIdNum) && !string.IsNullOrEmpty(gameRoot))
            TryNativeSteamUnsubscribe(gameRoot, fileIdNum);

        if (Directory.Exists(folderPath))
            Directory.Delete(folderPath, true);
    }


}


