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


public class DebriefEntity
{
    public string Id;
    public string Name;
    public string Pilot;
    public string RawSource;
    public string Type;       // "Air", "Missile", "Ground", "Sea", "Base" (совместимый тип для карты)
    public string Class;      // точный класс из реплея: "Air", "Chopper", "Ground", "Sea", "Missile"
    public string Coalition;  // "Allies", "Enemies", "Neutral"
    public string RoleTag;    // "AWACS", "Tanker", "Fighter", "Bomber", "SAM", "Missile"
    public bool IsPlayer;
    // Сырой код типа из файла реплея (ReplayEntity.entityType) — для диагностики стороны.
    // -1 = неизвестно (не нативный .vtr).
    public int NativeType = -1;
    public double FirstTime = double.MaxValue;
    public double LastTime = double.MinValue;
    public List<DebriefSample> Track = new List<DebriefSample>();

    public DebriefSample GetStateAt(double t)
    {
        if (Track.Count == 0) return null;
        if (Type == "Ground" || Type == "Base")
        {
            if (t > LastTime + 1.0) return null;
            return Track[0];
        }
        if (t < FirstTime - 0.5 || t > LastTime + 1.0) return null;
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

