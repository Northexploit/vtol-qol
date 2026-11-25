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
// === ОНЛАЙН-КАТАЛОГ И ЗАГРУЗЧИК STEAM WORKSHOP (ЧЕРЕЗ STEAMCMD) ==========
// =========================================================================
public class WorkshopCatalogItem
{
    public string PublishedFileId;
    public string Title;
    public string PreviewUrl;
    public string AircraftTag;
}

