using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;


public static class DebriefEngine
{
    public static string GetReplaysDir()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL", "Replays");
        try { Directory.CreateDirectory(dir); } catch { }
        return dir;
    }

    public static string FindVtolRootDir()
    {
        string mDir = SettingsManager.Get("RadioMusicDir", "");
        if (!string.IsNullOrEmpty(mDir) && Directory.Exists(mDir))
        {
            string parent = Path.GetFullPath(Path.Combine(mDir, ".."));
            if (File.Exists(Path.Combine(parent, "VTOLVR.exe"))) return parent;
        }
        string steamPath = (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", "") as string ?? "").Replace('/', '\\');
        if (!string.IsNullOrEmpty(steamPath))
        {
            string p = Path.Combine(steamPath, @"steamapps\common\VTOL VR");
            if (Directory.Exists(p)) return p;
        }
        string defP = @"C:\Program Files (x86)\Steam\steamapps\common\VTOL VR";
        if (Directory.Exists(defP)) return defP;
        return "";
    }

    public static List<string> FindAvailableReplays()
    {
        List<string> files = new List<string>();
        string localDir = GetReplaysDir();
        if (Directory.Exists(localDir))
        {
            files.AddRange(Directory.GetFiles(localDir, "*.vtolrep"));
            files.AddRange(Directory.GetFiles(localDir, "*.acmi"));
        }

        // Штатные реплеи VTOL VR (SaveData\Replays\<любое_имя>\replay.vtr)
        string vtolSaveReplays = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Boundless Dynamics, LLC\VTOLVR\SaveData\Replays");
        if (Directory.Exists(vtolSaveReplays))
        {
            try
            {
                foreach (string vtr in Directory.GetFiles(vtolSaveReplays, "*.vtr", SearchOption.AllDirectories))
                    if (!files.Contains(vtr)) files.Add(vtr);
            }
            catch { }
        }

        string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string[] tacDirs = new string[]
        {
            Path.Combine(docs, "Tacview"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "Tacview")
        };
        foreach (string td in tacDirs)
        {
            if (Directory.Exists(td))
            {
                try
                {
                    foreach (string f in Directory.GetFiles(td, "*.acmi", SearchOption.AllDirectories))
                        if (!files.Contains(f)) files.Add(f);
                }
                catch { }
            }
        }

        files.Sort((a, b) => {
            bool aNat = a.EndsWith(".vtr", StringComparison.OrdinalIgnoreCase);
            bool bNat = b.EndsWith(".vtr", StringComparison.OrdinalIgnoreCase);
            if (aNat != bNat) return aNat ? -1 : 1;
            return File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a));
        });
        return files;
    }

    public static DebriefSession LoadSession(string filePath, string preferredPilot)
    {
        DebriefSession s;
        if (filePath.EndsWith(".vtolrep", StringComparison.OrdinalIgnoreCase))
            s = LoadVtolRep(filePath, preferredPilot);
        else if (filePath.EndsWith(".vtr", StringComparison.OrdinalIgnoreCase) ||
                 Path.GetFileName(filePath).Equals("info.vts", StringComparison.OrdinalIgnoreCase))
            s = LoadNativeVtolReplay(filePath, preferredPilot);
        else
            s = LoadAcmi(filePath, preferredPilot);


        ComputeSessionBounds(s);
        try
        {
            string dbgPath = filePath + ".debug.txt";
            File.WriteAllText(dbgPath, BuildDebugReport(s), Encoding.UTF8);
        }
        catch { }
        return s;
    }

    public static string BuildDebugReport(DebriefSession s)
    {
        if (s == null) return "Сессия не загружена.";
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== ДИАГНОСТИЧЕСКИЙ ДАМП ПОВТОРА VTOL QoL ===");
        sb.AppendLine("Файл: " + (s.SourceFile ?? ""));
        if (!string.IsNullOrEmpty(LastVtrDiagnostic))
            sb.AppendLine("VTR-Диагностика: " + LastVtrDiagnostic);
        sb.AppendLine("Миссия: " + (s.Title ?? "") + " | Карта: " + (s.MapTitle ?? ""));
        sb.AppendLine("Пилот: " + (s.PilotName ?? "") + " | Длительность: " + s.Duration.ToString("F1", CultureInfo.InvariantCulture) + " сек");
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "Границы X: [{0:F0} .. {1:F0}], Y: [{2:F0} .. {3:F0}]",
            s.BoundsMinX, s.BoundsMaxX, s.BoundsMinY, s.BoundsMaxY));
        sb.AppendLine(string.Format("Сводка: Фраги игрока={0}, Союзников={1}, Пусков={2}, RWR={3}, Угроз={4}",
            s.PlayerKills, s.AlliedKills, s.PlayerLaunches, s.RwrSpikesCount, s.ThreatMissilesCount));
        sb.AppendLine();
        sb.AppendLine("--- ЮНИТЫ И РАКЕТЫ НА КАРТЕ (" + s.Entities.Count + ") ---");
        foreach (DebriefEntity e in s.Entities)
        {
            string pStart = e.Track.Count > 0 ? string.Format(CultureInfo.InvariantCulture, "({0:F0}, {1:F0}, alt={2:F0})", e.Track[0].X, e.Track[0].Y, e.Track[0].Alt) : "нет";
            string pEnd = e.Track.Count > 0 ? string.Format(CultureInfo.InvariantCulture, "({0:F0}, {1:F0}, alt={2:F0})", e.Track[e.Track.Count - 1].X, e.Track[e.Track.Count - 1].Y, e.Track[e.Track.Count - 1].Alt) : "нет";
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "[{0} | {1} | {2}] {3} (Пилот: {4}) | t=[{5:F1}..{6:F1}] | точек={7} | старт={8} -> финиш={9}",
                e.Type, e.Coalition, e.RoleTag ?? "-", e.Name, e.Pilot ?? "-", e.FirstTime, e.LastTime, e.Track.Count, pStart, pEnd));
        }
        sb.AppendLine();
        sb.AppendLine("--- КЛЮЧЕВЫЕ БОЕВЫЕ СОБЫТИЯ (ПУСКИ / ПОПАДАНИЯ / ПОВРЕЖДЕНИЯ / ГИБЕЛЬ) ---");
        foreach (TacticalEvent ev in s.Events)
        {
            if (ev.Kind == "RADAR") continue;
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "[{0:F1}s] [{1}/{2}] {3} | pos=({4:F0}, {5:F0}) | {6}",
                ev.Time, ev.Kind, ev.Severity, ev.Title, ev.WorldX, ev.WorldY, ev.Description));
        }
        sb.AppendLine();
        sb.AppendLine("--- ПОЛНАЯ ХРОНОЛОГИЯ СОБЫТИЙ (" + s.Events.Count + ") ---");
        foreach (TacticalEvent ev in s.Events)
        {
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "[{0:F1}s] [{1}/{2}] {3} | pos=({4:F0}, {5:F0}) | {6}",
                ev.Time, ev.Kind, ev.Severity, ev.Title, ev.WorldX, ev.WorldY, ev.Description));
        }
        return sb.ToString();
    }

    private static void ComputeSessionBounds(DebriefSession s)
    {
        if (s == null) return;
        foreach (DebriefEntity ent in s.Entities)
        {
            if (ent.Type == "Ground") continue;
            for (int i = 0; i < ent.Track.Count; i++)
                s.ExpandBounds(ent.Track[i].X, ent.Track[i].Y);
        }
        foreach (TacticalEvent ev in s.Events)
        {
            if (Math.Abs(ev.WorldX) > 1.0 || Math.Abs(ev.WorldY) > 1.0)
                s.ExpandBounds(ev.WorldX, ev.WorldY);
        }
        if (s.BoundsMinX > s.BoundsMaxX)
        {
            s.BoundsMinX = 0; s.BoundsMaxX = 60000;
            s.BoundsMinY = 0; s.BoundsMaxY = 60000;
        }
    }

    private static bool IsEnemyUnitType(string unitNameOrType)
    {
        if (string.IsNullOrEmpty(unitNameOrType)) return false;
        return Regex.IsMatch(unitNameOrType,
            @"\b(?:ASF-|B-11|GAV-|AEW-50|EUCAV|Manta|SAAW|IRAPC|MBT2-E|NMSS|DMS|DroneGunBoat|SamBattery|SamFCR|Enemy|FLKM)\b",
            RegexOptions.IgnoreCase);
    }

    private static bool IsGroundOrSeaUnit(string unitNameOrType)
    {
        if (string.IsNullOrEmpty(unitNameOrType)) return false;
        return Regex.IsMatch(unitNameOrType,
            @"\b(?:SAM|Radar|Phalanx|MK29|C-RAM|Tank|MBT|IRAPC|SAAW|Artillery|Cruiser|Carrier|Boat|Soldier|Truck|MANPADS|Infantry|VLS|Launcher|Turret|CIWS|MainGun|Ship)\b",
            RegexOptions.IgnoreCase);
    }

    private static string DetectUnitRole(string unitNameOrType)
    {
        if (string.IsNullOrEmpty(unitNameOrType)) return "Fighter";
        if (Regex.IsMatch(unitNameOrType, @"(?:Overlord|E-4|AWACS|AEW)", RegexOptions.IgnoreCase)) return "AWACS";
        if (Regex.IsMatch(unitNameOrType, @"(?:KC-49|Tanker)", RegexOptions.IgnoreCase)) return "Tanker";
        if (Regex.IsMatch(unitNameOrType, @"(?:B-11|Bomber)", RegexOptions.IgnoreCase)) return "Bomber";
        if (Regex.IsMatch(unitNameOrType, @"(?:SAM|Radar|Patriot|SAAW)", RegexOptions.IgnoreCase)) return "SAM";
        return "Fighter";
    }
    private static string StripBracketSuffix(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        string res = s.Trim();
        int idxBr = res.IndexOf('[');
        if (idxBr > 0) res = res.Substring(0, idxBr).Trim();
        int idxPar = res.IndexOf('(');
        if (idxPar > 0) res = res.Substring(0, idxPar).Trim();
        return res;
    }
    private static string ExtractBracketId(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        int a = s.IndexOf('[');
        int b = s.IndexOf(']', a + 1);
        if (a >= 0 && b > a + 1) return s.Substring(a + 1, b - a - 1);
        return "";
    }
    private static double ParseDoubleSafe(string s, double defVal = 0.0)
    {
        if (string.IsNullOrEmpty(s)) return defVal;
        double v;
        if (double.TryParse(s.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
            return v;
        return defVal;
    }
    // =====================================================================
    // === ПОИСК ФАЙЛОВ КАРТ/СЦЕНАРИЕВ (.vts / .vtm) =======================
    // =====================================================================
    private static string FindMapFolder(string mapId, string vtsPath)
    {
        if (string.IsNullOrEmpty(mapId)) return null;
        List<string> candidates = new List<string>();
        if (!string.IsNullOrEmpty(vtsPath))
        {
            string vtsDir = Path.GetDirectoryName(vtsPath);
            candidates.Add(Path.Combine(vtsDir, mapId));
            candidates.Add(Path.Combine(Path.GetFullPath(Path.Combine(vtsDir, "..")), mapId));
        }
        string root = FindVtolRootDir();
        if (!string.IsNullOrEmpty(root))
        {
            candidates.Add(Path.Combine(root, "CustomMaps", mapId));
            candidates.Add(Path.Combine(root, "CustomScenarios", mapId));
            string wsDir = Path.GetFullPath(Path.Combine(root, @"....\workshop\content\667970"));
            if (Directory.Exists(wsDir))
            {
                try
                {
                    foreach (string sub in Directory.GetDirectories(wsDir))
                    {
                        string mSub = Path.Combine(sub, mapId);
                        if (Directory.Exists(mSub)) candidates.Add(mSub);
                    }
                }
                catch { }
            }
        }
        foreach (string c in candidates)
            if (Directory.Exists(c)) return c;
        return null;
    }
    private static string FindVtsByScenarioId(string scenarioId)
    {
        if (string.IsNullOrEmpty(scenarioId)) return null;
        string root = FindVtolRootDir();
        if (string.IsNullOrEmpty(root)) return null;
        List<string> searchRoots = new List<string>();
        searchRoots.Add(Path.Combine(root, "CustomScenarios"));
        string wsDir = Path.GetFullPath(Path.Combine(root, @"....\workshop\content\667970"));
        if (Directory.Exists(wsDir)) searchRoots.Add(wsDir);
        foreach (string sr in searchRoots)
        {
            if (!Directory.Exists(sr)) continue;
            try
            {
                foreach (string f in Directory.GetFiles(sr, "*.vts", SearchOption.AllDirectories))
                {
                    if (Path.GetFileNameWithoutExtension(f).Equals(scenarioId, StringComparison.OrdinalIgnoreCase))
                        return f;
                }
            }
            catch { }
        }
        return null;
    }
    private static void BuildTerrainFromMapFolder(DebriefSession session, string mapFolder, string mapId)
    {
        if (string.IsNullOrEmpty(mapFolder) || !Directory.Exists(mapFolder)) return;
        int mapChunks = 64;
        string biome = "Boreal";
        string vtmFile = Path.Combine(mapFolder, mapId + ".vtm");
        if (!File.Exists(vtmFile))
        {
            string[] vtms = Directory.GetFiles(mapFolder, ".vtm");
            if (vtms.Length > 0) vtmFile = vtms[0];
        }
        if (File.Exists(vtmFile))
        {
            string txt = File.ReadAllText(vtmFile, Encoding.UTF8);
            Match mSz = Regex.Match(txt, @"mapSize\s=\s*(\d+)");
            if (mSz.Success) int.TryParse(mSz.Groups[1].Value, out mapChunks);
            Match mBio = Regex.Match(txt, @"biome\s*=\s*([^\r\n]+)");
            if (mBio.Success) biome = mBio.Groups[1].Value.Trim();
        }
        double totalMeters = Math.Max(16, mapChunks) * 3072.0;
        session.MapMinX = 0;
        session.MapMinY = 0;
        session.MapSizeMeters = totalMeters;
        session.MapTitle = string.Format("{0} ({1:F0}x{1:F0} км, {2})", mapId, totalMeters / 1000.0, biome);
        string[] heightCandidates = new string[] { "height.png", "heightmap.png", "preview.png", "map.png" };
        string hPath = null;
        foreach (string hc in heightCandidates)
        {
            string p = Path.Combine(mapFolder, hc);
            if (File.Exists(p)) { hPath = p; break; }
        }
        if (hPath == null)
        {
            string[] pngs = Directory.GetFiles(mapFolder, "*.png");
            if (pngs.Length > 0) hPath = pngs[0];
        }
        if (hPath != null && File.Exists(hPath))
        {
            try
            {
                using (Bitmap raw = new Bitmap(hPath))
                {
                    int sz = 512;
                    Bitmap bmp = new Bitmap(sz, sz, PixelFormat.Format32bppArgb);
                    using (Bitmap scaled = new Bitmap(raw, new Size(sz, sz)))
                    {
                        bool isDesert = biome.IndexOf("Desert", StringComparison.OrdinalIgnoreCase) >= 0;
                        bool isArctic = biome.IndexOf("Arctic", StringComparison.OrdinalIgnoreCase) >= 0;
                        for (int y = 0; y < sz; y++)
                        {
                            for (int x = 0; x < sz; x++)
                            {
                                Color c = scaled.GetPixel(x, y);
                                int elev = c.R;
                                Color outCol;
                                if (elev < 28)
                                    outCol = Color.FromArgb(255, 11, 24, 34);
                                else if (elev < 35)
                                    outCol = Color.FromArgb(255, 22, 54, 66);
                                else
                                {
                                    bool isContour = ((elev % 24) == 0);
                                    double hNorm = Math.Min(1.0, (elev - 35) / 220.0);
                                    int r, g, b;
                                    if (isDesert)
                                    {
                                        r = (int)(46 + hNorm * 58);
                                        g = (int)(40 + hNorm * 46);
                                        b = (int)(28 + hNorm * 30);
                                    }
                                    else if (isArctic)
                                    {
                                        r = (int)(48 + hNorm * 70);
                                        g = (int)(58 + hNorm * 75);
                                        b = (int)(68 + hNorm * 80);
                                    }
                                    else
                                    {
                                        r = (int)(22 + hNorm * 42);
                                        g = (int)(44 + hNorm * 54);
                                        b = (int)(30 + hNorm * 36);
                                    }
                                    if (isContour)
                                    {
                                        r = Math.Min(255, r + 24);
                                        g = Math.Min(255, g + 34);
                                        b = Math.Min(255, b + 24);
                                    }
                                    outCol = Color.FromArgb(255, r, g, b);
                                }
                                bmp.SetPixel(x, sz - 1 - y, outCol);
                            }
                        }
                    }
                    session.TerrainBitmap = bmp;
                }
            }
            catch { }
        }
    }
    private static void BuildProceduralTacticalTerrain(DebriefSession session, double centerX, double centerY, double sizeMeters, int seed, bool isDesert)
    {
        int sz = 420;
        Bitmap bmp = new Bitmap(sz, sz, PixelFormat.Format32bppArgb);
        session.MapMinX = centerX - sizeMeters * 0.5;
        session.MapMinY = centerY - sizeMeters * 0.5;
        session.MapSizeMeters = sizeMeters;
        double p1 = (seed % 100) * 0.07;
        double p2 = ((seed / 100) % 100) * 0.11;
        for (int py = 0; py < sz; py++)
        {
            double ny = (double)py / sz;
            for (int px = 0; px < sz; px++)
            {
                double nx = (double)px / sz;
                double h = 0.52
                + 0.24 * Math.Sin(nx * 5.2 + p1) * Math.Cos(ny * 4.7 + p2)
                + 0.14 * Math.Sin((nx + ny) * 11.0 + p2)
                + 0.08 * Math.Cos((nx * 19.0 - ny * 17.0) + p1);
                double waterLine = isDesert ? 0.26 : 0.44;
                Color outCol;
                if (h < waterLine)
                    outCol = Color.FromArgb(255, 11, 24, 34);
                else if (h < waterLine + 0.035)
                    outCol = Color.FromArgb(255, 22, 56, 68);
                else
                {
                    double elev = Math.Min(1.0, (h - waterLine) / (1.0 - waterLine));
                    int band = (int)(elev * 9.0);
                    bool contour = Math.Abs(elev * 9.0 - band) < 0.08;
                    int r = isDesert ? (int)(44 + elev * 52) : (int)(20 + elev * 38);
                    int g = isDesert ? (int)(38 + elev * 42) : (int)(40 + elev * 52);
                    int b = isDesert ? (int)(28 + elev * 28) : (int)(28 + elev * 34);
                    if (contour) { r += 22; g += 30; b += 22; }
                    outCol = Color.FromArgb(255, Math.Min(255, r), Math.Min(255, g), Math.Min(255, b));
                }
                bmp.SetPixel(px, sz - 1 - py, outCol);
            }
        }
        session.TerrainBitmap = bmp;
    }
    private static void ParseVtsScenarioLayout(DebriefSession session, string vtsPath, out double playerSpawnX, out double playerSpawnY, out double playerHeading)
    {
        playerSpawnX = 50000;
        playerSpawnY = 50000;
        playerHeading = 0;
        if (string.IsNullOrEmpty(vtsPath) || !File.Exists(vtsPath)) return;
        string text = File.ReadAllText(vtsPath, Encoding.UTF8);
        Match mTitle = Regex.Match(text, @"(?m)^\sscenarioName\s=\s*(.+)$");
        if (mTitle.Success) session.Title = mTitle.Groups[1].Value.Trim();
        Match mMap = Regex.Match(text, @"(?m)^\smapID\s=\s*(.+)$");
        string mapId = mMap.Success ? mMap.Groups[1].Value.Trim() : "";
        if (!string.IsNullOrEmpty(mapId))
        {
            string mapFolder = FindMapFolder(mapId, vtsPath);
            BuildTerrainFromMapFolder(session, mapFolder, mapId);
        }
        MatchCollection mcUnits = Regex.Matches(text, @"UnitSpawner\s*{([^{}](?:{[^{}]}[^{}]))}");
        foreach (Match mu in mcUnits)
        {
            string block = mu.Groups[1].Value;
            Match mName = Regex.Match(block, @"(?m)^\sunitName\s=\s*(.+)$");
            Match mId = Regex.Match(block, @"(?m)^\sunitID\s=\s*(.+)$");
            Match mPos = Regex.Match(block, @"globalPosition\s*=\s*([-\d\.E\+]+),\s*([-\d\.E\+]+),\s*([-\d\.E\+]+)");
            Match mRot = Regex.Match(block, @"rotation\s*=\s*([-\d\.E\+]+),\s*([-\d\.E\+]+),\s*([-\d\.E\+]+)");
            Match mGrp = Regex.Match(block, @"(?m)^\sunitGroup\s=\s*(.+)$");
            if (!mPos.Success) continue;
            double ux = ParseDoubleSafe(mPos.Groups[1].Value);
            double ualt = ParseDoubleSafe(mPos.Groups[2].Value);
            double uz = ParseDoubleSafe(mPos.Groups[3].Value);
            double uyaw = mRot.Success ? ParseDoubleSafe(mRot.Groups[2].Value) : 0;
            string uName = mName.Success ? mName.Groups[1].Value.Trim() : "Unit";
            string uTypeId = mId.Success ? mId.Groups[1].Value.Trim() : "";
            string uGroup = mGrp.Success ? mGrp.Groups[1].Value.Trim() : "";
            if (uTypeId.Equals("PlayerSpawn", StringComparison.OrdinalIgnoreCase))
            {
                playerSpawnX = ux;
                playerSpawnY = uz;
                playerHeading = uyaw;
                session.MapMarkers.Add(new MapPoiMarker
                {
                    Kind = "Base",
                    Label = "ТОЧКА СТАРТА",
                    Coalition = "Allies",
                    X = ux,
                    Y = uz
                });
            }
            else if (uTypeId.IndexOf("Carrier", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                session.MapMarkers.Add(new MapPoiMarker
                {
                    Kind = "Carrier",
                    Label = uName,
                    Coalition = "Allies",
                    X = ux,
                    Y = uz
                });
            }
            else if (uTypeId.IndexOf("Sam", StringComparison.OrdinalIgnoreCase) >= 0 ||
            uTypeId.IndexOf("Patriot", StringComparison.OrdinalIgnoreCase) >= 0 ||
            uTypeId.IndexOf("SAAW", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                bool isAllied = uGroup.StartsWith("Allied", StringComparison.OrdinalIgnoreCase) ||
                uTypeId.IndexOf("Patriot", StringComparison.OrdinalIgnoreCase) >= 0;
                session.MapMarkers.Add(new MapPoiMarker
                {
                    Kind = "SAM",
                    Label = uName,
                    Coalition = isAllied ? "Allies" : "Enemies",
                    X = ux,
                    Y = uz,
                    RadiusMeters = uTypeId.IndexOf("SAAW", StringComparison.OrdinalIgnoreCase) >= 0 ? 4500 : 18000
                });
            }
            else if (uGroup.StartsWith("Enemy", StringComparison.OrdinalIgnoreCase) && ualt > 200)
            {
                DebriefEntity enemy = new DebriefEntity
                {
                    Id = "vts_" + session.Entities.Count,
                    Name = uName,
                    Pilot = uGroup,
                    Type = "Air",
                    Coalition = "Enemies",
                    RoleTag = DetectUnitRole(uName),
                    FirstTime = 0,
                    LastTime = 9999
                };
                enemy.Track.Add(new DebriefSample { Time = 0, X = ux, Y = uz, Alt = ualt, Heading = uyaw, Speed = 240 });
                session.Entities.Add(enemy);
            }
        }
        MatchCollection mcWpt = Regex.Matches(text, @"WAYPOINT\s*{([^{}])}");
        foreach (Match mw in mcWpt)
        {
            string wb = mw.Groups[1].Value;
            Match mWn = Regex.Match(wb, @"(?m)^\sname\s*=\s*(.+)$");
            Match mWp = Regex.Match(wb, @"globalPoint\s*=\s*([-\d\.E\+]+),\s*([-\d\.E\+]+),\s*([-\d\.E\+]+)");
            if (mWp.Success)
            {
                double wx = ParseDoubleSafe(mWp.Groups[1].Value);
                double wz = ParseDoubleSafe(mWp.Groups[3].Value);
                session.MapMarkers.Add(new MapPoiMarker
                {
                    Kind = "Waypoint",
                    Label = mWn.Success ? mWn.Groups[1].Value.Trim() : "WPT",
                    Coalition = "Allies",
                    X = wx,
                    Y = wz
                });
            }
        }
    }
    // =====================================================================
    // === ПАРСЕР РЕПЛЕЯ (.vtolrep): ПРЯМОЙ ТРЕК (POS|) + ЛОГБУК + ЛОГ =====
    // =====================================================================
    private class LogActorRecord
    {
        public string Name;
        public string UnitType;
        public string Coalition;
        public string RoleTag;
        public bool IsGround;
        public double SpawnTime;
        public double DeathTime = -1;
        public string KilledBy = "";
        public bool HasVelocity;
        public double Vx, Vy, Vz;
        public DebriefEntity Entity;
    }
    private class AiMissileEvent
    {
        public double Time;
        public string MissileAndShooter;
        public string TargetName;
    }
    private class RadarPingEvent
    {
        public double Time;
        public string Kind;
        public string Source;
        public string Target;
    }
    private static DebriefSession LoadVtolRep(string filePath, string preferredPilot)
    {
        DebriefSession session = new DebriefSession();
        session.SourceFile = filePath;
        session.Title = Path.GetFileNameWithoutExtension(filePath);
        session.HasFullRadarCoords = true;
        DebriefEntity player = new DebriefEntity
        {
            Id = "player",
            Name = "Player",
            Pilot = preferredPilot ?? "Pilot",
            Type = "Air",
            Coalition = "Allies",
            RoleTag = "Fighter",
            IsPlayer = true
        };
        session.Entities.Add(player);
        string vtsPath = "";
        string campaignId = "";
        string scenarioId = "";
        double maxT = 15.0;
        string deathCauseCode = "";
        bool hasDirectPosTelemetry = false;
        Dictionary<string, DebriefEntity> posEntities = new Dictionary<string, DebriefEntity>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, LogActorRecord> actors = new Dictionary<string, LogActorRecord>(StringComparer.OrdinalIgnoreCase);
        List<AiMissileEvent> aiMissiles = new List<AiMissileEvent>();
        List<RadarPingEvent> radarPings = new List<RadarPingEvent>();
        List<double> threatLockTimes = new List<double>();
        List<string> threatLockDetails = new List<string>();
        foreach (string raw in File.ReadAllLines(filePath, Encoding.UTF8))
        {
            string rawLineCopy = raw;
            string[] p = raw.Split('|');
            if (p.Length < 3) continue;
            double t = ParseDoubleSafe(p[0], 0.0);
            if (t > maxT) maxT = t;
            string evType = p[1];
            string arg1 = p.Length > 2 ? p[2] : "";
            string arg2 = p.Length > 3 ? p[3] : "";
            // Прямая 3D-телеметрия от трек-логгера игры:
            // t|POS|id|name|type|coalition|x|alt|z|hdg|pitch|roll|spd
            if (evType == "POS" && p.Length >= 13)
            {
                hasDirectPosTelemetry = true;
                string uid = p[2];
                string uname = p[3];
                string utype = p[4];
                string ucoal = p[5];
                double wx = ParseDoubleSafe(p[6]);
                double alt = ParseDoubleSafe(p[7]);
                double wy = ParseDoubleSafe(p[8]);
                double hdg = ParseDoubleSafe(p[9]);
                double pitch = ParseDoubleSafe(p[10]);
                double roll = ParseDoubleSafe(p[11]);
                double spd = ParseDoubleSafe(p[12]);
                DebriefEntity ent;
                if (uid.Equals("player", StringComparison.OrdinalIgnoreCase))
                {
                    ent = player;
                    if (!string.IsNullOrEmpty(uname)) ent.Name = uname;
                }
                else if (!posEntities.TryGetValue(uid, out ent))
                {
                    ent = new DebriefEntity
                    {
                        Id = uid,
                        Name = uname,
                        Pilot = uname,
                        Type = utype,
                        Coalition = ucoal,
                        RoleTag = DetectUnitRole(uname)
                    };
                    posEntities[uid] = ent;
                    session.Entities.Add(ent);
                }
                ent.Track.Add(new DebriefSample
                {
                    Time = t,
                    X = wx,
                    Y = wy,
                    Alt = alt,
                    Heading = hdg,
                    Pitch = pitch,
                    Roll = roll,
                    Speed = spd
                });
                if (t < ent.FirstTime) ent.FirstTime = t;
                if (t > ent.LastTime) ent.LastTime = t;
                continue;
            }
            if (evType == "META")
            {
                if (!string.IsNullOrEmpty(arg1)) { player.Pilot = arg1; session.PilotName = arg1; }
                if (!string.IsNullOrEmpty(arg2)) player.Name = arg2;
            }
            else if (evType == "VTS")
            {
                vtsPath = arg1;
            }
            else if (evType == "SCENARIO")
            {
                campaignId = arg1;
                scenarioId = arg2;
                if (!string.IsNullOrEmpty(campaignId) || !string.IsNullOrEmpty(scenarioId))
                    session.Title = (campaignId + " " + scenarioId).Trim();
            }
            else if (evType == "ACTOR_REG")
            {
                if (!actors.ContainsKey(arg1))
                {
                    bool isEnemy = arg2.Equals("EnemyPlayer", StringComparison.OrdinalIgnoreCase) ||
                                   IsEnemyUnitType(arg1) || IsEnemyUnitType(arg2);
                    actors[arg1] = new LogActorRecord
                    {
                        Name = arg1,
                        UnitType = arg2,
                        Coalition = isEnemy ? "Enemies" : "Allies",
                        RoleTag = DetectUnitRole(arg1 + " " + arg2),
                        IsGround = IsGroundOrSeaUnit(arg1 + " " + arg2),
                        SpawnTime = t
                    };
                }
            }
            else if (evType == "UNIT_SPAWN")
            {
                LogActorRecord rec;
                if (!actors.TryGetValue(arg1, out rec))
                {
                    bool isEnemy = IsEnemyUnitType(arg1);
                    rec = new LogActorRecord
                    {
                        Name = arg1,
                        UnitType = arg1,
                        Coalition = isEnemy ? "Enemies" : "Allies",
                        RoleTag = DetectUnitRole(arg1),
                        IsGround = false,
                        SpawnTime = t
                    };
                    actors[arg1] = rec;
                }
                string[] vp = arg2.Split(';');
                if (vp.Length >= 3)
                {
                    rec.Vx = ParseDoubleSafe(vp[0]);
                    rec.Vy = ParseDoubleSafe(vp[1]);
                    rec.Vz = ParseDoubleSafe(vp[2]);
                    rec.HasVelocity = true;
                    rec.SpawnTime = t;
                }
            }
            else if (evType == "RADAR_DETECT")
            {
                bool isPlayerRadar = arg1.Equals("PLAYER", StringComparison.OrdinalIgnoreCase);
                radarPings.Add(new RadarPingEvent
                {
                    Time = t,
                    Kind = isPlayerRadar ? "PLAYER_RADAR" : "AWACS_LINK",
                    Source = isPlayerRadar ? player.Name : arg1,
                    Target = arg2
                });
                session.Events.Add(new TacticalEvent
                {
                    Time = t,
                    Kind = "RADAR",
                    Severity = "INFO",
                    Title = (isPlayerRadar ? "Радар БРЛС: контакт " : ("Даталинк " + arg1 + ": ")) + arg2,
                    Description = isPlayerRadar
                ? ("Бортовой радар обнаружил цель: " + arg2)
                : string.Format("Самолёт ДРЛО {0} передал целеуказание по цели {1}", arg1, arg2)
                });
            }
            else if (evType == "RWR_DETECT")
            {
                session.RwrSpikesCount++;
                radarPings.Add(new RadarPingEvent
                {
                    Time = t,
                    Kind = "RWR_SPIKE",
                    Source = arg1,
                    Target = player.Name
                });
                session.Events.Add(new TacticalEvent
                {
                    Time = t,
                    Kind = "RWR",
                    Severity = "WARNING",
                    Title = "СПО (RWR): облучение от " + arg1,
                    Description = "Радар противника (" + arg1 + ") засветил ваш борт.",
                    Advice = "Вы находитесь в зоне обнаружения РЛС противника. Следите за сменой тона СПО на захват/пуск."
                });
            }
            else if (evType == "RADAR_LOCK")
            {
                radarPings.Add(new RadarPingEvent
                {
                    Time = t,
                    Kind = "PLAYER_LOCK",
                    Source = player.Name,
                    Target = arg1
                });
                session.Events.Add(new TacticalEvent
                {
                    Time = t,
                    Kind = "RADAR",
                    Severity = "INFO",
                    Title = "Захват цели (TWS/STT): " + arg1,
                    Description = "Цель " + arg1 + " взята на сопровождение БРЛС."
                });
            }
            else if (evType == "AI_MSL_LOCK")
            {
                aiMissiles.Add(new AiMissileEvent
                {
                    Time = t,
                    MissileAndShooter = arg1,
                    TargetName = arg2
                });
            }
            else if (evType == "THREAT_LOCK")
            {
                session.ThreatMissilesCount++;
                threatLockTimes.Add(t);
                threatLockDetails.Add(arg1 + " от " + arg2);
                aiMissiles.Add(new AiMissileEvent
                {
                    Time = t,
                    MissileAndShooter = arg1 + " (" + arg2 + ")",
                    TargetName = player.Name
                });
                radarPings.Add(new RadarPingEvent
                {
                    Time = t,
                    Kind = "RWR_SPIKE",
                    Source = arg2,
                    Target = player.Name
                });
                session.Events.Add(new TacticalEvent
                {
                    Time = t,
                    Kind = "THREAT",
                    Severity = "CRITICAL",
                    Title = "ПУСК/ЗАХВАТ РАКЕТОЙ: " + arg1 + " (" + arg2 + ")",
                    Description = string.Format("ГСН вражеской ракеты {0} ({1}) перешла в активное самонаведение на ваш борт!", arg1, arg2),
                    Advice = "Немедленно ставьте ракету на 3–9 часов (Notch 90°), снижайтесь к рельефу и отстреливайте дипольные отражатели (Chaff)."
                });
            }
            else if (evType == "DAMAGE")
            {
                bool isPlayerDmg = arg1.Equals("PLAYER", StringComparison.OrdinalIgnoreCase);
                session.Events.Add(new TacticalEvent
                {
                    Time = t,
                    Kind = "DAMAGE",
                    Severity = isPlayerDmg ? "CRITICAL" : "INFO",
                    Title = arg2,
                    Description = isPlayerDmg ? "Критическое повреждение силовой установки вашего борта!" : ("Повреждение юнита: " + arg2)
                });
            }
            else if (evType == "PART_LOSS")
            {
                session.Events.Add(new TacticalEvent
                {
                    Time = t,
                    Kind = "DAMAGE",
                    Severity = "WARNING",
                    Title = arg1,
                    Description = "Зафиксирован отрыв элемента конструкции или вооружения при поражении: " + arg1
                });
            }
            else if (evType == "TAKEOFF")
            {
                session.Events.Add(new TacticalEvent
                {
                    Time = t,
                    Kind = "INFO",
                    Severity = "INFO",
                    Title = "Взлёт (" + arg1 + ")",
                    Description = "Борт оторвался от полосы / палубы: " + arg1
                });
            }
            else if (evType == "LANDED")
            {
                session.Events.Add(new TacticalEvent
                {
                    Time = t,
                    Kind = "INFO",
                    Severity = "GOOD",
                    Title = "Посадка (" + arg1 + ")",
                    Description = "Успешное приземление: " + arg1
                });
            }
            else if (evType == "OBJECTIVE")
            {
                bool ok = arg1.Equals("Completed", StringComparison.OrdinalIgnoreCase);
                session.Events.Add(new TacticalEvent
                {
                    Time = t,
                    Kind = "INFO",
                    Severity = ok ? "GOOD" : "WARNING",
                    Title = (ok ? "Задача выполнена: " : "Задача провалена: ") + arg2,
                    Description = arg2
                });
            }
            else if (evType == "LAUNCH")
            {
                session.PlayerLaunches++;
                session.Events.Add(new TacticalEvent
                {
                    Time = t,
                    Kind = "LAUNCH",
                    Severity = "INFO",
                    Title = "Пуск " + arg1,
                    Description = string.Format("Пилот выполнил пуск ракеты {0}", arg1)
                });
            }
            else if (evType == "KILL")
            {
                session.PlayerKills++;
                MarkActorKilled(actors, arg1, t, player.Name);
                session.Events.Add(new TacticalEvent
                {
                    Time = t,
                    Kind = "KILL",
                    Severity = "GOOD",
                    Title = "Сбит: " + arg1,
                    Description = string.Format("Цель {0} уничтожена ({1})", arg1, string.IsNullOrEmpty(arg2) ? "АСП" : arg2)
                });
            }
            else if (evType == "WORLD_KILL")
            {
                session.AlliedKills++;
                MarkActorKilled(actors, arg1, t, arg2);
                session.Events.Add(new TacticalEvent
                {
                    Time = t,
                    Kind = "INFO",
                    Severity = "INFO",
                    Title = string.Format("Бой ИИ: {0} сбит ({1})", arg1, arg2),
                    Description = string.Format("Юнит {0} уничтожен огнём {1}", arg1, arg2)
                });
            }
            else if (evType == "KIA" || evType == "EJECT")
            {
                deathCauseCode = arg2;
                session.Events.Add(new TacticalEvent
                {
                    Time = t,
                    Kind = evType,
                    Severity = "CRITICAL",
                    Title = evType == "EJECT" ? "Катапультирование" : ("Борт потерян: " + arg1),
                    Description = arg1
                });
            }
        }
        session.Duration = maxT;
        session.HasExactTelemetry = hasDirectPosTelemetry;
        session.IsReconstructedFromLog = !hasDirectPosTelemetry;
        if (string.IsNullOrEmpty(vtsPath) && !string.IsNullOrEmpty(scenarioId))
            vtsPath = FindVtsByScenarioId(scenarioId) ?? "";
        double baseX = 48000, baseY = 48000, startHdg = 35.0;
        if (!string.IsNullOrEmpty(vtsPath) && File.Exists(vtsPath))
            ParseVtsScenarioLayout(session, vtsPath, out baseX, out baseY, out startHdg);
        if (hasDirectPosTelemetry)
        {
            // Привязываем координаты событий к реальному треку игрока/цели
            foreach (TacticalEvent ev in session.Events)
            {
                DebriefSample ps = player.GetStateAt(ev.Time);
                if (ps != null) { ev.WorldX = ps.X; ev.WorldY = ps.Y; }
            }
            BuildRadarBeamsFromEntities(session, player, radarPings);
            if (session.TerrainBitmap == null)
            {
                ComputeSessionBounds(session);
                double cx = (session.BoundsMinX + session.BoundsMaxX) * 0.5;
                double cy = (session.BoundsMinY + session.BoundsMaxY) * 0.5;
                bool isDesert = (campaignId + " " + session.Title).IndexOf("desert", StringComparison.OrdinalIgnoreCase) >= 0;
                BuildProceduralTacticalTerrain(session, cx, cy, 140000.0, Math.Abs(session.Title.GetHashCode()), isDesert);
                session.MapTitle = "Тактическая карта (Точный 3D-трек)";
            }
        }
        else
        {
            session.HasFullRadarCoords = false;
            session.HasExactTelemetry = false;
            session.IsReconstructedFromLog = true;
            session.MapTitle = "Только лог событий (нет 3D-координат)";
        }
        AnalyzeReconstructedSession(session, player, threatLockTimes, threatLockDetails, deathCauseCode);
        return session;
    }
    private static void MarkActorKilled(Dictionary<string, LogActorRecord> actors, string cleanTargetName, double killTime, string killer)
    {
        foreach (KeyValuePair<string, LogActorRecord> kv in actors)
        {
            if (kv.Value.DeathTime < 0 &&
            (kv.Key.Equals(cleanTargetName, StringComparison.OrdinalIgnoreCase) ||
            kv.Key.StartsWith(cleanTargetName + " ", StringComparison.OrdinalIgnoreCase)))
            {
                kv.Value.DeathTime = killTime;
                kv.Value.KilledBy = killer;
                break;
            }
        }
    }
    private static DebriefEntity FindEntityByFuzzyName(DebriefSession session, string queryName)
    {
        if (string.IsNullOrEmpty(queryName)) return null;
        foreach (DebriefEntity e in session.Entities)
            if (e.Name.Equals(queryName, StringComparison.OrdinalIgnoreCase)) return e;
        foreach (DebriefEntity e in session.Entities)
            if (e.Name.IndexOf(queryName, StringComparison.OrdinalIgnoreCase) >= 0 ||
            queryName.IndexOf(e.Name, StringComparison.OrdinalIgnoreCase) >= 0)
                return e;
        return null;
    }
    private static void BuildRadarBeamsFromEntities(DebriefSession session, DebriefEntity player, List<RadarPingEvent> radarPings)
    {
        foreach (RadarPingEvent rp in radarPings)
        {
            DebriefEntity srcEnt = rp.Source.Equals(player.Name, StringComparison.OrdinalIgnoreCase)
            ? player
            : FindEntityByFuzzyName(session, rp.Source);
            DebriefEntity dstEnt = rp.Target.Equals(player.Name, StringComparison.OrdinalIgnoreCase)
            ? player
            : FindEntityByFuzzyName(session, rp.Target);
            DebriefSample s1 = srcEnt != null ? srcEnt.GetStateAt(rp.Time) : null;
            DebriefSample s2 = dstEnt != null ? dstEnt.GetStateAt(rp.Time) : null;
            if (s1 != null && s2 != null)
            {
                session.Beams.Add(new TacticalBeam
                {
                    StartTime = rp.Time,
                    EndTime = rp.Time + (rp.Kind == "PLAYER_LOCK" || rp.Kind == "RWR_SPIKE" ? 12.0 : 6.0),
                    Kind = rp.Kind,
                    FromName = srcEnt.Name,
                    ToName = dstEnt.Name,
                    FromX = s1.X,
                    FromY = s1.Y,
                    ToX = s2.X,
                    ToY = s2.Y
                });
            }
        }
    }
    private static void ReconstructFullBattlefield(
    DebriefSession session,
    DebriefEntity player,
    double startX,
    double startY,
    double startHdg,
    Dictionary<string, LogActorRecord> actors,
        List<AiMissileEvent> aiMissiles,
    List<RadarPingEvent> radarPings)
    {
        double combatBearingRad = startHdg * Math.PI / 180.0;
        foreach (KeyValuePair<string, LogActorRecord> kv in actors)
        {
            LogActorRecord rec = kv.Value;
            if (rec.Coalition == "Enemies" && rec.HasVelocity && (Math.Abs(rec.Vx) > 1 || Math.Abs(rec.Vz) > 1))
            {
                combatBearingRad = Math.Atan2(-rec.Vx, -rec.Vz);
                break;
            }
        }
        double combatZoneX = startX + Math.Sin(combatBearingRad) * 36000.0;
        double combatZoneY = startY + Math.Cos(combatBearingRad) * 36000.0;
        session.MapMarkers.Add(new MapPoiMarker
        {
            Kind = "Bullseye",
            Label = "BULLSEYE / ЗОНА БОЯ",
            Coalition = "Neutral",
            X = combatZoneX,
            Y = combatZoneY
        });
        int enemyIdx = 0;
        int allyAirIdx = 0;
        int samIdx = 0;
        foreach (KeyValuePair<string, LogActorRecord> kv in actors)
        {
            LogActorRecord rec = kv.Value;
            double endT = rec.DeathTime > 0 ? rec.DeathTime : session.Duration;
            if (rec.IsGround)
            {
                double gx, gy;
                if (rec.Coalition == "Allies")
                {
                    double ang = combatBearingRad + ((samIdx % 3) - 1) * 0.45;
                    gx = startX + Math.Sin(ang) * (2500.0 + samIdx * 1200.0);
                    gy = startY + Math.Cos(ang) * (2500.0 + samIdx * 1200.0);
                }
                else
                {
                    double ang = combatBearingRad + ((samIdx % 5) - 2) * 0.22;
                    gx = startX + Math.Sin(ang) * (46000.0 + (samIdx % 3) * 6000.0);
                    gy = startY + Math.Cos(ang) * (46000.0 + (samIdx % 3) * 6000.0);
                }
                samIdx++;
                if (rec.RoleTag == "SAM")
                {
                    session.MapMarkers.Add(new MapPoiMarker
                    {
                        Kind = "SAM",
                        Label = rec.Name,
                        Coalition = rec.Coalition,
                        X = gx,
                        Y = gy,
                        RadiusMeters = 16000.0
                    });
                }
                DebriefEntity gEnt = new DebriefEntity
                {
                    Id = "actor_" + rec.Name,
                    Name = rec.Name,
                    Pilot = rec.UnitType,
                    Type = "Ground",
                    Coalition = rec.Coalition,
                    RoleTag = rec.RoleTag,
                    FirstTime = rec.SpawnTime,
                    LastTime = endT
                };
                gEnt.Track.Add(new DebriefSample { Time = rec.SpawnTime, X = gx, Y = gy, Alt = 15, Heading = 0, Speed = 0 });
                rec.Entity = gEnt;
                session.Entities.Add(gEnt);
                continue;
            }
            double x0, y0, alt0, vx, vz, hdg, spd;
            if (rec.Coalition == "Allies")
            {
                if (rec.RoleTag == "AWACS")
                {
                    double awAng = combatBearingRad - 0.35;
                    x0 = startX + Math.Sin(awAng) * 16000.0;
                    y0 = startY + Math.Cos(awAng) * 16000.0;
                    alt0 = 8500.0;
                    spd = 210.0;
                    hdg = (combatBearingRad * 180.0 / Math.PI + 90.0 + 360.0) % 360.0;
                    vx = Math.Sin(hdg * Math.PI / 180.0) * spd;
                    vz = Math.Cos(hdg * Math.PI / 180.0) * spd;
                }
                else if (rec.RoleTag == "Tanker")
                {
                    double tkAng = combatBearingRad + 0.42;
                    x0 = startX + Math.Sin(tkAng) * 19000.0;
                    y0 = startY + Math.Cos(tkAng) * 19000.0;
                    alt0 = 4200.0;
                    spd = 195.0;
                    hdg = (combatBearingRad * 180.0 / Math.PI - 90.0 + 360.0) % 360.0;
                    vx = Math.Sin(hdg * Math.PI / 180.0) * spd;
                    vz = Math.Cos(hdg * Math.PI / 180.0) * spd;
                }
                else
                {
                    allyAirIdx++;
                    double wingSide = (allyAirIdx % 2 == 1 ? -1.0 : 1.0);
                    double wingOff = wingSide * (3500.0 + allyAirIdx * 2000.0);
                    double trailBack = allyAirIdx * 2400.0;
                    double perpRad = combatBearingRad + Math.PI * 0.5;
                    x0 = startX + Math.Sin(perpRad) * wingOff - Math.Sin(combatBearingRad) * trailBack;
                    y0 = startY + Math.Cos(perpRad) * wingOff - Math.Cos(combatBearingRad) * trailBack;
                    alt0 = 5200.0 + allyAirIdx * 450.0;
                    spd = 275.0;
                    hdg = (combatBearingRad * 180.0 / Math.PI + 360.0) % 360.0;
                    vx = Math.Sin(combatBearingRad) * spd;
                    vz = Math.Cos(combatBearingRad) * spd;
                }
            }
            else
            {
                double spreadAngle = combatBearingRad + ((enemyIdx % 5) - 2) * 0.14;
                double distFromBase = 52000.0 + (enemyIdx / 3) * 15000.0;
                x0 = startX + Math.Sin(spreadAngle) * distFromBase;
                y0 = startY + Math.Cos(spreadAngle) * distFromBase;
                alt0 = rec.RoleTag == "Bomber" ? 7200.0 : (4600.0 + (enemyIdx % 3) * 650.0);
                if (rec.HasVelocity && (Math.Abs(rec.Vx) > 1 || Math.Abs(rec.Vz) > 1))
                {
                    vx = rec.Vx;
                    vz = rec.Vz;
                    spd = Math.Sqrt(vx * vx + vz * vz);
                    hdg = (Math.Atan2(vx, vz) * 180.0 / Math.PI + 360.0) % 360.0;
                }
                else
                {
                    spd = rec.RoleTag == "Bomber" ? 230.0 : 280.0;
                    hdg = ((combatBearingRad + Math.PI) * 180.0 / Math.PI + 360.0) % 360.0;
                    vx = Math.Sin(hdg * Math.PI / 180.0) * spd;
                    vz = Math.Cos(hdg * Math.PI / 180.0) * spd;
                }
                enemyIdx++;
            }
            DebriefEntity airEnt = new DebriefEntity
            {
                Id = "actor_" + rec.Name,
                Name = rec.Name,
                Pilot = rec.UnitType,
                Type = "Air",
                Coalition = rec.Coalition,
                RoleTag = rec.RoleTag,
                FirstTime = rec.SpawnTime,
                LastTime = endT
            };
            for (double st = rec.SpawnTime; st <= endT; st += 0.5)
            {
                double dt = st - rec.SpawnTime;
                double px, py, stepHdg = hdg;
                if (rec.RoleTag == "AWACS" || rec.RoleTag == "Tanker")
                {
                    double orbitAng = dt * 0.032;
                    px = x0 + Math.Cos(orbitAng) * 6000.0;
                    py = y0 + Math.Sin(orbitAng) * 6000.0;
                    stepHdg = (Math.Atan2(-Math.Sin(orbitAng), Math.Cos(orbitAng)) * 180.0 / Math.PI + 360.0) % 360.0;
                }
                else if (rec.Coalition == "Allies" && st > rec.SpawnTime + 115.0)
                {
                    // Ведомые истребители патрулируют зону боя разнесёнными галсами (восьмёркой), а не в одной точке
                    double phase = allyAirIdx * 2.4;
                    double patrolT = (st - rec.SpawnTime - 115.0) * 0.025 + phase;
                    double rx = 14000.0 + allyAirIdx * 3500.0;
                    double ry = 9000.0 + allyAirIdx * 2500.0;
                    px = combatZoneX + Math.Sin(patrolT) * rx;
                    py = combatZoneY + Math.Sin(patrolT * 2.0) * ry * 0.5;
                    double vxPatrol = Math.Cos(patrolT) * rx * 0.025;
                    double vyPatrol = Math.Cos(patrolT * 2.0) * ry * 0.025;
                    stepHdg = (Math.Atan2(vxPatrol, vyPatrol) * 180.0 / Math.PI + 360.0) % 360.0;
                }
                else if (rec.Coalition == "Enemies" && rec.RoleTag == "Fighter" && dt > 20.0)
                {
                    // Вражеские истребители в зоне боя выполняют отворот (Crank) и противоракетные змейки (Notch)
                    double weavePhase = enemyIdx * 1.7 + (dt - 20.0) * 0.09;
                    double weaveOffset = Math.Sin(weavePhase) * 3200.0;
                    double perpX = -vz / Math.Max(1.0, spd);
                    double perpY = vx / Math.Max(1.0, spd);
                    px = x0 + vx * dt + perpX * weaveOffset;
                    py = y0 + vz * dt + perpY * weaveOffset;
                    double dVx = vx + perpX * Math.Cos(weavePhase) * 3200.0 * 0.09;
                    double dVy = vz + perpY * Math.Cos(weavePhase) * 3200.0 * 0.09;
                    stepHdg = (Math.Atan2(dVx, dVy) * 180.0 / Math.PI + 360.0) % 360.0;
                }
                else
                {
                    px = x0 + vx * dt;
                    py = y0 + vz * dt;
                }
                airEnt.Track.Add(new DebriefSample
                {
                    Time = st,
                    X = px,
                    Y = py,
                    Alt = alt0,
                    Heading = stepHdg,
                    Speed = spd
                });
            }
            rec.Entity = airEnt;
            session.Entities.Add(airEnt);
        }

        // Строим опорные точки игрока с учётом попадания ракеты и планирования без двигателей
        double curX = startX, curY = startY, curAlt = 25.0, curHdg = startHdg, curSpd = 0.0, lastT = 0.0;
        bool enginesFailed = false;
        double engineFailTime = -1;
        double engineFailAlt = 5500.0;

        List<DebriefSample> rawKeyPoints = new List<DebriefSample>();
        rawKeyPoints.Add(new DebriefSample { Time = 0, X = curX, Y = curY, Alt = curAlt, Heading = curHdg, Speed = 0 });

        session.Events.Sort((a, b) => a.Time.CompareTo(b.Time));
        double lastLaunchTime = -1;
        double lastLaunchX = curX, lastLaunchY = curY, lastLaunchHdg = curHdg;
        int mslCounter = 0;

        double kiaTimeEnd = session.Duration;
        foreach (TacticalEvent ev in session.Events)
            if (ev.Kind == "KIA" || ev.Kind == "EJECT") { kiaTimeEnd = ev.Time; break; }

        foreach (TacticalEvent ev in session.Events)
        {
            double dt = Math.Max(0.5, ev.Time - lastT);
            if (ev.Title.StartsWith("Взлёт"))
            {
                curSpd = 155.0;
                curAlt = 400.0;
                curHdg = (combatBearingRad * 180.0 / Math.PI + 360.0) % 360.0;
                curX += Math.Sin(combatBearingRad) * 2800.0;
                curY += Math.Cos(combatBearingRad) * 2800.0;
            }
            else if (enginesFailed)
            {
                // Режим планирования без двигателей: плавное снижение к земле и потеря скорости
                double glideProgress = Math.Max(0.0, Math.Min(1.0, (ev.Time - engineFailTime) / Math.Max(5.0, kiaTimeEnd - engineFailTime)));
                curAlt = Math.Max(0.0, engineFailAlt * (1.0 - glideProgress));
                curSpd = Math.Max(95.0, 265.0 - glideProgress * 155.0);
                double glideRad = curHdg * Math.PI / 180.0 + 0.04;
                double stepDist = curSpd * Math.Min(dt, 25.0);
                curX += Math.Sin(glideRad) * stepDist;
                curY += Math.Cos(glideRad) * stepDist;
                curHdg = (glideRad * 180.0 / Math.PI + 360.0) % 360.0;
            }
            else
            {
                double distToZone = Math.Sqrt(Math.Pow(combatZoneX - curX, 2) + Math.Pow(combatZoneY - curY, 2));
                double targetHdgRad = combatBearingRad;
                if (distToZone < 14000.0)
                {
                    targetHdgRad = combatBearingRad + Math.Sin(ev.Time * 0.07) * 0.85;
                    curAlt = 5600.0 + Math.Cos(ev.Time * 0.05) * 1200.0;
                    curSpd = 280.0;
                }
                else
                {
                    targetHdgRad = Math.Atan2(combatZoneX - curX, combatZoneY - curY);
                    curAlt = Math.Min(6800.0, curAlt + dt * 28.0);
                    curSpd = Math.Min(315.0, Math.Max(215.0, curSpd + dt * 2.5));
                }
                double stepDist = Math.Min(distToZone + 8000.0, curSpd * Math.Min(dt, 45.0));
                curX += Math.Sin(targetHdgRad) * stepDist;
                curY += Math.Cos(targetHdgRad) * stepDist;
                curHdg = (targetHdgRad * 180.0 / Math.PI + 360.0) % 360.0;
            }

            if (ev.Kind == "DAMAGE" && !enginesFailed)
            {
                enginesFailed = true;
                engineFailTime = ev.Time;
                engineFailAlt = Math.Max(1500.0, curAlt);
            }

            if (ev.Kind == "KIA" && ev.Title.IndexOf("землёй", StringComparison.OrdinalIgnoreCase) >= 0)
                curAlt = 0.0;

            ev.WorldX = curX;
            ev.WorldY = curY;

            rawKeyPoints.Add(new DebriefSample
            {
                Time = ev.Time,
                X = curX,
                Y = curY,
                Alt = curAlt,
                Heading = curHdg,
                Speed = curSpd
            });

            if (ev.Kind == "LAUNCH")
            {
                lastLaunchTime = ev.Time;
                lastLaunchX = curX;
                lastLaunchY = curY;
                lastLaunchHdg = curHdg;
            }
            else if (ev.Kind == "KILL")
            {
                string killedName = ev.Title.Replace("Сбит:", "").Trim();
                DebriefEntity victim = null;
                foreach (DebriefEntity cand in session.Entities)
                {
                    if (cand.Type == "Air" && cand.Coalition == "Enemies" &&
                        cand.Name.IndexOf(killedName, StringComparison.OrdinalIgnoreCase) >= 0 &&
                        Math.Abs(cand.LastTime - ev.Time) < 2.0)
                    {
                        victim = cand;
                        break;
                    }
                }
                if (victim == null) victim = FindEntityByFuzzyName(session, killedName);
                DebriefSample vState = victim != null ? victim.GetStateAt(ev.Time) : null;
                double tof = (lastLaunchTime > 0 && (ev.Time - lastLaunchTime) > 1.0 && (ev.Time - lastLaunchTime) < 55.0)
                    ? (ev.Time - lastLaunchTime)
                    : 12.0;
                double hitX, hitY;
                if (vState != null)
                {
                    hitX = vState.X;
                    hitY = vState.Y;
                }
                else
                {
                    double estRange = Math.Min(38000.0, Math.Max(3500.0, tof * 880.0));
                    double hitRad = lastLaunchHdg * Math.PI / 180.0;
                    hitX = lastLaunchX + Math.Sin(hitRad) * estRange;
                    hitY = lastLaunchY + Math.Cos(hitRad) * estRange;
                }
                double realDist = Math.Sqrt(Math.Pow(hitX - lastLaunchX, 2) + Math.Pow(hitY - lastLaunchY, 2));
                ev.WorldX = hitX;
                ev.WorldY = hitY;
                ev.Description += string.Format(" | Подлёт: {0:F0} сек (~{1:F1} км)", tof, realDist / 1000.0);
                mslCounter++;
                DebriefEntity mslEnt = new DebriefEntity
                {
                    Id = "msl_player_" + mslCounter,
                    Name = "AIM-120C",
                    Type = "Missile",
                    Coalition = "Allies",
                    FirstTime = ev.Time - tof,
                    LastTime = ev.Time
                };
                mslEnt.Track.Add(new DebriefSample { Time = ev.Time - tof, X = lastLaunchX, Y = lastLaunchY, Alt = curAlt, Heading = lastLaunchHdg, Speed = 950 });
                mslEnt.Track.Add(new DebriefSample { Time = ev.Time, X = hitX, Y = hitY, Alt = curAlt + 300, Heading = lastLaunchHdg, Speed = 780 });
                session.Entities.Add(mslEnt);
            }
            lastT = ev.Time;
        }

        if (session.Duration > lastT + 1.0)
        {
            double rem = session.Duration - lastT;
            double rad = curHdg * Math.PI / 180.0;
            rawKeyPoints.Add(new DebriefSample
            {
                Time = session.Duration,
                X = curX + Math.Sin(rad) * curSpd * Math.Min(rem, 25.0),
                Y = curY + Math.Cos(rad) * curSpd * Math.Min(rem, 25.0),
                Alt = curAlt,
                Heading = curHdg,
                Speed = curSpd
            });
        }

        // Сглаживаем ломаную линию игрока сплайном Catmull-Rom (шаг 1.5 сек) для плавных виражей
        player.FirstTime = 0;
        player.LastTime = session.Duration;
        if (rawKeyPoints.Count < 3)
        {
            player.Track.AddRange(rawKeyPoints);
        }
        else
        {
            for (int i = 0; i < rawKeyPoints.Count - 1; i++)
            {
                DebriefSample p0 = rawKeyPoints[Math.Max(0, i - 1)];
                DebriefSample p1 = rawKeyPoints[i];
                DebriefSample p2 = rawKeyPoints[i + 1];
                DebriefSample p3 = rawKeyPoints[Math.Min(rawKeyPoints.Count - 1, i + 2)];

                double segDt = Math.Max(0.5, p2.Time - p1.Time);
                int steps = Math.Max(2, Math.Min(40, (int)(segDt / 1.5)));
                for (int s = 0; s < steps; s++)
                {
                    double u = (double)s / steps;
                    double u2 = u * u;
                    double u3 = u2 * u;

                    double sx = 0.5 * ((2.0 * p1.X) + (-p0.X + p2.X) * u + (2.0 * p0.X - 5.0 * p1.X + 4.0 * p2.X - p3.X) * u2 + (-p0.X + 3.0 * p1.X - 3.0 * p2.X + p3.X) * u3);
                    double sy = 0.5 * ((2.0 * p1.Y) + (-p0.Y + p2.Y) * u + (2.0 * p0.Y - 5.0 * p1.Y + 4.0 * p2.Y - p3.Y) * u2 + (-p0.Y + 3.0 * p1.Y - 3.0 * p2.Y + p3.Y) * u3);
                    double st = p1.Time + segDt * u;
                    double salt = p1.Alt + (p2.Alt - p1.Alt) * u;
                    double sspd = p1.Speed + (p2.Speed - p1.Speed) * u;

                    double tx = 0.5 * ((-p0.X + p2.X) + 2.0 * (2.0 * p0.X - 5.0 * p1.X + 4.0 * p2.X - p3.X) * u + 3.0 * (-p0.X + 3.0 * p1.X - 3.0 * p2.X + p3.X) * u2);
                    double ty = 0.5 * ((-p0.Y + p2.Y) + 2.0 * (2.0 * p0.Y - 5.0 * p1.Y + 4.0 * p2.Y - p3.Y) * u + 3.0 * (-p0.Y + 3.0 * p1.Y - 3.0 * p2.Y + p3.Y) * u2);
                    double shdg = (Math.Abs(tx) > 0.01 || Math.Abs(ty) > 0.01)
                        ? ((Math.Atan2(tx, ty) * 180.0 / Math.PI + 360.0) % 360.0)
                        : p1.Heading;

                    player.Track.Add(new DebriefSample
                    {
                        Time = st,
                        X = sx,
                        Y = sy,
                        Alt = salt,
                        Heading = shdg,
                        Speed = sspd
                    });
                }
            }
            player.Track.Add(rawKeyPoints[rawKeyPoints.Count - 1]);
        }

        // Ракеты ИИ и ракеты противника по игроку (строго от того типа самолёта, который указан в скобках стрелка!)
        Dictionary<string, double> recentAiShots = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < aiMissiles.Count; i++)
        {
            AiMissileEvent am = aiMissiles[i];
            string shotKey = am.MissileAndShooter + "->" + am.TargetName;
            double prevShotTime;
            if (recentAiShots.TryGetValue(shotKey, out prevShotTime) && (am.Time - prevShotTime) < 9.0)
                continue;
            recentAiShots[shotKey] = am.Time;

            DebriefEntity tgtEnt = am.TargetName.Equals(player.Name, StringComparison.OrdinalIgnoreCase)
                ? player
                : FindEntityByFuzzyName(session, am.TargetName);
            DebriefSample tSt = tgtEnt != null ? tgtEnt.GetStateAt(am.Time) : null;
            if (tSt == null) continue;

            // Извлекаем имя самолёта-стрелка из "ASF-MRM (ASF-30)" -> "ASF-30"
            string shooterTypeHint = "";
            int pOpen = am.MissileAndShooter.IndexOf('(');
            int pClose = am.MissileAndShooter.LastIndexOf(')');
            if (pOpen > 0 && pClose > pOpen)
                shooterTypeHint = am.MissileAndShooter.Substring(pOpen + 1, pClose - pOpen - 1).Trim();

            bool isEnemyMsl = IsEnemyUnitType(am.MissileAndShooter);
            DebriefEntity shooterEnt = null;
            double launchT = Math.Max(0, am.Time - 8.0);

            foreach (DebriefEntity cand in session.Entities)
            {
                if (cand.Type != "Air" || cand.IsPlayer) continue;
                if (cand.RoleTag == "AWACS" || cand.RoleTag == "Tanker" || cand.RoleTag == "Bomber") continue;
                if (isEnemyMsl ? cand.Coalition != "Enemies" : cand.Coalition != "Allies") continue;
                if (!string.IsNullOrEmpty(shooterTypeHint) &&
                    cand.Name.IndexOf(shooterTypeHint, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                if (cand.GetStateAt(launchT) != null)
                {
                    shooterEnt = cand;
                    if (i % 2 == 1) break;
                }
            }

            DebriefSample sSt = shooterEnt != null ? shooterEnt.GetStateAt(launchT) : null;
            double sx = sSt != null ? sSt.X : (tSt.X + (isEnemyMsl ? 14000 : -14000));
            double sy = sSt != null ? sSt.Y : (tSt.Y + 6000);
            double mslHdg = (Math.Atan2(tSt.X - sx, tSt.Y - sy) * 180.0 / Math.PI + 360.0) % 360.0;

            DebriefEntity aiMsl = new DebriefEntity
            {
                Id = "ai_msl_" + i,
                Name = am.MissileAndShooter,
                Type = "Missile",
                Coalition = isEnemyMsl ? "Enemies" : "Allies",
                FirstTime = launchT,
                LastTime = am.Time + 3.0
            };
            aiMsl.Track.Add(new DebriefSample { Time = launchT, X = sx, Y = sy, Alt = 5200, Heading = mslHdg, Speed = 880 });
            aiMsl.Track.Add(new DebriefSample { Time = am.Time + 3.0, X = tSt.X, Y = tSt.Y, Alt = tSt.Alt, Heading = mslHdg, Speed = 740 });
            session.Entities.Add(aiMsl);
        }
        BuildRadarBeamsFromEntities(session, player, radarPings);
    }
    private static void AnalyzeReconstructedSession(
    DebriefSession session,
    DebriefEntity player,
        List<double> threatLockTimes,
    List<string> threatLockDetails,
    string deathCauseCode)
    {
        List<TacticalEvent> launches = new List<TacticalEvent>();
        int kills = 0;
        foreach (TacticalEvent ev in session.Events)
        {
            if (ev.Kind == "LAUNCH") launches.Add(ev);
            else if (ev.Kind == "KILL") kills++;
            else if (ev.Kind == "KIA")
            {
                if (deathCauseCode == "CFIT" || ev.Title.IndexOf("землёй", StringComparison.OrdinalIgnoreCase) >= 0)
                    ev.Advice = "Потеря пространственной ориентировки / столкновение с рельефом (CFIT). Контролируйте радиовысотомер и вертикальную скорость при маневрировании у земли, используйте автопилот удержания высоты.";
                else if (deathCauseCode == "GFORCE" || ev.Title.IndexOf("перегрузки", StringComparison.OrdinalIgnoreCase) >= 0)
                    ev.Advice = "Превышена эксплуатационная перегрузка планера (G-Limit). Не дёргайте РУС до упора на сверхзвуковой скорости (> 1.1 Маха) — сначала сбросьте тягу и выпустите воздушный тормоз.";
                else
                    ev.Advice = "Борт уничтожен в бою. Следите за индикатором СПО (RWR) и при облучении ракетой ставьте её на траверз (Notch 90°).";
                session.Mistakes.Add(ev);
            }
        }
        for (int i = 2; i < launches.Count; i++)
        {
            if (launches[i].Time - launches[i - 2].Time <= 5.0)
            {
                TacticalEvent err = new TacticalEvent
                {
                    Time = launches[i].Time,
                    Kind = "MISTAKE",
                    Severity = "WARNING",
                    Title = "Залповый перерасход боекомплекта (3 ракеты подряд)",
                    Description = string.Format("Выпущено 3 ракеты за {0:F1} сек без оценки попадания.", launches[i].Time - launches[i - 2].Time),
                    Advice = "Выдерживайте паузу 6–10 секунд между пусками, чтобы оценить манёвр цели и сохранить боекомплект для следующих волн.",
                    WorldX = launches[i].WorldX,
                    WorldY = launches[i].WorldY
                };
                session.Events.Add(err);
                session.Mistakes.Add(err);
            }
        }
        for (int i = 0; i < threatLockTimes.Count; i++)
        {
            if (i > 0 && threatLockTimes[i] - threatLockTimes[i - 1] < 12.0) continue;
            DebriefSample ps = player.GetStateAt(threatLockTimes[i]);
            TacticalEvent err = new TacticalEvent
            {
                Time = threatLockTimes[i],
                Kind = "MISTAKE",
                Severity = "WARNING",
                Title = "Вражеская ракета вышла на активное наведение (" + threatLockDetails[i] + ")",
                Description = "ГСН ракеты противника захватила ваш самолёт на конечном участке траектории.",
                Advice = "Выполните энергичный отворот на 90° от направления угрозы (Notch), отстреливайте серию дипольных отражателей (Chaff) и уходите со снижением к земле.",
                WorldX = ps != null ? ps.X : 0,
                WorldY = ps != null ? ps.Y : 0
            };
            session.Mistakes.Add(err);
        }
        if (launches.Count >= 4 && kills == 0)
        {
            TacticalEvent err = new TacticalEvent
            {
                Time = launches[launches.Count - 1].Time,
                Kind = "MISTAKE",
                Severity = "CRITICAL",
                Title = string.Format("Низкая эффективность пусков: 0 попаданий из {0}", launches.Count),
                Description = "Ни одна из выпущенных ракет не поразила цель.",
                Advice = "Не пускайте ракеты с предельной дистанции по отворачивающей цели и удерживайте радарный подсвет до перехода ГСН в активный режим."
            };
            session.Events.Add(err);
            session.Mistakes.Add(err);
        }
        session.Events.Sort((a, b) => a.Time.CompareTo(b.Time));
        session.Mistakes.Sort((a, b) => a.Time.CompareTo(b.Time));
    }
    // =====================================================================
    // === ПАРСЕР TACVIEW ACMI (.acmi / .txt.acmi / .zip.acmi) =============
    // =====================================================================
    private static DebriefSession LoadAcmi(string filePath, string preferredPilot)
    {
        DebriefSession session = new DebriefSession();
        session.SourceFile = filePath;
        session.Title = Path.GetFileNameWithoutExtension(filePath);
        session.PilotName = preferredPilot ?? "";
        session.HasFullRadarCoords = true;
        session.HasExactTelemetry = true;
        List<string> lines = ReadAcmiLines(filePath);
        double refLat = 0, refLon = 0;
        bool hasRef = false;
        double curTime = 0;
        Dictionary<string, DebriefEntity> map = new Dictionary<string, DebriefEntity>();
        Dictionary<string, double[]> lastRawCoord = new Dictionary<string, double[]>();
        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("//")) continue;
            if (line.StartsWith("#"))
            {
                curTime = ParseDoubleSafe(line.Substring(1), curTime);
                continue;
            }
            if (line.StartsWith("-"))
            {
                string remId = line.Substring(1).Trim();
                DebriefEntity remEnt;
                if (map.TryGetValue(remId, out remEnt)) remEnt.LastTime = curTime;
                continue;
            }
            int commaIdx = line.IndexOf(',');
            if (commaIdx <= 0) continue;
            string objId = line.Substring(0, commaIdx).Trim();
            string payload = line.Substring(commaIdx + 1);
            if (objId == "0")
            {
                Match mLat = Regex.Match(payload, @"ReferenceLatitude=([-\d.]+)");
                Match mLon = Regex.Match(payload, @"ReferenceLongitude=([-\d.]+)");
                if (mLat.Success) refLat = ParseDoubleSafe(mLat.Groups[1].Value, refLat);
                if (mLon.Success) refLon = ParseDoubleSafe(mLon.Groups[1].Value, refLon);
                Match mTitle = Regex.Match(payload, @"Title=([^,]+)");
                if (mTitle.Success) session.Title = mTitle.Groups[1].Value.Trim();
                continue;
            }
            DebriefEntity ent;
            if (!map.TryGetValue(objId, out ent))
            {
                ent = new DebriefEntity { Id = objId, Name = "Object " + objId, Type = "Air", Coalition = "Neutral" };
                map[objId] = ent;
                session.Entities.Add(ent);
            }
            string[] parts = payload.Split(',');
            foreach (string part in parts)
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string k = part.Substring(0, eq).Trim();
                string v = part.Substring(eq + 1).Trim();
                if (k == "Name") ent.Name = v;
                else if (k == "Pilot") ent.Pilot = v;
                else if (k == "Coalition") ent.Coalition = v;
                else if (k == "Type")
                {
                    if (v.IndexOf("Missile", StringComparison.OrdinalIgnoreCase) >= 0 || v.IndexOf("Weapon", StringComparison.OrdinalIgnoreCase) >= 0)
                        ent.Type = "Missile";
                    else if (v.IndexOf("Ground", StringComparison.OrdinalIgnoreCase) >= 0 || v.IndexOf("SAM", StringComparison.OrdinalIgnoreCase) >= 0)
                        ent.Type = "Ground";
                    else if (v.IndexOf("Sea", StringComparison.OrdinalIgnoreCase) >= 0)
                        ent.Type = "Sea";
                    else
                        ent.Type = "Air";
                }
                else if (k == "T")
                {
                    string[] tc = v.Split('|');
                    double[] prev;
                    if (!lastRawCoord.TryGetValue(objId, out prev)) prev = new double[6];
                    for (int i = 0; i < Math.Min(tc.Length, 6); i++)
                    {
                        if (!string.IsNullOrEmpty(tc[i]))
                            prev[i] = ParseDoubleSafe(tc[i], prev[i]);
                    }
                    lastRawCoord[objId] = prev;
                    double lon = refLon + prev[0];
                    double lat = refLat + prev[1];
                    if (!hasRef && (Math.Abs(lon) > 0.001 || Math.Abs(lat) > 0.001))
                    {
                        refLon = lon;
                        refLat = lat;
                        hasRef = true;
                    }
                    if (hasRef)
                    {
                        double metersPerDegLat = 111320.0;
                        double metersPerDegLon = 111320.0 * Math.Cos(refLat * Math.PI / 180.0);
                        double wx = (lon - refLon) * metersPerDegLon;
                        double wy = (lat - refLat) * metersPerDegLat;
                        double alt = prev[2];
                        double roll = prev[3];
                        double pitch = prev[4];
                        double yaw = (prev[5] + 360.0) % 360.0;
                        double spd = 0;
                        if (ent.Track.Count > 0)
                        {
                            DebriefSample lastS = ent.Track[ent.Track.Count - 1];
                            double dt = curTime - lastS.Time;
                            if (dt > 0.01)
                            {
                                double dx = wx - lastS.X;
                                double dy = wy - lastS.Y;
                                double dz = alt - lastS.Alt;
                                spd = Math.Sqrt(dx * dx + dy * dy + dz * dz) / dt;
                                if (yaw == 0 && (Math.Abs(dx) > 0.1 || Math.Abs(dy) > 0.1))
                                    yaw = (Math.Atan2(dx, dy) * 180.0 / Math.PI + 360.0) % 360.0;
                            }
                            else spd = lastS.Speed;
                        }
                        ent.Track.Add(new DebriefSample
                        {
                            Time = curTime,
                            X = wx,
                            Y = wy,
                            Alt = alt,
                            Roll = roll,
                            Pitch = pitch,
                            Heading = yaw,
                            Speed = spd
                        });
                        if (curTime < ent.FirstTime) ent.FirstTime = curTime;
                        if (curTime > ent.LastTime) ent.LastTime = curTime;
                    }
                }
            }
        }
        session.Duration = Math.Max(1.0, curTime);
        BuildProceduralTacticalTerrain(session, 0, 0, 140000.0, Math.Abs(session.Title.GetHashCode()), false);
        IdentifyAcmiPlayerAndAnalyze(session, preferredPilot);
        return session;
    }
    private static List<string> ReadAcmiLines(string filePath)
    {
        List<string> lines = new List<string>();
        byte[] header = new byte[4];
        using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            fs.Read(header, 0, 4);
        if (header[0] == 0x50 && header[1] == 0x4B)
        {
            using (ZipArchive arch = ZipFile.OpenRead(filePath))
            {
                foreach (ZipArchiveEntry entry in arch.Entries)
                {
                    if (entry.Name.EndsWith(".acmi", StringComparison.OrdinalIgnoreCase) || entry.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    {
                        using (StreamReader sr = new StreamReader(entry.Open(), Encoding.UTF8))
                        {
                            string ln;
                            while ((ln = sr.ReadLine()) != null) lines.Add(ln);
                        }
                        break;
                    }
                }
            }
        }
        else
        {
            lines.AddRange(File.ReadAllLines(filePath, Encoding.UTF8));
        }
        return lines;
    }
    private static void IdentifyAcmiPlayerAndAnalyze(DebriefSession session, string preferredPilot)
    {
        DebriefEntity player = null;
        foreach (DebriefEntity e in session.Entities)
        {
            if (e.Type != "Air") continue;
            if (!string.IsNullOrEmpty(preferredPilot) &&
            (!string.IsNullOrEmpty(e.Pilot) && e.Pilot.IndexOf(preferredPilot, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                player = e;
                break;
            }
        }
        if (player == null)
        {
            foreach (DebriefEntity e in session.Entities)
            {
                if (e.Type == "Air" && e.Track.Count > 10) { player = e; break; }
            }
        }
        if (player == null) return;
        player.IsPlayer = true;
        session.PilotName = !string.IsNullOrEmpty(player.Pilot) ? player.Pilot : player.Name;
        session.Events.Sort((a, b) => a.Time.CompareTo(b.Time));
        session.Mistakes.Sort((a, b) => a.Time.CompareTo(b.Time));
    }
    // =====================================================================
    // === ИМПОРТ ПРОШЛОГО ВЫЛЕТА ИЗ PLAYER.LOG + ШТАТНОГО FLIGHTLOG =======
    // =====================================================================
    private static string GetRegexGroup(Match m, int idx)
    {
        if (m == null || !m.Success || m.Groups.Count <= idx) return "";
        int i = 0;
        foreach (Group g in m.Groups)
        {
            if (i == idx) return g.Value;
            i++;
        }
        return "";
    }

    private static bool IsPlayerInBracketTag(string text, string nick)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(nick)) return false;
        int pOpen = text.IndexOf('(');
        int pClose = text.LastIndexOf(')');
        if (pOpen < 0 || pClose <= pOpen) return false;
        string inside = text.Substring(pOpen + 1, pClose - pOpen - 1);
        foreach (string part in inside.Split(','))
        {
            if (part.Trim().Equals(nick, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static string ImportFromPlayerLog(string logFilePath, string fallbackNick)
    {
        if (string.IsNullOrEmpty(logFilePath) || !File.Exists(logFilePath))
            throw new FileNotFoundException("Файл Player.log не найден.");

        string content;
        using (FileStream fs = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (StreamReader sr = new StreamReader(fs, Encoding.UTF8))
            content = sr.ReadToEnd();

        string myNick = fallbackNick ?? "";
        Match mPilot = Regex.Match(content, @"Set current pilot to\s+([^\r\n]+)", RegexOptions.IgnoreCase);
        if (mPilot.Success)
        {
            string p = GetRegexGroup(mPilot, 1).Trim();
            if (!p.Equals("none", StringComparison.OrdinalIgnoreCase) && p.Length > 0)
                myNick = p;
        }
        if (string.IsNullOrEmpty(myNick))
        {
            Match mActor = Regex.Match(content, @"Registering actor Player \[\d+\]\s*\(([^\)]+)\)", RegexOptions.IgnoreCase);
            if (mActor.Success) myNick = GetRegexGroup(mActor, 1).Trim();
        }
        if (string.IsNullOrEmpty(myNick)) myNick = "Pilot";

        // Разбиваем Player.log строго ПО ЦЕЛЬНЫМ КАТКАМ / МИССИЯМ (а не по респавнам внутри боя!)
        string[] allLines = content.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        List<int> battleStarts = new List<int>();
        List<string> battleTitles = new List<string>();
        List<string> battleCamps = new List<string>();
        List<string> battleScens = new List<string>();
        List<string> battleVts = new List<string>();

        string curCamp = "";
        string curScen = "";
        string curVts = "";

        for (int i = 0; i < allLines.Length; i++)
        {
            string ln = allLines[i];
            if (ln.StartsWith("LoadCampaign(", StringComparison.OrdinalIgnoreCase))
            {
                Match mc = Regex.Match(ln, @"LoadCampaign\(([^,\)]+),\s*([^\)]+)\)", RegexOptions.IgnoreCase);
                if (mc.Success)
                {
                    curCamp = GetRegexGroup(mc, 1).Trim();
                    curScen = GetRegexGroup(mc, 2).Trim();
                    curVts = "";
                }
            }
            else if (ln.IndexOf("Loaded LOD info", StringComparison.OrdinalIgnoreCase) < 0 &&
                     ln.IndexOf(".vts", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Match mv = Regex.Match(ln, @"([A-Za-z]:\\[^\r\n]+?\.vts)\b", RegexOptions.IgnoreCase);
                if (mv.Success)
                {
                    string cand = GetRegexGroup(mv, 1).Trim();
                    if (File.Exists(cand)) curVts = cand;
                }
            }

            // Старт целой миссии / катки (игнорируем стартовую сцену меню SamplerScene)
            if (ln.StartsWith("VTMapManager scenarioReady!", StringComparison.OrdinalIgnoreCase))
            {
                if (battleStarts.Count == 0 || (i - battleStarts[battleStarts.Count - 1]) > 80)
                {
                    battleStarts.Add(i);
                    battleCamps.Add(curCamp);
                    battleScens.Add(curScen);
                    battleVts.Add(curVts);
                    string titleTag = !string.IsNullOrEmpty(curScen) ? curScen : (!string.IsNullOrEmpty(curVts) ? Path.GetFileNameWithoutExtension(curVts) : "Battle");
                    battleTitles.Add(titleTag);
                }
            }
        }

        if (battleStarts.Count == 0)
        {
            battleStarts.Add(0);
            battleCamps.Add(curCamp);
            battleScens.Add(curScen);
            battleVts.Add(curVts);
            battleTitles.Add("Battle");
        }

        Dictionary<string, double> exactFlightLogTimes = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (Match mDump in Regex.Matches(content, @"Dumped flight log to:\s*([^\r\n]+\.txt)", RegexOptions.IgnoreCase))
        {
            string flPath = GetRegexGroup(mDump, 1).Trim();
            if (File.Exists(flPath))
            {
                foreach (string flLine in File.ReadAllLines(flPath, Encoding.UTF8))
                {
                    Match mTs = Regex.Match(flLine, @"^\[(\d+):(\d+)\]\s*(.+)$");
                    if (mTs.Success)
                    {
                        double sec = ParseDoubleSafe(GetRegexGroup(mTs, 1)) * 60.0 + ParseDoubleSafe(GetRegexGroup(mTs, 2));
                        exactFlightLogTimes[GetRegexGroup(mTs, 3).Trim()] = sec;
                    }
                }
            }
        }

        string lastCreatedPath = "";
        char sep = '|';

        for (int bIdx = 0; bIdx < battleStarts.Count; bIdx++)
        {
            int lineFrom = Math.Max(0, battleStarts[bIdx] - 40);
            int lineTo = (bIdx + 1 < battleStarts.Count) ? (battleStarts[bIdx + 1] - 40) : allLines.Length;
            string campId = battleCamps[bIdx];
            string scenId = battleScens[bIdx];
            string vtsPath = battleVts[bIdx];
            string firstVeh = "F/A-26B";

            for (int k = lineFrom; k < lineTo; k++)
            {
                if (allLines[k].StartsWith("Set current vehicle to ", StringComparison.OrdinalIgnoreCase))
                {
                    string v = allLines[k].Substring("Set current vehicle to ".Length).Trim();
                    if (!v.Equals("none", StringComparison.OrdinalIgnoreCase) && v.Length > 0) { firstVeh = v; break; }
                }
            }

            StringBuilder sb = new StringBuilder();
            sb.Append(string.Format(CultureInfo.InvariantCulture, "0.0{0}META{0}{1}{0}{2}\r\n", sep, myNick, firstVeh));
            if (!string.IsNullOrEmpty(vtsPath))
                sb.Append(string.Format("0.0{0}VTS{0}{1}{0}\r\n", sep, vtsPath));
            if (!string.IsNullOrEmpty(campId) || !string.IsNullOrEmpty(scenId))
                sb.Append(string.Format("0.0{0}SCENARIO{0}{1}{0}{2}\r\n", sep, campId, scenId));

            double curT = 2.0;
            double lastEngineFailLogT = -99.0;
            Dictionary<string, string> targetIdToMsl = new Dictionary<string, string>();
            Dictionary<string, string> actorIdToPlayerLabel = new Dictionary<string, string>();
            HashSet<string> enemyPlayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string lastFiredMsl = "AIM-120C";
            string currentPlayerVeh = firstVeh;
            int playerSpawnCount = 0;
            int meaningfulEvents = 0;

            for (int i = lineFrom; i < lineTo; i++)
            {
                string line = allLines[i];

                if (line.StartsWith("Set current vehicle to ", StringComparison.OrdinalIgnoreCase))
                {
                    string v = line.Substring("Set current vehicle to ".Length).Trim();
                    if (!v.Equals("none", StringComparison.OrdinalIgnoreCase) && v.Length > 0)
                        currentPlayerVeh = v;
                    continue;
                }

                if (line.StartsWith("RPC_TeamChanged(", StringComparison.OrdinalIgnoreCase))
                {
                    int p1 = line.IndexOf('(');
                    int p2 = line.IndexOf(')');
                    if (p1 > 0 && p2 > p1)
                    {
                        string[] tp = line.Substring(p1 + 1, p2 - p1 - 1).Split(',');
                        if (tp.Length >= 2 && tp[1].Trim().Equals("Enemy", StringComparison.OrdinalIgnoreCase))
                            enemyPlayers.Add(tp[0].Trim());
                    }
                    continue;
                }

                if (line.StartsWith("Registering actor MP Spawn [", StringComparison.OrdinalIgnoreCase))
                {
                    int b1 = line.IndexOf('[');
                    int b2 = line.IndexOf(']', b1 + 1);
                    int p1 = line.IndexOf('(', b2 + 1);
                    int p2 = line.LastIndexOf(')');
                    if (b1 > 0 && b2 > b1 && p1 > b2 && p2 > p1)
                    {
                        string spId = line.Substring(b1 + 1, b2 - b1 - 1).Trim();
                        string acPilot = line.Substring(p1 + 1, p2 - p1 - 1).Trim();
                        actorIdToPlayerLabel[spId] = acPilot;
                    }
                }

                if (line.StartsWith("FlightLogger:", StringComparison.OrdinalIgnoreCase))
                {
                    string flBody = line.Substring("FlightLogger:".Length).Trim();
                    double exactT;
                    if (exactFlightLogTimes.TryGetValue(flBody, out exactT) && exactT >= curT)
                        curT = exactT;
                }

                Match mTime = Regex.Match(line, @"(?:Time:|mission start time to)\s*(\d+[\.,]\d+)", RegexOptions.IgnoreCase);
                if (mTime.Success)
                {
                    double parsedT = ParseDoubleSafe(GetRegexGroup(mTime, 1), curT);
                    if (parsedT > curT) curT = parsedT;
                }
                else
                {
                    curT += 0.25;
                }

                // Конец всей миссии (возврат в главное меню)
                if (line.StartsWith("VTOLMPSceneManager: Disconnecting and returning to main menu", StringComparison.OrdinalIgnoreCase) ||
                    (line.IndexOf("Unloading 6 Unused Serialized files", StringComparison.OrdinalIgnoreCase) >= 0 && curT > 45.0))
                    break;

                // Респавн игрока внутри одной катки!
                if (line.StartsWith("FlightLogger: " + myNick + " has spawned", StringComparison.OrdinalIgnoreCase))
                {
                    playerSpawnCount++;
                    if (playerSpawnCount > 1)
                    {
                        sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:F1}{1}TAKEOFF{1}Респавн: {2}{1}\r\n", curT, sep, currentPlayerVeh));
                        meaningfulEvents++;
                    }
                    continue;
                }
                if (line.StartsWith("FlightLogger:", StringComparison.OrdinalIgnoreCase))
                {
                    string fl = line.Substring("FlightLogger:".Length).Trim();
                    int idxTo = fl.IndexOf(myNick + " took off from ", StringComparison.OrdinalIgnoreCase);
                    if (idxTo >= 0)
                    {
                        string baseName = fl.Substring(idxTo + (myNick + " took off from ").Length).Trim().TrimEnd('.');
                        if (!baseName.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                        {
                            sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:F1}{1}TAKEOFF{1}{2}{1}\r\n", curT, sep, baseName));
                            meaningfulEvents++;
                        }
                        continue;
                    }
                    int idxLd = fl.IndexOf(myNick + " landed at ", StringComparison.OrdinalIgnoreCase);
                    if (idxLd >= 0)
                    {
                        string baseName = fl.Substring(idxLd + (myNick + " landed at ").Length).Trim().TrimEnd('.');
                        if (!baseName.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                            sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:F1}{1}LANDED{1}{2}{1}\r\n", curT, sep, baseName));
                        continue;
                    }
                    if (fl.IndexOf("ejected", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    fl.IndexOf(myNick, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:F1}{1}EJECT{1}Катапультирование экипажа ({2}){1}EJECT\r\n", curT, sep, currentPlayerVeh));
                        meaningfulEvents++;
                        continue;
                    }
                    int idxComp = fl.IndexOf("Completed objective:", StringComparison.OrdinalIgnoreCase);
                    if (idxComp >= 0)
                    {
                        sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:F1}{1}OBJECTIVE{1}Completed{1}{2}\r\n", curT, sep, fl.Substring(idxComp + 20).Trim()));
                        continue;
                    }
                    int idxFail = fl.IndexOf("Failed objective:", StringComparison.OrdinalIgnoreCase);
                    if (idxFail >= 0)
                    {
                        sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:F1}{1}OBJECTIVE{1}Failed{1}{2}\r\n", curT, sep, fl.Substring(idxFail + 17).Trim()));
                        continue;
                    }
                    int idxMpKilled = fl.IndexOf(" killed ", StringComparison.OrdinalIgnoreCase);
                    if (idxMpKilled > 0 && fl.IndexOf(" was killed", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        string killerNick = fl.Substring(0, idxMpKilled).Trim();
                        string rest = fl.Substring(idxMpKilled + " killed ".Length).Trim();
                        string rawTgt = rest;
                        string wp = "";
                        int idxWith = rest.IndexOf(" with ", StringComparison.OrdinalIgnoreCase);
                        if (idxWith > 0)
                        {
                            rawTgt = rest.Substring(0, idxWith).Trim().TrimEnd('.');
                            wp = rest.Substring(idxWith + " with ".Length).Trim().TrimEnd('.');
                        }
                        else rawTgt = rawTgt.TrimEnd('.');
                        if (killerNick.Equals(myNick, StringComparison.OrdinalIgnoreCase))
                        {
                            sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:F1}{1}KILL{1}{2}{1}{3}\r\n", curT, sep, rawTgt, wp));
                            meaningfulEvents++;
                        }
                        else if (IsPlayerInBracketTag(rawTgt, myNick) || rawTgt.Equals(myNick, StringComparison.OrdinalIgnoreCase))
                        {
                            string msg = string.IsNullOrEmpty(wp) ? ("Сбит игроком: " + killerNick) : ("Сбит игроком " + killerNick + " (" + wp + ")");
                            sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:F1}{1}KIA{1}{2}{1}ENEMY\r\n", curT, sep, msg));
                            meaningfulEvents++;
                        }
                        else
                        {
                            sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:F1}{1}WORLD_KILL{1}{2}{1}{3}\r\n", curT, sep, rawTgt, killerNick));
                            meaningfulEvents++;
                        }
                        curT += 1.0;
                        continue;
                    }
                    int idxWasKilled = fl.IndexOf(" was killed", StringComparison.OrdinalIgnoreCase);
                    if (idxWasKilled > 0)
                    {
                        string rawTgt = fl.Substring(0, idxWasKilled).Trim();
                        string rawKlr = "";
                        string wp = "";
                        int idxBy = fl.IndexOf(" was killed by ", StringComparison.OrdinalIgnoreCase);
                        if (idxBy > 0)
                        {
                            string rest = fl.Substring(idxBy + " was killed by ".Length).Trim();
                            int idxWith = rest.IndexOf(" with ", StringComparison.OrdinalIgnoreCase);
                            if (idxWith > 0)
                            {
                                rawKlr = rest.Substring(0, idxWith).Trim().TrimEnd('.');
                                wp = rest.Substring(idxWith + " with ".Length).Trim().TrimEnd('.');
                            }
                            else
                            {
                                int idxDot = rest.IndexOf('.');
                                rawKlr = (idxDot > 0 ? rest.Substring(0, idxDot) : rest).Trim().TrimEnd('.');
                            }
                        }
                        if (IsPlayerInBracketTag(rawTgt, myNick) || rawTgt.Equals(myNick, StringComparison.OrdinalIgnoreCase))
                        {
                            bool isCfit = rawKlr.Equals("Environment", StringComparison.OrdinalIgnoreCase);
                            string msg = isCfit ? "Столкновение с землёй (CFIT)" : (string.IsNullOrEmpty(rawKlr) ? "Борт уничтожен" : ("Сбит: " + rawKlr));
                            sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:F1}{1}KIA{1}{2}{1}{3}\r\n", curT, sep, msg, isCfit ? "CFIT" : "ENEMY"));
                            meaningfulEvents++;
                        }
                        else if (!string.IsNullOrEmpty(rawKlr) && IsPlayerInBracketTag(rawKlr, myNick))
                        {
                            string tid = ExtractBracketId(rawTgt);
                            if (string.IsNullOrEmpty(wp) && !string.IsNullOrEmpty(tid) && targetIdToMsl.ContainsKey(tid))
                                wp = targetIdToMsl[tid];
                            sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:F1}{1}KILL{1}{2}{1}{3}\r\n", curT, sep, StripBracketSuffix(rawTgt), wp));
                            meaningfulEvents++;
                        }
                        else
                        {
                            sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:F1}{1}WORLD_KILL{1}{2}{1}{3}\r\n", curT, sep, StripBracketSuffix(rawTgt), string.IsNullOrEmpty(rawKlr) ? "Бой" : StripBracketSuffix(rawKlr)));
                        }
                        curT += 1.0;
                        continue;
                    }
                    Match mG = Regex.Match(fl, @"Died by instantaneous G-force\s*([\d,]+)", RegexOptions.IgnoreCase);
                    if (mG.Success)
                    {
                        sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:F1}{1}KIA{1}Разрушение от перегрузки ({2} G){1}GFORCE\r\n", curT, sep, GetRegexGroup(mG, 1)));
                        meaningfulEvents++;
                        continue;
                    }
                }
                if (line.StartsWith("Registering actor ", StringComparison.OrdinalIgnoreCase))
                {
                    string body = line.Substring("Registering actor ".Length).Trim();
                    if (body.StartsWith("MP Spawn [", StringComparison.OrdinalIgnoreCase))
                    {
                        int pFirst = body.IndexOf('(');
                        int pLast = body.LastIndexOf(')');
                        if (pFirst > 0 && pLast > pFirst)
                        {
                            string mpUnitAndPilot = body.Substring(pFirst + 1, pLast - pFirst - 1).Trim();
                            if (!IsPlayerInBracketTag(mpUnitAndPilot, myNick) && mpUnitAndPilot.IndexOf('(') > 0)
                            {
                                bool isEnemyPl = false;
                                foreach (string ep in enemyPlayers)
                                    if (IsPlayerInBracketTag(mpUnitAndPilot, ep)) { isEnemyPl = true; break; }
                                sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:F1}{1}ACTOR_REG{1}{2}{1}{3}\r\n",
                                    curT, sep, mpUnitAndPilot, isEnemyPl ? "EnemyPlayer" : "AlliedPlayer"));
                            }
                        }
                        continue;
                    }

                    int pOpen = body.LastIndexOf('(');
                    int pClose = body.LastIndexOf(')');
                    if (pOpen > 0 && pClose > pOpen)
                    {
                        string u = body.Substring(0, pOpen).Trim();
                        string tp = body.Substring(pOpen + 1, pClose - pOpen - 1).Trim();
                        string combined = u + " " + tp;

                        // Отсекаем мелкий фоновый декор и под-модули кораблей, оставляя авиацию, ЗРК, радары и крупные корабли
                        if (Regex.IsMatch(u, @"^(?:GunTurret|MainGun|DCSamBattery|DroneCarrierVLS|DroneCarrierRadar|ForwardRadars|AftRadars|EscortCruiser|PhalanxUnit|RAM-launcher|CIWS|MK29|Watchman|SLAM|SRAD|Allied Infantry|Enemy Infantry|Z20x2)", RegexOptions.IgnoreCase))
                            continue;

                        if (!u.StartsWith("Player") && u.IndexOf("Clone") < 0 && u != "null" &&
                            !Regex.IsMatch(combined, @"\b(?:AIM-|ASF-MRM|ASF-SRM|AIRS-T|AGM-|GBU-|MK-|HARM|FLKM|BSM|SaawMissile|CAGM|Maverick|SideARM)\b", RegexOptions.IgnoreCase) &&
                            !IsPlayerInBracketTag(combined, myNick))
                        {
                            sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:F1}{1}ACTOR_REG{1}{2}{1}{3}\r\n", curT, sep, u, tp));
                        }
                    }
                    continue;
                }

                int idxFly = line.IndexOf("set rb vel from InitialFlyRoutine", StringComparison.OrdinalIgnoreCase);
                if (idxFly > 0)
                {
                    int idEnd = line.IndexOf(']');
                    string u = idEnd > 0 ? line.Substring(0, idEnd + 1).Trim() : StripBracketSuffix(line.Substring(0, idxFly));
                    Match mVec = Regex.Match(line.Substring(idxFly), @"(([-\d,]+),\s*([-\d,]+),\s*([-\d,]+))");
                    if (mVec.Success)
                    {
                        string vel = string.Format("{0};{1};{2}",
                        GetRegexGroup(mVec, 1).Replace(',', '.'),
                        GetRegexGroup(mVec, 2).Replace(',', '.'),
                        GetRegexGroup(mVec, 3).Replace(',', '.'));
                        sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:F1}{1}UNIT_SPAWN{1}{2}{1}{3}\r\n", curT, sep, u, vel));
                    }
                    continue;
                }
                int idxFiring = line.IndexOf(" is firing ", StringComparison.OrdinalIgnoreCase);
                int idxAt = line.IndexOf(" at ", StringComparison.OrdinalIgnoreCase);
                if (idxFiring > 0 && idxAt > idxFiring)
                {
                    string samName = StripBracketSuffix(line.Substring(0, idxFiring));
                    string mslPart = StripBracketSuffix(line.Substring(idxFiring + " is firing ".Length, idxAt - (idxFiring + " is firing ".Length)));
                    string tgtPart = line.Substring(idxAt + " at ".Length).Trim();
                    string tgtId = ExtractBracketId(tgtPart);
                    string resolvedTgt = (!string.IsNullOrEmpty(tgtId) && actorIdToPlayerLabel.ContainsKey(tgtId))
                    ? actorIdToPlayerLabel[tgtId]
                    : tgtPart;
                    if (IsPlayerInBracketTag(resolvedTgt, myNick))
                    {
                        sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:F1}{1}THREAT_LOCK{1}{2}{1}{3}\r\n", curT, sep, mslPart, samName));
                        meaningfulEvents++;
                    }
                    else
                    {
                        sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:F1}{1}AI_MSL_LOCK{1}{2} ({3}){1}{4}\r\n", curT, sep, mslPart, samName, resolvedTgt));
                    }
                    continue;
                }
                if (line.EndsWith(" via radar.", StringComparison.OrdinalIgnoreCase))
                {
                    int idxDet = line.IndexOf(" detected ", StringComparison.OrdinalIgnoreCase);
                    if (idxDet > 0)
                    {
                        string obs = line.Substring(0, idxDet).Trim();
                        string tgt = line.Substring(idxDet + " detected ".Length);
                        tgt = tgt.Substring(0, tgt.Length - " via radar.".Length).Trim();
                        bool isMissileTarget = Regex.IsMatch(tgt, @"\b(?:AIM-|ASF-MRM|ASF-SRM|AIRS-T|AGM-|FLKM|HARM|CAGM|Maverick|SideARM|GBU)\b", RegexOptions.IgnoreCase);
                        bool targetIsMe = tgt.Equals(myNick, StringComparison.OrdinalIgnoreCase) || IsPlayerInBracketTag(tgt, myNick);
                        bool obsIsMe = obs.Equals(myNick, StringComparison.OrdinalIgnoreCase) || IsPlayerInBracketTag(obs, myNick);
                        if (targetIsMe && (IsEnemyUnitType(obs) || obs.IndexOf("SAM", StringComparison.OrdinalIgnoreCase) >= 0))
                            sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:F1}{1}RWR_DETECT{1}{2}{1}Радарное облучение\r\n", curT, sep, obs));
                        else if (obsIsMe && !isMissileTarget)
                            sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:F1}{1}RADAR_DETECT{1}PLAYER{1}{2}\r\n", curT, sep, tgt));
                        else if ((obs.IndexOf("Overlord", StringComparison.OrdinalIgnoreCase) >= 0 || obs.IndexOf("AWACS", StringComparison.OrdinalIgnoreCase) >= 0) && !isMissileTarget)
                            sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:F1}{1}RADAR_DETECT{1}{2}{1}{3}\r\n", curT, sep, obs, tgt));
                    }
                    continue;
                }
                if (line.StartsWith("SoftLockActor(", StringComparison.OrdinalIgnoreCase))
                {
                    int p1 = line.IndexOf('(');
                    int p2 = line.LastIndexOf(')');
                    if (p1 >= 0 && p2 > p1)
                    {
                        string inside = line.Substring(p1 + 1, p2 - p1 - 1).Trim();
                        string cleanT = inside.StartsWith("MP Spawn")
                        ? inside.Substring(inside.IndexOf('(') + 1).TrimEnd(')')
                        : StripBracketSuffix(inside);
                        sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:F1}{1}RADAR_LOCK{1}{2}{1}\r\n", curT, sep, cleanT));
                    }
                    continue;
                }
                if (line.StartsWith("Starting missile info for ", StringComparison.OrdinalIgnoreCase) &&
                IsPlayerInBracketTag(line, myNick))
                {
                    string rest = line.Substring("Starting missile info for ".Length);
                    int pOpen = rest.IndexOf('(');
                    lastFiredMsl = pOpen > 0 ? rest.Substring(0, pOpen).Trim() : "AIM-120C";
                    sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:F1}{1}LAUNCH{1}{2}{1}\r\n", curT, sep, lastFiredMsl));
                    meaningfulEvents++;
                    curT += 1.0;
                    continue;
                }
                int idxAcq = line.IndexOf(" acquired ", StringComparison.OrdinalIgnoreCase);
                int idxTgtColon = line.IndexOf("target:", StringComparison.OrdinalIgnoreCase);
                if (idxAcq > 0 && idxTgtColon > idxAcq)
                {
                    string leftSide = line.Substring(0, idxAcq).Trim();
                    string tf = line.Substring(idxTgtColon + "target:".Length).Trim();
                    int p1 = leftSide.IndexOf('(');
                    int p2 = leftSide.LastIndexOf(')');
                    if (p1 > 0 && p2 > p1)
                    {
                        string msl = leftSide.Substring(0, p1).Trim();
                        string sh = leftSide.Substring(p1 + 1, p2 - p1 - 1).Trim();
                        bool shIsMe = sh.Equals(myNick, StringComparison.OrdinalIgnoreCase) || IsPlayerInBracketTag(sh, myNick);
                        if (shIsMe)
                        {
                            string tid = ExtractBracketId(tf);
                            if (!string.IsNullOrEmpty(tid)) targetIdToMsl[tid] = lastFiredMsl;
                            sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:F1}{1}MSL_ACTIVE{1}{2}{1}{3}\r\n", curT, sep, lastFiredMsl, StripBracketSuffix(tf)));
                        }
                        else if (IsPlayerInBracketTag(tf, myNick))
                        {
                            sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:F1}{1}THREAT_LOCK{1}{2}{1}{3}\r\n", curT, sep, msl, sh));
                            meaningfulEvents++;
                        }
                        else
                        {
                            string cleanTf = tf.StartsWith("MP Spawn") ? tf.Substring(tf.IndexOf('(') + 1).TrimEnd(')') : StripBracketSuffix(tf);
                            sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:F1}{1}AI_MSL_LOCK{1}{2} ({3}){1}{4}\r\n", curT, sep, msl, sh, cleanTf));
                        }
                    }
                    continue;
                }
                if (line.StartsWith("Failed engine ", StringComparison.OrdinalIgnoreCase))
                {
                    string part = line.Substring("Failed engine ".Length).Trim();
                    string myAcShort = currentPlayerVeh.Replace("/", "");
                    if (part.IndexOf(myAcShort, StringComparison.OrdinalIgnoreCase) >= 0 && (curT - lastEngineFailLogT) > 15.0)
                    {
                        lastEngineFailLogT = curT;
                        string niceTitle = part.IndexOf("right", StringComparison.OrdinalIgnoreCase) >= 0
                            ? "Отказ правого двигателя"
                            : (part.IndexOf("left", StringComparison.OrdinalIgnoreCase) >= 0 ? "Отказ левого двигателя" : "Отказ двигателя борта");
                        sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:F1}{1}DAMAGE{1}PLAYER{1}{2}\r\n", curT, sep, niceTitle));
                        meaningfulEvents++;
                    }
                    continue;
                }
            }
            if (meaningfulEvents == 0 && battleStarts.Count > 1) continue;
            // Имя файла привязываем к дате/названию миссии (при повторном импорте перезаписывается без дублей!)
            string safeTitle = Regex.Replace(battleTitles[bIdx], @"[^A-Za-z0-9-]", "");
            if (safeTitle.Length > 24) safeTitle = safeTitle.Substring(0, 24);
            string dateTag = File.GetLastWriteTime(logFilePath).ToString("yyyyMMdd_HHmm");
            string outPath = Path.Combine(GetReplaysDir(), string.Format("Battle{0}#{1}{2}.vtolrep", dateTag, bIdx + 1, safeTitle));
            File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8);
            lastCreatedPath = outPath;
        }
        return lastCreatedPath;
    }

    // =====================================================================
    // === ЧТЕНИЕ ШТАТНОГО РЕПЛЕЯ ИГРЫ (SaveData\Replays\<dir>\info.vts) ===
    private static DebriefSession LoadNativeVtolReplay(string inputPath, string preferredPilot)
    {
        string repDir = Path.GetDirectoryName(inputPath);
        string folderName = Path.GetFileName(repDir);
        string infoVtsPath = Path.Combine(repDir, "info.cfg");
        if (!File.Exists(infoVtsPath)) infoVtsPath = Path.Combine(repDir, "info.vts");
        string explicitDataPath = null;

        if (inputPath.EndsWith(".vtr", StringComparison.OrdinalIgnoreCase))
        {
            // Проверяем: является ли .vtr ZIP-архивом или прямым бинарным потоком ReplaySerializer
            byte[] sig = new byte[2];
            using (FileStream fs = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                fs.Read(sig, 0, 2);

            if (sig[0] == 0x50 && sig[1] == 0x4B)
            {
                repDir = Path.Combine(GetReplaysDir(), "_vtr_unpack_" + folderName);
                try { if (Directory.Exists(repDir)) Directory.Delete(repDir, true); } catch { }
                Directory.CreateDirectory(repDir);
                ZipFile.ExtractToDirectory(inputPath, repDir);
                infoVtsPath = Path.Combine(repDir, "info.vts");
            }
            else
            {
                // Прямой бинарный файл потока VTOL VR
                explicitDataPath = inputPath;
                if (!File.Exists(infoVtsPath)) infoVtsPath = "";
            }
        }
        else
        {
            folderName = Path.GetFileName(repDir);
            infoVtsPath = inputPath;
        }

        string infoTxt = (!string.IsNullOrEmpty(infoVtsPath) && File.Exists(infoVtsPath))
            ? File.ReadAllText(infoVtsPath, Encoding.UTF8)
            : "";

        double mapSize = 196608.0;
        string edgeMode = "Water";
        string coastSide = "North";
        string scenName = folderName;
        string campId = "";

        Match mSz = Regex.Match(infoTxt, @"mapSize\s*=\s*([\d\.]+)");
        if (mSz.Success) mapSize = ParseDoubleSafe(GetRegexGroup(mSz, 1), 196608.0);
        Match mEdge = Regex.Match(infoTxt, @"edgeMode\s*=\s*([^\r\n]+)");
        if (mEdge.Success) edgeMode = GetRegexGroup(mEdge, 1).Trim();
        Match mCoast = Regex.Match(infoTxt, @"coastSide\s*=\s*([^\r\n]+)");
        if (mCoast.Success) coastSide = GetRegexGroup(mCoast, 1).Trim();
        Match mSn = Regex.Match(infoTxt, @"scenarioName\s*=\s*([^\r\n]+)");
        if (mSn.Success) scenName = GetRegexGroup(mSn, 1).Trim();
        Match mCid = Regex.Match(infoTxt, @"campaignID\s*=\s*([^\r\n]+)");
        if (mCid.Success) campId = GetRegexGroup(mCid, 1).Trim();
        string scenId = "";
        Match mSid = Regex.Match(infoTxt, @"scenarioID\s*=\s*([^\r\n]+)");
        if (mSid.Success) scenId = GetRegexGroup(mSid, 1).Trim();

        DebriefSession s = new DebriefSession();
        s.SourceFile = infoVtsPath;
        s.Title = string.Format("{0} [{1}]", scenName, folderName);
        s.MapTitle = string.Format("Карта VTOL ({0:F0}x{0:F0} км, {1}/{2})", mapSize / 1000.0, edgeMode, coastSide);
        s.HasFullRadarCoords = true;
        s.PilotName = preferredPilot ?? "Pilot";

        string dataPath = !string.IsNullOrEmpty(explicitDataPath) ? explicitDataPath : Path.Combine(repDir, "data");
        bool parsedBinary = false;
        if (File.Exists(dataPath))
        {
            parsedBinary = ParseBinaryVtrData(s, dataPath, mapSize, preferredPilot);
        }

        // Проверяем, относится ли текущий Player.log именно к этому реплею (по scenarioID или названию миссии)
        string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low", @"Boundless Dynamics, LLC\VTOLVR\Player.log");
        bool logMatchesThisReplay = false;
        if (File.Exists(logPath))
        {
            try
            {
                string logHead = File.ReadAllText(logPath, Encoding.UTF8);
                if ((!string.IsNullOrEmpty(scenId) && logHead.IndexOf(scenId, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(scenName) && logHead.IndexOf(scenName, StringComparison.OrdinalIgnoreCase) >= 0))
                    logMatchesThisReplay = true;
            }
            catch { }
        }

        if (logMatchesThisReplay)
        {
            try
            {
                string repFile = ImportFromPlayerLog(logPath, preferredPilot);
                if (!string.IsNullOrEmpty(repFile) && File.Exists(repFile))
                {
                    DebriefSession logSession = LoadVtolRep(repFile, preferredPilot);
                    if (parsedBinary && s.Entities.Count > 0)
                    {
                        s.PlayerKills = logSession.PlayerKills;
                        s.AlliedKills = logSession.AlliedKills;
                        s.PlayerLaunches = logSession.PlayerLaunches;
                        s.RwrSpikesCount = logSession.RwrSpikesCount;
                        s.ThreatMissilesCount = logSession.ThreatMissilesCount;
                        s.RwrSpikesCount = logSession.RwrSpikesCount;
                        s.HasExactTelemetry = true;
                        s.IsReconstructedFromLog = false;
                        s.Events.Clear();
                        s.Mistakes.Clear();
                        s.HasExactTelemetry = true;
                        s.IsReconstructedFromLog = false;

                        List<DebriefEntity> playerSorties = new List<DebriefEntity>();
                        foreach (DebriefEntity e in s.Entities)
                            if (e.IsPlayer) playerSorties.Add(e);
                        playerSorties.Sort((a, b) => a.FirstTime.CompareTo(b.FirstTime));

                        double timeOffset = 0;
                        if (s.Events.Count > 0 && s.Duration > 10 && s.Events[s.Events.Count - 1].Time > s.Duration + 30.0)
                            timeOffset = s.Events[s.Events.Count - 1].Time - s.Duration;

                        int kiaIdx = 0;
                        int respawnIdx = 1;
                        List<TacticalEvent> validLogEvents = new List<TacticalEvent>();

                        foreach (TacticalEvent ev in s.Events)
                        {
                            // Все пуски, угрозы, попадания ракет и гибель бортов строим строго по 3D-трекам VTR;
                            // из лога берём только внутренние отказы систем самолёта (DAMAGE)
                            if (ev.Kind != "DAMAGE") continue;

                            if (timeOffset > 0 && ev.Time > timeOffset)
                                ev.Time = ev.Time - timeOffset;
                            if (ev.Time < 2.0 || ev.Time > s.Duration) continue;

                            foreach (DebriefEntity e in playerSorties)
                            {
                                DebriefSample ps = e.GetStateAt(ev.Time);
                                if (ps != null) { ev.WorldX = ps.X; ev.WorldY = ps.Y; break; }
                            }
                            validLogEvents.Add(ev);
                        }
                        s.Events = validLogEvents;
                        s.Mistakes.Clear();
                    }
                }
            }
            catch { }
        }

        // Добавляем чистые события из самого 3D-потока VTR (пуски ваших ракет и пуски союзников)
        BuildNativeVtrEvents(s);
        BuildPilotInsights(s);
        s.Events.Sort((a, b) => a.Time.CompareTo(b.Time));

        ComputeSessionBounds(s);
        // Определяем реальные мировые границы карты из координат юнитов и heightmap.pngb:
        // Если есть отрицательные Y (как на карте Strategic Bombing: Y от -43 км до +51 км, X от 0 до +98 км),
        // карта имеет размер 2*mapSize с началом X=0, Y=-mapSize (или X=-mapSize при отрицательных X).
        bool negX = s.BoundsMinX < -500.0;
        bool negY = s.BoundsMinY < -500.0;
        double realMapSpan = (negX || negY || s.BoundsMaxX > mapSize + 500.0 || s.BoundsMaxY > mapSize + 500.0)
            ? (mapSize * 2.0)
            : mapSize;
        double realMinX = negX ? (-realMapSpan * 0.5) : 0.0;
        double realMinY = negY ? (-realMapSpan * 0.5) : 0.0;

        if (s.TerrainBitmap != null) { s.TerrainBitmap.Dispose(); s.TerrainBitmap = null; }
        s.MapMinX = realMinX;
        s.MapMinY = realMinY;
        s.MapSizeMeters = realMapSpan;

        string hmPath = Path.Combine(repDir, "heightmap.pngb");
        if (File.Exists(hmPath))
            TryBuildTerrainFromHeightmapPngb(s, hmPath, mapSize);

        return s;
    }
    private static string LastVtrDiagnostic = "";


    private static byte[] TryDecompressLz4AtOffset(byte[] fileBytes, int headerOffset)
    {
        if (fileBytes.Length <= headerOffset + 8) return null;
        int uncompLen = BitConverter.ToInt32(fileBytes, headerOffset);
        if (uncompLen <= fileBytes.Length || uncompLen > 64 * 1024 * 1024) return null;

        // Пробуем старт LZ4-потока сразу после uncompLen (+4) и после пары [uncompLen][compLen] (+8)
        int[] dataStarts = new int[] { headerOffset + 4, headerOffset + 8 };
        foreach (int startPos in dataStarts)
        {
            try
            {
                byte[] dst = new byte[uncompLen];
                int srcPos = startPos;
                int dstPos = 0;

                while (srcPos < fileBytes.Length && dstPos < uncompLen)
                {
                    int token = fileBytes[srcPos++];
                    int litLen = token >> 4;
                    if (litLen == 15)
                    {
                        int b;
                        do { if (srcPos >= fileBytes.Length) { dstPos = -1; break; } b = fileBytes[srcPos++]; litLen += b; } while (b == 255);
                        if (dstPos < 0) break;
                    }
                    if (srcPos + litLen > fileBytes.Length || dstPos + litLen > uncompLen) break;
                    Buffer.BlockCopy(fileBytes, srcPos, dst, dstPos, litLen);
                    srcPos += litLen;
                    dstPos += litLen;
                    if (dstPos >= uncompLen || srcPos + 2 > fileBytes.Length) break;

                    int offset = fileBytes[srcPos] | (fileBytes[srcPos + 1] << 8);
                    srcPos += 2;
                    if (offset <= 0 || offset > dstPos) { dstPos = -1; break; }

                    int matchLen = (token & 0x0F) + 4;
                    if ((token & 0x0F) == 15)
                    {
                        int b;
                        do { if (srcPos >= fileBytes.Length) { dstPos = -1; break; } b = fileBytes[srcPos++]; matchLen += b; } while (b == 255);
                        if (dstPos < 0) break;
                    }
                    int matchSrc = dstPos - offset;
                    for (int k = 0; k < matchLen && dstPos < uncompLen; k++)
                        dst[dstPos++] = dst[matchSrc + k];
                }
                if (dstPos > uncompLen / 2) return dst;
            }
            catch { }
        }
        return null;
    }

    private static byte[] DecompressVtrIfLz4(byte[] fileBytes)
    {
        if (fileBytes == null || fileBytes.Length < 16) return fileBytes;
        StringBuilder hex = new StringBuilder();
        for (int i = 0; i < Math.Min(32, fileBytes.Length); i++)
            hex.Append(fileBytes[i].ToString("X2") + " ");
        LastVtrDiagnostic = string.Format("Размер файла={0} байт | Первые 32 байта HEX: {1}", fileBytes.Length, hex.ToString().Trim());

        int[] offsets = new int[] { 0, 4, 8, 12, 16 };
        foreach (int off in offsets)
        {
            byte[] res = TryDecompressLz4AtOffset(fileBytes, off);
            if (res != null)
            {
                LastVtrDiagnostic += string.Format(" | LZ4 OK на смещении +{0} -> {1} байт", off, res.Length);
                return res;
            }
        }
        LastVtrDiagnostic += " | LZ4 не сработал ни на одном смещении";
        return fileBytes;
    }

    private static byte[] UnpackViaGameDlls(byte[] compressedBytes, out string info)
    {
        StringBuilder log = new StringBuilder();
        try
        {
            string root = FindVtolRootDir();
            if (string.IsNullOrEmpty(root)) { info = "Папка VTOL VR не найдена"; return null; }
            string managed = Path.Combine(root, @"VTOLVR_Data\Managed");

            AppDomain.CurrentDomain.AssemblyResolve += (s, args) => {
                string dllName = new System.Reflection.AssemblyName(args.Name).Name + ".dll";
                string p = Path.Combine(managed, dllName);
                return File.Exists(p) ? System.Reflection.Assembly.LoadFrom(p) : null;
            };

            // 1. Сканируем весь заголовок файла (до 16 КБ) на точный старт LZ4-блока через K4os.LZ4Codec.Decode
            string lz4Dll = Path.Combine(managed, "K4os.Compression.LZ4.dll");
            if (File.Exists(lz4Dll))
            {
                System.Reflection.Assembly asm = System.Reflection.Assembly.LoadFrom(lz4Dll);
                Type codecType = asm.GetType("K4os.Compression.LZ4.LZ4Codec");
                if (codecType != null)
                {
                    System.Reflection.MethodInfo decMethod = codecType.GetMethod("Decode",
                        new Type[] { typeof(byte[]), typeof(int), typeof(int), typeof(byte[]), typeof(int), typeof(int) });
                    if (decMethod != null)
                    {
                        byte[] dst = new byte[4 * 1024 * 1024];
                        int maxOff = Math.Min(compressedBytes.Length - 16, 16384);
                        for (int off = 0; off < maxOff; off++)
                        {
                            try
                            {
                                int written = (int)decMethod.Invoke(null, new object[] { compressedBytes, off, compressedBytes.Length - off, dst, 0, dst.Length });
                                if (written > 10000)
                                {
                                    byte[] combined = new byte[off + written];
                                    Buffer.BlockCopy(compressedBytes, 0, combined, 0, off);
                                    Buffer.BlockCopy(dst, 0, combined, off, written);
                                    log.AppendFormat("K4os.LZ4Codec(+{0}) OK -> {1} байт; ", off, combined.Length);

                                    try
                                    {
                                        string gameDllHmPath = Path.Combine(managed, "Assembly-CSharp.dll");
                                        if (File.Exists(gameDllHmPath))
                                        {
                                            System.Reflection.Assembly gAsm = System.Reflection.Assembly.LoadFrom(gameDllHmPath);
                                            Type vtrType = gAsm.GetType("VTResources");
                                            if (vtrType != null)
                                            {
                                                foreach (System.Reflection.MethodInfo mGustave in vtrType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance))
                                                {
                                                    if (mGustave.Name != "GetTextureGustave") continue;
                                                    System.Reflection.MethodBody mb = mGustave.GetMethodBody();
                                                    byte[] il = mb != null ? mb.GetILAsByteArray() : null;
                                                    StringBuilder sbIl = new StringBuilder();
                                                    if (il != null)
                                                    {
                                                        for (int i = 0; i < il.Length; i++)
                                                        {
                                                            sbIl.Append(il[i].ToString("X2") + " ");
                                                            if ((il[i] == 0x28 || il[i] == 0x6F || il[i] == 0x73) && i + 4 < il.Length)
                                                            {
                                                                int tok = BitConverter.ToInt32(il, i + 1);
                                                                try
                                                                {
                                                                    System.Reflection.MemberInfo mem = mGustave.Module.ResolveMember(tok);
                                                                    if (mem != null) sbIl.Append("[" + mem.DeclaringType.Name + "." + mem.Name + "] ");
                                                                }
                                                                catch { }
                                                            }
                                                        }
                                                    }
                                                    LastVtrDiagnostic += " | GustaveIL(" + il.Length + "): " + sbIl.ToString();
                                                }
                                            }
                                        }

                                    }
                                    catch (Exception gEx) { log.Append("GameDllErr: " + gEx.Message); }

                                    info = log.ToString();
                                    return combined;
                                }
                            }
                            catch { }
                        }
                    }
                }
            }

            // 2. Инспектируем родной ReplaySerializer из Assembly-CSharp.dll (с перехватом ReflectionTypeLoadException)
            string gameDllPath = Path.Combine(managed, "Assembly-CSharp.dll");
            if (File.Exists(gameDllPath))
            {
                System.Reflection.Assembly gAsm = System.Reflection.Assembly.LoadFrom(gameDllPath);
                Type vtrType = gAsm.GetType("VTResources");
                if (vtrType != null)
                {
                    StringBuilder vtrMethods = new StringBuilder();
                    foreach (System.Reflection.MethodInfo m in vtrType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                    {
                        if (m.Name.IndexOf("Tex", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            m.Name.IndexOf("Png", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            m.Name.IndexOf("Image", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            m.Name.IndexOf("Bytes", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            List<string> ps = new List<string>();
                            foreach (System.Reflection.ParameterInfo pi in m.GetParameters())
                                ps.Add(pi.ParameterType.Name + " " + pi.Name);
                            vtrMethods.AppendFormat("{0} {1}({2}); ", m.ReturnType.Name, m.Name, string.Join(", ", ps.ToArray()));
                        }
                    }
                    LastVtrDiagnostic += " | VTResources: " + vtrMethods.ToString();
                }
            }
        }
        catch (Exception ex)
        {
            log.Append("Err: " + (ex.InnerException != null ? ex.InnerException.Message : ex.Message));
        }
        info = log.ToString();
        return null;
    }

    private static byte[] ExtractUncompressedVtrStream(byte[] fileBytes, out string diag)
    {
        diag = string.Format("Файл={0} байт", fileBytes.Length);
        if (fileBytes.Length < 32) return fileBytes;

        string dllInfo;
        byte[] dllUnpacked = UnpackViaGameDlls(fileBytes, out dllInfo);
        if (!string.IsNullOrEmpty(dllInfo)) diag += " | " + dllInfo;
        if (dllUnpacked != null) return dllUnpacked;

        int maxScan = Math.Min(fileBytes.Length - 16, 32768);
        for (int off = 0; off < maxScan; off++)
        {
            int uncompLen = BitConverter.ToInt32(fileBytes, off);
            if (uncompLen > fileBytes.Length && uncompLen < 32 * 1024 * 1024)
            {
                byte[] unpacked = TryDecompressLz4AtOffset(fileBytes, off);
                if (unpacked != null && unpacked.Length > 1024)
                {
                    diag += string.Format(" | LZ4 распакован со смещения +{0}: {1} байт", off, unpacked.Length);
                    return unpacked;
                }
            }
        }


        // Проверяем также случай, когда uncompLen записан в последние 4 байта файла
        int tailLen = BitConverter.ToInt32(fileBytes, fileBytes.Length - 4);
        if (tailLen > fileBytes.Length && tailLen < 64 * 1024 * 1024)
        {
            byte[] trimmed = new byte[fileBytes.Length];
            Buffer.BlockCopy(fileBytes, 4, trimmed, 4, fileBytes.Length - 8);
            byte[] tailBytes = BitConverter.GetBytes(tailLen);
            Buffer.BlockCopy(tailBytes, 0, trimmed, 0, 4);
            byte[] unpacked = TryDecompressLz4AtOffset(trimmed, 0);
            if (unpacked != null && unpacked.Length > 1024)
            {
                diag += string.Format(" | LZ4 распакован (tail-size): {0} байт", unpacked.Length);
                return unpacked;
            }
        }

        diag += " | LZ4-заголовок не найден (читаем напрямую)";
        return fileBytes;
    }

    private static bool TryLoadViaNativeGameSerializer(DebriefSession session, string vtrFilePath, double mapSize, string preferredPilot, out string diagLog)
    {
        StringBuilder log = new StringBuilder();
        try
        {
            string root = FindVtolRootDir();
            if (string.IsNullOrEmpty(root)) { diagLog = "VTOL root not found"; return false; }
            string managed = Path.Combine(root, @"VTOLVR_Data\Managed");
            string gameDllPath = Path.Combine(managed, "Assembly-CSharp.dll");
            if (!File.Exists(gameDllPath)) { diagLog = "Assembly-CSharp.dll not found"; return false; }

            AppDomain.CurrentDomain.AssemblyResolve += (s, args) => {
                string dllName = new System.Reflection.AssemblyName(args.Name).Name + ".dll";
                string p = Path.Combine(managed, dllName);
                return File.Exists(p) ? System.Reflection.Assembly.LoadFrom(p) : null;
            };

            System.Reflection.Assembly gAsm = System.Reflection.Assembly.LoadFrom(gameDllPath);
            Type serType = gAsm.GetType("ReplaySystem.ReplaySerializer");
            Type recType = gAsm.GetType("ReplaySystem.ReplayRecorder");
            if (serType == null || recType == null) { diagLog = "Replay types not found"; return false; }

            // Создаём экземпляры без запуска Unity-движка (через GetUninitializedObject)
            object serializer = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(serType);
            object recorder = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(recType);

            // Инициализируем внутренние словари/списки у recorder и serializer
            System.Reflection.BindingFlags bf = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            foreach (System.Reflection.FieldInfo fi in serType.GetFields(bf))
            {
                if (fi.GetValue(serializer) != null) continue;
                try
                {
                    if (fi.FieldType == typeof(byte[]))
                        fi.SetValue(serializer, new byte[4 * 1024 * 1024]);
                    else if (fi.FieldType.IsGenericType || fi.FieldType.GetConstructor(Type.EmptyTypes) != null)
                        fi.SetValue(serializer, Activator.CreateInstance(fi.FieldType));
                }
                catch { }
            }
            foreach (System.Reflection.FieldInfo fi in recType.GetFields(bf))
            {
                if (fi.GetValue(recorder) != null) continue;
                try
                {
                    if (fi.FieldType.IsGenericType || fi.FieldType.GetConstructor(Type.EmptyTypes) != null)
                        fi.SetValue(recorder, Activator.CreateInstance(fi.FieldType));
                }
                catch { }
            }

            // 1. Отключаем логгер Unity, чтобы вызов Debug.Log не обращался к отсутствующему UnityPlayer.dll
            try
            {
                string uCoreDll = Path.Combine(managed, "UnityEngine.CoreModule.dll");
                if (File.Exists(uCoreDll))
                {
                    System.Reflection.Assembly uAsm = System.Reflection.Assembly.LoadFrom(uCoreDll);
                    Type dbgType = uAsm.GetType("UnityEngine.Debug");
                    if (dbgType != null)
                    {
                        System.Reflection.PropertyInfo pLogger = dbgType.GetProperty("unityLogger");
                        object loggerObj = pLogger != null ? pLogger.GetValue(null, null) : null;
                        if (loggerObj != null)
                        {
                            System.Reflection.PropertyInfo pEn = loggerObj.GetType().GetProperty("logEnabled");
                            if (pEn != null) pEn.SetValue(loggerObj, false, null);
                        }
                    }
                }
            }
            catch { }

            // 2. Вызываем обязательную инициализацию словаря типов ReplaySerializer.CreateTypeDictionary()
            System.Reflection.MethodInfo mCreateDict = serType.GetMethod("CreateTypeDictionary", bf);
            if (mCreateDict != null) mCreateDict.Invoke(serializer, null);

            // 3. Вызываем родной ReplaySerializer.Deserialize(string filepath, ReplayRecorder recorder)
            System.Reflection.MethodInfo deserMethod = serType.GetMethod("Deserialize", new Type[] { typeof(string), recType });
            if (deserMethod == null) deserMethod = serType.GetMethod("LoadFromFile", new Type[] { typeof(string), recType });
            if (deserMethod == null) { diagLog = "Deserialize(string, recorder) not found"; return false; }

            object okObj = deserMethod.Invoke(serializer, new object[] { vtrFilePath, recorder });
            bool ok = (okObj is bool) && (bool)okObj;
            log.AppendFormat("NativeReplaySerializer.Deserialize={0}; ", ok);

            System.Reflection.FieldInfo fDuration = recType.GetField("<totalDuration>k__BackingField", bf);
            float totalDur = fDuration != null ? (float)fDuration.GetValue(recorder) : 0f;
            log.AppendFormat("totalDuration={0:F1}s; ", totalDur);

            System.Reflection.FieldInfo fEntities = recType.GetField("trackedEntities", bf);
            System.Reflection.FieldInfo fTracks = recType.GetField("motionTracks", bf);
            System.Collections.IList entList = fEntities != null ? fEntities.GetValue(recorder) as System.Collections.IList : null;
            System.Collections.IDictionary trackDict = fTracks != null ? fTracks.GetValue(recorder) as System.Collections.IDictionary : null;

            log.AppendFormat("trackedEntities={0}; motionTracks={1}; ",
                entList != null ? entList.Count : -1,
                trackDict != null ? trackDict.Count : -1);

            if (trackDict == null || trackDict.Count == 0)
            {
                diagLog = log.ToString();
                return false;
            }

            // Извлекаем 100% точные имена, родные команды (Teams.Allied / Teams.Enemy) и parentId носителей из объектов ReplayEntity
            Dictionary<int, string> idToRealName = new Dictionary<int, string>();
            Dictionary<int, string> idToNativeTeam = new Dictionary<int, string>();
            Dictionary<int, int> idToParentId = new Dictionary<int, int>();

            try
            {
                System.Collections.Generic.Queue<object> q = new System.Collections.Generic.Queue<object>();
                System.Collections.Generic.HashSet<object> visited = new System.Collections.Generic.HashSet<object>();
                q.Enqueue(recorder);
                q.Enqueue(serializer);

                while (q.Count > 0)
                {
                    object cur = q.Dequeue();
                    if (cur == null || visited.Contains(cur)) continue;
                    Type ct = cur.GetType();
                    if (ct.IsPrimitive || ct == typeof(string) || ct.IsEnum || ct == typeof(byte[])) continue;
                    visited.Add(cur);

                    if (cur is System.Collections.IDictionary)
                    {
                        foreach (System.Collections.DictionaryEntry entry in (System.Collections.IDictionary)cur)
                            if (entry.Value != null) q.Enqueue(entry.Value);
                        continue;
                    }
                    if (cur is System.Collections.IEnumerable)
                    {
                        foreach (object item in (System.Collections.IEnumerable)cur)
                            if (item != null) q.Enqueue(item);
                        continue;
                    }

                    System.Reflection.FieldInfo fId = ct.GetField("id", bf);
                    System.Reflection.FieldInfo fMeta = ct.GetField("metaData", bf);
                    if (fId != null && fId.FieldType == typeof(int) && fMeta != null)
                    {
                        int eid = (int)fId.GetValue(cur);
                        object metaObj = fMeta.GetValue(cur);

                        foreach (System.Reflection.FieldInfo ef in ct.GetFields(bf))
                        {
                            object ev = ef.GetValue(cur);
                            if (ev == null) continue;
                            if (ef.Name.Equals("team", StringComparison.OrdinalIgnoreCase) || ef.FieldType.Name == "Teams")
                                idToNativeTeam[eid] = ev.ToString().IndexOf("Enemy", StringComparison.OrdinalIgnoreCase) >= 0 ? "Enemies" : "Allies";
                            else if (ef.FieldType == typeof(int) && ef.Name.IndexOf("parent", StringComparison.OrdinalIgnoreCase) >= 0)
                                idToParentId[eid] = (int)ev;
                        }

                        if (metaObj != null && !idToRealName.ContainsKey(eid))
                        {
                            foreach (System.Reflection.FieldInfo mf in metaObj.GetType().GetFields(bf))
                            {
                                object mv = mf.GetValue(metaObj);
                                if (mv == null) continue;
                                if (mf.FieldType == typeof(string) && !string.IsNullOrEmpty(mv as string))
                                    idToRealName[eid] = mv as string;
                                else if (mf.Name.Equals("team", StringComparison.OrdinalIgnoreCase) || mf.FieldType.Name == "Teams")
                                    idToNativeTeam[eid] = mv.ToString().IndexOf("Enemy", StringComparison.OrdinalIgnoreCase) >= 0 ? "Enemies" : "Allies";
                                else if (mf.FieldType == typeof(int) && mf.Name.IndexOf("parent", StringComparison.OrdinalIgnoreCase) >= 0)
                                    idToParentId[eid] = (int)mv;
                            }
                        }
                    }

                    if (ct.Namespace != null && ct.Namespace.StartsWith("ReplaySystem"))
                    {
                        foreach (System.Reflection.FieldInfo fi in ct.GetFields(bf))
                        {
                            try
                            {
                                object fv = fi.GetValue(cur);
                                if (fv != null && !visited.Contains(fv)) q.Enqueue(fv);
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }

            string myNick = preferredPilot ?? "";
            int totalFrames = 0;
            double maxT = totalDur > 1.0f ? totalDur : 1.0;
            Dictionary<int, DebriefEntity> idToEntity = new Dictionary<int, DebriefEntity>();

            // Точный тип юнита из штатного реплея: ReplayRecorder.ReplayEntity.entityType
            // 0/1 — самолёты сторон A/B, 2/3 — наземные A/B, 4/5 — морские A/B, 6 — ракета, 7/8 — вертолёты A/B
            Dictionary<int, int> idToEntityType = new Dictionary<int, int>();
            try
            {
                System.Collections.IDictionary entDict = recType.GetField("entityDict", bf).GetValue(recorder) as System.Collections.IDictionary;
                if (entDict != null)
                {
                    foreach (System.Collections.DictionaryEntry ede in entDict)
                    {
                        object re = ede.Value;
                        if (re == null) continue;
                        Type ret = re.GetType();
                        System.Reflection.FieldInfo fIdE = ret.GetField("id", bf);
                        System.Reflection.FieldInfo fTypeE = ret.GetField("entityType", bf);
                        if (fIdE == null || fTypeE == null) continue;
                        int eid = Convert.ToInt32(fIdE.GetValue(re));
                        int etype = Convert.ToInt32(fTypeE.GetValue(re));
                        idToEntityType[eid] = etype;
                    }
                }
            }
            catch { }

            foreach (System.Collections.DictionaryEntry de in trackDict)
            {
                int entId = Convert.ToInt32(de.Key);
                object mTrack = de.Value;
                if (mTrack == null) continue;

                string name = idToRealName.ContainsKey(entId) ? idToRealName[entId] : ("Unit_" + entId);
                Type mTrackType = mTrack.GetType();

                System.Collections.IList keys = null;
                Type curT = mTrackType;
                while (curT != null && keys == null)
                {
                    foreach (System.Reflection.FieldInfo tf in curT.GetFields(bf | System.Reflection.BindingFlags.DeclaredOnly))
                    {
                        if (typeof(System.Collections.IList).IsAssignableFrom(tf.FieldType))
                        {
                            keys = tf.GetValue(mTrack) as System.Collections.IList;
                            if (keys != null) break;
                        }
                    }
                    curT = curT.BaseType;
                }
                if (keys == null || keys.Count == 0) continue;

                bool isGround = IsGroundOrSeaUnit(name);
                bool isMissile = Regex.IsMatch(name, @"\b(?:ASMRM|AIM-|ASF-MRM|ASF-SRM|AIRS-T|AGM-|GBU-|MK-|HARM|FLKM|BSM|9M311|SaawMissile|CAGM)\b", RegexOptions.IgnoreCase);
                bool isMe = !string.IsNullOrEmpty(myNick) && IsPlayerInBracketTag(name, myNick);

                int entTypeCode = -1;
                if (idToEntityType.TryGetValue(entId, out entTypeCode) == false) entTypeCode = -1;
                if (entTypeCode >= 0)
                {
                    // Штатная классификация из файла реплея — без эвристик по имени
                    isMissile = entTypeCode == 6;
                    isGround = entTypeCode == 2 || entTypeCode == 3 || entTypeCode == 4 || entTypeCode == 5;
                }

                bool isExplicitAlly = isMe ||
                      name.IndexOf("Allied", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      name.IndexOf("TasherMan", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isExplicitEnemy = !isExplicitAlly && (
                                       IsEnemyUnitType(name) ||
                                       name.IndexOf("aerograch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       name.IndexOf("moso", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       name.IndexOf("arnie", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       name.IndexOf("idk", StringComparison.OrdinalIgnoreCase) >= 0);

                // entityType: чётные = сторона A (союзники), нечётные = сторона B (противник)
                string coalition;
                if (entTypeCode >= 0 && entTypeCode != 6)
                    coalition = (entTypeCode % 2 == 0) ? "Allies" : "Enemies";
                else
                    coalition = isExplicitEnemy ? "Enemies" : "Allies";

                string unitType = isMissile ? "Missile" : (isGround ? "Ground" : "Air");
                string unitClass = isMissile ? "Missile"
                                 : (entTypeCode == 4 || entTypeCode == 5) ? "Sea"
                                 : (entTypeCode == 7 || entTypeCode == 8) ? "Chopper"
                                 : (isGround ? "Ground" : "Air");
                string roleTag;
                if (isMissile) roleTag = "Missile";
                else if (unitClass == "Sea") roleTag = "Ship";
                else if (unitClass == "Ground")
                    roleTag = Regex.IsMatch(name, @"(?:SAM|Radar|SAAW|Phalanx|C-RAM|CIWS|VLS|Launcher|Turret)", RegexOptions.IgnoreCase) ? "SAM" : "Ground";
                else roleTag = DetectUnitRole(name);

                DebriefEntity ent = new DebriefEntity
                {
                    Id = "vtr_" + entId,
                    Name = name,
                    Pilot = name,
                    Type = unitType,
                    Class = unitClass,
                    Coalition = coalition,
                    RoleTag = roleTag,
                    IsPlayer = isMe
                };

                foreach (object kf in keys)
                {
                    if (kf == null) continue;
                    Type kfType = kf.GetType();
                    float kt = 0f;
                    double wx = 0, wy = 0, wz = 0;

                    double vx = 0, vy = 0, vz = 0;
                    double qx = 0, qy = 0, qz = 0, qw = 0;
                    bool hasQuat = false;

                    foreach (System.Reflection.FieldInfo kff in kfType.GetFields(bf))
                    {
                        if (kff.FieldType == typeof(float) && kff.Name.Equals("t", StringComparison.OrdinalIgnoreCase))
                            kt = (float)kff.GetValue(kf);
                        else if (kff.FieldType.Name == "FixedPoint")
                        {
                            object fp = kff.GetValue(kf);
                            Type fpT = fp.GetType();
                            System.Reflection.FieldInfo fGp = fpT.GetField("globalPoint", bf);
                            if (fGp != null)
                            {
                                object v3d = fGp.GetValue(fp);
                                Type v3T = v3d.GetType();
                                wx = Convert.ToDouble(v3T.GetField("x", bf).GetValue(v3d));
                                wy = Convert.ToDouble(v3T.GetField("y", bf).GetValue(v3d));
                                wz = Convert.ToDouble(v3T.GetField("z", bf).GetValue(v3d));
                            }
                            else
                            {
                                System.Reflection.FieldInfo fx = fpT.GetField("x", bf);
                                System.Reflection.FieldInfo fy = fpT.GetField("y", bf);
                                System.Reflection.FieldInfo fz = fpT.GetField("z", bf);
                                if (fx != null && fy != null && fz != null)
                                {
                                    wx = Convert.ToDouble(fx.GetValue(fp));
                                    wy = Convert.ToDouble(fy.GetValue(fp));
                                    wz = Convert.ToDouble(fz.GetValue(fp));
                                }
                            }
                        }
                        else if (kff.FieldType.Name == "Vector3" && kff.Name.Equals("velocity", StringComparison.OrdinalIgnoreCase))
                        {
                            object v3 = kff.GetValue(kf);
                            if (v3 != null)
                            {
                                Type v3T = v3.GetType();
                                vx = Convert.ToDouble(v3T.GetField("x", bf).GetValue(v3));
                                vy = Convert.ToDouble(v3T.GetField("y", bf).GetValue(v3));
                                vz = Convert.ToDouble(v3T.GetField("z", bf).GetValue(v3));
                            }
                        }
                        else if (kff.FieldType.Name == "Quaternion" && kff.Name.Equals("rotation", StringComparison.OrdinalIgnoreCase))
                        {
                            object q = kff.GetValue(kf);
                            if (q != null)
                            {
                                Type qT = q.GetType();
                                qx = Convert.ToDouble(qT.GetField("x", bf).GetValue(q));
                                qy = Convert.ToDouble(qT.GetField("y", bf).GetValue(q));
                                qz = Convert.ToDouble(qT.GetField("z", bf).GetValue(q));
                                qw = Convert.ToDouble(qT.GetField("w", bf).GetValue(q));
                                hasQuat = true;
                            }
                        }
                        else if (kff.FieldType.Name == "Vector3" && wx == 0 && wz == 0)
                        {
                            object v3 = kff.GetValue(kf);
                            Type v3T = v3.GetType();
                            wx = Convert.ToDouble(v3T.GetField("x", bf).GetValue(v3));
                            wy = Convert.ToDouble(v3T.GetField("y", bf).GetValue(v3));
                            wz = Convert.ToDouble(v3T.GetField("z", bf).GetValue(v3));
                        }
                    }

                    double hdg = 0, spd = 0, pitch = 0, roll = 0;
                    if (hasQuat)
                    {
                        // Точная ориентация из кватерниона штатного реплея (Unity: x,y,z,w)
                        double fx = 2.0 * (qx * qz + qw * qy);
                        double fy = 2.0 * (qy * qz - qw * qx);
                        double fz = 1.0 - 2.0 * (qx * qx + qy * qy);
                        hdg = (Math.Atan2(fx, fz) * 180.0 / Math.PI + 360.0) % 360.0;
                        pitch = Math.Asin(Math.Max(-1.0, Math.Min(1.0, fy))) * 180.0 / Math.PI;

                        // Крен: наклон вектора "вправо" и "вверх" относительно мировой вертикали
                        double rx = 1.0 - 2.0 * (qy * qy + qz * qz);
                        double ry = 2.0 * (qx * qy + qw * qz);
                        double uy = 1.0 - 2.0 * (qx * qx + qz * qz);
                        if (Math.Abs(ry) > 1e-4 || Math.Abs(uy) > 1e-4)
                            roll = -Math.Atan2(ry, uy) * 180.0 / Math.PI;
                        spd = Math.Sqrt(vx * vx + vy * vy + vz * vz);
                    }
                    else if (ent.Track.Count > 0)
                    {
                        DebriefSample prev = ent.Track[ent.Track.Count - 1];
                        double dt = kt - prev.Time;
                        double dx = wx - prev.X;
                        double dy = wy - prev.Alt;
                        double dz = wz - prev.Y;
                        double dist2d = Math.Sqrt(dx * dx + dz * dz);
                        double dist3d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                        if (dt > 0.01)
                        {
                            spd = dist3d / dt;
                            if (dist2d > 0.5)
                                pitch = Math.Atan2(dy, dist2d) * 180.0 / Math.PI;
                            else
                                pitch = prev.Pitch;
                        }
                        hdg = dist2d > 0.5 ? ((Math.Atan2(dx, dz) * 180.0 / Math.PI + 360.0) % 360.0) : prev.Heading;
                        if (dt > 0.01 && dist2d > 5.0)
                        {
                            double dHdg = ((hdg - prev.Heading + 540.0) % 360.0) - 180.0;
                            double turnRateRad = (dHdg * Math.PI / 180.0) / dt;
                            double bankRad = Math.Atan((spd * turnRateRad) / 9.81);
                            double rawRoll = Math.Max(-82.0, Math.Min(82.0, bankRad * 180.0 / Math.PI));
                            roll = prev.Roll + (rawRoll - prev.Roll) * 0.45;
                        }
                        else
                        {
                            roll = prev.Roll * 0.85;
                        }
                    }
                    ent.Track.Add(new DebriefSample { Time = kt, X = wx, Y = wz, Alt = wy, Heading = hdg, Pitch = pitch, Roll = roll, Speed = spd, VX = vx, VY = vy, VZ = vz });
                    if (kt < ent.FirstTime) ent.FirstTime = kt;
                    if (kt > ent.LastTime) ent.LastTime = kt;
                    if (kt > maxT) maxT = kt;
                    totalFrames++;
                }
                if (ent.Track.Count > 0)
                {
                    idToEntity[entId] = ent;
                    session.Entities.Add(ent);
                }
            }

            // 1. Определяем базовую точку спавна игрока и центр вражеской базы для 100% точного разделения свой/чужой на любой карте!
            double myBaseX = double.NaN, myBaseY = double.NaN;
            double enemySumX = 0, enemySumY = 0;
            int enemyCount = 0;
            foreach (DebriefEntity e in session.Entities)
            {
                if (e.Type == "Missile" || e.Track.Count == 0) continue;
                DebriefSample fs = e.Track[0];
                if (e.IsPlayer && double.IsNaN(myBaseX)) { myBaseX = fs.X; myBaseY = fs.Y; }
                if (e.Coalition == "Enemies") { enemySumX += fs.X; enemySumY += fs.Y; enemyCount++; }
            }
            double enemyBaseX = enemyCount > 0 ? (enemySumX / enemyCount) : double.NaN;
            double enemyBaseY = enemyCount > 0 ? (enemySumY / enemyCount) : double.NaN;

            foreach (DebriefEntity e in session.Entities)
            {
                if (e.Type == "Missile" || e.Track.Count == 0) continue;
                bool isProtectedAlly = e.IsPlayer ||
                                       e.Name.IndexOf("Allied", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       e.Name.IndexOf("TasherMan", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isProtectedAlly && !double.IsNaN(myBaseX) && !double.IsNaN(enemyBaseX))
                {
                    DebriefSample fs = e.Track[0];
                    double distToMe = Math.Sqrt(Math.Pow(fs.X - myBaseX, 2) + Math.Pow(fs.Y - myBaseY, 2));
                    double distToEnemy = Math.Sqrt(Math.Pow(fs.X - enemyBaseX, 2) + Math.Pow(fs.Y - enemyBaseY, 2));
                    // Если юнит/корабль/ЗРК/игрок появился на вражеской половине карты — он противник!
                    if (distToEnemy < distToMe * 0.85)
                        e.Coalition = "Enemies";
                }

                if (e.Type == "Ground" && DetectUnitRole(e.Name) == "SAM")
                {
                    DebriefSample fs = e.Track[0];
                    session.MapMarkers.Add(new MapPoiMarker
                    {
                        Kind = "SAM",
                        Label = e.Name,
                        Coalition = e.Coalition,
                        X = fs.X,
                        Y = fs.Y,
                        RadiusMeters = 18000.0
                    });
                }
            }

            // 2. Определяем носителя каждой ракеты по ближайшему борту/ЗРК/кораблю в точке пуска (3D)
            foreach (DebriefEntity msl in session.Entities)
            {
                if (msl.Type != "Missile" || msl.Track.Count == 0) continue;
                DebriefSample mStart = msl.Track[0];

                DebriefEntity bestLauncher = null;
                double bestDist = 1500.0;
                foreach (DebriefEntity cand in session.Entities)
                {
                    if (cand == msl || cand.Type == "Missile") continue;
                    DebriefSample cPos = cand.GetStateAt(msl.FirstTime);
                    if (cPos == null) continue;
                    double d = Math.Sqrt(Math.Pow(cPos.X - mStart.X, 2) + Math.Pow(cPos.Y - mStart.Y, 2) + Math.Pow(cPos.Alt - mStart.Alt, 2));
                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestLauncher = cand;
                    }
                }

                if (bestLauncher != null)
                {
                    msl.Coalition = bestLauncher.Coalition;
                    if (bestLauncher.IsPlayer)
                    {
                        msl.Pilot = "MY_MISSILE:" + bestLauncher.Name;
                        session.PlayerLaunches++;
                    }
                    else
                    {
                        msl.Pilot = bestLauncher.Name;
                    }
                }
            }

            // Декодируем реальные дорожки из customTracks (радарные захваты БРЛС/ЗРК/АРЛ-ракет и станции РЭБ EF-24G)
            int decodedBeams = 0;
            try
            {
                System.Reflection.FieldInfo fCustom = recType.GetField("customTracks", bf);
                System.Collections.IDictionary customDict = fCustom != null ? fCustom.GetValue(recorder) as System.Collections.IDictionary : null;
                if (customDict != null)
                {
                    foreach (System.Collections.DictionaryEntry cde in customDict)
                    {
                        object cTrack = cde.Value;
                        if (cTrack == null) continue;
                        Type ctType = cTrack.GetType();
                        object meta = null;
                        System.Collections.IList kfList = null;
                        Type walk = ctType;
                        while (walk != null && walk != typeof(object))
                        {
                            foreach (System.Reflection.FieldInfo fi in walk.GetFields(bf | System.Reflection.BindingFlags.DeclaredOnly))
                            {
                                object v = fi.GetValue(cTrack);
                                if (fi.Name == "metadata") meta = v;
                                else if (v is System.Collections.IList) kfList = (System.Collections.IList)v;
                            }
                            walk = walk.BaseType;
                        }
                        if (kfList == null || kfList.Count == 0) continue;

                        string metaTypeName = meta != null ? meta.GetType().Name : "";
                        bool isJammer = !metaTypeName.Equals("RadarLockReplayMetadata", StringComparison.OrdinalIgnoreCase);

                        int ownerId = -1;
                        if (meta != null)
                        {
                            foreach (System.Reflection.FieldInfo mf in meta.GetType().GetFields(bf))
                            {
                                if (mf.FieldType == typeof(int))
                                {
                                    int iv = (int)mf.GetValue(meta);
                                    if (iv >= 0 && idToEntity.ContainsKey(iv)) { ownerId = iv; break; }
                                }
                            }
                        }

                        DebriefEntity srcEnt = null;
                        idToEntity.TryGetValue(ownerId, out srcEnt);
                        if (srcEnt == null) continue;

                        if (isJammer)
                        {
                            // В штатном реплее у джаммера есть только: режим (NOISE/DRFM/SAS), диапазон
                            // (Low/Mid/High), направление антенны и кадры Start/Stop. Цели в файле НЕТ,
                            // поэтому не приписываем подавление конкретному юниту — рисуем реальный сектор/луч.
                            bool segActive = false;
                            double segStart = 0, segEnd = 0;
                            double segDx = 0, segDy = 0, segDz = 0;
                            string segMode = "", segBand = "";

                            for (int k = 0; k < kfList.Count; k++)
                            {
                                object kf = kfList[k];
                                if (kf == null) continue;
                                Type kft = kf.GetType();
                                float kt = 0f;
                                string evState = "";
                                string txMode = "ECM";
                                string txBand = "";
                                double dirX = 0, dirY = 0, dirZ = 0;

                                foreach (System.Reflection.FieldInfo kff in kft.GetFields(bf))
                                {
                                    object kv = kff.GetValue(kf);
                                    if (kv == null) continue;
                                    if (kff.FieldType == typeof(float) && kff.Name.Equals("t", StringComparison.OrdinalIgnoreCase))
                                        kt = (float)kv;
                                    else if (kff.Name == "type")
                                        evState = kv.ToString();
                                    else if (kff.Name == "transmitMode")
                                        txMode = kv.ToString();
                                    else if (kff.Name == "band")
                                        txBand = kv.ToString();
                                    else if (kff.FieldType.Name == "Vector3")
                                    {
                                        Type v3t = kv.GetType();
                                        dirX = Convert.ToDouble(v3t.GetField("x", bf).GetValue(kv));
                                        dirY = Convert.ToDouble(v3t.GetField("y", bf).GetValue(kv));
                                        dirZ = Convert.ToDouble(v3t.GetField("z", bf).GetValue(kv));
                                    }
                                }

                                double len3d = Math.Sqrt(dirX * dirX + dirY * dirY + dirZ * dirZ);
                                bool isStop = evState.Equals("Stop", StringComparison.OrdinalIgnoreCase) || len3d < 1e-4;

                                if (!isStop)
                                {
                                    if (segActive && (!string.Equals(segMode, txMode, StringComparison.OrdinalIgnoreCase) ||
                                                      !string.Equals(segBand, txBand, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        AddJammerBeam(session, srcEnt, segStart, segEnd, segMode, segBand, segDx, segDy, segDz);
                                        segActive = false;
                                    }
                                    if (!segActive)
                                    {
                                        segActive = true;
                                        segStart = kt;
                                        segMode = txMode;
                                        segBand = txBand;
                                    }
                                    segEnd = kt + 0.35;
                                    segDx = dirX / len3d;
                                    segDy = dirY / len3d;
                                    segDz = dirZ / len3d;
                                }
                                else if (segActive)
                                {
                                    segEnd = Math.Max(kt, segEnd);
                                    AddJammerBeam(session, srcEnt, segStart, segEnd, segMode, segBand, segDx, segDy, segDz);
                                    segActive = false;
                                }
                            }
                            if (segActive)
                            {
                                segEnd = Math.Max(srcEnt.LastTime, segEnd);
                                AddJammerBeam(session, srcEnt, segStart, segEnd, segMode, segBand, segDx, segDy, segDz);
                            }
                            continue;
                        }

                        for (int k = 0; k < kfList.Count; k++)
                        {
                            object kf = kfList[k];
                            if (kf == null) continue;
                            Type kft = kf.GetType();
                            float kt = 0f;
                            int targetId = -1;

                            foreach (System.Reflection.FieldInfo kff in kft.GetFields(bf))
                            {
                                object kv = kff.GetValue(kf);
                                if (kff.FieldType == typeof(float) && kff.Name.Equals("t", StringComparison.OrdinalIgnoreCase))
                                    kt = (float)kv;
                                else if (kff.FieldType == typeof(int))
                                {
                                    int iv = (int)kv;
                                    if (iv >= 0 && iv != ownerId && idToEntity.ContainsKey(iv))
                                        targetId = iv;
                                }
                            }

                            if (targetId < 0) continue;
                            DebriefEntity dstEnt = null;
                            idToEntity.TryGetValue(targetId, out dstEnt);
                            if (dstEnt == null) continue;

                            if (dstEnt.Coalition == "Allies" && srcEnt.Coalition != "Allies")
                                srcEnt.Coalition = "Enemies";

                            float endKt = (float)Math.Min(srcEnt.LastTime, dstEnt.LastTime);
                            if (k + 1 < kfList.Count && kfList[k + 1] != null)
                            {
                                System.Reflection.FieldInfo ftNext = kfList[k + 1].GetType().GetField("t", bf);
                                if (ftNext != null)
                                {
                                    float nextT = (float)ftNext.GetValue(kfList[k + 1]);
                                    if (nextT > kt) endKt = Math.Min(endKt, nextT);
                                }
                            }
                            if (endKt <= kt) continue;

                            bool isMyBeam = srcEnt.IsPlayer || (srcEnt.Pilot != null && srcEnt.Pilot.StartsWith("MY_MISSILE:"));
                            string bKind = srcEnt.Coalition == "Enemies" ? "RWR_SPIKE" : (isMyBeam ? "MY_LOCK" : "ALLY_LOCK");
                            string labelFrom = (srcEnt.Type == "Missile" ? "[АРЛ] " : "") + srcEnt.Name;

                            DebriefSample s1 = srcEnt.GetStateAt(kt) ?? (srcEnt.Track.Count > 0 ? srcEnt.Track[0] : null);
                            DebriefSample s2 = dstEnt.GetStateAt(kt) ?? (dstEnt.Track.Count > 0 ? dstEnt.Track[0] : null);
                            if (s1 != null && s2 != null)
                            {
                                session.Beams.Add(new TacticalBeam
                                {
                                    StartTime = kt,
                                    EndTime = endKt,
                                    Kind = bKind,
                                    FromId = srcEnt.Id,
                                    ToId = dstEnt.Id,
                                    FromName = labelFrom,
                                    ToName = dstEnt.Name,
                                    FromX = s1.X,
                                    FromY = s1.Y,
                                    ToX = s2.X,
                                    ToY = s2.Y
                                });
                                decodedBeams++;
                            }
                        }
                    }
                }
            }
            catch { }

            // Точные взрывы из штатного eventTrack (eventType 0 = Explosion)
            try
            {
                object evtTrack = recType.GetField("eventTrack", bf).GetValue(recorder);
                System.Collections.IList ekfs = null;
                if (evtTrack != null)
                {
                    Type etT = evtTrack.GetType();
                    while (etT != null && ekfs == null)
                    {
                        foreach (System.Reflection.FieldInfo f in etT.GetFields(bf | System.Reflection.BindingFlags.DeclaredOnly))
                            if (typeof(System.Collections.IList).IsAssignableFrom(f.FieldType)) { ekfs = f.GetValue(evtTrack) as System.Collections.IList; if (ekfs != null) break; }
                        etT = etT.BaseType;
                    }
                }
                if (ekfs != null)
                {
                    int flareCount = 0, chaffCount = 0;
                    foreach (object kf in ekfs)
                    {
                        if (kf == null) continue;
                        Type kft = kf.GetType();
                        if (kft.Name != "WorldEventKeyframe") continue;
                        System.Reflection.FieldInfo fEt = kft.GetField("eventType", bf);
                        if (fEt == null) continue;
                        int evType = Convert.ToInt32(fEt.GetValue(kf));
                        if (evType != 0 && evType != 2 && evType != 3) continue;
                        double ex = 0, ey = 0, ez = 0, etime = 0;
                        foreach (System.Reflection.FieldInfo f in kft.GetFields(bf))
                        {
                            object v = f.GetValue(kf);
                            if (v == null) continue;
                            if (f.FieldType == typeof(float) && f.Name.Equals("t", StringComparison.OrdinalIgnoreCase))
                                etime = (float)v;
                            else if (f.FieldType.Name == "FixedPoint")
                            {
                                Type fpT = v.GetType();
                                System.Reflection.FieldInfo gp = fpT.GetField("globalPoint", bf);
                                if (gp != null)
                                {
                                    object v3 = gp.GetValue(v);
                                    Type v3t = v3.GetType();
                                    ex = Convert.ToDouble(v3t.GetField("x", bf).GetValue(v3));
                                    ey = Convert.ToDouble(v3t.GetField("y", bf).GetValue(v3));
                                    ez = Convert.ToDouble(v3t.GetField("z", bf).GetValue(v3));
                                }
                            }
                        }
                        if (evType == 0)
                        {
                            // Игра пишет серию кадров на один взрыв (кластер, попадание, детонация),
                            // поэтому склеиваем близкие по времени и месту события в один маркер.
                            bool dup = false;
                            for (int ei = session.Explosions.Count - 1; ei >= 0; ei--)
                            {
                                DebriefExplosion prev = session.Explosions[ei];
                                if (etime - prev.Time > 0.25) break;
                                if (Math.Abs(etime - prev.Time) > 0.25) continue;
                                double ddx = ex - prev.X, ddy = ez - prev.Y, ddz = ey - prev.Alt;
                                if (ddx * ddx + ddy * ddy + ddz * ddz <= 3600.0) { dup = true; break; }
                            }
                            if (!dup)
                                session.Explosions.Add(new DebriefExplosion { Time = etime, X = ex, Y = ez, Alt = ey });
                        }
                        else
                        {
                            session.Flares.Add(new DebriefFlare { Time = etime, X = ex, Y = ez, Alt = ey, IsChaff = (evType == 3) });
                            if (evType == 3) chaffCount++; else flareCount++;
                        }
                    }
                    log.AppendFormat(" | Взрывов={0}, ЛТЦ={1}, ДО={2}", session.Explosions.Count, flareCount, chaffCount);
                }
            }
            catch { }

            ComputePlayerPeaks(session);

            log.AppendFormat(" | Лучей РЛС/АРЛ/РЭБ={0} | Итого юнитов={1}, кадров={2}, длительность={3:F1}с", decodedBeams, session.Entities.Count, totalFrames, maxT);
            diagLog = log.ToString();
            if (session.Entities.Count > 0 && totalFrames > 10)
            {
                session.Duration = maxT;
                session.HasFullRadarCoords = true;
                session.HasExactTelemetry = true;
                session.IsReconstructedFromLog = false;
                return true;
            }
        }
        catch (Exception ex)
        {
            log.Append("NativeErr: " + (ex.InnerException != null ? ex.InnerException.ToString() : ex.ToString()));
        }
        diagLog = log.ToString();
        return false;
    }
    private static void ComputePlayerPeaks(DebriefSession s)
    {
        DebriefEntity player = null;
        foreach (DebriefEntity e in s.Entities)
            if (e.IsPlayer && e.Track.Count > 1) { player = e; break; }
        if (player == null) return;

        for (int i = 1; i < player.Track.Count; i++)
        {
            DebriefSample a = player.Track[i - 1];
            DebriefSample b = player.Track[i];
            double dt = b.Time - a.Time;
            if (dt <= 0.02) continue;
            double va = Math.Sqrt(a.VX * a.VX + a.VY * a.VY + a.VZ * a.VZ);
            double vb = Math.Sqrt(b.VX * b.VX + b.VY * b.VY + b.VZ * b.VZ);
            if (va < 5.0 || vb < 5.0) continue; // обрыв трека / телепорт
            double dvx = b.VX - a.VX, dvy = b.VY - a.VY, dvz = b.VZ - a.VZ;
            double dv = Math.Sqrt(dvx * dvx + dvy * dvy + dvz * dvz);
            double g = dv / dt / 9.80665;
            if (g > 25.0) continue; // выброс в данных
            if (g > s.PlayerMaxG) { s.PlayerMaxG = g; s.PlayerMaxGTime = b.Time; }
            if (b.Speed > s.PlayerMaxSpeed) s.PlayerMaxSpeed = b.Speed;
            if (b.Alt < s.PlayerMinAlt) { s.PlayerMinAlt = b.Alt; s.PlayerMinAltTime = b.Time; }
        }
    }

    // Анализ реальных данных полёта: перегрузка, CFIT, реакция на пуск.
    // Никаких домыслов о том, чего нет в файле (например, отстрела ЛТЦ/ДО — их в реплее нет).
    private static void BuildPilotInsights(DebriefSession s)
    {
        DebriefEntity player = null;
        foreach (DebriefEntity e in s.Entities)
            if (e.IsPlayer) { player = e; break; }

        foreach (TacticalEvent ev in s.Events)
        {
            if (string.IsNullOrEmpty(ev.Category))
            {
                if (ev.Kind == "THREAT" || ev.Kind == "RWR" || ev.Kind == "RADAR" || ev.Kind == "JAMMER") ev.Category = "THREAT";
                else if (ev.Kind == "KILL" || ev.Kind == "GROUND_KILL" || (ev.Kind == "LAUNCH" && ev.Severity == "GOOD")) ev.Category = "SUCCESS";
                else if (ev.Kind == "KIA" && ev.Severity == "WARNING") ev.Category = "INFO";   // потеря союзника — не ошибка пилота
                else if (ev.Kind == "KIA" || ev.Kind == "DAMAGE" || ev.Kind == "MISTAKE" || ev.Kind == "CFIT") ev.Category = "MISTAKE";
                else ev.Category = "INFO";
            }
            if (player != null && double.IsNaN(ev.Altitude))
            {
                DebriefSample st = player.GetStateAt(ev.Time);
                if (st != null) { ev.Altitude = st.Alt; ev.SpeedKmh = st.Speed * 3.6; }
            }
        }

        if (player == null || player.Track.Count < 2) return;

        if (s.PlayerMaxG >= 9.0)
        {
            DebriefSample at = player.GetStateAt(s.PlayerMaxGTime);
            TacticalEvent ev = new TacticalEvent
            {
                Time = s.PlayerMaxGTime,
                Kind = "MISTAKE",
                Severity = s.PlayerMaxG >= 11.0 ? "CRITICAL" : "WARNING",
                Category = "MISTAKE",
                Title = string.Format("Перегрузка {0:F1} G", s.PlayerMaxG),
                Description = string.Format("Пиковая перегрузка борта {0:F1} G на высоте {1:F0} м. Устойчивая перегрузка выше 9 G ведёт к потере сознания и разрушению планера.", s.PlayerMaxG, at != null ? at.Alt : 0),
                Advice = "Плавнее работайте РУС на выводе из пикирования: наращивайте перегрузку постепенно и следите за индикатором G.",
                WorldX = at != null ? at.X : 0,
                WorldY = at != null ? at.Y : 0,
                Altitude = at != null ? at.Alt : double.NaN
            };
            s.Events.Add(ev);
            s.Mistakes.Add(ev);
        }

        DebriefSample last = player.Track[player.Track.Count - 1];
        double ground = s.GetGroundAltitudeAt(last.X, last.Y);
        double agl = last.Alt - ground;
        bool destroyedByWeapon = false;
        foreach (TacticalEvent ev in s.Events)
            if (ev.Kind == "KIA" && Math.Abs(ev.Time - last.Time) < 3.0) { destroyedByWeapon = true; break; }

        bool cfit = !destroyedByWeapon && last.Time < s.Duration - 3.0 && agl < 60.0 && last.Speed > 25.0;
        if (cfit)
        {
            TacticalEvent ev = new TacticalEvent
            {
                Time = last.Time,
                Kind = "CFIT",
                Severity = "CRITICAL",
                Category = "MISTAKE",
                Title = "Столкновение с рельефом",
                Description = string.Format("Трек оборвался на высоте {0:F0} м над рельефом при скорости {1:F0} км/ч — признаки столкновения с землёй (CFIT).", agl, last.Speed * 3.6),
                Advice = "Контролируйте высоту по радиовысотомеру в горах и на малых высотах; не отвлекайтесь на БРЛС в пикировании.",
                WorldX = last.X,
                WorldY = last.Y,
                Altitude = last.Alt,
                SpeedKmh = last.Speed * 3.6
            };
            s.Events.Add(ev);
            s.Mistakes.Add(ev);
        }

        // Реакция на пуск: считаем реальные курс и перегрузку в течение 8 секунд после угрозы
        foreach (TacticalEvent threat in s.Events.ToArray())
        {
            if (threat.Kind != "THREAT" || threat.Severity != "CRITICAL") continue;
            DebriefSample t0 = player.GetStateAt(threat.Time);
            DebriefSample t1 = player.GetStateAt(threat.Time + 8.0);
            if (t0 == null || t1 == null) continue;

            double dHdg = Math.Abs(((t1.Heading - t0.Heading + 540.0) % 360.0) - 180.0);
            double maxG = 0;
            for (int i = 1; i < player.Track.Count; i++)
            {
                DebriefSample a = player.Track[i - 1], b = player.Track[i];
                if (b.Time < threat.Time || b.Time > threat.Time + 8.0) continue;
                double dt = b.Time - a.Time;
                if (dt <= 0.02) continue;
                double dv = Math.Sqrt(Math.Pow(b.VX - a.VX, 2) + Math.Pow(b.VY - a.VY, 2) + Math.Pow(b.VZ - a.VZ, 2));
                double g = dv / dt / 9.80665;
                if (g > maxG) maxG = g;
            }

            if (dHdg < 35.0 && maxG < 3.5)
            {
                TacticalEvent ev = new TacticalEvent
                {
                    Time = threat.Time + 8.0,
                    Kind = "MISTAKE",
                    Severity = "WARNING",
                    Category = "MISTAKE",
                    Title = "Нет оборонительного манёвра после пуска",
                    Description = string.Format("Через 8 с после пуска по вам ({0}) курс изменился на {1:F0}°, перегрузка не превысила {2:F1} G — ракета продолжала наведение.", threat.Title, dHdg, maxG),
                    Advice = "Сразу после пуска/срабатывания СПО: отворот 90–120° (notch) к линии визирования, снижение и отстрел ДО; на конечном участке — манёвр 7–9 G.",
                    WorldX = t0.X,
                    WorldY = t0.Y,
                    Altitude = t0.Alt,
                    SpeedKmh = t0.Speed * 3.6
                };
                s.Events.Add(ev);
                s.Mistakes.Add(ev);
            }
        }
    }

    private static void AddJammerBeam(DebriefSession s, DebriefEntity src, double startT, double endT,
        string mode, string band, double dx, double dy, double dz)
    {
        if (endT <= startT + 0.05) return;
        DebriefSample st = src.GetStateAt(startT) ?? (src.Track.Count > 0 ? src.Track[0] : null);
        if (st == null) return;
        string tag = string.IsNullOrEmpty(band) ? mode : (mode + " · " + band);
        s.Beams.Add(new TacticalBeam
        {
            StartTime = startT,
            EndTime = endT,
            Kind = "JAMMER",
            FromId = src.Id,
            ToId = "",
            FromName = src.Name,
            ToName = tag,
            JamMode = mode ?? "",
            JamBand = band ?? "",
            FromX = st.X,
            FromY = st.Y,
            DirX = dx,
            DirY = dz,   // мировая ось Z -> плоскость карты
            DirZ = dy    // мировая ось Y -> высота
        });
    }

    private static void BuildNativeVtrEvents(DebriefSession s)
    {
        // 1. Все пуски ракет (твои, союзные и вражеские по захваченной цели)
        foreach (DebriefEntity ent in s.Entities)
        {
            if (ent.Type != "Missile" || ent.Track.Count == 0) continue;
            DebriefSample ms = ent.Track[0];
            bool isMine = ent.Pilot != null && ent.Pilot.StartsWith("MY_MISSILE:");

            if (isMine)
            {
                s.Events.Add(new TacticalEvent
                {
                    Time = ent.FirstTime,
                    Kind = "LAUNCH",
                    Severity = "GOOD",
                    Title = "Ваш пуск: " + ent.Name,
                    Description = string.Format("Пуск ракеты {0} с вашего борта.", ent.Name),
                    WorldX = ms.X,
                    WorldY = ms.Y
                });
            }
            else if (ent.Coalition == "Allies")
            {
                s.Events.Add(new TacticalEvent
                {
                    Time = ent.FirstTime,
                    Kind = "LAUNCH",
                    Severity = "INFO",
                    Title = string.Format("Пуск союзника ({0}): {1}", ent.Pilot, ent.Name),
                    Description = string.Format("Союзник {0} выполнил пуск {1}.", ent.Pilot, ent.Name),
                    WorldX = ms.X,
                    WorldY = ms.Y
                });
            }
            else if (ent.Coalition == "Enemies")
            {
                // Определяем цель вражеской ракеты строго по факту захвата из session.Beams (без дистанций)
                DebriefEntity lockedTarget = null;
                foreach (TacticalBeam bm in s.Beams)
                {
                    if (bm.Kind != "RWR_SPIKE") continue;
                    bool fromThisMsl = bm.FromName.IndexOf(ent.Name, StringComparison.OrdinalIgnoreCase) >= 0 &&
                                       bm.StartTime >= ent.FirstTime - 1.0 && bm.StartTime <= ent.LastTime + 1.0;
                    bool fromLauncher = !string.IsNullOrEmpty(ent.Pilot) &&
                                        bm.FromName.IndexOf(ent.Pilot, StringComparison.OrdinalIgnoreCase) >= 0 &&
                                        Math.Abs(bm.StartTime - ent.FirstTime) <= 3.0;
                    if (fromThisMsl || fromLauncher)
                    {
                        foreach (DebriefEntity cand in s.Entities)
                        {
                            if (cand.Coalition == "Allies" && cand.Name.Equals(bm.ToName, StringComparison.OrdinalIgnoreCase) &&
                                cand.FirstTime <= ent.FirstTime && cand.LastTime >= ent.FirstTime)
                            {
                                lockedTarget = cand;
                                break;
                            }
                        }
                        if (lockedTarget != null) break;
                    }
                }

                if (lockedTarget != null && lockedTarget.IsPlayer)
                {
                    s.ThreatMissilesCount++;
                    TacticalEvent thr = new TacticalEvent
                    {
                        Time = ent.FirstTime,
                        Kind = "THREAT",
                        Severity = "CRITICAL",
                        Title = string.Format("УГРОЗА: пуск {0} ({1}) по вам", ent.Name, ent.Pilot),
                        Description = string.Format("Противник ({0}) выполнил пуск ракеты {1} с захватом вашего борта!", ent.Pilot, ent.Name),
                        Advice = "Выполните отворот на 90° (Notch) с отстрелом ДО (Chaff) и снижением.",
                        WorldX = ms.X,
                        WorldY = ms.Y
                    };
                    s.Events.Add(thr);
                    s.Mistakes.Add(thr);
                }
                else if (lockedTarget != null)
                {
                    bool isAntiMissile = (lockedTarget.Type == "Missile");
                    s.Events.Add(new TacticalEvent
                    {
                        Time = ent.FirstTime,
                        Kind = "THREAT",
                        Severity = isAntiMissile ? "INFO" : "WARNING",
                        Title = isAntiMissile
                            ? string.Format("ПВО врага ({0}): перехват {1} ракетой {2}", ent.Pilot, lockedTarget.Name, ent.Name)
                            : string.Format("Пуск врага ({0}): {1} по {2}", ent.Pilot, ent.Name, lockedTarget.Name),
                        Description = isAntiMissile
                            ? string.Format("ЗРК/корабль противника ({0}) выпустил {1} на перехват ракеты {2}.", ent.Pilot, ent.Name, lockedTarget.Name)
                            : string.Format("Противник ({0}) выпустил {1} по союзнику {2}.", ent.Pilot, ent.Name, lockedTarget.Name),
                        WorldX = ms.X,
                        WorldY = ms.Y
                    });
                }
            }
        }

        // 2. Все попадания ракет и гибель юнитов (трек цели должен оборваться в пределах +-2.5 сек от взрыва ракеты)
        HashSet<string> killedIds = new HashSet<string>();
        // Ракета -> имя поражённой цели (только подтверждённые попадания)
        Dictionary<string, string> hitByMissile = new Dictionary<string, string>();

        foreach (DebriefEntity msl in s.Entities)
        {
            if (msl.Type != "Missile" || msl.Track.Count == 0) continue;
            if (msl.LastTime >= s.Duration - 1.5) continue;

            DebriefSample mEnd = msl.Track[msl.Track.Count - 1];
            bool isMine = msl.Pilot != null && msl.Pilot.StartsWith("MY_MISSILE:");

            DebriefEntity hitVictim = null;
            double bestDist = 450.0;

            foreach (DebriefEntity cand in s.Entities)
            {
                if (cand == msl || cand.Type == "Missile" || cand.Coalition == msl.Coalition) continue;
                if (killedIds.Contains(cand.Id)) continue;
                if (cand.LastTime >= s.Duration - 3.0) continue;
                if (Math.Abs(cand.LastTime - msl.LastTime) > 2.5) continue;


                DebriefSample cAtImpact = cand.GetStateAt(Math.Min(msl.LastTime, cand.LastTime));
                if (cAtImpact == null) continue;

                double d3d = Math.Sqrt(Math.Pow(cAtImpact.X - mEnd.X, 2) + Math.Pow(cAtImpact.Y - mEnd.Y, 2) + Math.Pow(cAtImpact.Alt - mEnd.Alt, 2));
                if (d3d < bestDist)
                {
                    bestDist = d3d;
                    hitVictim = cand;
                }
            }

            if (hitVictim != null)
            {
                killedIds.Add(hitVictim.Id);
                hitByMissile[msl.Id] = hitVictim.Name;
                double hitTime = msl.LastTime;
                DebriefSample hitPos = hitVictim.GetStateAt(Math.Min(hitTime, hitVictim.LastTime)) ?? mEnd;

                bool isStaticOrGround = (hitVictim.Type == "Ground") ||
                                        (hitVictim.Name.IndexOf("Tent", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                        (hitVictim.Name.IndexOf("Bunker", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                        (hitVictim.Name.IndexOf("SAM", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                        (hitVictim.Name.IndexOf("SAAW", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                        (hitVictim.Name.IndexOf("Boat", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                        (hitVictim.Name.IndexOf("Ship", StringComparison.OrdinalIgnoreCase) >= 0);

                if (isStaticOrGround)
                {
                    foreach (MapPoiMarker poi in s.MapMarkers)
                    {
                        if (poi.Kind == "SAM" && Math.Abs(poi.X - hitPos.X) < 150.0 && Math.Abs(poi.Y - hitPos.Y) < 150.0)
                            poi.RadiusMeters = -hitTime;
                    }
                    if (isMine) s.PlayerKills++;
                    else if (msl.Coalition == "Allies") s.AlliedKills++;

                    s.Events.Add(new TacticalEvent
                    {
                        Time = hitTime,
                        Kind = "GROUND_KILL",
                        Severity = "GOOD",
                        Title = string.Format("Уничтожен{0}: {1} ({2})", isMine ? " вами" : "", hitVictim.Name, msl.Name),
                        Description = string.Format("Наземная/морская цель {0} уничтожена ({1}).", hitVictim.Name, msl.Name),
                        WorldX = hitPos.X,
                        WorldY = hitPos.Y
                    });
                }
                else if (hitVictim.IsPlayer)
                {
                    TacticalEvent kia = new TacticalEvent
                    {
                        Time = hitTime,
                        Kind = "KIA",
                        Severity = "CRITICAL",
                        Title = string.Format("Борт потерян: {0} ({1})", hitVictim.Name, msl.Name),
                        Description = string.Format("Ваш борт {0} сбит ракетой {1} ({2}) на высоте {3:F0} м.", hitVictim.Name, msl.Name, msl.Pilot, hitPos.Alt),
                        Advice = "На конечном участке наведения ракеты выполните отворот с перегрузкой 7-9G перпендикулярно вектору угрозы и отстрелите ДО/ЛТЦ.",
                        WorldX = hitPos.X,
                        WorldY = hitPos.Y
                    };
                    s.Events.Add(kia);
                    s.Mistakes.Add(kia);
                }
                else if (hitVictim.Coalition == "Enemies")
                {
                    if (isMine) s.PlayerKills++;
                    else s.AlliedKills++;

                    s.Events.Add(new TacticalEvent
                    {
                        Time = hitTime,
                        Kind = "KILL",
                        Severity = "GOOD",
                        Title = string.Format("{0}: {1} ({2})", isMine ? "Сбит вами" : "Сбит союзником", hitVictim.Name, msl.Name),
                        Description = string.Format("Цель {0} сбита ракетой {1} ({2}) на высоте {3:F0} м.", hitVictim.Name, msl.Name, isMine ? "ваш пуск" : msl.Pilot, hitPos.Alt),
                        WorldX = hitPos.X,
                        WorldY = hitPos.Y
                    });
                }
                else
                {
                    s.Events.Add(new TacticalEvent
                    {
                        Time = hitTime,
                        Kind = "KIA",
                        Severity = "WARNING",
                        Title = string.Format("Потеря союзника: {0} ({1})", hitVictim.Name, msl.Name),
                        Description = string.Format("Союзный борт {0} сбит ракетой {1} ({2}) на высоте {3:F0} м.", hitVictim.Name, msl.Name, msl.Pilot, hitPos.Alt),
                        WorldX = hitPos.X,
                        WorldY = hitPos.Y
                    });
                }
            }
        }
        // 3. Фиксация начала электронной атаки (РЭБ) любым участником боя (группируем непрерывные кадры поворота антенны)
        List<TacticalBeam> jamBeams = new List<TacticalBeam>();
        foreach (TacticalBeam bm in s.Beams)
            if (bm.Kind == "JAMMER") jamBeams.Add(bm);
        jamBeams.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

        Dictionary<string, double> lastJamEndByActor = new Dictionary<string, double>();
        foreach (TacticalBeam jb in jamBeams)
        {
            string key = jb.FromId ?? jb.FromName;
            double prevEnd;
            if (lastJamEndByActor.TryGetValue(key, out prevEnd) && (jb.StartTime - prevEnd) < 4.0)
            {
                if (jb.EndTime > prevEnd) lastJamEndByActor[key] = jb.EndTime;
                continue;
            }
            lastJamEndByActor[key] = jb.EndTime;

            s.Events.Add(new TacticalEvent
            {
                Time = jb.StartTime,
                Kind = "JAMMER",
                Severity = "WARNING",
                Title = string.Format("Внимание: электронная атака ({0})", jb.FromName),
                Description = string.Format("Борт {0} включил станцию радиоэлектронного подавления (режим {1}).", jb.FromName, jb.ToName),
                WorldX = jb.FromX,
                WorldY = jb.FromY
            });
        }

        // 4. Сводная аналитика пусков: только факты из треков (носитель, время, время полёта, поражение)
        foreach (DebriefEntity msl in s.Entities)
        {
            if (msl.Type != "Missile" || msl.Track.Count == 0) continue;

            bool isMine = msl.Pilot != null && msl.Pilot.StartsWith("MY_MISSILE:");
            string side = isMine ? "МОЙ" : (msl.Coalition == "Allies" ? "СОЮЗ" : (msl.Coalition == "Enemies" ? "ВРАГ" : "—"));

            DebriefLaunch dl = new DebriefLaunch();
            dl.Time = msl.FirstTime;
            dl.MissileName = msl.Name;
            dl.MissileId = msl.Id;
            dl.Launcher = isMine ? "" : (msl.Pilot ?? "");
            dl.Coalition = msl.Coalition;
            dl.Side = side;
            dl.FlightTime = Math.Max(0.0, msl.LastTime - msl.FirstTime);

            string targetName;
            if (hitByMissile.TryGetValue(msl.Id, out targetName))
            {
                dl.TargetName = targetName;
                dl.Result = "ПОПАДАНИЕ";

                // Дистанция пуска — 3D-расстояние до цели в момент старта ракеты
                foreach (DebriefEntity cand in s.Entities)
                {
                    if (cand.Name != targetName) continue;
                    DebriefSample tAtLaunch = cand.GetStateAt(msl.FirstTime);
                    if (tAtLaunch == null) break;
                    DebriefSample mAtLaunch = msl.Track[0];
                    dl.LaunchDist = Math.Sqrt(
                        Math.Pow(tAtLaunch.X - mAtLaunch.X, 2) +
                        Math.Pow(tAtLaunch.Y - mAtLaunch.Y, 2) +
                        Math.Pow(tAtLaunch.Alt - mAtLaunch.Alt, 2));
                    break;
                }
            }
            else
            {
                dl.TargetName = "";
                dl.Result = "ПРОМАХ";
            }

            s.Launches.Add(dl);
        }
        s.Launches.Sort((a, b) => a.Time.CompareTo(b.Time));
    }


    private static void TryBuildTerrainFromHeightmapPngb(DebriefSession s, string hmPath, double mapSize)
    {
        try
        {
            byte[] fileRaw = File.ReadAllBytes(hmPath);
            if (fileRaw.Length < 32) return;

            byte b0 = (byte)fileRaw.GetValue(0);
            byte b1 = (byte)fileRaw.GetValue(1);
            byte b2 = (byte)fileRaw.GetValue(2);
            byte b3 = (byte)fileRaw.GetValue(3);

            byte[] pngBytes = new byte[fileRaw.Length];
            if (b0 == 0x89 && b1 == 0x50 && b2 == 0x4E && b3 == 0x47)
            {
                Buffer.BlockCopy(fileRaw, 0, pngBytes, 0, fileRaw.Length);
            }
            else
            {
                byte shift = (byte)(b0 - 0x89); // 0x58 в VTOL VR
                for (int i = 0; i < fileRaw.Length; i++)
                    pngBytes.SetValue((byte)((byte)fileRaw.GetValue(i) - shift), i);
            }

            using (MemoryStream ms = new MemoryStream(pngBytes))
            using (Bitmap srcPng = new Bitmap(ms))
            {
                int gw = srcPng.Width;
                int gh = srcPng.Height;

                double[,] rawElev = new double[gw, gh];
                for (int y = 0; y < gh; y++)
                {
                    for (int x = 0; x < gw; x++)
                    {
                        Color px = srcPng.GetPixel(x, y);
                        rawElev[x, y] = (px.R == px.G && px.G == px.B)
                            ? (px.R / 255.0)
                            : ((px.R * 256.0 + px.G) / 65535.0);
                    }
                }

                List<double[]> refPoints = new List<double[]>();
                foreach (DebriefEntity ent in s.Entities)
                {
                    if (ent.Type != "Ground" || ent.Track.Count == 0) continue;
                    DebriefSample st = null;
                    foreach (DebriefSample smp in ent.Track) { st = smp; break; }
                    if (st == null) continue;

                    bool isSea = ent.Name.IndexOf("Ship", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 ent.Name.IndexOf("Cruiser", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 ent.Name.IndexOf("Carrier", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 ent.Name.IndexOf("Boat", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (isSea)
                        refPoints.Add(new double[] { st.X, st.Y, 0.0, st.Alt });
                    else if (st.Alt > 35.0)
                        refPoints.Add(new double[] { st.X, st.Y, 1.0, st.Alt });
                }

                double chunkSpan = (gw - 1) * 153.6;
                double[] candidateSpans = new double[] { mapSize, chunkSpan, mapSize * 2.0, chunkSpan * 2.0 };
                double[] candidateOriginsX = new double[] { 0.0, -mapSize * 0.5, -mapSize, -chunkSpan * 0.5 };
                double[] candidateOriginsY = new double[] { 0.0, -mapSize * 0.5, -mapSize, -chunkSpan * 0.5 };

                int bestOrient = 1;
                double bestSpan = mapSize;
                double bestMinX = s.MapMinX;
                double bestMinY = s.MapMinY;
                double bestScore = -1e9;

                if (refPoints.Count > 0)
                {
                    foreach (double sp in candidateSpans)
                    {
                        if (sp < 10000) continue;
                        foreach (double ox in candidateOriginsX)
                        {
                            if (s.BoundsMinX < ox - 2000 || s.BoundsMaxX > ox + sp + 2000) continue;
                            foreach (double oy in candidateOriginsY)
                            {
                                if (s.BoundsMinY < oy - 2000 || s.BoundsMaxY > oy + sp + 2000) continue;

                                for (int orient = 0; orient < 8; orient++)
                                {
                                    double score = 0;
                                    foreach (double[] rp in refPoints)
                                    {
                                        double rpX = (double)rp.GetValue(0);
                                        double rpY = (double)rp.GetValue(1);
                                        double rpIsLand = (double)rp.GetValue(2);

                                        double u = (rpX - ox) / sp;
                                        double v = (rpY - oy) / sp;
                                        if (u < 0 || u > 1 || v < 0 || v > 1) { score -= 50; continue; }

                                        int px = Math.Max(0, Math.Min(gw - 1, (int)(u * (gw - 1))));
                                        int py = Math.Max(0, Math.Min(gh - 1, (int)(v * (gh - 1))));

                                        int tx = px, ty = py;
                                        switch (orient)
                                        {
                                            case 0: tx = px; ty = py; break;
                                            case 1: tx = px; ty = gh - 1 - py; break;
                                            case 2: tx = gw - 1 - px; ty = py; break;
                                            case 3: tx = gw - 1 - px; ty = gh - 1 - py; break;
                                            case 4: tx = py; ty = px; break;
                                            case 5: tx = py; ty = gh - 1 - px; break;
                                            case 6: tx = gw - 1 - py; ty = px; break;
                                            case 7: tx = gw - 1 - py; ty = gh - 1 - px; break;
                                        }

                                        double e = rawElev[tx, ty];
                                        bool pixelIsLand = e > 0.03;
                                        if (rpIsLand > 0.5)
                                            score += pixelIsLand ? (10.0 + e * 20.0) : -25.0;
                                        else
                                            score += !pixelIsLand ? 12.0 : -20.0;
                                    }

                                    if (score > bestScore)
                                    {
                                        bestScore = score;
                                        bestOrient = orient;
                                        bestSpan = sp;
                                        bestMinX = ox;
                                        bestMinY = oy;
                                    }
                                }
                            }
                        }
                    }
                }

                Bitmap bmp = new Bitmap(gw, gh, PixelFormat.Format32bppArgb);
                for (int y = 0; y < gh; y++)
                {
                    for (int x = 0; x < gw; x++)
                    {
                        double elev = rawElev[x, y];
                        Color c;
                        if (elev < 0.025)
                            c = Color.FromArgb(255, 10, 25, 40);
                        else if (elev < 0.055)
                            c = Color.FromArgb(255, 22, 58, 72);
                        else
                        {
                            double ln = (elev - 0.055) / 0.945;
                            int band = (int)(ln * 14.0);
                            bool contour = Math.Abs(ln * 14.0 - band) < 0.055;
                            int r = (int)(18 + ln * 52);
                            int g = (int)(38 + ln * 68);
                            int b = (int)(26 + ln * 42);
                            if (contour) { r += 20; g += 28; b += 20; }
                            c = Color.FromArgb(255, Math.Min(255, r), Math.Min(255, g), Math.Min(255, b));
                        }

                        // Применяем обратное преобразование ориентации, чтобы экранный пиксель (sx, sy) соответствовал (tx, ty)
                        int sx = x, sy = y;
                        switch (bestOrient)
                        {
                            case 0: sx = x; sy = y; break;
                            case 1: sx = x; sy = gh - 1 - y; break;
                            case 2: sx = gw - 1 - x; sy = y; break;
                            case 3: sx = gw - 1 - x; sy = gh - 1 - y; break;
                            case 4: sx = y; sy = x; break;
                            case 5: sx = gh - 1 - y; sy = x; break;
                            case 6: sx = y; sy = gw - 1 - x; break;
                            case 7: sx = gh - 1 - y; sy = gw - 1 - x; break;
                        }
                        bmp.SetPixel(sx, gh - 1 - sy, c);
                    }
                }

                // 100% нативная формула VTMapGenerator из Assembly-CSharp.dll: hm_minHeight = -80.0f, hm_maxHeight = 6000.0f
                const double hmMinHeight = -80.0;
                const double hmMaxHeight = 6000.0;
                const double hmSpan = hmMaxHeight - hmMinHeight; // 6080.0 м

                float[,] hGrid = new float[gw, gh];
                for (int gy = 0; gy < gh; gy++)
                {
                    for (int gx = 0; gx < gw; gx++)
                    {
                        int tx = gx, ty = gy;
                        switch (bestOrient)
                        {
                            case 0: tx = gx; ty = gy; break;
                            case 1: tx = gx; ty = gh - 1 - gy; break;
                            case 2: tx = gw - 1 - gx; ty = gy; break;
                            case 3: tx = gw - 1 - gx; ty = gh - 1 - gy; break;
                            case 4: tx = gy; ty = gx; break;
                            case 5: tx = gy; ty = gh - 1 - gx; break;
                            case 6: tx = gw - 1 - gy; ty = gx; break;
                            case 7: tx = gw - 1 - gy; ty = gh - 1 - gx; break;
                        }
                        double elev = rawElev[tx, ty];
                        double worldAlt = hmMinHeight + elev * hmSpan;
                        hGrid[gx, gy] = worldAlt <= 0.0 ? 0f : (float)worldAlt;
                    }
                }
                s.TerrainHeights = hGrid;

                if (s.TerrainBitmap != null) s.TerrainBitmap.Dispose();
                s.TerrainBitmap = bmp;
                s.MapMinX = bestMinX;
                s.MapMinY = bestMinY;
                s.MapSizeMeters = bestSpan;
                LastVtrDiagnostic += string.Format(" | HM_PNGB OK ({0}x{1}, orient={2}, origin=({3:F0},{4:F0}), span={5:F0}, range=[-80..6000m])",
                    gw, gh, bestOrient, bestMinX, bestMinY, bestSpan);
            }
        }
        catch (Exception ex)
        {
            LastVtrDiagnostic += " | HMErr=" + ex.Message;
        }
    }
    private static bool ParseBinaryVtrData(DebriefSession session, string dataPath, double mapSize, string preferredPilot)
    {
        string nativeDiag;
        if (TryLoadViaNativeGameSerializer(session, dataPath, mapSize, preferredPilot, out nativeDiag))
        {
            LastVtrDiagnostic = nativeDiag;
            return true;
        }
        LastVtrDiagnostic = nativeDiag;
        return false;
    }
}

