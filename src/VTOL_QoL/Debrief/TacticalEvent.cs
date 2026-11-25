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


public class TacticalEvent
{
    public double Time;
    public string Kind;        // "LAUNCH", "KILL", "KIA", "EJECT", "MISTAKE", "THREAT", "RWR", "RADAR", "DAMAGE", "INFO"
    public string Severity;    // "CRITICAL", "WARNING", "GOOD", "INFO"
    public string Title;
    public string Description;
    public string Advice;
    public string RawSource;
    public double WorldX;
    public double WorldY;
    public double Altitude = double.NaN;   // высота события (если известна из трека)
    public double SpeedKmh = double.NaN;   // скорость в момент события
    public double DistanceM = double.NaN;  // дистанция до угрозы/цели
    public string Category = "";           // "THREAT", "MISTAKE", "SUCCESS", "INFO" — для фильтров хронологии
}

