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
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using OpenFileDialog = System.Windows.Forms.OpenFileDialog;
using SaveFileDialog = System.Windows.Forms.SaveFileDialog;


// =========================================================================
// === СКАНЕР ПУБЛИЧНЫХ ЛОББИ VTOL VR (STEAMWORKS MATCHMAKING + SDR TEST) ===
// =========================================================================
public class VtolLobbyInfo
{
    public ulong LobbyId;
    public string Name;
    public string Mission;
    public string State;
    public int Members;
    public int MaxMembers;
    public bool HasPassword;
    public string ConnectHint;
    public bool IsReachableWarning;
    public Dictionary<string, string> RawData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

