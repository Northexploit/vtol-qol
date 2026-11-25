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


public class TacticalBeam
{
    public double StartTime;
    public double EndTime;
    public string Kind;       // "RADAR_SCAN", "AWACS_LINK", "PLAYER_LOCK", "RWR_SPIKE", "JAMMER"
    public string FromId;
    public string ToId;
    public string FromName;
    public string ToName;
    public double FromX;
    public double FromY;
    public double ToX;
    public double ToY;
    public double DirX;
    public double DirY;
    public double DirZ;
    public string JamMode = "";   // "NOISE", "DRFM", "SAS" — только для Kind == "JAMMER"
    public string JamBand = "";   // "Low", "Mid", "High" — только для Kind == "JAMMER"
}

