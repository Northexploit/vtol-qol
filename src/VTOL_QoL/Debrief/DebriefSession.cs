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


public class DebriefSession
{
    public string SourceFile;
    public string Title;
    public string MapTitle;
    public string PilotName;
    public double Duration;
    public bool HasFullRadarCoords;
    public bool HasExactTelemetry;      // true, если загружен реальный трек-лог (POS|) или ACMI
    public bool IsReconstructedFromLog;

    // Границы всех объектов боя для авто-центрирования камеры
    public double BoundsMinX = double.MaxValue;
    public double BoundsMaxX = double.MinValue;
    public double BoundsMinY = double.MaxValue;
    public double BoundsMaxY = double.MinValue;

    // Статистика боя
    public int PlayerKills;
    public int AlliedKills;
    public int PlayerLaunches;
    public int RwrSpikesCount;
    public int ThreatMissilesCount;

    // 2D-подложка карты высот/береговой линии
    public Bitmap TerrainBitmap;
    public float[,] TerrainHeights;
    public double MapMinX;
    public double MapMinY;
    public double MapSizeMeters;
    // Heightmap не всегда покрывает всю карту: на больших картах она может занимать
    // только один квадрант. Поэтому у рельефа свои границы.
    public double TerrainMinX;
    public double TerrainMinY;
    public double TerrainSizeMeters;

    public double GetGroundAltitudeAt(double wx, double wy)
    {
        if (TerrainHeights == null) return 0.0;
        double tMinX = TerrainSizeMeters > 100.0 ? TerrainMinX : MapMinX;
        double tMinY = TerrainSizeMeters > 100.0 ? TerrainMinY : MapMinY;
        double tSize = TerrainSizeMeters > 100.0 ? TerrainSizeMeters : MapSizeMeters;
        if (tSize <= 100.0) return 0.0;
        // Высотная карта может быть не квадратной: X и Y масштабируются независимо,
        // иначе рельеф «съезжает» по одной из осей.
        int nW = TerrainHeights.GetLength(0);
        int nH = TerrainHeights.GetLength(1);
        if (nW < 2 || nH < 2) return 0.0;
        double u = (wx - tMinX) / tSize;
        double v = (wy - tMinY) / tSize;
        if (u < 0.0 || u > 1.0 || v < 0.0 || v > 1.0) return 0.0;

        double fx = u * (nW - 1);
        double fy = v * (nH - 1);
        int x0 = Math.Max(0, Math.Min(nW - 2, (int)fx));
        int y0 = Math.Max(0, Math.Min(nH - 2, (int)fy));
        double tx = fx - x0;
        double ty = fy - y0;

        double h00 = TerrainHeights[x0, y0];
        double h10 = TerrainHeights[x0 + 1, y0];
        double h01 = TerrainHeights[x0, y0 + 1];
        double h11 = TerrainHeights[x0 + 1, y0 + 1];

        return (h00 * (1.0 - tx) + h10 * tx) * (1.0 - ty) + (h01 * (1.0 - tx) + h11 * tx) * ty;
    }
    public List<MapPoiMarker> MapMarkers = new List<MapPoiMarker>();
    // Аэродромы, восстановленные по взлётам/посадкам бортов (в .vtr их нет)
    public List<MapPoiMarker> Airfields = new List<MapPoiMarker>();

    public List<DebriefEntity> Entities = new List<DebriefEntity>();
    public List<TacticalBeam> Beams = new List<TacticalBeam>();
    public List<TacticalEvent> Events = new List<TacticalEvent>();
    public List<TacticalEvent> Mistakes = new List<TacticalEvent>();
    public List<DebriefExplosion> Explosions = new List<DebriefExplosion>();
    public List<DebriefFlare> Flares = new List<DebriefFlare>();
    public List<DebriefLaunch> Launches = new List<DebriefLaunch>();
    // Точные пиковые значения полёта игрока (из velocity/rotation штатного реплея)
    public double PlayerMaxG = 0.0;
    public double PlayerMaxGTime = 0.0;
    public double PlayerMaxSpeed = 0.0;
    public double PlayerMinAlt = double.MaxValue;
    public double PlayerMinAltTime = 0.0;

    public void ExpandBounds(double x, double y)
    {
        if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y)) return;
        if (x < BoundsMinX) BoundsMinX = x;
        if (x > BoundsMaxX) BoundsMaxX = x;
        if (y < BoundsMinY) BoundsMinY = y;
        if (y > BoundsMaxY) BoundsMaxY = y;
    }
}

