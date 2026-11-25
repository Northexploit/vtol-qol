using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

// =========================================================================
// === ДВИЖОК ТАКТИЧЕСКОГО ДЕБРИФИНГА И АНАЛИЗА ОШИБОК ПИЛОТА ==============
// =========================================================================
public class DebriefSample
{
    public double Time;
    public double X;       // метры (Восток)
    public double Y;       // метры (Север)
    public double Alt;     // метры (Высота)
    public double Heading; // градусы 0..360
    public double Pitch;   // градусы
    public double Roll;    // градусы
    public double Speed;   // м/с
}

public class DebriefEntity
{
    public string Id;
    public string Name;
    public string Pilot;
    public string Type;       // "Air", "Missile", "Ground", "Sea"
    public string Coalition;  // "Allies", "Enemies", "Neutral"
    public bool IsPlayer;
    public double FirstTime = double.MaxValue;
    public double LastTime = double.MinValue;
    public List<DebriefSample> Track = new List<DebriefSample>();

    public DebriefSample GetStateAt(double t)
    {
        if (Track.Count == 0) return null;
        if (t < FirstTime - 0.5 || t > LastTime + 1.5) return null;
        if (t <= Track[0].Time) return Track[0];
        if (t >= Track[Track.Count - 1].Time) return Track[Track.Count - 1];

        int lo = 0, hi = Track.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (Track[mid].Time < t) lo = mid + 1;
            else hi = mid - 1;
        }
        int idx2 = Math.Min(Track.Count - 1, Math.Max(1, lo));
        int idx1 = idx2 - 1;
        DebriefSample s1 = Track[idx1];
        DebriefSample s2 = Track[idx2];
        double dt = s2.Time - s1.Time;
        if (dt <= 0.0001) return s1;

        double k = (t - s1.Time) / dt;
        double dHdg = ((s2.Heading - s1.Heading + 540.0) % 360.0) - 180.0;

        return new DebriefSample
        {
            Time = t,
            X = s1.X + (s2.X - s1.X) * k,
            Y = s1.Y + (s2.Y - s1.Y) * k,
            Alt = s1.Alt + (s2.Alt - s1.Alt) * k,
            Heading = (s1.Heading + dHdg * k + 360.0) % 360.0,
            Pitch = s1.Pitch + (s2.Pitch - s1.Pitch) * k,
            Roll = s1.Roll + (s2.Roll - s1.Roll) * k,
            Speed = s1.Speed + (s2.Speed - s1.Speed) * k
        };
    }
}

public class TacticalEvent
{
    public double Time;
    public string Kind;        // "LAUNCH", "KILL", "KIA", "EJECT", "MISTAKE", "INFO"
    public string Severity;    // "CRITICAL", "WARNING", "GOOD", "INFO"
    public string Title;
    public string Description;
    public string Advice;
    public double WorldX;
    public double WorldY;
}

public class DebriefSession
{
    public string SourceFile;
    public string Title;
    public string PilotName;
    public double Duration;
    public bool HasFullRadarCoords;
    public List<DebriefEntity> Entities = new List<DebriefEntity>();
    public List<TacticalEvent> Events = new List<TacticalEvent>();
    public List<TacticalEvent> Mistakes = new List<TacticalEvent>();
}

public static class DebriefEngine
{
    public static string GetReplaysDir()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL", "Replays");
        try { Directory.CreateDirectory(dir); } catch { }
        return dir;
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

        files.Sort((a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
        return files;
    }

    public static DebriefSession LoadSession(string filePath, string preferredPilot)
    {
        if (filePath.EndsWith(".vtolrep", StringComparison.OrdinalIgnoreCase))
            return LoadVtolRep(filePath, preferredPilot);
        return LoadAcmi(filePath, preferredPilot);
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
                double t;
                if (double.TryParse(line.Substring(1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out t))
                    curTime = t;
                continue;
            }

            if (line.StartsWith("-"))
            {
                string remId = line.Substring(1).Trim();
                DebriefEntity remEnt;
                if (map.TryGetValue(remId, out remEnt))
                    remEnt.LastTime = curTime;
                continue;
            }

            int commaIdx = line.IndexOf(',');
            if (commaIdx <= 0) continue;

            string objId = line.Substring(0, commaIdx).Trim();
            string payload = line.Substring(commaIdx + 1);

            if (objId == "0")
            {
                Match mLat = Regex.Match(payload, @"ReferenceLatitude=([-\d\.]+)");
                Match mLon = Regex.Match(payload, @"ReferenceLongitude=([-\d\.]+)");
                if (mLat.Success) double.TryParse(mLat.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out refLat);
                if (mLon.Success) double.TryParse(mLon.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out refLon);
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
                    if (!lastRawCoord.TryGetValue(objId, out prev))
                        prev = new double[6];

                    for (int i = 0; i < Math.Min(tc.Length, 6); i++)
                    {
                        if (!string.IsNullOrEmpty(tc[i]))
                        {
                            double val;
                            if (double.TryParse(tc[i], NumberStyles.Float, CultureInfo.InvariantCulture, out val))
                                prev[i] = val;
                        }
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
        IdentifyPlayerAndAnalyze(session, preferredPilot);
        return session;
    }

    private static List<string> ReadAcmiLines(string filePath)
    {
        List<string> lines = new List<string>();
        byte[] header = new byte[4];
        using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            fs.Read(header, 0, 4);

        // Если это ZIP-контейнер (.zip.acmi)
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

    // =====================================================================
    // === ПАРСЕР ВСТРОЕННЫХ РЕПЛЕЕВ ИЗ PLAYER.LOG (.vtolrep) ==============
    // =====================================================================
    private static DebriefSession LoadVtolRep(string filePath, string preferredPilot)
    {
        DebriefSession session = new DebriefSession();
        session.SourceFile = filePath;
        session.Title = Path.GetFileNameWithoutExtension(filePath);
        session.HasFullRadarCoords = false;
        DebriefEntity player = new DebriefEntity
        {
            Id = "player",
            Name = "Player",
            Pilot = preferredPilot ?? "Pilot",
            Type = "Air",
            Coalition = "Allies",
            IsPlayer = true
        };
        session.Entities.Add(player);
        double maxT = 10.0;
        foreach (string raw in File.ReadAllLines(filePath, Encoding.UTF8))
        {
            string[] p = raw.Split('|');
            if (p.Length < 3) continue;
            double t;
            double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out t);
            if (t > maxT) maxT = t;
            string evType = p[1];
            string arg1 = p.Length > 2 ? p[2] : "";
            string arg2 = p.Length > 3 ? p[3] : "";
            if (evType == "META")
            {
                if (!string.IsNullOrEmpty(arg1)) { player.Pilot = arg1; session.PilotName = arg1; }
                if (!string.IsNullOrEmpty(arg2)) player.Name = arg2;
            }
            else if (evType == "LAUNCH")
            {
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
                session.Events.Add(new TacticalEvent
                {
                    Time = t,
                    Kind = "KILL",
                    Severity = "GOOD",
                    Title = "Сбит: " + arg1,
                    Description = string.Format("Цель {0} уничтожена ({1})", arg1, string.IsNullOrEmpty(arg2) ? "АСП" : arg2)
                });
            }
            else if (evType == "KIA" || evType == "EJECT")
            {
                session.Events.Add(new TacticalEvent
                {
                    Time = t,
                    Kind = evType,
                    Severity = "CRITICAL",
                    Title = evType == "EJECT" ? "Катапультирование" : "Борт потерян",
                    Description = arg1
                });
            }
        }
        session.Duration = maxT;
        AnalyzeTelemetryEvents(session);
        return session;
    }

    // =====================================================================
    // === АВТОМАТИЧЕСКИЙ ТАКТИЧЕСКИЙ КОУЧ (ПОИСК ОШИБОК ПИЛОТА) ===========
    // =====================================================================
    private static void IdentifyPlayerAndAnalyze(DebriefSession session, string preferredPilot)
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

        // 1. Анализ каждого пуска ракеты пилотом
        foreach (DebriefEntity msl in session.Entities)
        {
            if (msl.Type != "Missile" || msl.Track.Count < 2) continue;
            DebriefSample launchPt = msl.Track[0];
            DebriefSample pState = player.GetStateAt(launchPt.Time);
            if (pState == null) continue;
            double distToPlayer = Math.Sqrt(Math.Pow(launchPt.X - pState.X, 2) + Math.Pow(launchPt.Y - pState.Y, 2));
            if (distToPlayer > 350.0) continue;

            DebriefEntity target = null;
            double bestDist = double.MaxValue;
            foreach (DebriefEntity cand in session.Entities)
            {
                if (cand == player || cand.Type != "Air" || cand.Coalition == player.Coalition) continue;
                DebriefSample cState = cand.GetStateAt(launchPt.Time);
                if (cState == null) continue;
                double d = Math.Sqrt(Math.Pow(cState.X - pState.X, 2) + Math.Pow(cState.Y - pState.Y, 2));
                if (d < bestDist) { bestDist = d; target = cand; }
            }
            session.Events.Add(new TacticalEvent
            {
                Time = launchPt.Time,
                Kind = "LAUNCH",
                Severity = "INFO",
                Title = "Пуск " + msl.Name,
                Description = target != null ? string.Format("По цели {0} (дистанция {1:F1} км)", target.Name, bestDist / 1000.0) : msl.Name,
                WorldX = launchPt.X,
                WorldY = launchPt.Y
            });

            if (target != null && bestDist > 26000.0 && (pState.Alt < 3500.0 || pState.Speed < 270.0))
            {
                TacticalEvent err = new TacticalEvent
                {
                    Time = launchPt.Time,
                    Kind = "MISTAKE",
                    Severity = "WARNING",
                    Title = string.Format("Пуск {0} с малой энергией ({1:F1} км)", msl.Name, bestDist / 1000.0),
                    Description = string.Format("Высота {0:F0} м, скорость {1:F0} км/ч — ракета быстро потеряет энергию в плотных слоях атмосферы.", pState.Alt, pState.Speed * 3.6),
                    Advice = "Перед дальним пуском наберите 7 000–9 000 м и разгонитесь выше 0.95 Маха, либо сблизьтесь до 18–20 км.",
                    WorldX = pState.X,
                    WorldY = pState.Y
                };
                session.Events.Add(err);
                session.Mistakes.Add(err);
            }
        }
        // 2. Анализ уклонения от вражеских ракет (Notch / кинематика)
        foreach (DebriefEntity enemyMsl in session.Entities)
        {
            if (enemyMsl.Type != "Missile" || enemyMsl.Track.Count < 4) continue;
            DebriefSample m0 = enemyMsl.Track[0];
            DebriefSample p0 = player.GetStateAt(m0.Time);
            if (p0 == null) continue;
            double d0 = Math.Sqrt(Math.Pow(m0.X - p0.X, 2) + Math.Pow(m0.Y - p0.Y, 2));
            if (d0 < 400.0) continue;
            double minApproach = double.MaxValue;
            double dangerTime = -1;
            for (int i = 0; i < enemyMsl.Track.Count; i++)
            {
                DebriefSample ms = enemyMsl.Track[i];
                DebriefSample ps = player.GetStateAt(ms.Time);
                if (ps == null) continue;
                double d = Math.Sqrt(Math.Pow(ms.X - ps.X, 2) + Math.Pow(ms.Y - ps.Y, 2));
                if (d < minApproach) { minApproach = d; dangerTime = ms.Time; }
            }
            if (minApproach < 4500.0 && dangerTime > m0.Time + 2.0)
            {
                DebriefSample pMid = player.GetStateAt((m0.Time + dangerTime) * 0.5);
                DebriefSample mMid = enemyMsl.GetStateAt((m0.Time + dangerTime) * 0.5);
                if (pMid != null && mMid != null)
                {
                    double bearingToMsl = (Math.Atan2(mMid.X - pMid.X, mMid.Y - pMid.Y) * 180.0 / Math.PI + 360.0) % 360.0;
                    double aspect = Math.Abs(((bearingToMsl - pMid.Heading + 540.0) % 360.0) - 180.0);
                    if (aspect < 35.0)
                    {
                        TacticalEvent err = new TacticalEvent
                        {
                            Time = pMid.Time,
                            Kind = "MISTAKE",
                            Severity = "CRITICAL",
                            Title = "Отсутствие отворота (Notch) от ракеты " + enemyMsl.Name,
                            Description = string.Format("Угол на ракету {0:F0}° (лобовое сближение) — максимальная скорость сближения и идеальный доплеровский засвет для ГСН.", aspect),
                            Advice = "Выполните отворот на 90° от курса ракеты (поставьте её на 3 или 9 часов), снижайтесь к земле и отстреливайте дипольные отражатели (Chaff).",
                            WorldX = pMid.X,
                            WorldY = pMid.Y
                        };
                        session.Events.Add(err);
                        session.Mistakes.Add(err);
                    }
                }
            }
        }
        // 3. Проверка потери энергии в маневренном бою (Stall / Energy Trap)
        double lastStallWarn = -999;
        for (int i = 0; i < player.Track.Count; i++)
        {
            DebriefSample s = player.Track[i];
            if (s.Time > 25.0 && s.Alt > 150.0 && s.Alt < 4500.0 && s.Speed < 105.0 && (s.Time - lastStallWarn) > 30.0)
            {
                lastStallWarn = s.Time;
                TacticalEvent err = new TacticalEvent
                {
                    Time = s.Time,
                    Kind = "MISTAKE",
                    Severity = "WARNING",
                    Title = string.Format("Критическая потеря скорости ({0:F0} км/ч)", s.Speed * 3.6),
                    Description = string.Format("На высоте {0:F0} м скорость упала ниже 380 км/ч — самолёт теряет управляемость и становится лёгкой целью.", s.Alt),
                    Advice = "Ослабьте взятие РУС на себя, переведите нос ниже горизонта для разгона и используйте форсаж.",
                    WorldX = s.X,
                    WorldY = s.Y
                };
                session.Events.Add(err);
                session.Mistakes.Add(err);
            }
        }
        session.Events.Sort((a, b) => a.Time.CompareTo(b.Time));
        session.Mistakes.Sort((a, b) => a.Time.CompareTo(b.Time));
    }
    private static void AnalyzeTelemetryEvents(DebriefSession session)
    {
        // Анализ лога боя (.vtolrep): спам ракетами (Overkill) и низкая результативность
        List<TacticalEvent> launches = new List<TacticalEvent>();
        int kills = 0;
        foreach (TacticalEvent ev in session.Events)
        {
            if (ev.Kind == "LAUNCH") launches.Add(ev);
            else if (ev.Kind == "KILL") kills++;
        }
        for (int i = 2; i < launches.Count; i++)
        {
            if (launches[i].Time - launches[i - 2].Time <= 4.0)
            {
                TacticalEvent err = new TacticalEvent
                {
                    Time = launches[i].Time,
                    Kind = "MISTAKE",
                    Severity = "WARNING",
                    Title = "Залповый перерасход боекомплекта (3 ракеты за 4 сек)",
                    Description = "Выпущено несколько ракет подряд без оценки попадания первой.",
                    Advice = "Выдерживайте паузу 5–8 секунд между пусками, чтобы оценить манёвр цели и сохранить БК."
                };
                session.Events.Add(err);
                session.Mistakes.Add(err);
            }
        }
        if (launches.Count >= 4 && kills == 0)
        {
            TacticalEvent err = new TacticalEvent
            {
                Time = launches[launches.Count - 1].Time,
                Kind = "MISTAKE",
                Severity = "CRITICAL",
                Title = string.Format("Низкая эффективность пусков: 0 попаданий из {0}", launches.Count),
                Description = "Все выпущенные ракеты ушли мимо цели или потеряли захват.",
                Advice = "Не пускайте ракеты с предельной дистанции по отворачивающей цели и удерживайте радарный подсвет до перехода ГСН в активный режим."
            };
            session.Events.Add(err);
            session.Mistakes.Add(err);
        }
        session.Events.Sort((a, b) => a.Time.CompareTo(b.Time));
        session.Mistakes.Sort((a, b) => a.Time.CompareTo(b.Time));
    }
}
