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
// === МЕНЕДЖЕР ТРЕКОВ (RadioMusic) ========================================
// =========================================================================
public class MusicManagerForm : Form
{
    private string musicDir;
    private ListBox lstTracks;
    private Label lblCount;

    public MusicManagerForm(string dir)
    {
        this.musicDir = dir;
        this.Text = "VTOL QoL — Управление треками RadioMusic";
        DarkWindowHelper.Apply(this);
        this.Size = new Size(640, 500);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.BackColor = Color.FromArgb(18, 22, 20);
        this.ForeColor = Color.FromArgb(0, 255, 102);
        this.Font = new Font("Consolas", 9.5f, FontStyle.Regular);

        Label lblTitle = new Label() { Text = "СПИСОК ТРЕКОВ В RADIOMUSIC:", Left = 20, Top = 15, AutoSize = true, Font = new Font("Consolas", 10f, FontStyle.Bold) };
        this.Controls.Add(lblTitle);

        lstTracks = new ListBox();
        lstTracks.Location = new Point(20, 42);
        lstTracks.Size = new Size(430, 360);
        lstTracks.BackColor = Color.FromArgb(24, 28, 26);
        lstTracks.ForeColor = Color.White;
        lstTracks.BorderStyle = BorderStyle.FixedSingle;
        lstTracks.SelectionMode = SelectionMode.MultiExtended;

        this.Controls.Add(lstTracks);

        int top = 42, btnW = 150, btnH = 36, gap = 10;

        Button btnPlay = CreateBtn("▶ Прослушать", ref top, btnW, btnH, gap, Color.FromArgb(0, 110, 140));
        btnPlay.Click += (s, e) => {
            if (lstTracks.SelectedItem == null) return;
            string path = Path.Combine(musicDir, lstTracks.SelectedItem.ToString());
            if (File.Exists(path)) try { Process.Start(path); } catch { }
        };

        Button btnShuffle = CreateBtn("🔀 Перемешать", ref top, btnW, btnH, gap, Color.FromArgb(70, 50, 110));
        btnShuffle.Click += (s, e) => {
            if (MusicShuffler.IsGameRunning()) { MessageBox.Show("Закройте VTOL VR перед перемешиванием треков.", "Игра запущена", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (MusicShuffler.ShuffleNow(musicDir, true))
            {
                LoadTracks();
                lblCount.Text = "Всего треков: " + lstTracks.Items.Count + "  |  Плейлист перемешан!";
            }
        };


        Button btnCleanPrefix = CreateBtn("Убрать 0001_", ref top, btnW, btnH, gap, Color.FromArgb(45, 75, 60));
        btnCleanPrefix.Click += (s, e) => {
            if (MusicShuffler.IsGameRunning()) { MessageBox.Show("Закройте VTOL VR перед переименованием.", "Игра запущена", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            foreach (string f in Directory.GetFiles(musicDir, "*.mp3"))
            {
                string name = Path.GetFileName(f);
                string clean = Regex.Replace(name, @"^\d{4}_", "");
                if (clean != name) try { File.Move(f, Path.Combine(musicDir, clean)); } catch { }
            }
            LoadTracks();
        };

        Button btnDelete = CreateBtn("Удалить выбранные", ref top, btnW, btnH, gap, Color.FromArgb(140, 35, 35));
        btnDelete.Click += BtnDelete_Click;

        Button btnRefresh = CreateBtn("Обновить список", ref top, btnW, btnH, gap, Color.FromArgb(40, 50, 45));
        btnRefresh.Click += (s, e) => LoadTracks();

        lblCount = new Label() { Left = 20, Top = 415, Width = 430, ForeColor = Color.Silver };
        this.Controls.Add(lblCount);

        Button btnClose = new Button() { Text = "Закрыть", Left = 460, Top = 410, Width = 150, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(30, 36, 33), ForeColor = Color.White };
        btnClose.Click += (s, e) => this.Close();
        this.Controls.Add(btnClose);

        LoadTracks();
    }

    private Button CreateBtn(string text, ref int top, int width, int height, int gap, Color backColor)
    {
        Button btn = new Button() { Text = text, Location = new Point(460, top), Size = new Size(width, height), FlatStyle = FlatStyle.Flat, BackColor = backColor, ForeColor = Color.White };
        this.Controls.Add(btn);
        top += height + gap;
        return btn;
    }

    private void LoadTracks()
    {
        lstTracks.Items.Clear();
        if (!Directory.Exists(musicDir)) return;
        string[] files = Directory.GetFiles(musicDir, "*.mp3");
        Array.Sort(files);
        foreach (string f in files) lstTracks.Items.Add(Path.GetFileName(f));
        lblCount.Text = "Всего треков: " + lstTracks.Items.Count;
    }

    private void BtnDelete_Click(object sender, EventArgs e)
    {
        if (lstTracks.SelectedItems.Count == 0) return;
        if (MessageBox.Show("Удалить выбранные треки (" + lstTracks.SelectedItems.Count + " шт.)?", "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            foreach (object item in lstTracks.SelectedItems)
            {
                string path = Path.Combine(musicDir, item.ToString());
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
            LoadTracks();
        }
    }
}

