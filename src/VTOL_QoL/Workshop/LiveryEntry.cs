using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using OpenFileDialog = System.Windows.Forms.OpenFileDialog;
using SaveFileDialog = System.Windows.Forms.SaveFileDialog;


// =========================================================================
// === ПАРСЕР ЛИВРЕЙ =======================================================
// =========================================================================
public class LiveryEntry
{
    public string FolderPath;
    public string Name;
    public string Aircraft;
    public string TexturePath;
    public string PreviewPath;
    public string ModelHint;
    public bool Valid;
    public string Source; // "Local" / "Workshop"
}

