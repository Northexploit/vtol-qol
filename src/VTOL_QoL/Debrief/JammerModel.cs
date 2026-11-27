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


// Модель помех, восстановленная из IL игры (RadarJammer / RadarJammer+JTransmitter).
// Никаких "примерных" конусов: мощность считается по той же формуле, что в игре.
public static class JammerModel
{
    // RadarJammer..ctor: directionalPower = 8, signalLeakFactor = 0.03
    public const double DirectionalPower = 8.0;
    public const double SignalLeakFactor = 0.03;

    // Конус сильного подавления для отрисовки. Диаграмма в игре — cos^8:
    // на 10° от оси сигнал ещё 88% от осевого (cos(10°)^8 = 0.88), на 23.5° — 50%.
    // Рисуем зону именно сильного подавления, а не «широкий лепесток» на -10 дБ.
    public const double NoiseConeHalfAngleDeg = 10.0;
    // Максимальная дальность конуса. В игре РЭБ достаёт очень далеко, но конус
    // не должен занимать полкарты: длину ограничиваем дистанцией до цели + запас.
    public const double NoiseConeRangeM = 150000.0;

    // Угол (в градусах) от оси луча, на котором относительный сигнал падает до level.
    // cos(theta)^directionalPower = level  =>  theta = acos(level^(1/power))
    public static double AngleForLevel(double level)
    {
        if (level <= 0.0) return 90.0;
        if (level >= 1.0) return 0.0;
        return Math.Acos(Math.Pow(level, 1.0 / DirectionalPower)) * 180.0 / Math.PI;
    }

    // Аналог JTransmitter.GetSignalStrength (без множителей power/maxPowerOutput, они постоянны
    // для конкретного передатчика). Возвращает относительный сигнал: 1.0 — точно по оси на
    // опорной дистанции. 0 — цель вне лепестка или за препятствием.
    public static double SignalFactor(double dirX, double dirY, double dirZ,
                                      double fromX, double fromY, double fromZ,
                                      double toX, double toY, double toZ)
    {
        double dl = Math.Sqrt(dirX * dirX + dirY * dirY + dirZ * dirZ);
        if (dl < 1e-6) return 0.0;
        double dx = toX - fromX;
        double dy = toY - fromY;
        double dz = toZ - fromZ;
        double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (dist < 1.0) return 0.0;

        // dot(dir, target - pos) <= 0  ->  передатчик смотрит в другую сторону
        double dot = (dx * dirX + dy * dirY + dz * dirZ) / dl;
        if (dot <= 0.0) return 0.0;

        double norm = dot / dist;                       // косинус угла между осью и целью
        double lobe = Math.Pow(norm, DirectionalPower); // основной лепесток
        double leak = SignalLeakFactor;                 // "просачивание" во все стороны
        return lobe + leak;
    }

    // Цвет/подпись уровня помех для UI
    public static string LevelLabel(double signal)
    {
        if (signal >= 0.75) return "СИЛЬНО";
        if (signal >= 0.40) return "СРЕДНЕ";
        if (signal >= 0.10) return "СЛАБО";
        return "";
    }
}

