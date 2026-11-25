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


// Один пуск ракеты, собранный строго из фактов реплея:
// носитель (Pilot), момент старта (FirstTime), конец трека (LastTime) и факт поражения цели.
public class DebriefLaunch
{
    public double Time;
    public string MissileName;
    public string MissileId;
    public string Launcher;      // кто выпустил (Pilot ракеты)
    public string Coalition;     // "Allies" / "Enemies" / "Neutral"
    public string Side;          // "МОЙ" / "СОЮЗ" / "ВРАГ"
    public string TargetName;    // имя поражённой цели, если она известна по факту
    public double LaunchDist = double.NaN;   // 3D-дистанция до цели в момент пуска
    public double FlightTime;    // время полёта ракеты до конца трека
    public string Result;        // "ПОПАДАНИЕ" / "ПРОМАХ"
}

