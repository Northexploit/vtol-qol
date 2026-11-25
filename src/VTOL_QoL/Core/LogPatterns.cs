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
// === ПАТТЕРНЫ РАСПОЗНАВАНИЯ ЛОГА (log_patterns.ini) =====================
// =========================================================================
public class LogPatterns
{
    public Dictionary<string, List<string>> Aircraft = new Dictionary<string, List<string>>();
    public List<string> CallsignPatterns = new List<string>();
    public List<string> KillTemplates = new List<string>();
    public List<string> EjectTemplates = new List<string>();
    public List<string> DiagnosticKeywords = new List<string>();

    public static string GetPath()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL");
        try { Directory.CreateDirectory(dir); } catch { }
        return Path.Combine(dir, "log_patterns.ini");
    }

    public void LoadOrCreateDefault()
    {
        string filePath = GetPath();
        if (!File.Exists(filePath))
        {
            try { File.WriteAllText(filePath, DefaultContent(), Encoding.UTF8); } catch { }
        }
        try { Parse(File.ReadAllLines(filePath, Encoding.UTF8)); } catch { ParseDefaultFallback(); }
    }

    private void ParseDefaultFallback()
    {
        Parse(DefaultContent().Replace("\r\n", "\n").Split('\n'));
    }

    private string DefaultContent()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("; Паттерны распознавания строк Player.log. Можно редактировать вручную.");
        sb.AppendLine("; Используйте {NICK} как заполнитель позывного пилота.");
        sb.AppendLine("[Aircraft:FA-26B]");
        sb.AppendLine("Pattern=FA-26B|FA26B|F/A-26B|F/A26B");
        sb.AppendLine("[Aircraft:F-45A]");
        sb.AppendLine("Pattern=F-45A|F45A");
        sb.AppendLine("[Aircraft:AV-42C]");
        sb.AppendLine("Pattern=AV-42C|AV42C|VTOL4");
        sb.AppendLine("[Aircraft:AH-94]");
        sb.AppendLine("Pattern=AH-94|AH94");
        sb.AppendLine("[Aircraft:T-55]");
        sb.AppendLine("Pattern=T-55|T55");
        sb.AppendLine("[Aircraft:EF-24G]");
        sb.AppendLine("Pattern=EF-24G|EF24G");
        sb.AppendLine("[Callsign]");
        sb.AppendLine("Pattern1=((?:Alpha|Bravo|Charlie|Delta|Echo|Foxtrot|Golf|Hotel|India|Juliet|Kilo|Lima|Mike|November|Oscar|Papa|Quebec|Romeo|Sierra|Tango|Uniform|Victor|Whiskey|X-?ray|Yankee|Zulu)\\s+\\d+-\\d+)");
        sb.AppendLine("[Kill]");
        sb.AppendLine("Pattern1=was killed by {NICK}");
        sb.AppendLine("[Eject]");
        sb.AppendLine(@"Pattern1=\${NICK}\$ ejected");
        sb.AppendLine(@"Pattern2=\${NICK}\$ was killed");
        sb.AppendLine("[Diagnostics]");
        sb.AppendLine("Keyword1=spawn");
        sb.AppendLine("Keyword2=vehicle");
        sb.AppendLine("Keyword3=callsign");
        sb.AppendLine("Keyword4=ejected");
        sb.AppendLine("Keyword5=killed");
        return sb.ToString();
    }

    private void Parse(string[] lines)
    {
        string section = "";
        foreach (string raw in lines)
        {
            string l = raw.Trim();
            if (l.Length == 0 || l.StartsWith(";")) continue;
            if (l.StartsWith("[") && l.EndsWith("]"))
            {
                section = l.Substring(1, l.Length - 2);
                continue;
            }
            int idx = l.IndexOf('=');
            if (idx < 0) continue;
            string val = l.Substring(idx + 1).Trim();
            if (section.StartsWith("Aircraft:"))
            {
                string plane = section.Substring("Aircraft:".Length);
                string[] alts = val.Split('|');
                List<string> list;
                if (!Aircraft.TryGetValue(plane, out list)) { list = new List<string>(); Aircraft[plane] = list; }
                list.AddRange(alts);
            }
            else if (section == "Callsign") CallsignPatterns.Add(val);
            else if (section == "Kill") KillTemplates.Add(val);
            else if (section == "Eject") EjectTemplates.Add(val);
            else if (section == "Diagnostics") DiagnosticKeywords.Add(val);
        }
    }

    public string NormalizeAircraft(string raw)
    {
        foreach (KeyValuePair<string, List<string>> kv in Aircraft)
            foreach (string alt in kv.Value)
                if (string.Equals(raw, alt, StringComparison.OrdinalIgnoreCase)) return kv.Key;
        return raw;
    }

    public string BuildAircraftRegex()
    {
        List<string> allAlts = new List<string>();
        foreach (KeyValuePair<string, List<string>> kv in Aircraft) allAlts.AddRange(kv.Value);
        for (int i = 0; i < allAlts.Count; i++) allAlts[i] = Regex.Escape(allAlts[i]);
        return "(" + string.Join("|", allAlts.ToArray()) + ")";
    }
}

