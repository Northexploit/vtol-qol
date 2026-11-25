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


// Точка реального взрыва из штатного реплея (eventTrack / WorldEventKeyframe)
public class DebriefExplosion
{
    public double Time;
    public double X;
    public double Y;
    public double Alt;
}

