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


// =========================================================================
// === ДВИЖОК ТАКТИЧЕСКОЙ 2D-КАРТЫ, ТРЕК-ЛОГГЕРА И РАЗБОРА БОЯ =============
// =========================================================================
public class DebriefSample
{
    public double Time;
    public double X;       // метры (Восток)
    public double Y;       // метры (Север)
    public double Alt;     // метры (Высота)
    public double Heading; // градусы 0..360
    public double Pitch;
    public double Roll;
    public double Speed;   // м/с
    public double VX, VY, VZ; // точный вектор скорости из реплея (м/с)
}

