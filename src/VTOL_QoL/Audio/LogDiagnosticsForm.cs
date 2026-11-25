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
// === ДИАГНОСТИКА ЛОГА (ПОДБОР ПАТТЕРНОВ) ================================
// =========================================================================
public class LogDiagnosticsForm : Form
{
    private TextBox txtLog;
    private CheckBox chkEnabled;

    public LogDiagnosticsForm()
    {
        this.Text = "VTOL QoL — Диагностика Player.log";
        DarkWindowHelper.Apply(this);
        this.Size = new Size(760, 560);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.BackColor = Color.FromArgb(18, 22, 20);
        this.ForeColor = Color.FromArgb(0, 255, 102);
        this.Font = new Font("Consolas", 9f);

        chkEnabled = new CheckBox() { Text = "Включить запись диагностики (для подбора паттернов)", Left = 20, Top = 15, Width = 500, Checked = SettingsManager.GetBool("DiagnosticsEnabled", false), ForeColor = Color.FromArgb(210, 240, 220) };
        this.Controls.Add(chkEnabled);

        txtLog = new TextBox();
        txtLog.Left = 20; txtLog.Top = 45; txtLog.Width = 705; txtLog.Height = 420;
        txtLog.Multiline = true; txtLog.ScrollBars = ScrollBars.Both; txtLog.ReadOnly = true; txtLog.WordWrap = false;
        txtLog.BackColor = Color.FromArgb(24, 28, 26); txtLog.ForeColor = Color.White; txtLog.Font = new Font("Consolas", 8.5f);
        this.Controls.Add(txtLog);

        Button btnRefresh = new Button() { Text = "Обновить", Left = 20, Top = 478, Width = 120, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(35, 45, 40), ForeColor = Color.White };
        btnRefresh.Click += (s, e) => LoadDiag();
        this.Controls.Add(btnRefresh);

        Button btnOpenPatterns = new Button() { Text = "Открыть log_patterns.ini", Left = 150, Top = 478, Width = 200, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 110, 140), ForeColor = Color.White };
        btnOpenPatterns.Click += (s, e) => { try { Process.Start("notepad.exe", LogPatterns.GetPath()); } catch { } };
        this.Controls.Add(btnOpenPatterns);

        Button btnClear = new Button() { Text = "Очистить", Left = 360, Top = 478, Width = 120, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(140, 35, 35), ForeColor = Color.White };
        btnClear.Click += (s, e) => { try { File.Delete(GetDiagPath()); } catch { } LoadDiag(); };
        this.Controls.Add(btnClear);

        Button btnSave = new Button() { Text = "Сохранить", Left = 585, Top = 478, Width = 140, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 140, 60), ForeColor = Color.White, DialogResult = DialogResult.OK };
        btnSave.Click += (s, e) => { SettingsManager.SetBool("DiagnosticsEnabled", chkEnabled.Checked); SettingsManager.Save(); };
        this.Controls.Add(btnSave);

        LoadDiag();
    }

    private string GetDiagPath()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL", "log_diagnostics.txt");
    }

    private void LoadDiag()
    {
        try
        {
            string path = GetDiagPath();
            txtLog.Text = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "(пусто — включите диагностику и полетайте)";
            txtLog.SelectionStart = txtLog.Text.Length;
            txtLog.ScrollToCaret();
        }
        catch { }
    }
}

