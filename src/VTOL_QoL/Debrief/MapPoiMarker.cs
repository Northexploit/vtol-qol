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


public class MapPoiMarker
{
    public string Kind; // "Base", "Carrier", "SAM", "Waypoint", "Bullseye"
    public string Label;
    public string Coalition;
    public double X;
    public double Y;
    public double RadiusMeters;
}

