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
// === БЭКАП И ЗАЩИТА СОХРАНЕНИЙ (без изменений логики) ====================
// =========================================================================
public class SaveVaultForm : Form
{
    private ListBox lstBackups;
    private Label lblSaveStatus;

    public SaveVaultForm()
    {
        this.Text = "VTOL QoL — Резервные копии сохранений";
        DarkWindowHelper.Apply(this);
        this.Size = new Size(620, 480);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.BackColor = Color.FromArgb(18, 22, 20);
        this.ForeColor = Color.FromArgb(0, 255, 102);
        this.Font = new Font("Consolas", 9.5f);

        Label lbl = new Label() { Text = "АРХИВ СНАПШОТОВ СОХРАНЕНИЙ:", Left = 20, Top = 15, AutoSize = true, Font = new Font("Consolas", 10f, FontStyle.Bold) };
        this.Controls.Add(lbl);

        lblSaveStatus = new Label() { Left = 20, Top = 40, Width = 560, ForeColor = Color.Silver };
        this.Controls.Add(lblSaveStatus);

        lstBackups = new ListBox();
        lstBackups.Location = new Point(20, 68);
        lstBackups.Size = new Size(410, 310);
        lstBackups.BackColor = Color.FromArgb(24, 28, 26);
        lstBackups.ForeColor = Color.White;
        lstBackups.BorderStyle = BorderStyle.FixedSingle;
        this.Controls.Add(lstBackups);

        int top = 68, btnW = 155, btnH = 38, gap = 10;
        Button btnCreate = CreateBtn("+ Создать бэкап", ref top, btnW, btnH, gap, Color.FromArgb(0, 140, 60));
        btnCreate.Click += (s, e) => CreateSnapshot();
        Button btnRestore = CreateBtn("Восстановить", ref top, btnW, btnH, gap, Color.FromArgb(160, 100, 0));
        btnRestore.Click += BtnRestore_Click;
        Button btnDelete = CreateBtn("Удалить архив", ref top, btnW, btnH, gap, Color.FromArgb(140, 35, 35));
        btnDelete.Click += BtnDelete_Click;
        Button btnOpenDir = CreateBtn("Папка бэкапов", ref top, btnW, btnH, gap, Color.FromArgb(40, 50, 45));
        btnOpenDir.Click += (s, e) => { string dir = GetBackupsDir(); if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); Process.Start("explorer.exe", dir); };

        Button btnClose = new Button() { Text = "Закрыть", Left = 440, Top = 390, Width = 155, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(30, 36, 33), ForeColor = Color.White };
        btnClose.Click += (s, e) => this.Close();
        this.Controls.Add(btnClose);

        RefreshBackups();
    }

    private Button CreateBtn(string text, ref int top, int width, int height, int gap, Color backColor)
    {
        Button btn = new Button() { Text = text, Location = new Point(440, top), Size = new Size(width, height), FlatStyle = FlatStyle.Flat, BackColor = backColor, ForeColor = Color.White };
        this.Controls.Add(btn);
        top += height + gap;
        return btn;
    }

    private string GetSaveDataDir() { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Boundless Dynamics, LLC", "VTOLVR", "SaveData"); }
    private string GetBackupsDir() { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL", "SaveBackups"); }

    private void RefreshBackups()
    {
        lstBackups.Items.Clear();
        string bDir = GetBackupsDir();
        if (Directory.Exists(bDir))
        {
            string[] dirs = Directory.GetDirectories(bDir);
            Array.Sort(dirs); Array.Reverse(dirs);
            foreach (string d in dirs) lstBackups.Items.Add(Path.GetFileName(d));
        }
        string sDir = GetSaveDataDir();
        lblSaveStatus.Text = Directory.Exists(sDir) ? "Папка сохранений: " + sDir : "Папка сохранений игры пока не создана.";
    }

    private void CreateSnapshot()
    {
        string sDir = GetSaveDataDir();
        if (!Directory.Exists(sDir)) { MessageBox.Show("Папка сохранений не найдена:\n" + sDir, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        string targetDir = Path.Combine(GetBackupsDir(), "Backup_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        try
        {
            Directory.CreateDirectory(targetDir);
            foreach (string f in Directory.GetFiles(sDir, "*.*", SearchOption.AllDirectories))
            {
                string rel = f.Substring(sDir.Length).TrimStart('\\', '/');
                string dst = Path.Combine(targetDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.Copy(f, dst, true);
            }
            RefreshBackups();
            MessageBox.Show("Бэкап создан: " + Path.GetFileName(targetDir), "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show("Ошибка: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void BtnRestore_Click(object sender, EventArgs e)
    {
        if (lstBackups.SelectedItem == null) { MessageBox.Show("Выберите бэкап.", "Внимание", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        string bSource = Path.Combine(GetBackupsDir(), lstBackups.SelectedItem.ToString());
        string sDir = GetSaveDataDir();
        if (MessageBox.Show("Восстановить сохранения? Текущий прогресс будет перезаписан.", "Внимание", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
        {
            try
            {
                Directory.CreateDirectory(sDir);
                foreach (string f in Directory.GetFiles(bSource, "*.*", SearchOption.AllDirectories))
                {
                    string rel = f.Substring(bSource.Length).TrimStart('\\', '/');
                    string dst = Path.Combine(sDir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    File.Copy(f, dst, true);
                }
                MessageBox.Show("Сохранения восстановлены!", "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show("Ошибка: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }

    private void BtnDelete_Click(object sender, EventArgs e)
    {
        if (lstBackups.SelectedItem == null) return;
        string target = Path.Combine(GetBackupsDir(), lstBackups.SelectedItem.ToString());
        if (MessageBox.Show("Удалить бэкап?", "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            try { if (Directory.Exists(target)) Directory.Delete(target, true); RefreshBackups(); }
            catch (Exception ex) { MessageBox.Show("Ошибка: " + ex.Message); }
        }
    }
}

