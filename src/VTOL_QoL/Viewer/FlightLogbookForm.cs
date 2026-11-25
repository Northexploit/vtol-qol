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
// === ТАКТИЧЕСКИЙ ЖУРНАЛ ВЫЛЕТОВ (без изменений логики) ===================
// =========================================================================
public class FlightLogbookForm : Form
{
    private ListView lvLog;
    private Label lblSummary;
    private Label lblSubStats;

    public FlightLogbookForm()
    {
        this.Text = "VTOL QoL — Журнал боевых вылетов";
        DarkWindowHelper.Apply(this);
        this.Size = new Size(1060, 620);
        this.MinimumSize = new Size(880, 460);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.Sizable;
        this.MaximizeBox = true;
        this.BackColor = Color.FromArgb(18, 22, 20);
        this.ForeColor = Color.FromArgb(0, 255, 102);
        this.Font = new Font("Consolas", 9.5f);

        Label lblTitle = new Label() { Text = "/// ТАКТИЧЕСКИЙ ЖУРНАЛ БОЕВЫХ ВЫЛЕТОВ ///", Left = 20, Top = 14, AutoSize = true, Font = new Font("Consolas", 11f, FontStyle.Bold) };
        this.Controls.Add(lblTitle);

        Panel pnlStats = new Panel() { Left = 20, Top = 42, Width = 1004, Height = 52, BackColor = Color.FromArgb(25, 32, 28), BorderStyle = BorderStyle.FixedSingle };
        pnlStats.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        this.Controls.Add(pnlStats);

        lblSummary = new Label() { Left = 12, Top = 6, Width = 975, Height = 20, ForeColor = Color.Cyan, Font = new Font("Consolas", 9.5f, FontStyle.Bold) };
        lblSummary.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        pnlStats.Controls.Add(lblSummary);

        lblSubStats = new Label() { Left = 12, Top = 27, Width = 975, Height = 20, ForeColor = Color.FromArgb(170, 230, 190) };
        lblSubStats.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        pnlStats.Controls.Add(lblSubStats);

        lvLog = new ListView();
        lvLog.Location = new Point(20, 104);
        lvLog.Size = new Size(1004, 420);
        lvLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        lvLog.View = View.Details;
        lvLog.FullRowSelect = true;
        lvLog.OwnerDraw = true;
        lvLog.BackColor = Color.FromArgb(20, 24, 22);
        lvLog.ForeColor = Color.White;
        lvLog.BorderStyle = BorderStyle.FixedSingle;

        lvLog.ColumnWidthChanging += (s, e) => {
            e.Cancel = true;
            e.NewWidth = lvLog.Columns[e.ColumnIndex].Width;
        };

        lvLog.DrawColumnHeader += (s, e) => {
            Rectangle fillRect = e.Bounds;
            if (e.ColumnIndex == lvLog.Columns.Count - 1)
                fillRect.Width += 200; // перекрываем пустой белый хвост заголовка справа
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(28, 36, 32)))
                e.Graphics.FillRectangle(bg, fillRect);
            using (Pen borderPen = new Pen(Color.FromArgb(45, 65, 55)))
                e.Graphics.DrawRectangle(borderPen, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, new Font("Consolas", 9.5f, FontStyle.Bold),
                new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height),
                Color.FromArgb(0, 255, 102), TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        };

        lvLog.DrawSubItem += (s, e) => {
            bool isSelected = e.Item.Selected;
            Color rowBg = isSelected
                ? Color.FromArgb(35, 65, 52)
                : (e.ItemIndex % 2 == 0 ? Color.FromArgb(20, 24, 22) : Color.FromArgb(24, 29, 26));
            using (SolidBrush bg = new SolidBrush(rowBg))
                e.Graphics.FillRectangle(bg, e.Bounds);
            using (Pen linePen = new Pen(Color.FromArgb(32, 40, 36)))
                e.Graphics.DrawLine(linePen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, lvLog.Font,
                new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height),
                e.Item.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };
        lvLog.Columns.Add("Дата и время", 145);
        lvLog.Columns.Add("Статус", 72);
        lvLog.Columns.Add("Борт", 195);
        lvLog.Columns.Add("Позывной", 145);
        lvLog.Columns.Add("Налет", 58);
        lvLog.Columns.Add("Сбито", 55);
        lvLog.Columns.Add("Детали события (цель, противник, оружие)", 310);
        this.Controls.Add(lvLog);

        Button btnRefresh = new Button() { Text = "Обновить", Left = 20, Top = 536, Width = 120, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(35, 45, 40), ForeColor = Color.FromArgb(0, 255, 102) };
        btnRefresh.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnRefresh.Click += (s, e) => LoadLog();
        this.Controls.Add(btnRefresh);

        Button btnExport = new Button() { Text = "Экспорт отчета", Left = 150, Top = 536, Width = 145, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 110, 140), ForeColor = Color.White };
        btnExport.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnExport.Click += BtnExport_Click;
        this.Controls.Add(btnExport);

        Button btnClear = new Button() { Text = "Очистить журнал", Left = 305, Top = 536, Width = 145, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(120, 30, 30), ForeColor = Color.White };
        btnClear.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnClear.Click += BtnClear_Click;
        this.Controls.Add(btnClear);

        DebriefRadarForm openDebrief = null;
        Button btnDebrief = new Button() { Text = "🎯 Разбор боя (Повторы)", Left = 460, Top = 536, Width = 205, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(70, 50, 115), ForeColor = Color.Cyan, Font = new Font("Consolas", 9.5f, FontStyle.Bold) };
        btnDebrief.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnDebrief.Click += (s, e) => {
            if (openDebrief != null && !openDebrief.IsDisposed) { openDebrief.Activate(); openDebrief.BringToFront(); return; }
            openDebrief = new DebriefRadarForm();
            openDebrief.FormClosed += (s2, e2) => { openDebrief = null; };
            openDebrief.Show(this);
        };
        this.Controls.Add(btnDebrief);

        Button btnClose = new Button() { Text = "Закрыть", Left = 894, Top = 536, Width = 130, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(40, 50, 45), ForeColor = Color.White };
        btnClose.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        btnClose.Click += (s, e) => this.Close();
        this.Controls.Add(btnClose);

        Action fitLastColumn = () => {
            if (lvLog.Columns.Count >= 7)
            {
                int fixedW = 0;
                for (int i = 0; i < 6; i++) fixedW += lvLog.Columns[i].Width;
                int rem = lvLog.ClientSize.Width - fixedW;
                if (rem > 220) lvLog.Columns[6].Width = rem;
            }
        };

        this.Shown += (s, e) => fitLastColumn();
        this.SizeChanged += (s, e) => fitLastColumn();

        LoadLog();
    }


    private string GetLogFilePath()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL", "flight_logbook.txt");
    }

    private void LoadLog()
    {
        lvLog.Items.Clear();
        string path = GetLogFilePath();
        int totalKills = 0, totalSorties = 0, totalLosses = 0;
        Dictionary<string, int> planeUsage = new Dictionary<string, int>();

        if (File.Exists(path))
        {
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string line = lines[i];
                if (string.IsNullOrEmpty(line.Trim())) continue;
                string[] p = line.Split('|');
                string time = p.Length > 0 ? p[0] : "";
                string ev = p.Length > 1 ? p[1] : "";
                string plane = p.Length > 2 ? p[2] : "";
                string cs = p.Length > 3 ? p[3] : "";
                string dur = p.Length > 4 ? p[4] : "—";
                string killsStr = p.Length > 5 ? p[5] : "0";
                string note = p.Length > 6 ? p[6] : "";

                ListViewItem item = new ListViewItem(time);
                item.SubItems.Add(ev); item.SubItems.Add(plane); item.SubItems.Add(cs);
                item.SubItems.Add(dur); item.SubItems.Add(killsStr); item.SubItems.Add(note);

                if (ev == "KILL") { item.ForeColor = Color.Cyan; totalKills++; }
                else if (ev == "EJECT" || ev == "KIA") { item.ForeColor = Color.OrangeRed; totalLosses++; }
                else if (ev == "SORTIE")
                {
                    item.ForeColor = Color.LightGreen;
                    totalSorties++;
                    if (!string.IsNullOrEmpty(plane) && plane.IndexOf("ангар", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        if (!planeUsage.ContainsKey(plane)) planeUsage[plane] = 0;
                        planeUsage[plane]++;
                    }
                }
                else item.ForeColor = Color.Silver;
                lvLog.Items.Add(item);
            }
        }

        string favPlane = "Нет данных"; int maxCount = 0;
        foreach (KeyValuePair<string, int> kv in planeUsage) if (kv.Value > maxCount) { maxCount = kv.Value; favPlane = kv.Key; }
        double kd = totalLosses > 0 ? Math.Round((double)totalKills / totalLosses, 2) : totalKills;
        lblSummary.Text = string.Format("ВЫЛЕТОВ: {0}  |  СБИТО: {1}  |  ПОТЕРЬ: {2}  |  K/D: {3}", totalSorties, totalKills, totalLosses, kd);

        string weapSummary = LoadWeaponStatsSummary();
        lblSubStats.Text = string.Format("Борт: {0} ({1})  |  {2}", favPlane, maxCount, weapSummary);
    }

    private string LoadWeaponStatsSummary()
    {
        try
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL", "weapon_stats.ini");
            if (!File.Exists(path)) return "Статистика оружия: ожидание пусков";
            List<string> parts = new List<string>();
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                string[] p = line.Split('|');
                if (p.Length >= 3)
                {
                    int fired, hits;
                    int.TryParse(p[1], out fired);
                    int.TryParse(p[2], out hits);
                    if (fired > 0)
                    {
                        int acc = Math.Min(100, (int)Math.Round((hits * 100.0) / fired));
                        parts.Add(string.Format("{0}: {1}/{2} ({3}%)", p[0], hits, fired, acc));
                    }
                    else if (hits > 0)
                    {
                        parts.Add(string.Format("{0}: {1} сбито", p[0], hits));
                    }
                }
            }
            return parts.Count > 0 ? ("Оружие: " + string.Join(" | ", parts.ToArray())) : "Статистика оружия: пусто";
        }
        catch { return ""; }
    }

    private void BtnClear_Click(object sender, EventArgs e)
    {
        if (MessageBox.Show("Очистить историю вылетов и статистику оружия?", "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            try
            {
                string path = GetLogFilePath();
                if (File.Exists(path)) File.Delete(path);

                string weapPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL", "weapon_stats.ini");
                if (File.Exists(weapPath)) File.Delete(weapPath);

                LoadLog();
            }
            catch (Exception ex) { MessageBox.Show("Ошибка: " + ex.Message); }
        }
    }


    private void BtnExport_Click(object sender, EventArgs e)
    {
        using (SaveFileDialog sfd = new SaveFileDialog() { Filter = "Текстовый отчет (*.txt)|*.txt", FileName = "VTOL_Flight_Report.txt" })
        {
            if (sfd.ShowDialog() == DialogResult.OK)
            {
                string path = GetLogFilePath();
                if (File.Exists(path)) File.Copy(path, sfd.FileName, true);
                MessageBox.Show("Отчет сохранен: " + sfd.FileName, "Экспорт завершен", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}

