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
// === ОКНО БРАУЗЕРА ПУБЛИЧНЫХ СЕРВЕРОВ VTOL VR (LOBBIES + VPN DIAGNOSTICS) =
// =========================================================================
public class ServerBrowserForm : Form
{
    private string gameRoot;
    private ListView lvServers;
    private Label lblNetBanner;
    private Label lblSummary;
    private TextBox txtFilter;
    private CheckBox chkHideFull;
    private Button btnRefresh;
    private List<VtolLobbyInfo> allLobbies = new List<VtolLobbyInfo>();
    private List<VtolLobbyInfo> shownLobbies = new List<VtolLobbyInfo>();

    public ServerBrowserForm(string root)
    {
        this.gameRoot = root;
        this.Text = "VTOL QoL — Публичные серверы (Steam Matchmaking)";
        DarkWindowHelper.Apply(this);
        this.Size = new Size(1020, 600);
        this.MinimumSize = new Size(860, 440);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.Sizable;
        this.MaximizeBox = true;
        this.BackColor = Color.FromArgb(18, 22, 20);
        this.ForeColor = Color.FromArgb(0, 255, 102);
        this.Font = new Font("Consolas", 9.5f);

        Label lblTitle = new Label() { Text = "/// МОНИТОРИНГ ПУБЛИЧНЫХ СЕРВЕРОВ VTOL VR ///", Left = 20, Top = 12, AutoSize = true, Font = new Font("Consolas", 11f, FontStyle.Bold) };
        this.Controls.Add(lblTitle);

        Panel pnlTop = new Panel() { Left = 20, Top = 38, Width = 964, Height = 54, BackColor = Color.FromArgb(24, 30, 27), BorderStyle = BorderStyle.FixedSingle };
        pnlTop.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        this.Controls.Add(pnlTop);

        lblNetBanner = new Label() { Left = 10, Top = 6, Width = 940, Height = 18, ForeColor = Color.Cyan, Font = new Font("Consolas", 9f, FontStyle.Bold), Text = "Проверка сетевого маршрута и Steam Relay..." };
        lblNetBanner.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        pnlTop.Controls.Add(lblNetBanner);

        Label lblSearch = new Label() { Text = "Поиск:", Left = 10, Top = 30, AutoSize = true, ForeColor = Color.LightGray };
        pnlTop.Controls.Add(lblSearch);

        txtFilter = new TextBox() { Left = 64, Top = 27, Width = 230, Height = 22, BackColor = Color.FromArgb(30, 38, 34), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        txtFilter.TextChanged += (s, e) => ApplyFilter();
        pnlTop.Controls.Add(txtFilter);

        chkHideFull = new CheckBox() { Text = "Скрыть полные", Left = 310, Top = 28, AutoSize = true, ForeColor = Color.FromArgb(200, 235, 215), Checked = false };
        chkHideFull.CheckedChanged += (s, e) => ApplyFilter();
        pnlTop.Controls.Add(chkHideFull);

        lblSummary = new Label() { Left = 460, Top = 30, Width = 490, Height = 18, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.FromArgb(160, 225, 185), Text = "Загрузка..." };
        lblSummary.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        pnlTop.Controls.Add(lblSummary);

        lvServers = new ListView();
        lvServers.Location = new Point(20, 100);
        lvServers.Size = new Size(964, 405);
        lvServers.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        lvServers.View = View.Details;
        lvServers.FullRowSelect = true;
        lvServers.OwnerDraw = true;
        lvServers.BackColor = Color.FromArgb(20, 24, 22);
        lvServers.ForeColor = Color.White;
        lvServers.BorderStyle = BorderStyle.FixedSingle;

        lvServers.ColumnWidthChanging += (s, e) => { e.Cancel = true; e.NewWidth = lvServers.Columns[e.ColumnIndex].Width; };

        lvServers.DrawColumnHeader += (s, e) => {
            Rectangle fillRect = e.Bounds;
            if (e.ColumnIndex == lvServers.Columns.Count - 1) fillRect.Width += 200;
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(28, 36, 32))) e.Graphics.FillRectangle(bg, fillRect);
            using (Pen borderPen = new Pen(Color.FromArgb(45, 65, 55))) e.Graphics.DrawRectangle(borderPen, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, new Font("Consolas", 9.5f, FontStyle.Bold),
                new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height),
                Color.FromArgb(0, 255, 102), TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        };

        lvServers.DrawSubItem += (s, e) => {
            Color rowBg = e.Item.Selected ? Color.FromArgb(35, 65, 52) : (e.ItemIndex % 2 == 0 ? Color.FromArgb(20, 24, 22) : Color.FromArgb(24, 29, 26));
            using (SolidBrush bg = new SolidBrush(rowBg)) e.Graphics.FillRectangle(bg, e.Bounds);
            using (Pen linePen = new Pen(Color.FromArgb(32, 40, 36))) e.Graphics.DrawLine(linePen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, lvServers.Font,
                new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height),
                e.Item.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };

        lvServers.Columns.Add("Название сервера", 285);
        lvServers.Columns.Add("Миссия / Сценарий", 245);
        lvServers.Columns.Add("Статус", 130);
        lvServers.Columns.Add("Игроки", 85);
        lvServers.Columns.Add("Совместимость / Сеть", 205);
        this.Controls.Add(lvServers);

        btnRefresh = new Button() { Text = "🔄 Обновить список", Left = 20, Top = 516, Width = 175, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(35, 45, 40), ForeColor = Color.FromArgb(0, 255, 102) };
        btnRefresh.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnRefresh.Click += (s, e) => LoadServersAsync();
        this.Controls.Add(btnRefresh);

        Button btnRelayTest = new Button() { Text = "🔍 Тест P2P / VPN", Left = 205, Top = 516, Width = 175, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 65, 80), ForeColor = Color.Cyan };
        btnRelayTest.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        btnRelayTest.Click += (s, e) => RunRelayDiagnostics();
        this.Controls.Add(btnRelayTest);

        Button btnClose = new Button() { Text = "Закрыть", Left = 854, Top = 516, Width = 130, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(40, 50, 45), ForeColor = Color.White };
        btnClose.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        btnClose.Click += (s, e) => this.Close();
        this.Controls.Add(btnClose);

        Action fitLastCol = () => {
            if (lvServers.Columns.Count >= 5)
            {
                int fixedW = 0;
                for (int i = 0; i < 4; i++) fixedW += lvServers.Columns[i].Width;
                int rem = lvServers.ClientSize.Width - fixedW;
                if (rem > 160) lvServers.Columns[4].Width = rem;
            }
        };
        this.Shown += (s, e) => { fitLastCol(); LoadServersAsync(); };
        this.SizeChanged += (s, e) => fitLastCol();
    }
    private void RunRelayDiagnostics()
    {
        string vpn = SteamLobbyScanner.DetectActiveVpnOrTunnel();
        int ping;
        bool ok = SteamLobbyScanner.CheckSteamRelayReachable(out ping);
        string msg = string.Format(
            "Активный VPN / туннельный адаптер: {0}\n" +
            "Связь с кластером Steam Datagram Relay (Valve P2P): {1}\n\n" +
            "Совет при ошибках подключения:\n" +
            "• Если включён VPN или DPI-обход, лобби могут отображаться в списке, но при входе зависать на подключении из-за блокировки UDP-трафика.\n" +
            "• Попробуйте отключить VPN или добавить VTOLVR.exe и steam.exe в исключения (Split Tunneling).",
            string.IsNullOrEmpty(vpn) ? "Не обнаружен (прямой маршрут)" : vpn,
            ok ? ("Доступен (" + ping + " мс)") : "Не отвечает (блокировка или нет сети)"
        );
        MessageBox.Show(msg, "Диагностика мультиплеера VTOL VR", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private void LoadServersAsync()
    {
        btnRefresh.Enabled = false;
        lblSummary.Text = "Сканирование лобби Steam...";
        lvServers.Items.Clear();

        ThreadPool.QueueUserWorkItem(delegate {
            try
            {
                string diag;
                List<VtolLobbyInfo> list = SteamLobbyScanner.FetchPublicLobbies(gameRoot, out diag);
                this.Invoke(new Action(delegate {
                    lblNetBanner.Text = diag;
                    lblNetBanner.ForeColor = diag.StartsWith("⚠") ? Color.Gold : Color.Cyan;
                    allLobbies = list;
                    ApplyFilter();
                    btnRefresh.Enabled = true;
                }));
            }
            catch (Exception ex)
            {
                try
                {
                    this.Invoke(new Action(delegate {
                        lblNetBanner.Text = "Ошибка сканирования: " + ex.Message;
                        lblNetBanner.ForeColor = Color.OrangeRed;
                        lblSummary.Text = "0 серверов";
                        btnRefresh.Enabled = true;
                    }));
                }
                catch { }
            }
        });
    }

    private void ApplyFilter()
    {
        lvServers.Items.Clear();
        shownLobbies.Clear();
        string q = txtFilter.Text.Trim();
        int totalPlayers = 0;

        foreach (VtolLobbyInfo l in allLobbies)
        {
            totalPlayers += l.Members;
            bool isFull = (l.MaxMembers > 0 && l.Members >= l.MaxMembers);
            if (chkHideFull.Checked && isFull) continue;
            if (!string.IsNullOrEmpty(q) &&
                l.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 &&
                l.Mission.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;

            shownLobbies.Add(l);
            ListViewItem item = new ListViewItem(l.Name);
            item.SubItems.Add(l.Mission);
            item.SubItems.Add(l.State);
            item.SubItems.Add(l.MaxMembers > 0 ? (l.Members + " / " + l.MaxMembers) : l.Members.ToString());
            item.SubItems.Add(l.ConnectHint);

            if (l.State == "Дебрифинг") item.ForeColor = Color.OrangeRed;
            else if (isFull) item.ForeColor = Color.Orange;
            else if (l.IsReachableWarning) item.ForeColor = Color.Gold;
            else if (l.State == "В миссии") item.ForeColor = Color.Cyan;
            else item.ForeColor = Color.FromArgb(0, 255, 102);

            lvServers.Items.Add(item);
        }

        lblSummary.Text = string.Format("Серверов: {0} (показано {1}) | Пилотов в сети: {2}", allLobbies.Count, shownLobbies.Count, totalPlayers);
    }
}

