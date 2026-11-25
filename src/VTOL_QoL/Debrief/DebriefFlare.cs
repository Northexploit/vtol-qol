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


// Отстрел ЛТЦ/ДО из eventTrack (eventType 2 = Flare, 3 = Chaff).
// В текущих реплеях VTOL VR таких кадров нет — список будет пустым, отрисовка молчит.
public class DebriefFlare
{
    public double Time;
    public double X;
    public double Y;
    public double Alt;
    public bool IsChaff;   // true = ДО, false = ЛТЦ
}

