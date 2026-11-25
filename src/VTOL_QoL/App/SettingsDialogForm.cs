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
// === ГЛАВНАЯ ФОРМА ========================================================
// =========================================================================
public class SettingsDialogForm : Form
{
    public CheckBox ChkShuffle;
    public CheckBox ChkLoudNorm;
    public CheckBox ChkDiscord;
    public CheckBox ChkDucking;
    public CheckBox ChkAudioSwitch;
    public CheckBox ChkDiagnostics;
    public CheckBox ChkKillSound;

    public SettingsDialogForm()
    {
        this.Text = "VTOL QoL — Опции";
        DarkWindowHelper.Apply(this);
        this.Size = new Size(490, 345);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.BackColor = Color.FromArgb(18, 22, 20);
        this.ForeColor = Color.FromArgb(0, 255, 102);
        this.Font = new Font("Consolas", 9.5f);

        Label lblTitle = new Label() { Text = "/// КОНФИГУРАЦИЯ МОДУЛЕЙ ///", Left = 20, Top = 14, AutoSize = true, Font = new Font("Consolas", 10.5f, FontStyle.Bold) };
        this.Controls.Add(lblTitle);

        ChkShuffle = AddStyledCheck("Умный шаффл музыки между вылетами", 48, SettingsManager.GetBool("Shuffle", true));
        ChkLoudNorm = AddStyledCheck("Выравнивание громкости (-14 LUFS)", 78, SettingsManager.GetBool("LoudNorm", true));
        ChkDiscord = AddStyledCheck("Discord Rich Presence (статус полёта)", 108, SettingsManager.GetBool("Discord", true));
        ChkDucking = AddStyledCheck("Авто-приглушение радио при разговоре", 138, SettingsManager.GetBool("Ducking", true));
        ChkAudioSwitch = AddStyledCheck("Авто-переключение аудио на VR-шлем", 168, SettingsManager.GetBool("AudioSwitch", true));
        ChkDiagnostics = AddStyledCheck("Диагностика лога игры (Player.log)", 198, SettingsManager.GetBool("DiagnosticsEnabled", false));
        ChkKillSound = AddStyledCheck("Озвучка серий сбитий в наушниках", 228, SettingsManager.GetBool("KillSoundEnabled", true));

        SaveVaultForm openVault = null;
        Button btnBackup = new Button() { Text = "🛡 Бэкап сохранений", Left = 22, Top = 266, Width = 195, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(130, 80, 0), ForeColor = Color.White };
        btnBackup.Click += (s, e) => {
            if (openVault != null && !openVault.IsDisposed) { openVault.Activate(); openVault.BringToFront(); return; }
            openVault = new SaveVaultForm();
            openVault.FormClosed += (s2, e2) => { openVault = null; };
            openVault.Show(this);
        };
        this.Controls.Add(btnBackup);

        Button btnClose = new Button() { Text = "Готово", Left = 335, Top = 266, Width = 120, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 130, 65), ForeColor = Color.White };
        btnClose.Click += (s, e) => this.Close();
        this.Controls.Add(btnClose);
    }


    private CheckBox AddStyledCheck(string text, int top, bool isChecked)
    {
        CheckBox cb = new CheckBox();
        cb.Text = text;
        cb.Location = new Point(22, top);
        cb.Size = new Size(435, 24);
        cb.Checked = isChecked;
        cb.ForeColor = Color.FromArgb(210, 240, 220);
        cb.Cursor = Cursors.Hand;

        cb.Paint += (s, e) => {
            e.Graphics.Clear(this.BackColor);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            int boxSize = 15;
            int boxY = (cb.Height - boxSize) / 2;
            Rectangle boxRect = new Rectangle(1, boxY, boxSize, boxSize);

            Color fill = cb.Checked ? Color.FromArgb(0, 75, 42) : Color.FromArgb(24, 30, 27);
            Color border = cb.Checked ? Color.FromArgb(0, 255, 102) : Color.FromArgb(65, 95, 80);

            using (SolidBrush br = new SolidBrush(fill))
                e.Graphics.FillRectangle(br, boxRect);
            using (Pen pen = new Pen(border, 1.4f))
                e.Graphics.DrawRectangle(pen, boxRect);

            if (cb.Checked)
            {
                using (Pen checkPen = new Pen(Color.FromArgb(0, 255, 102), 2.1f))
                {
                    checkPen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    checkPen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    e.Graphics.DrawLines(checkPen, new Point[] {
                        new Point(4, boxY + 8),
                        new Point(7, boxY + 11),
                        new Point(13, boxY + 4)
                    });
                }
            }

            TextRenderer.DrawText(e.Graphics, cb.Text, cb.Font,
                new Rectangle(23, 0, cb.Width - 23, cb.Height),
                cb.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        };

        this.Controls.Add(cb);
        return cb;
    }
}

