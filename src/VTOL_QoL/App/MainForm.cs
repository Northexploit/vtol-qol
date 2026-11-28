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


public class MainForm : Form
{
    [DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    private TextBox txtPath;
    private Button btnStopDownload;
    private Button btnManageMusic;
    private Panel pnlDropZone;
    private Label lblDropText;
    private bool optShuffle;
    private bool optLoudNorm;
    private bool optDiscord;
    private bool optDucking;
    private bool optAudioSwitch;
    private bool optDiagnostics;
    private bool optKillSound;
    // Диагностические режимы (--perf-test / --dump-vtr) не должны трогать Steam и его конфиги.
    private static bool diagnosticMode = false;
    private Button btnInstall;
    private Label lblStatus;
    private ProgressBar progressBar;
    private Button btnLiveryManager;
    private Button btnDebrief;
    private Button btnServers;
    private volatile Process currentWorkerProcess = null;
    private volatile bool isCancelRequested = false;
    private volatile bool isDownloadBusy = false;
    private GameTracker tracker = new GameTracker();
    private Label lblIndDiscord;
    private Label lblIndMusic;
    private Label lblIndAudio;
    private Label lblIndPilot;
    private Label lblIndSteam;
    private Label lblIndNet;
    private volatile int netState = 0; // 0 = проверка, 1 = всё ок, 2 = частично, 3 = нет сети
    private volatile bool netYtOk = false;
    private volatile bool netDcOk = false;
    private volatile bool netStmOk = false;
    private volatile string netVpnName = "";
    private ToolTip indTipRef = null;
    private System.Windows.Forms.Timer uiStatusTimer;
    private Font indFontNormal = new Font("Consolas", 8.5f, FontStyle.Bold);
    private Font indFontUnderline = new Font("Consolas", 8.5f, FontStyle.Bold | FontStyle.Underline);
    private volatile bool bgDiscordRunning = false;
    private volatile bool bgSteamRunning = false;
    private volatile int bgTrackCount = -1;
    private volatile bool bgProbeBusy = false;
    private SettingsDialogForm openSettingsForm = null;
    private ServerBrowserForm openServersForm = null;
    private MusicManagerForm openMusicForm = null;
    private LiveryManagerForm openLiveryForm = null;
    private DebriefRadarForm openDebriefForm = null;
    private AudioSettingsForm openAudioForm = null;
    private LogDiagnosticsForm openDiagForm = null;

    private bool BringExistingToFront(Form f)
    {
        if (f != null && !f.IsDisposed)
        {
            if (f.WindowState == FormWindowState.Minimized)
                f.WindowState = FormWindowState.Normal;
            f.Activate();
            f.BringToFront();
            return true;
        }
        return false;
    }

    public MainForm()
    {
        this.Text = "VTOL QoL";
        this.Size = new Size(568, 386);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.None;
        this.MaximizeBox = false;
        this.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(this.Handle, 0xA1, 0x2, 0); } };
        this.KeyPreview = true;
        try
        {
            string localIco = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
            if (File.Exists(localIco))
                this.Icon = new Icon(localIco);
            else
            {
                Stream icoStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("icon.ico");
                if (icoStream != null)
                    this.Icon = new Icon(icoStream);
                else
                    this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
        }
        catch { }
        this.BackColor = Color.FromArgb(18, 22, 20);
        this.ForeColor = Color.FromArgb(0, 255, 102);
        this.Font = new Font("Consolas", 9.5f, FontStyle.Regular);

        optShuffle = SettingsManager.GetBool("Shuffle", true);
        optLoudNorm = SettingsManager.GetBool("LoudNorm", true);
        optDiscord = SettingsManager.GetBool("Discord", true);
        optDucking = SettingsManager.GetBool("Ducking", true);
        optAudioSwitch = SettingsManager.GetBool("AudioSwitch", true);
        optDiagnostics = SettingsManager.GetBool("DiagnosticsEnabled", false);
        optKillSound = SettingsManager.GetBool("KillSoundEnabled", true);

        this.DoubleBuffered = true;
        this.Paint += (s, e) => {
            Graphics g = e.Graphics;
            Rectangle rc = this.ClientRectangle;

            using (System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                path.AddRectangle(rc);
                using (System.Drawing.Drawing2D.PathGradientBrush pgb = new System.Drawing.Drawing2D.PathGradientBrush(path))
                {
                    pgb.CenterColor = Color.FromArgb(24, 32, 28);
                    pgb.SurroundColors = new Color[] { Color.FromArgb(14, 18, 16) };
                    g.FillRectangle(pgb, rc);
                }
            }

            using (Pen gridPen = new Pen(Color.FromArgb(24, 34, 29), 1))
            {
                for (int x = 24; x < rc.Width; x += 28)
                    g.DrawLine(gridPen, x, 0, x, rc.Height);
                for (int y = 24; y < rc.Height; y += 28)
                    g.DrawLine(gridPen, 0, y, rc.Width, y);
            }

            using (Pen hudPen = new Pen(Color.FromArgb(45, 85, 65), 1))
            {
                g.DrawRectangle(hudPen, 10, 8, rc.Width - 21, rc.Height - 17);
            }
            using (Pen cornerPen = new Pen(Color.FromArgb(0, 190, 85), 2))
            {
                int l = 10, t = 8, r = rc.Width - 11, b = rc.Height - 9, len = 14;
                g.DrawLine(cornerPen, l, t, l + len, t); g.DrawLine(cornerPen, l, t, l, t + len);
                g.DrawLine(cornerPen, r, t, r - len, t); g.DrawLine(cornerPen, r, t, r, t + len);
                g.DrawLine(cornerPen, l, b, l + len, b); g.DrawLine(cornerPen, l, b, l, b - len);
                g.DrawLine(cornerPen, r, b, r - len, b); g.DrawLine(cornerPen, r, b, r, b - len);
            }
        };

        Label lblTitle = new Label();
        lblTitle.Text = "/// VTOL QoL ///";
        lblTitle.Font = new Font("Consolas", 12f, FontStyle.Bold);
        lblTitle.BackColor = Color.Transparent;
        lblTitle.Location = new Point(22, 14);
        lblTitle.AutoSize = true;
        lblTitle.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(this.Handle, 0xA1, 0x2, 0); } };
        this.Controls.Add(lblTitle);

        Button btnWinMin = new Button();
        btnWinMin.Text = "—";
        btnWinMin.Font = new Font("Consolas", 9.5f, FontStyle.Bold);
        btnWinMin.Location = new Point(482, 12);
        btnWinMin.Size = new Size(30, 24);
        btnWinMin.FlatStyle = FlatStyle.Flat;
        btnWinMin.FlatAppearance.BorderColor = Color.FromArgb(45, 85, 65);
        btnWinMin.BackColor = Color.FromArgb(24, 32, 28);
        btnWinMin.ForeColor = Color.FromArgb(0, 255, 102);
        btnWinMin.Cursor = Cursors.Hand;
        btnWinMin.Click += (s, e) => { this.WindowState = FormWindowState.Minimized; };
        this.Controls.Add(btnWinMin);

        Button btnWinClose = new Button();
        btnWinClose.Text = "✕";
        btnWinClose.Font = new Font("Consolas", 9.5f, FontStyle.Bold);
        btnWinClose.Location = new Point(515, 12);
        btnWinClose.Size = new Size(30, 24);
        btnWinClose.FlatStyle = FlatStyle.Flat;
        btnWinClose.FlatAppearance.BorderColor = Color.FromArgb(45, 85, 65);
        btnWinClose.BackColor = Color.FromArgb(24, 32, 28);
        btnWinClose.ForeColor = Color.FromArgb(0, 255, 102);
        btnWinClose.Cursor = Cursors.Hand;
        btnWinClose.MouseEnter += (s, e) => { btnWinClose.BackColor = Color.FromArgb(165, 35, 35); btnWinClose.ForeColor = Color.White; };
        btnWinClose.MouseLeave += (s, e) => { btnWinClose.BackColor = Color.FromArgb(24, 32, 28); btnWinClose.ForeColor = Color.FromArgb(0, 255, 102); };
        btnWinClose.Click += (s, e) => { this.Close(); };
        this.Controls.Add(btnWinClose);

        txtPath = new TextBox();
        txtPath.Visible = false;
        txtPath.Text = SettingsManager.Get("RadioMusicDir", FindVtolMusicDir());
        this.Controls.Add(txtPath);


        pnlDropZone = new Panel();
        pnlDropZone.Location = new Point(24, 44);
        pnlDropZone.Size = new Size(520, 86);
        pnlDropZone.BackColor = Color.FromArgb(26, 34, 30);
        pnlDropZone.BorderStyle = BorderStyle.FixedSingle;
        pnlDropZone.AllowDrop = true;
        pnlDropZone.Cursor = Cursors.Hand;
        pnlDropZone.DragEnter += new DragEventHandler(PnlDropZone_DragEnter);
        pnlDropZone.DragLeave += new EventHandler(PnlDropZone_DragLeave);
        pnlDropZone.DragDrop += new DragEventHandler(PnlDropZone_DragDrop);
        pnlDropZone.Click += (s, e) => TryPasteLinkFromClipboard(true);
        this.Controls.Add(pnlDropZone);

        lblDropText = new Label();
        lblDropText.Text = "[ ПЕРЕТАЩИТЕ ТРЕКИ, ПАПКИ ИЛИ ССЫЛКУ СЮДА ]\nНажмите Ctrl+V или кликните для вставки ссылки\n(MP3, FLAC, WAV, MP4, YouTube, SoundCloud, Радио)";
        lblDropText.TextAlign = ContentAlignment.MiddleCenter;
        lblDropText.Dock = DockStyle.Fill;
        lblDropText.ForeColor = Color.FromArgb(160, 230, 185);
        lblDropText.AllowDrop = true;
        lblDropText.Cursor = Cursors.Hand;
        lblDropText.DragEnter += new DragEventHandler(PnlDropZone_DragEnter);
        lblDropText.DragDrop += new DragEventHandler(PnlDropZone_DragDrop);
        lblDropText.Click += (s, e) => TryPasteLinkFromClipboard(true);
        pnlDropZone.Controls.Add(lblDropText);

        this.KeyDown += (s, e) => {
            if (e.Control && e.KeyCode == Keys.V)
            {
                e.SuppressKeyPress = true;
                TryPasteLinkFromClipboard(true);
            }
        };

        progressBar = new ProgressBar();
        progressBar.Location = new Point(24, 138);
        progressBar.Size = new Size(430, 16);
        progressBar.Visible = false;
        this.Controls.Add(progressBar);

        btnStopDownload = new Button();
        btnStopDownload.Text = "⏹ СТОП";
        btnStopDownload.Location = new Point(460, 135);
        btnStopDownload.Size = new Size(84, 22);
        btnStopDownload.FlatStyle = FlatStyle.Flat;
        btnStopDownload.BackColor = Color.FromArgb(140, 35, 35);
        btnStopDownload.ForeColor = Color.White;
        btnStopDownload.Visible = false;
        btnStopDownload.Click += new EventHandler(BtnStopDownload_Click);
        this.Controls.Add(btnStopDownload);

        lblStatus = new Label();
        lblStatus.Text = "Готов к работе.";
        lblStatus.Location = new Point(22, 160);
        lblStatus.Size = new Size(520, 20);
        lblStatus.BackColor = Color.Transparent;
        lblStatus.ForeColor = Color.Silver;
        this.Controls.Add(lblStatus);

        btnInstall = new Button();
        btnInstall.Text = "▶ СТАРТ VTOL VR";
        btnInstall.Font = new Font("Consolas", 10f, FontStyle.Bold);
        btnInstall.Location = new Point(24, 188);
        btnInstall.Size = new Size(255, 42);
        btnInstall.FlatStyle = FlatStyle.Flat;
        btnInstall.BackColor = Color.FromArgb(0, 140, 60);
        btnInstall.ForeColor = Color.White;
        btnInstall.Click += new EventHandler(BtnInstall_Click);
        this.Controls.Add(btnInstall);

        btnLiveryManager = new Button();
        btnLiveryManager.Text = "✈ МЕНЕДЖЕР ЛИВРЕЙ";
        btnLiveryManager.Font = new Font("Consolas", 9.5f, FontStyle.Bold);
        btnLiveryManager.Location = new Point(290, 188);
        btnLiveryManager.Size = new Size(254, 42);
        btnLiveryManager.FlatStyle = FlatStyle.Flat;
        btnLiveryManager.BackColor = Color.FromArgb(70, 50, 110);
        btnLiveryManager.ForeColor = Color.White;
        btnLiveryManager.Click += new EventHandler(BtnLiveryManager_Click);
        this.Controls.Add(btnLiveryManager);

        btnManageMusic = new Button();
        btnManageMusic.Text = "♫ ТРЕКИ";
        btnManageMusic.Location = new Point(24, 238);
        btnManageMusic.Size = new Size(124, 38);
        btnManageMusic.FlatStyle = FlatStyle.Flat;
        btnManageMusic.BackColor = Color.FromArgb(0, 95, 125);
        btnManageMusic.ForeColor = Color.White;
        btnManageMusic.Click += new EventHandler(BtnManageMusic_Click);
        this.Controls.Add(btnManageMusic);

        btnServers = new Button();
        btnServers.Text = "🌐 СЕРВЕРЫ";
        btnServers.Location = new Point(156, 238);
        btnServers.Size = new Size(124, 38);
        btnServers.FlatStyle = FlatStyle.Flat;
        btnServers.BackColor = Color.FromArgb(30, 85, 115);
        btnServers.ForeColor = Color.White;
        btnServers.Click += new EventHandler(BtnServers_Click);
        this.Controls.Add(btnServers);

        btnDebrief = new Button();
        btnDebrief.Text = "🎯 РАЗБОР БОЯ";
        btnDebrief.Font = new Font("Consolas", 8.5f, FontStyle.Bold);
        btnDebrief.Location = new Point(288, 238);
        btnDebrief.Size = new Size(124, 38);
        btnDebrief.FlatStyle = FlatStyle.Flat;
        btnDebrief.BackColor = Color.FromArgb(70, 50, 115);
        btnDebrief.ForeColor = Color.Cyan;
        btnDebrief.Click += new EventHandler(BtnDebrief_Click);
        this.Controls.Add(btnDebrief);

        Button btnSettings = new Button();
        btnSettings.Text = "⚙ ОПЦИИ";
        btnSettings.Location = new Point(420, 238);
        btnSettings.Size = new Size(124, 38);
        btnSettings.FlatStyle = FlatStyle.Flat;
        btnSettings.BackColor = Color.FromArgb(52, 62, 58);
        btnSettings.ForeColor = Color.FromArgb(0, 255, 102);
        btnSettings.Click += (s, e) => OpenSettingsDialog();
        this.Controls.Add(btnSettings);

        Panel pnlIndicators = new Panel();
        pnlIndicators.Location = new Point(24, 286);
        pnlIndicators.Size = new Size(520, 62);
        pnlIndicators.BackColor = Color.FromArgb(22, 28, 25);
        pnlIndicators.BorderStyle = BorderStyle.FixedSingle;
        this.Controls.Add(pnlIndicators);

        ToolTip indTip = new ToolTip();
        indTipRef = indTip;
        System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += (s, e) => { netState = 0; TriggerNetworkCheck(true); };
        System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += (s, e) => { netState = 0; TriggerNetworkCheck(true); };

        lblIndDiscord = new Label() { Left = 8, Top = 5, Width = 245, Height = 16, Font = new Font("Consolas", 8.5f, FontStyle.Bold), ForeColor = Color.Silver, Text = "● Discord: проверка..." };
        lblIndDiscord.Click += (s, e) => {
            if (tracker.IsDiscordAccessDenied)
            {
                if (MessageBox.Show("Discord запущен от имени администратора.\nПерезапустить VTOL QoL с правами администратора для подключения?",
                    "Требуются права администратора", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath);
                        psi.Verb = "runas";
                        psi.UseShellExecute = true;
                        Process.Start(psi);
                        Application.Exit();
                    }
                    catch { }
                }
            }
            else if (Process.GetProcessesByName("Discord").Length == 0)
            {
                try
                {
                    string dcUpd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Discord\Update.exe");
                    if (File.Exists(dcUpd))
                    {
                        lblIndDiscord.Text = "● Discord: запуск...";
                        lblIndDiscord.ForeColor = Color.Gold;
                        Process.Start(dcUpd, "--processStart Discord.exe");
                    }
                }
                catch { }
            }
        };

        lblIndMusic = new Label() { Left = 260, Top = 5, Width = 250, Height = 16, Font = indFontNormal, ForeColor = Color.Silver, Text = "● Треки: проверка... ⚙", Cursor = Cursors.Hand };
        lblIndMusic.Click += new EventHandler(BtnBrowse_Click);
        lblIndMusic.MouseEnter += (s, e) => { lblIndMusic.Font = indFontUnderline; };
        lblIndMusic.MouseLeave += (s, e) => { lblIndMusic.Font = indFontNormal; };
        indTip.SetToolTip(lblIndMusic, "Нажмите, чтобы изменить путь к папке RadioMusic");

        lblIndAudio = new Label() { Left = 8, Top = 23, Width = 245, Height = 16, Font = indFontNormal, ForeColor = Color.Silver, Text = "● VR Аудио: проверка... ⚙", Cursor = Cursors.Hand };
        lblIndAudio.Click += new EventHandler(BtnAudioSettings_Click);
        lblIndAudio.MouseEnter += (s, e) => { lblIndAudio.Font = indFontUnderline; };
        lblIndAudio.MouseLeave += (s, e) => { lblIndAudio.Font = indFontNormal; };
        indTip.SetToolTip(lblIndAudio, "Нажмите, чтобы выбрать наушники и микрофон VR-шлема");

        lblIndPilot = new Label() { Left = 260, Top = 23, Width = 250, Height = 16, Font = indFontNormal, ForeColor = Color.Cyan, Text = "● Пилот: —" };

        lblIndSteam = new Label() { Left = 8, Top = 41, Width = 245, Height = 16, Font = indFontNormal, ForeColor = Color.Silver, Text = "● Steam: проверка... ⚙", Cursor = Cursors.Hand };

        lblIndSteam.Click += (s, e) => {
            RefreshStatusIndicators();
            if (IsSteamRunning()) return;   // Steam уже работает — просто обновили индикатор

            // Запускать Steam из админского процесса нельзя: Steam унаследует повышенные права
            // и потеряет доступ к своему разделу реестра.
            if (IsRunningAsAdmin())
            {
                MessageBox.Show(this,
                    "VTOL QoL запущен от имени администратора.\n\nЗапустите Steam вручную обычным ярлыком (меню «Пуск»). Если запустить его из программы с правами администратора, Steam теряет доступ к своим настройкам и реестру.",
                    "Запустите Steam вручную", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Обычный режим: запускаем Steam так же, как это делает ярлык — через оболочку Windows.
            // Но только если наш процесс вообще имеет право писать в раздел Steam:
            // иначе клиент унаследует ограничение и покажет фатальную ошибку реестра.
            if (!CanWriteSteamRegistry() && !PrepareSafeSteamLaunch()) return;

            try
            {
                lblIndSteam.Text = "● Steam: запуск клиента...";
                lblIndSteam.ForeColor = Color.Gold;
                Process.Start(new ProcessStartInfo("steam://open/main") { UseShellExecute = true });
            }
            catch
            {
                lblIndSteam.Text = "● Steam: запустите вручную ⚙";
                lblIndSteam.ForeColor = Color.Gold;
            }
        };
        lblIndSteam.MouseEnter += (s, e) => { lblIndSteam.Font = indFontUnderline; };
        lblIndSteam.MouseLeave += (s, e) => { lblIndSteam.Font = indFontNormal; };
        indTip.SetToolTip(lblIndSteam, "Нажмите, чтобы проверить интеграцию со Steam");

        lblIndNet = new Label() { Left = 260, Top = 41, Width = 250, Height = 16, Font = indFontNormal, ForeColor = Color.Silver, Text = "● Сеть: проверка... ↻", Cursor = Cursors.Hand };
        lblIndNet.Click += (s, e) => {
            netState = 0;
            RefreshStatusIndicators();
            TriggerNetworkCheck(true);
        };
        lblIndNet.MouseEnter += (s, e) => { lblIndNet.Font = indFontUnderline; };
        lblIndNet.MouseLeave += (s, e) => { lblIndNet.Font = indFontNormal; };
        indTip.SetToolTip(lblIndNet, "Проверка доступа к YouTube, Discord и Steam (нажмите для повтора)");

        pnlIndicators.Controls.Add(lblIndDiscord);
        pnlIndicators.Controls.Add(lblIndMusic);
        pnlIndicators.Controls.Add(lblIndAudio);
        pnlIndicators.Controls.Add(lblIndPilot);
        pnlIndicators.Controls.Add(lblIndSteam);
        pnlIndicators.Controls.Add(lblIndNet);

        Label lblAppLog = new Label();
        lblAppLog.Text = "🐞 лог проги";
        lblAppLog.Font = new Font("Consolas", 8f, FontStyle.Regular);
        lblAppLog.ForeColor = Color.FromArgb(75, 130, 100);
        lblAppLog.BackColor = Color.Transparent;
        lblAppLog.Location = new Point(22, 355);
        lblAppLog.AutoSize = true;
        lblAppLog.Cursor = Cursors.Hand;
        lblAppLog.MouseEnter += (s, e) => { lblAppLog.ForeColor = Color.FromArgb(0, 255, 102); };
        lblAppLog.MouseLeave += (s, e) => { lblAppLog.ForeColor = Color.FromArgb(75, 130, 100); };
        lblAppLog.Click += (s, e) => { try { Process.Start("notepad.exe", AppLogger.GetLogPath()); } catch { } };
        this.Controls.Add(lblAppLog);

        Label lblGameLog = new Label();
        lblGameLog.Text = "🔧 лог игры";
        lblGameLog.Font = new Font("Consolas", 8f, FontStyle.Regular);
        lblGameLog.ForeColor = Color.FromArgb(75, 130, 100);
        lblGameLog.BackColor = Color.Transparent;
        lblGameLog.Location = new Point(122, 355);
        lblGameLog.AutoSize = true;
        lblGameLog.Cursor = Cursors.Hand;
        lblGameLog.MouseEnter += (s, e) => { lblGameLog.ForeColor = Color.FromArgb(0, 255, 102); };
        lblGameLog.MouseLeave += (s, e) => { lblGameLog.ForeColor = Color.FromArgb(75, 130, 100); };
        lblGameLog.Click += new EventHandler(BtnDiagnostics_Click);
        this.Controls.Add(lblGameLog);

        Label lblAuthor = new Label();
        lblAuthor.Text = "by L.Shutze";
        lblAuthor.Font = new Font("Consolas", 8f, FontStyle.Italic);
        lblAuthor.ForeColor = Color.FromArgb(85, 140, 110);
        lblAuthor.BackColor = Color.Transparent;
        lblAuthor.Location = new Point(468, 355);
        lblAuthor.AutoSize = true;
        this.Controls.Add(lblAuthor);
        uiStatusTimer = new System.Windows.Forms.Timer();
        uiStatusTimer.Interval = 1500;
        uiStatusTimer.Tick += (s, e) => RefreshStatusIndicators();
        uiStatusTimer.Start();
        this.FormClosing += new FormClosingEventHandler(MainForm_FormClosing);
        this.Shown += new EventHandler(MainForm_Shown);
    }

    private void OpenSettingsDialog()
    {
        if (BringExistingToFront(openSettingsForm)) return;
        openSettingsForm = new SettingsDialogForm();
        openSettingsForm.FormClosed += (s, e) => {
            SettingsDialogForm dlg = openSettingsForm;
            openSettingsForm = null;
            if (dlg == null) return;
            optShuffle = dlg.ChkShuffle.Checked;
            optLoudNorm = dlg.ChkLoudNorm.Checked;
            optDiscord = dlg.ChkDiscord.Checked;
            optDucking = dlg.ChkDucking.Checked;
            optAudioSwitch = dlg.ChkAudioSwitch.Checked;
            optDiagnostics = dlg.ChkDiagnostics.Checked;
            optKillSound = dlg.ChkKillSound.Checked;

            tracker.DiscordEnabled = optDiscord;
            tracker.DuckingEnabled = optDucking;
            tracker.AudioSwitchEnabled = optAudioSwitch;
            tracker.DiagnosticsEnabled = optDiagnostics;
            SaveCheckboxSettings();
            RefreshStatusIndicators();
        };
        openSettingsForm.Show(this);
    }
    private void MainForm_Shown(object sender, EventArgs e)
    {
        if (DebriefRadarForm.PerfTestActive)
        {
            // Режим автотеста производительности 2D: сразу открываем разбор боя,
            // он сам загрузит повтор, снимет с паузы и прогонит цикл зума с логированием.
            BtnDebrief_Click(null, null);
        }
        ThreadPool.QueueUserWorkItem(delegate { SilentSync(true); });
        TriggerNetworkCheck();
        tracker.DiscordEnabled = optDiscord;
        tracker.DuckingEnabled = optDucking;
        tracker.AudioSwitchEnabled = optAudioSwitch;
        tracker.DiagnosticsEnabled = optDiagnostics;
        tracker.Start();
        RefreshStatusIndicators();
    }

    private DateTime lastNetCheckUtc = DateTime.MinValue;
    private int netCheckBusyFlag = 0;

    private void TriggerNetworkCheck(bool force = false)
    {
        if (force)
        {
            Interlocked.Exchange(ref netCheckBusyFlag, 1);
        }
        else if (Interlocked.CompareExchange(ref netCheckBusyFlag, 1, 0) != 0)
        {
            return;
        }
        lastNetCheckUtc = DateTime.UtcNow;

        ThreadPool.QueueUserWorkItem(delegate {
            try
            {
                netVpnName = SteamLobbyScanner.DetectActiveVpnOrTunnel();
                bool hasInet = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
                if (!hasInet)
                {
                    netYtOk = false; netDcOk = false; netStmOk = false;
                    netState = 3;
                }
                else
                {
                    // Проверяем YouTube, Discord и Steam параллельно с жёстким таймаутом 2 сек
                    Func<string, bool> probe = delegate(string url) { try { return CheckHostReachable(url); } catch { return false; } };
                    Task<bool>[] probes = new Task<bool>[]
                    {
                        Task.Run(delegate { return probe("https://youtube.com"); }),
                        Task.Run(delegate { return probe("https://discord.com"); }),
                        Task.Run(delegate { return probe("https://steamcommunity.com"); })
                    };
                    try { Task.WaitAll(probes, 2000); } catch { }

                    bool yt = probes[0].Status == TaskStatus.RanToCompletion && probes[0].Result;
                    bool dc = probes[1].Status == TaskStatus.RanToCompletion && probes[1].Result;
                    bool stm = probes[2].Status == TaskStatus.RanToCompletion && probes[2].Result;

                    netYtOk = yt;
                    netDcOk = dc;
                    netStmOk = stm;

                    if (netYtOk && netDcOk && netStmOk) netState = 1;
                    else if (!netYtOk && !netDcOk && !netStmOk) netState = 3;
                    else netState = 2;
                }

                try { this.BeginInvoke(new Action(RefreshStatusIndicators)); } catch { }
            }
            catch
            {
                netState = 3;
            }
            finally
            {
                Interlocked.Exchange(ref netCheckBusyFlag, 0);
            }
        });
    }

    private bool CheckHostReachable(string url)
    {
        try
        {
            WorkshopBrowser.EnableTls12();
            System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
            req.Method = "GET";
            req.KeepAlive = false;
            req.ServicePoint.ConnectionLeaseTimeout = 0;
            req.ServicePoint.MaxIdleTime = 0;
            req.Timeout = 1800;
            req.ReadWriteTimeout = 1800;
            req.AllowAutoRedirect = true;
            req.UserAgent = "Mozilla/5.0";
            using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)req.GetResponse())
                return ((int)resp.StatusCode >= 200 && (int)resp.StatusCode < 500);
        }
        catch (System.Net.WebException wex)
        {
            if (wex.Response != null)
            {
                try { wex.Response.Close(); } catch { }
                return true;
            }
            return false;
        }
        catch { return false; }
    }

    private void ProbeEnvironmentAsync()
    {
        if (bgProbeBusy) return;
        bgProbeBusy = true;
        string mDir = "";
        try { mDir = txtPath.Text.Trim('"'); } catch { }

        ThreadPool.QueueUserWorkItem(delegate {
            try
            {
                Process[] dc = Process.GetProcessesByName("Discord");
                bgDiscordRunning = dc.Length > 0;
                foreach (Process p in dc) p.Dispose();

                bgSteamRunning = IsSteamRunning();

                if (!string.IsNullOrEmpty(mDir) && Directory.Exists(mDir))
                {
                    int cnt = 0;
                    foreach (string f in Directory.EnumerateFiles(mDir, "*.mp3", SearchOption.TopDirectoryOnly))
                        cnt++;
                    bgTrackCount = cnt;
                }
                else
                {
                    bgTrackCount = -1;
                }
            }
            catch { }
            finally { bgProbeBusy = false; }
        });
    }

    private void RefreshStatusIndicators()
    {
        if (lblIndDiscord == null) return;

        if ((DateTime.UtcNow - lastNetCheckUtc).TotalSeconds >= 6)
            TriggerNetworkCheck();

        ProbeEnvironmentAsync();
        bool discordProcRunning = bgDiscordRunning;

        if (!optDiscord)
        {
            lblIndDiscord.Text = "● Discord: выключен";
            lblIndDiscord.ForeColor = Color.Gray;
            lblIndDiscord.Cursor = Cursors.Default;
        }
        else if (tracker.IsDiscordAccessDenied)
        {
            lblIndDiscord.Text = "● Discord: права админа ⚙";
            lblIndDiscord.ForeColor = Color.OrangeRed;
            lblIndDiscord.Cursor = Cursors.Hand;
        }
        else if (!discordProcRunning)
        {
            lblIndDiscord.Text = "● Discord: не запущен ⚙";
            lblIndDiscord.ForeColor = Color.OrangeRed;
            lblIndDiscord.Cursor = Cursors.Hand;
        }
        else if (tracker.IsDiscordConnected && tracker.IsDiscordLoggedIn)
        {
            string u = !string.IsNullOrEmpty(tracker.DiscordUsername) ? (" (" + tracker.DiscordUsername + ")") : "";
            lblIndDiscord.Text = tracker.IsGameActive ? ("● Discord: активен" + u) : ("● Discord: вход выполнен" + u);
            lblIndDiscord.ForeColor = Color.FromArgb(0, 255, 102);
            lblIndDiscord.Cursor = Cursors.Default;
        }
        else if (tracker.IsDiscordConnected && !tracker.IsDiscordLoggedIn)
        {
            lblIndDiscord.Text = "● Discord: не выполнен вход";
            lblIndDiscord.ForeColor = Color.Gold;
            lblIndDiscord.Cursor = Cursors.Default;
        }
        else
        {
            lblIndDiscord.Text = "● Discord: запущен (вход/ожидание)";
            lblIndDiscord.ForeColor = Color.FromArgb(140, 220, 175);
            lblIndDiscord.Cursor = Cursors.Default;
        }

        string mDir = txtPath.Text.Trim('"');
        if (!string.IsNullOrEmpty(mDir) && Directory.Exists(mDir))
        {
            int count = Math.Max(0, bgTrackCount);
            lblIndMusic.Text = "● RadioMusic: " + count + " тр. ⚙";
            lblIndMusic.ForeColor = count > 0 ? Color.FromArgb(0, 255, 102) : Color.Gold;
        }
        else
        {
            lblIndMusic.Text = "● RadioMusic: задать путь ⚙";
            lblIndMusic.ForeColor = Color.OrangeRed;
        }

        string vrPb = SettingsManager.Get("VrPlaybackId", "");
        string vrCp = SettingsManager.Get("VrCaptureId", "");
        if (!optAudioSwitch)
        {
            lblIndAudio.Text = "● VR Аудио: выключено ⚙";
            lblIndAudio.ForeColor = Color.Gray;
        }
        else if (!string.IsNullOrEmpty(vrPb) || !string.IsNullOrEmpty(vrCp))
        {
            lblIndAudio.Text = "● VR Аудио: настроено ⚙";
            lblIndAudio.ForeColor = Color.FromArgb(0, 255, 102);
        }
        else
        {
            lblIndAudio.Text = "● VR Аудио: не выбрано ⚙";
            lblIndAudio.ForeColor = Color.Gold;
        }

        string pilot = tracker.ActivePilotName;
        if (tracker.IsGameActive)
        {
            lblIndPilot.Text = "● " + (pilot != "—" ? pilot + ": " : "") + tracker.ActiveFlightSummary;
            lblIndPilot.ForeColor = Color.Cyan;
        }
        else
        {
            lblIndPilot.Text = "● Игра: не запущена";
            lblIndPilot.ForeColor = Color.Silver;
        }

        if (lblIndSteam != null)
        {
            bool steamRunning = bgSteamRunning;
            string appBat = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL", "Shuffle.bat");
            bool batReady = File.Exists(appBat);
            // Программа в ограниченном режиме: сама запустить Steam она не может —
            // клиент унаследует ограничения и покажет ошибку реестра. Подсказываем заранее.
            bool steamLaunchRestricted = !CanWriteSteamRegistry();

            if (steamLaunchRestricted && !steamRunning)
            {
                lblIndSteam.Text = "● Steam: запустите вручную (ограниченный режим) ⚙";
                lblIndSteam.ForeColor = Color.Gold;
            }
            else if (steamRunning && batReady)
            {
                lblIndSteam.Text = "● Steam: подключён ✔";
                lblIndSteam.ForeColor = Color.FromArgb(0, 255, 102);
            }
            else if (steamRunning && !batReady)
            {
                lblIndSteam.Text = "● Steam: настроить автозапуск ⚙";
                lblIndSteam.ForeColor = Color.Gold;
            }
            else
            {
                lblIndSteam.Text = "● Steam: не запущен ⚙";
                lblIndSteam.ForeColor = Color.OrangeRed;
            }
        }

        if (lblIndNet != null)
        {
            bool hasVpn = !string.IsNullOrEmpty(netVpnName);

            if (netState == 0)
            {
                lblIndNet.Text = "● Сеть: проверка... ↻";
                lblIndNet.ForeColor = Color.Silver;
            }
            else if (netState == 3)
            {
                lblIndNet.Text = "● Сеть: нет интернета ✖";
                lblIndNet.ForeColor = Color.OrangeRed;
            }
            else if (hasVpn)
            {
                // При включённом VPN всегда показываем состояние каждого сервиса (YT, DC, STM)
                lblIndNet.Text = string.Format("● VPN: YT{0} DC{1} STM{2}",
                    netYtOk ? "✔" : "✖", netDcOk ? "✔" : "✖", netStmOk ? "✔" : "✖");
                lblIndNet.ForeColor = (netState == 1) ? Color.Cyan : Color.Gold;
            }
            else if (netState == 1)
            {
                lblIndNet.Text = "● Сеть: Прямое соединение ✔";
                lblIndNet.ForeColor = Color.FromArgb(0, 255, 102);
            }
            else
            {
                lblIndNet.Text = string.Format("● Прямое: YT{0} DC{1} STM{2}",
                    netYtOk ? "✔" : "✖", netDcOk ? "✔" : "✖", netStmOk ? "✔" : "✖");
                lblIndNet.ForeColor = Color.Gold;
            }

            if (indTipRef != null)
            {
                string tipText = string.Format(
                    "Маршрут: {0}\nYouTube: {1} | Discord: {2} | Steam: {3}\n(Нажмите для принудительной проверки)",
                    hasVpn ? ("VPN / туннель (" + netVpnName + ")") : "Прямое подключение (без VPN)",
                    netYtOk ? "доступен" : "недоступен",
                    netDcOk ? "доступен" : "недоступен",
                    netStmOk ? "доступен" : "недоступен");
                if (indTipRef.GetToolTip(lblIndNet) != tipText)
                    indTipRef.SetToolTip(lblIndNet, tipText);
            }
        }
    }

    private void SaveCheckboxSettings()

    {
        SettingsManager.SetBool("Shuffle", optShuffle);
        SettingsManager.SetBool("LoudNorm", optLoudNorm);
        SettingsManager.SetBool("Discord", optDiscord);
        SettingsManager.SetBool("Ducking", optDucking);
        SettingsManager.SetBool("AudioSwitch", optAudioSwitch);
        SettingsManager.SetBool("DiagnosticsEnabled", optDiagnostics);
        SettingsManager.SetBool("KillSoundEnabled", optKillSound);
        SettingsManager.Set("RadioMusicDir", txtPath.Text);
        SettingsManager.Save();
    }


    private void BtnBrowse_Click(object sender, EventArgs e)
    {
        using (FolderBrowserDialog fbd = new FolderBrowserDialog())
        {
            fbd.Description = "Выберите папку RadioMusic в директории VTOL VR (текущая: " + txtPath.Text + ")";
            if (!string.IsNullOrEmpty(txtPath.Text) && Directory.Exists(txtPath.Text))
                fbd.SelectedPath = txtPath.Text;
            if (fbd.ShowDialog() == DialogResult.OK)
            {
                txtPath.Text = fbd.SelectedPath;
                SaveCheckboxSettings();
                RefreshStatusIndicators();
            }
        }
    }

    private void BtnInstall_Click(object sender, EventArgs e)
    {
        SaveCheckboxSettings();
        // Синхронизацию делаем в фоне, чтобы не подвешивать окно на время работы с файлами.
        ThreadPool.QueueUserWorkItem(delegate { SilentSync(false); });

        bool steamRunning = IsSteamRunning();
        bool elevated = IsRunningAsAdmin();
        bool steamWritable = CanWriteSteamRegistry();
        AppLogger.Log("STEAM", "Кнопка СТАРТ VTOL VR: Steam запущен=" + steamRunning
            + ", программа под админом=" + elevated
            + ", раздел Steam в реестре доступен для записи=" + steamWritable);

        // Сначала приводим Steam в известное состояние: клиент либо рабочий, либо его нет.
        // Бывает, что steam.exe запущен, но так и не записал свои данные в реестр —
        // такой клиент команд не обрабатывает, а пользователю показывает ошибку
        // «Путь к разделу Steam в реестре недоступен для записи».
        if (steamRunning && !CheckRunningSteamIsUsable())
        {
            UpdateStatus("Запустите Steam обычным ярлыком и нажмите кнопку снова.", 0);
            return;
        }
        steamRunning = IsSteamRunning();   // клиент мог быть закрыт при перезапуске

        // Опасно только ЗАПУСКАТЬ Steam из админского процесса: тогда Steam стартует
        // с повышенными правами и теряет доступ к своему реестру.
        // Если Steam уже работает — просьба «запусти игру» ему безвредна:
        // новый процесс не создаётся, права никуда не наследуются.
        if (!steamRunning && elevated)
        {
            UpdateStatus("Запустите Steam обычным ярлыком (не от имени администратора).", 0);
            MessageBox.Show(this,
                "Steam не запущен, а программа работает от имени администратора.\n\n"
                + "Сначала запустите Steam обычным ярлыком (меню «Пуск», не от имени администратора). "
                + "После этого кнопка будет запускать игру нормально.",
                "Сначала запустите Steam", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Второй случай того же вреда: у нашего процесса нет права записи в раздел
        // Steam в реестре. Причины две — урезанный токен (песочница, AppContainer,
        // низкий уровень целостности) или испорченные права самого раздела.
        // Steam, запущенный из такого процесса, наследует ограничение и показывает
        // «Путь к разделу Steam в реестре недоступен для записи»; игра не стартует.
        if (!steamRunning && !steamWritable && !PrepareSafeSteamLaunch())
        {
            UpdateStatus("Steam запустите вручную и нажмите кнопку снова.", 0);
            return;
        }

        // Steam бывает «полузапущенным»: steam.exe есть, а steamwebhelper нет.
        // В таком состоянии клиент не обрабатывает команды — игра просто не стартует,
        // и со стороны это выглядит как «кнопка ничего не делает».
        if (steamRunning && Process.GetProcessesByName("steamwebhelper").Length == 0)
        {
            AppLogger.Log("STEAM", "Steam в некорректном состоянии: steam.exe есть, steamwebhelper нет");
            if (elevated)
            {
                UpdateStatus("Steam работает некорректно. Перезапустите его вручную.", 0);
                MessageBox.Show(this,
                    "Steam запущен некорректно (нет процесса steamwebhelper), поэтому команда запуска игры до него не доходит.\n\n"
                    + "Перезапустите Steam вручную: закройте его и откройте обычным ярлыком.",
                    "Steam работает некорректно", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult fix = MessageBox.Show(this,
                "Steam запущен некорректно (нет процесса steamwebhelper), поэтому команда запуска игры до него не доходит.\n\n"
                + "Перезапустить Steam автоматически? Он будет закрыт и открыт заново обычным способом.",
                "Steam работает некорректно", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (fix != DialogResult.Yes)
            {
                UpdateStatus("Перезапустите Steam вручную и нажмите кнопку снова.", 0);
                return;
            }

            UpdateStatus("Перезапускаю Steam...", 40);
            try
            {
                foreach (Process sp in Process.GetProcessesByName("steam")) { try { sp.Kill(); } catch { } sp.Dispose(); }
                foreach (Process sp in Process.GetProcessesByName("steamwebhelper")) { try { sp.Kill(); } catch { } sp.Dispose(); }
                Thread.Sleep(3000);
                Process.Start(new ProcessStartInfo("steam://open/main") { UseShellExecute = true });

                for (int i = 0; i < 60; i++)
                {
                    Thread.Sleep(1000);
                    if (Process.GetProcessesByName("steamwebhelper").Length > 0) break;
                }
                AppLogger.Log("STEAM", "После перезапуска: steam=" + Process.GetProcessesByName("steam").Length
                    + " webhelper=" + Process.GetProcessesByName("steamwebhelper").Length);
            }
            catch (Exception exRestart) { AppLogger.Error("STEAM", "Перезапуск Steam", exRestart); }
        }

        UpdateStatus(steamRunning ? "Запускаю VTOL VR через Steam..." : "Запускаю Steam и VTOL VR...", 60);

        try
        {
            Process.Start(new ProcessStartInfo("steam://rungameid/667970") { UseShellExecute = true });
        }
        catch (Exception exStart)
        {
            AppLogger.Error("STEAM", "Запуск steam://rungameid/667970", exStart);
            UpdateStatus("Не удалось запустить игру: " + exStart.Message, 0);
            MessageBox.Show(this, "Не удалось запустить VTOL VR:\n" + exStart.Message,
                "Ошибка запуска", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Проверяем по факту: появился ли процесс игры. Иначе говорим прямо, что не сработало.
        // 90 секунд, а не 20: холодный старт Steam + SteamVR (vrmonitor, шлем) занимает
        // заметно больше, и раньше программа успевала объявить «игра не запустилась»,
        // хотя клиент в этот момент как раз поднимал VR.
        ThreadPool.QueueUserWorkItem(delegate
        {
            for (int i = 0; i < 90; i++)
            {
                Thread.Sleep(1000);
                if (Process.GetProcessesByName("VTOLVR").Length > 0)
                {
                    AppLogger.Log("STEAM", "VTOL VR запустился (через " + (i + 1) + " c)");
                    UpdateStatus("VTOL VR запускается...", 100);
                    return;
                }
                // Игра может стартовать под именем процесса, которое Steam задал сам:
                // проверяем ещё и окно SteamVR-сессии, чтобы не ругаться преждевременно.
                if (i == 30 && Process.GetProcessesByName("steam").Length == 0
                    && Process.GetProcessesByName("steamwebhelper").Length == 0)
                {
                    AppLogger.Log("STEAM", "Через 30 секунд после steam://rungameid клиент Steam так и не поднялся");
                    UpdateStatus("Steam не отвечает. Запустите его вручную и нажмите кнопку снова.", 0);
                    return;
                }
            }
            AppLogger.Log("STEAM", "VTOL VR не появился за 90 секунд после steam://rungameid");
            UpdateStatus("Игра не запустилась. Проверьте Steam.", 0);
        });
    }

    private void BtnServers_Click(object sender, EventArgs e)
    {
        if (BringExistingToFront(openServersForm)) return;
        string dir = txtPath.Text.Trim('"');
        string gameRoot = Directory.Exists(dir) ? Path.GetFullPath(Path.Combine(dir, "..")) : "";
        openServersForm = new ServerBrowserForm(gameRoot);
        openServersForm.FormClosed += (s, ev) => { openServersForm = null; };
        openServersForm.Show(this);
    }

    private void BtnManageMusic_Click(object sender, EventArgs e)
    {
        if (BringExistingToFront(openMusicForm)) return;
        string dir = txtPath.Text.Trim('"');
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            MessageBox.Show(this, "Сначала укажите корректную папку RadioMusic!", "Внимание", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        openMusicForm = new MusicManagerForm(dir);
        openMusicForm.FormClosed += (s, ev) => { openMusicForm = null; };
        openMusicForm.Show(this);
    }

    private void BtnLiveryManager_Click(object sender, EventArgs e)
    {
        if (BringExistingToFront(openLiveryForm)) return;
        string dir = txtPath.Text.Trim('"');
        string gameRoot = Directory.Exists(dir) ? Path.GetFullPath(Path.Combine(dir, "..")) : "";
        openLiveryForm = new LiveryManagerForm(gameRoot);
        openLiveryForm.FormClosed += (s, ev) => { openLiveryForm = null; };
        openLiveryForm.Show(this);
    }

    private void BtnDebrief_Click(object sender, EventArgs e)
    {
        if (BringExistingToFront(openDebriefForm)) return;
        openDebriefForm = new DebriefRadarForm();
        openDebriefForm.FormClosed += (s, ev) => { openDebriefForm = null; };
        openDebriefForm.Show(this);
    }

    private void BtnAudioSettings_Click(object sender, EventArgs e)
    {
        if (BringExistingToFront(openAudioForm)) return;
        openAudioForm = new AudioSettingsForm();
        openAudioForm.FormClosed += (s, ev) => { openAudioForm = null; };
        openAudioForm.Show(this);
    }

    private void BtnDiagnostics_Click(object sender, EventArgs e)
    {
        if (BringExistingToFront(openDiagForm)) return;
        openDiagForm = new LogDiagnosticsForm();
        openDiagForm.FormClosed += (s, ev) => {
            openDiagForm = null;
            optDiagnostics = SettingsManager.GetBool("DiagnosticsEnabled", false);
            tracker.DiagnosticsEnabled = optDiagnostics;
        };
        openDiagForm.Show(this);
    }

    private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
    {
        SaveCheckboxSettings();
        // Гасим дочерний процесс загрузки/записи (ffmpeg/yt-dlp), чтобы не оставлять его осиротевшим.
        try
        {
            isCancelRequested = true;
            if (currentWorkerProcess != null && !currentWorkerProcess.HasExited)
                currentWorkerProcess.Kill();
        }
        catch { }
        if (uiStatusTimer != null)
        {
            uiStatusTimer.Stop();
            uiStatusTimer.Dispose();
        }
        Environment.Exit(0);
    }

    private string FindVtolMusicDir()
    {
        string steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", "") as string;
        if (steamPath == null) steamPath = "";
        steamPath = steamPath.Replace('/', '\\');

        List<string> searchDirs = new List<string>();
        if (!string.IsNullOrEmpty(steamPath))
        {
            searchDirs.Add(Path.Combine(steamPath, @"steamapps\common\VTOL VR\RadioMusic"));
            string libVdf = Path.Combine(steamPath, @"steamapps\libraryfolders.vdf");
            if (File.Exists(libVdf))
            {
                string libText = File.ReadAllText(libVdf, Encoding.UTF8);
                foreach (Match m in Regex.Matches(libText, "\"path\"\\s+\"([^\"]+)\""))
                {
                    string libPath = m.Groups[1].Value.Replace("\\\\", "\\");
                    searchDirs.Add(Path.Combine(libPath, @"steamapps\common\VTOL VR\RadioMusic"));
                }
            }
        }

        foreach (char drive in "CDEFGH")
        {
            searchDirs.Add(drive + @":\SteamLibrary\steamapps\common\VTOL VR\RadioMusic");
            searchDirs.Add(drive + @":\Steam\steamapps\common\VTOL VR\RadioMusic");
            searchDirs.Add(drive + @":\Games\Steam\steamapps\common\VTOL VR\RadioMusic");
        }

        foreach (string dir in searchDirs) if (Directory.Exists(dir)) return dir;
        return "";
    }

    private static readonly object extractLock = new object();

    private void ExtractResource(string resName, string targetPath, bool forceOverwrite = false)
    {
        lock (extractLock)
        {
            if (!forceOverwrite && File.Exists(targetPath) && new FileInfo(targetPath).Length > 1024)
                return;

            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resName))
            {
                if (stream == null) return;
                string tmpPath = targetPath + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
                try
                {
                    using (FileStream fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        stream.CopyTo(fs);

                    if (File.Exists(targetPath))
                    {
                        try { File.Delete(targetPath); }
                        catch { File.Delete(tmpPath); return; }
                    }
                    File.Move(tmpPath, targetPath);
                }
                catch
                {
                    try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
                }
            }
        }
    }

    private void EnsureLatestTools(string toolsDir, out string ffmpegPath, out string ytdlpPath)
    {
        ffmpegPath = Path.Combine(toolsDir, "ffmpeg.exe");
        ytdlpPath = Path.Combine(toolsDir, "yt-dlp.exe");
        ExtractResource("ffmpeg.exe", ffmpegPath, false);

        string curVer = Assembly.GetExecutingAssembly().GetName().Version.ToString();
        string savedVer = SettingsManager.Get("BundledToolsVersion", "");
        bool versionChanged = !string.Equals(curVer, savedVer, StringComparison.OrdinalIgnoreCase);
        ExtractResource("yt-dlp.exe", ytdlpPath, versionChanged);
        if (versionChanged && File.Exists(ytdlpPath))
        {
            SettingsManager.Set("BundledToolsVersion", curVer);
            SettingsManager.Save();
        }

        string lastUpdateStr = SettingsManager.Get("LastYtDlpSelfUpdate", "");
        DateTime lastUpd;
        if (File.Exists(ytdlpPath) && (!DateTime.TryParse(lastUpdateStr, out lastUpd) || (DateTime.UtcNow - lastUpd).TotalDays >= 7))
        {
            try
            {
                Process pUpd = Process.Start(new ProcessStartInfo(ytdlpPath, "-U") { WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true });
                if (pUpd != null)
                {
                    if (!pUpd.WaitForExit(15000))
                    {
                        // Не оставляем осиротевший yt-dlp, перезаписывающий свой же exe.
                        try { pUpd.Kill(); pUpd.WaitForExit(3000); } catch { }
                    }
                    pUpd.Dispose();
                }
                SettingsManager.Set("LastYtDlpSelfUpdate", DateTime.UtcNow.ToString("o"));
                SettingsManager.Save();
            }
            catch { }
        }
    }

    private string SanitizeHttpUrl(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string trimmed = raw.Trim().Trim('"', '\'', '<', '>');
        if (trimmed.IndexOfAny(new char[] { '"', '\r', '\n', '\0', '|', '&', ';', '`' }) >= 0)
            return null;

        Uri uri;
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out uri)) return null;
        if (!uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) &&
            !uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            return null;

        return uri.AbsoluteUri;
    }

    private string GetUniqueOutputPath(string dir, string baseName, string ext, string sourcePath)
    {
        string cleanBase = Regex.Replace(baseName, @"^\d{4}_", "");
        if (string.IsNullOrWhiteSpace(cleanBase)) cleanBase = "Track";
        string candidate = Path.Combine(dir, cleanBase + ext);

        if (!string.IsNullOrEmpty(sourcePath) &&
            string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(candidate), StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(dir, "__reencode_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ext);
        }

        int counter = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(dir, string.Format("{0} ({1}){2}", cleanBase, counter, ext));
            counter++;
        }
        return candidate;
    }

    private void UpdateStatus(string text, int progress)
    {
        if (this.InvokeRequired) { try { this.Invoke(new Action<string, int>(UpdateStatus), text, progress); } catch { } return; }
        lblStatus.Text = text;
        if (progress >= 0 && progress <= 100) progressBar.Value = progress;
    }

    private void SetControlsEnabled(bool enabled)
    {
        if (this.InvokeRequired) { try { this.Invoke(new Action<bool>(SetControlsEnabled), enabled); } catch { } return; }
        btnInstall.Enabled = enabled;
        btnManageMusic.Enabled = enabled;
        btnLiveryManager.Enabled = enabled;
        btnDebrief.Enabled = enabled;
        btnServers.Enabled = enabled;
        pnlDropZone.AllowDrop = enabled;
        btnStopDownload.Visible = !enabled;
        btnStopDownload.Enabled = !enabled;
        progressBar.Visible = !enabled;
        if (enabled) { progressBar.Value = 0; currentWorkerProcess = null; }
    }

    private void BtnStopDownload_Click(object sender, EventArgs e)
    {
        isCancelRequested = true;
        try { if (currentWorkerProcess != null && !currentWorkerProcess.HasExited) currentWorkerProcess.Kill(); } catch { }
        UpdateStatus("Остановка процесса...", 0);
    }

    private void TryPasteLinkFromClipboard(bool notifyIfEmpty)
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                string safeUrl = SanitizeHttpUrl(Clipboard.GetText());
                if (!string.IsNullOrEmpty(safeUrl))
                {
                    StartUrlDownload(safeUrl);
                    return;
                }
            }
            if (notifyIfEmpty)
                UpdateStatus("Скопируйте корректную ссылку (http/https) или перетащите файлы.", 0);
        }
        catch { }
    }

    private void PnlDropZone_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.UnicodeText) || e.Data.GetDataPresent(DataFormats.Text))
        {
            e.Effect = DragDropEffects.Copy;
            pnlDropZone.BackColor = Color.FromArgb(0, 80, 45);
        }
    }

    private void PnlDropZone_DragLeave(object sender, EventArgs e) { pnlDropZone.BackColor = Color.FromArgb(26, 34, 30); }

    private void PnlDropZone_DragDrop(object sender, DragEventArgs e)
    {
        pnlDropZone.BackColor = Color.FromArgb(26, 34, 30);

        if (!e.Data.GetDataPresent(DataFormats.FileDrop) && (e.Data.GetDataPresent(DataFormats.UnicodeText) || e.Data.GetDataPresent(DataFormats.Text)))
        {
            string rawDrop = e.Data.GetData(DataFormats.UnicodeText) as string ?? e.Data.GetData(DataFormats.Text) as string ?? "";
            string safeUrl = SanitizeHttpUrl(rawDrop);
            if (!string.IsNullOrEmpty(safeUrl))
            {
                StartUrlDownload(safeUrl);
                return;
            }
        }

        string gameDir = txtPath.Text.Trim('"');
        if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
        {
            MessageBox.Show("Сначала укажите папку RadioMusic!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string[] droppedItems = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (droppedItems == null || droppedItems.Length == 0) return;

        if (isDownloadBusy)
        {
            UpdateStatus("Уже идёт загрузка/обработка. Дождитесь завершения.", 0);
            return;
        }
        isDownloadBusy = true;

        SetControlsEnabled(false);
        ThreadPool.QueueUserWorkItem(delegate {
            try
            {
                string toolsDir = GetToolsDir();
                string ffmpegPath = Path.Combine(toolsDir, "ffmpeg.exe");
                ExtractResource("ffmpeg.exe", ffmpegPath, false);

                List<string> filesToProcess = new List<string>();
                string validExts = ".mp3.flac.wav.ogg.m4a.opus.aac.wma.mp4.mkv.webm.avi";

                foreach (string item in droppedItems)
                {
                    if (Directory.Exists(item))
                    {
                        foreach (string f in Directory.GetFiles(item, "*.*", SearchOption.AllDirectories))
                            if (validExts.Contains(Path.GetExtension(f).ToLower())) filesToProcess.Add(f);
                    }
                    else if (File.Exists(item) && validExts.Contains(Path.GetExtension(item).ToLower()))
                        filesToProcess.Add(item);
                }

                if (filesToProcess.Count == 0) { UpdateStatus("Не найдено поддерживаемых файлов.", 0); return; }

                isCancelRequested = false;
                for (int i = 0; i < filesToProcess.Count; i++)
                {
                    if (isCancelRequested) { UpdateStatus("Обработка отменена.", 0); return; }
                    string src = filesToProcess[i];
                    string baseName = Path.GetFileNameWithoutExtension(src);
                    bool isSameDir = string.Equals(Path.GetFullPath(Path.GetDirectoryName(src)), Path.GetFullPath(gameDir), StringComparison.OrdinalIgnoreCase);

                    if (isSameDir && Path.GetExtension(src).Equals(".mp3", StringComparison.OrdinalIgnoreCase) && !optLoudNorm)
                        continue;

                    string outMp3 = GetUniqueOutputPath(gameDir, baseName, ".mp3", src);
                    bool isInPlaceReencode = Path.GetFileName(outMp3).StartsWith("__reencode_");
                    int pct = (int)(((i + 1) / (double)filesToProcess.Count) * 100);
                    UpdateStatus(string.Format("Обработка [{0}/{1}]: {2}...", i + 1, filesToProcess.Count, baseName), pct);

                    if (File.Exists(ffmpegPath))
                    {
                        string afFilter = optLoudNorm ? "-af loudnorm=I=-14:TP=-1.5:LRA=11 " : "";
                        string args = string.Format("-y -i \"{0}\" -vn {1}-codec:a libmp3lame -qscale:a 2 \"{2}\"", src, afFilter, outMp3);
                        Process p = Process.Start(new ProcessStartInfo(ffmpegPath, args) { WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true });
                        currentWorkerProcess = p;
                        if (p != null) { p.WaitForExit(); p.Dispose(); }

                        if (isInPlaceReencode)
                        {
                            if (!isCancelRequested && File.Exists(outMp3) && new FileInfo(outMp3).Length > 1024)
                            {
                                File.Delete(src);
                                File.Move(outMp3, src);
                            }
                            else
                            {
                                try { if (File.Exists(outMp3)) File.Delete(outMp3); } catch { }
                            }
                        }
                    }
                    else if (!isSameDir)
                    {
                        File.Copy(src, outMp3, false);
                    }
                }

                if (!isCancelRequested) UpdateStatus(string.Format("Готово! Обработано треков: {0}", filesToProcess.Count), 100);
            }
            catch (Exception ex) { UpdateStatus("Ошибка: " + ex.Message, 0); }
            finally { isDownloadBusy = false; SetControlsEnabled(true); }
        });
    }


    private bool IsDirectAudioStreamUrl(string url)
    {
        Uri u;
        if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return false;
        string host = u.Host.ToLowerInvariant();
        if (host.Contains("youtube.com") || host.Contains("youtu.be") ||
            host.Contains("soundcloud.com") || host.Contains("twitch.tv") ||
            host.Contains("bandcamp.com") || host.Contains("vimeo.com"))
            return false;

        string path = u.AbsolutePath.ToLowerInvariant();
        return path.EndsWith(".m3u8") || path.EndsWith(".pls") || path.EndsWith(".aac") ||
               path.EndsWith(".ogg") || path.EndsWith(".mp3") || path.EndsWith(".opus") ||
               path.Contains("/stream") || path.Contains("/radio") || path.Contains("/live");
    }

    private void StartUrlDownload(string rawUrl)
    {
        string url = SanitizeHttpUrl(rawUrl);
        string gameDir = txtPath.Text.Trim('"');
        if (string.IsNullOrEmpty(url))
        {
            UpdateStatus("Некорректная или небезопасная ссылка.", 0);
            return;
        }
        if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
        {
            MessageBox.Show("Сначала укажите папку RadioMusic!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (isDownloadBusy)
        {
            UpdateStatus("Уже идёт загрузка/обработка. Дождитесь завершения.", 0);
            return;
        }
        isDownloadBusy = true;

        SetControlsEnabled(false);
        UpdateStatus("Подготовка загрузчика...", 20);

        ThreadPool.QueueUserWorkItem(delegate {
            try
            {
                string toolsDir = GetToolsDir();
                string ffmpegPath, ytdlpPath;
                EnsureLatestTools(toolsDir, out ffmpegPath, out ytdlpPath);

                string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL");
                Directory.CreateDirectory(appDataDir);
                File.AppendAllText(Path.Combine(appDataDir, "links.txt"), url + "\r\n", Encoding.UTF8);
                bool isLiveStream = IsDirectAudioStreamUrl(url);

                isCancelRequested = false;
                if (isLiveStream && File.Exists(ffmpegPath))
                {
                    UpdateStatus("Запись прямого радиоэфира (15 мин)...", 60);
                    string outRadio = Path.Combine(gameDir, "RadioStream_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mp3");
                    string afFilter = optLoudNorm ? "-af loudnorm=I=-14:TP=-1.5:LRA=11 " : "";
                    string args = string.Format("-y -i \"{0}\" -t 900 {1}-codec:a libmp3lame -qscale:a 2 \"{2}\"", url, afFilter, outRadio);
                    Process p = Process.Start(new ProcessStartInfo(ffmpegPath, args) { WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true });
                    currentWorkerProcess = p;
                    if (p != null) { p.WaitForExit(); p.Dispose(); }
                }
                else if (File.Exists(ytdlpPath))
                {
                    UpdateStatus("yt-dlp: скачивание (СТОП для отмены)...", 60);
                    string archiveFile = Path.Combine(appDataDir, "downloaded.txt");
                    string legacyArchive = Path.Combine(gameDir, "downloaded.txt");
                    if (!File.Exists(archiveFile) && File.Exists(legacyArchive))
                    {
                        try { File.Move(legacyArchive, archiveFile); } catch { }
                    }
                    string postArgs = optLoudNorm ? "--postprocessor-args \"ffmpeg:-af loudnorm=I=-14:TP=-1.5:LRA=11\"" : "";
                    string args = string.Format("--no-exec -x --audio-format mp3 --ffmpeg-location \"{0}\" {1} --download-archive \"{2}\" -o \"{3}\\%(title)s.%(ext)s\" -- \"{4}\"", toolsDir, postArgs, archiveFile, gameDir, url);
                    Process p = Process.Start(new ProcessStartInfo(ytdlpPath, args) { WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true });
                    currentWorkerProcess = p;
                    if (p != null) { p.WaitForExit(); p.Dispose(); }
                }

                if (isCancelRequested) UpdateStatus("Загрузка отменена.", 0);
                else UpdateStatus("Загрузка завершена!", 100);
            }
            catch (Exception ex) { UpdateStatus("Ошибка загрузки: " + ex.Message, 0); }
            finally { isDownloadBusy = false; SetControlsEnabled(true); }
        });
    }
    private string GetToolsDir()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL", "tools");
        try { Directory.CreateDirectory(dir); } catch { }
        return dir;
    }

    private void SilentSync(bool firstRun)
    {
        string gameDir = "";
        try { this.Invoke(new Action(delegate { gameDir = txtPath.Text.Trim('"'); })); } catch { }
        if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir)) { UpdateStatus("Укажите папку RadioMusic через Обзор...", 0); return; }

        string step = "подготовка";
        try
        {
            UpdateStatus("Синхронизация модулей VTOL QoL...", 30);
            string toolsDir = GetToolsDir();
            step = "извлечение ffmpeg/yt-dlp";
            ExtractResource("ffmpeg.exe", Path.Combine(toolsDir, "ffmpeg.exe"), false);
            ExtractResource("yt-dlp.exe", Path.Combine(toolsDir, "yt-dlp.exe"), false);

            step = "папка приложения";
            string appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL");
            Directory.CreateDirectory(appFolder);
            step = "определение путей";
            string currentExe = Process.GetCurrentProcess().MainModule.FileName;
            // ВАЖНО: больше НЕ копируем exe в AppData и НЕ запускаем оттуда.
            // Раньше Shuffle.bat (автозапуск из Steam) указывал на копию в AppData, и она
            // навсегда оставалась СТАРОЙ сборкой: именно она самоповышалась до администратора
            // и ломала Steam, а обновить её мешала блокировка запущенного файла.
            // Теперь bat всегда указывает на реально запущенный exe.
            string targetExeForBat = currentExe;
            step = "подготовка пути";

            string legacyBat = Path.Combine(gameDir, "Shuffle.bat");
            if (File.Exists(legacyBat))
            {
                try { File.Delete(legacyBat); } catch { }
            }

            step = "запись Shuffle.bat";
            string batBody = "@echo off\r\nstart \"\" /b \"" + targetExeForBat + "\" --shuffle \"" + gameDir + "\"\r\n";
            // Пишем bat РЯДОМ С EXE (папка программы обычно доступна на запись),
            // а не в AppData: там запись может быть запрещена, и раньше это давало
            // «Ошибка синхронизации: Отказано в доступе по пути».
            string batPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Shuffle.bat");
            try
            {
                File.WriteAllText(batPath, batBody, Encoding.ASCII);
            }
            catch (Exception exBat)
            {
                AppLogger.Error("SYNC", "Запись Shuffle.bat рядом с exe", exBat);
                batPath = Path.Combine(appFolder, "Shuffle.bat");
                File.WriteAllText(batPath, batBody, Encoding.ASCII);
            }

            // Ошибки интеграции со Steam не должны валить всю синхронизацию:
            // показываем понятный текст вместо сырого «Отказано в доступе по пути».
            // Steam: только ТИХОЕ лечение уже испорченного конфига. Свои параметры
// запуска больше не пишем вообще (шафл делает фоновый трекер), диалогов нет.
            try
            {
                RepairSteamConfig();
            }
            catch (UnauthorizedAccessException)
            {
                AppLogger.Log("STEAM", "Нет прав на запись в папку Steam (userdata/localconfig.vdf)");
            }
            catch (Exception exSteam)
            {
                AppLogger.Error("STEAM", "RepairSteamConfig", exSteam);
            }

            UpdateStatus("VTOL QoL готов к работе.", 100);
        }
        catch (Exception ex)
        {
            AppLogger.Error("SYNC", "SilentSync, этап: " + step, ex);
            // Гарантированный лог рядом с exe: app_debug.log по какой-то причине может не писаться,
            // а этот файл всегда доступен (папка программы).
            try
            {
                File.AppendAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sync_error.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " | этап: " + step + " | " +
                    ex.GetType().Name + ": " + ex.Message + Environment.NewLine + ex.StackTrace + Environment.NewLine + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch { }
            UpdateStatus("Ошибка синхронизации (" + step + "): " + ex.Message, 0);
        }
    }

    private string UnescapeVdfString(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        StringBuilder sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                char next = s[i + 1];
                if (next == '\\' || next == '"') { sb.Append(next); i++; continue; }
                if (next == 'n') { sb.Append('\n'); i++; continue; }
                if (next == 't') { sb.Append('\t'); i++; continue; }
            }
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    private string EscapeVdfString(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private bool FindVdfBlockBounds(string text, string keyName, out int blockStart, out int blockEnd)
    {
        blockStart = -1;
        blockEnd = -1;
        string pattern = "\"" + keyName + "\"";
        int idx = 0;
        bool inQuotes = false;

        while (idx < text.Length)
        {
            char c = text[idx];
            if (c == '\\' && inQuotes) { idx += 2; continue; }
            if (c == '"')
            {
                if (!inQuotes && idx + pattern.Length <= text.Length &&
                    string.Compare(text, idx, pattern, 0, pattern.Length, StringComparison.Ordinal) == 0)
                {
                    int afterKey = idx + pattern.Length;
                    while (afterKey < text.Length && char.IsWhiteSpace(text[afterKey])) afterKey++;
                    if (afterKey < text.Length && text[afterKey] == '{')
                    {
                        blockStart = idx;
                        int depth = 0;
                        bool innerQuotes = false;
                        for (int j = afterKey; j < text.Length; j++)
                        {
                            char cj = text[j];
                            if (cj == '\\' && innerQuotes) { j++; continue; }
                            if (cj == '"') { innerQuotes = !innerQuotes; continue; }
                            if (!innerQuotes)
                            {
                                if (cj == '{') depth++;
                                else if (cj == '}')
                                {
                                    depth--;
                                    if (depth == 0) { blockEnd = j; return true; }
                                }
                            }
                        }
                        return false;
                    }
                }
                inQuotes = !inQuotes;
            }
            idx++;
        }
        return false;
    }

    // Чистит параметры запуска от наших прежних вставок и НИЧЕГО не добавляет.
    // Шафл теперь делает фоновый трекер, поэтому Steam-параметры не нужны вообще.
    // Возвращает очищенное значение (может быть пустым — это значит «без параметров»).
    // Определяем кодировку localconfig.vdf. Файл Steam часто НЕ utf-8 (ANSI/cp1251).
    // Читать его как UTF-8 и записывать обратно в UTF-8 нельзя: русский текст
    // перекодируется, файл раздувается и Steam его ломает.
    private static System.Text.Encoding DetectVdfEncoding(string path)
    {
        try
        {
            byte[] head = new byte[Math.Min(65536, (int)new FileInfo(path).Length)];
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                fs.Read(head, 0, head.Length);

            if (head.Length >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF)
                return new System.Text.UTF8Encoding(true);

            try
            {
                System.Text.UTF8Encoding strict = new System.Text.UTF8Encoding(false, true);
                strict.GetString(head);
                return new System.Text.UTF8Encoding(false);   // валидный UTF-8 без BOM
            }
            catch { }
        }
        catch { }
        return System.Text.Encoding.Default;              // ANSI (cp1251 на русской системе)
    }

    // Убирает порчу, которую оставляли прежние версии: мусорный ключ "\" со значением
    // параметров запуска, оборванные строки с путём к Shuffle.bat и "%command%".
    private static string CleanVdfCorruption(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        string[] lines = Regex.Split(text, "\r\n|\n|\r");
        StringBuilder sb = new StringBuilder(text.Length);
        bool changed = false;

        foreach (string line in lines)
        {
            string t = line.Trim();
            bool junk = false;

            if (Regex.IsMatch(t, @"^""\\\\""\s+""")) junk = true;                                  // ключ "\"
            else if (Regex.IsMatch(t, @"^""[^""]*Shuffle\.bat""\s+""&\s*$", RegexOptions.IgnoreCase)) junk = true;
            else if (Regex.IsMatch(t, @"^""%command%""\s+""\s*$", RegexOptions.IgnoreCase)) junk = true;
            else if (Regex.IsMatch(t, @"^""LaunchOptions""\s+""[^""]*Shuffle\.bat", RegexOptions.IgnoreCase)) junk = true;

            if (junk) { changed = true; continue; }
            sb.Append(line).Append("\r\n");
        }

        return changed ? sb.ToString() : text;
    }

    private string MergeLaunchOptions(string existingRaw, string batPath)
    {
        string unescaped = UnescapeVdfString(existingRaw).Trim();

        // Убираем все «cmd /c ""...Shuffle.bat" & » — включая ссылки на уже удалённый
        // RadioMusic\Shuffle.bat. Именно из-за такой ссылки игра не стартовала,
        // а Steam показывал ошибку при нажатии «СТАРТ VTOL VR».
        string cleaned = Regex.Replace(
            unescaped,
            @"cmd\s+/c\s+""[^""]*Shuffle\.bat"".{0,8}?\s*&\s*",
            "",
            RegexOptions.IgnoreCase);

        // На всякий случай: любое одиночное упоминание нашего bat тоже убираем.
        cleaned = Regex.Replace(cleaned, @"""?""[^""]*Shuffle\.bat""?""", "", RegexOptions.IgnoreCase);

        cleaned = cleaned.Trim();
        while (cleaned.StartsWith("&")) cleaned = cleaned.Substring(1).Trim();
        cleaned = cleaned.Trim('"').Trim();

        // Если остался только огрызок "%command%" без параметров — трактуем как «пусто».
        if (cleaned.Equals("%command%", StringComparison.OrdinalIgnoreCase)) cleaned = "";

        return cleaned;
    }

    private static void CreateVersionedBackup(string vdfPath)
    {
        try
        {
            string bakPath = vdfPath + ".bak_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            File.Copy(vdfPath, bakPath, true);
            string dir = Path.GetDirectoryName(vdfPath);
            string basePattern = Path.GetFileName(vdfPath) + ".bak_*";
            string[] allBaks = Directory.GetFiles(dir, basePattern);
            if (allBaks.Length > 5)
            {
                Array.Sort(allBaks);
                for (int i = 0; i < allBaks.Length - 5; i++)
                    try { File.Delete(allBaks[i]); } catch { }
            }
        }
        catch { }
    }

    // =====================================================================
    // === ЛЕЧЕНИЕ КОНФИГА STEAM (без записи своих параметров) ==============
    // Программа больше НЕ добавляет ничего в Steam: только убирает свою
    // прежнюю порчу (мусорный ключ "\", оборванные строки, LaunchOptions
    // со ссылкой на удалённый Shuffle.bat). Никаких диалогов.
    // Вызывается тихо; шафл работает через фоновый трекер.
    // =====================================================================
    private static void RepairSteamConfig()
    {
        try
        {
            string steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", "") as string;
            if (string.IsNullOrEmpty(steamPath)) return;
            steamPath = steamPath.Replace('/', '\\');
            string userdata = Path.Combine(steamPath, "userdata");
            if (!Directory.Exists(userdata)) return;

            foreach (string vdf in Directory.GetFiles(userdata, "localconfig.vdf", SearchOption.AllDirectories))
            {
                try
                {
                    System.Text.Encoding enc = DetectVdfEncoding(vdf);
                    string text = File.ReadAllText(vdf, enc);
                    string cleaned = CleanVdfCorruption(text);
                    if (string.Equals(cleaned, text, StringComparison.Ordinal)) continue;

                    // Санитарная проверка: структура должна остаться целой.
                    int bo = Regex.Matches(cleaned, "\\{").Count, bc = Regex.Matches(cleaned, "\\}").Count;
                    if (bo != bc || cleaned.IndexOf("\"667970\"") < 0)
                    {
                        AppLogger.Log("STEAM", "Починка VDF отменена проверкой структуры: " + vdf);
                        continue;
                    }

                    CreateVersionedBackup(vdf);
                    WriteVdfAtomic(vdf, cleaned, enc);
                    AppLogger.Log("STEAM", "Конфиг Steam вычищен от старых параметров автозапуска: " + vdf);
                }
                catch (Exception exOne) { AppLogger.Error("STEAM", "RepairSteamConfig: " + vdf, exOne); }
            }
        }
        catch (Exception ex) { AppLogger.Error("STEAM", "RepairSteamConfig", ex); }
    }

    private void ConfigureSteam(string batPath)
    {
        // Диагностические режимы не должны ни закрывать Steam, ни править его конфиги.
        if (diagnosticMode) return;

        string steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", "") as string;
        if (steamPath == null) steamPath = "";
        steamPath = steamPath.Replace('/', '\\');
        if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(Path.Combine(steamPath, "userdata"))) return;

        string[] vdfFiles = Directory.GetFiles(Path.Combine(steamPath, "userdata"), "localconfig.vdf", SearchOption.AllDirectories);

        bool needsUpdate = false;
        foreach (string vdf in vdfFiles)
        {
            System.Text.Encoding enc = DetectVdfEncoding(vdf);
            string text = CleanVdfCorruption(File.ReadAllText(vdf, enc));
            int bStart, bEnd;
            if (FindVdfBlockBounds(text, "667970", out bStart, out bEnd))
            {
                string block = text.Substring(bStart, bEnd - bStart + 1);
                Match mLo = Regex.Match(block, "\"LaunchOptions\"\\s+\"((?:\\\\.|[^\"\\\\])*)\"");
                string curLo = mLo.Success ? mLo.Groups[1].Value : "";
                if (mLo.Success)
                {
                    string cleanedNow = MergeLaunchOptions(curLo, batPath);
                    // Нужно обновить, если в параметрах остались наши вставки
                    // (в т.ч. ссылка на удалённый RadioMusic\Shuffle.bat) или лишние кавычки.
                    string curUn = UnescapeVdfString(curLo).Trim();
                    if (!string.Equals(cleanedNow, curUn, StringComparison.Ordinal))
                    {
                        needsUpdate = true;
                        break;
                    }
                }
            }
        }
        if (!needsUpdate) return;

        DialogResult confirm = DialogResult.No;
        try
        {
            this.Invoke(new Action(delegate {
                confirm = MessageBox.Show(this,
                    "Добавить параметры автозапуска VTOL QoL к игре VTOL VR в Steam?\nВаши текущие параметры запуска будут сохранены.\n\nПеред этим Steam нужно закрыть вручную (меню Steam → Выход). Программа не закрывает и не перезапускает Steam, чтобы не повредить его настройки.",
                    "Настройка интеграции со Steam", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            }));
        }
        catch { return; }
        if (confirm != DialogResult.Yes) return;

        // БЕЗОПАСНО: никогда не закрываем и не перезапускаем Steam.
        // Раньше приложение вызывало steam://exit и затем запускало steam.exe — из-за этого
        // у пользователей ломался Steam («путь к разделу Steam в реестре недоступен для записи»):
        // оставались steamwebhelper, державшие блокировки, а новый steam.exe стартовал поверх них.
        // Плюс правка конфига работающего Steam бесполезна — Steam перезапишет её при выходе.
        int steamRunning;
        Process[] steamProcs = Process.GetProcessesByName("steam");
        steamRunning = steamProcs.Length;
        foreach (Process sp in steamProcs) sp.Dispose();

        if (steamRunning > 0)
        {
            try
            {
                this.Invoke(new Action(delegate {
                    MessageBox.Show(this,
                        "Steam сейчас запущен.\n\nПолностью закройте Steam (меню Steam → Выход), затем нажмите кнопку ещё раз — изменения применятся.\n\nSteam не закрывается автоматически, чтобы не повредить его настройки и реестр.",
                        "Нужно закрыть Steam вручную", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }));
            }
            catch { }
            UpdateStatus("Закройте Steam вручную и повторите.", 0);
            return;
        }

        foreach (string vdf in vdfFiles)
        {
            string text;
            try { text = CleanVdfCorruption(File.ReadAllText(vdf, DetectVdfEncoding(vdf))); }
            catch (Exception exR) { AppLogger.Error("STEAM", "Чтение VDF пропущено: " + vdf, exR); continue; }

            int startIdx, endIdx;
            if (!FindVdfBlockBounds(text, "667970", out startIdx, out endIdx)) continue;

            string appBlock = text.Substring(startIdx, endIdx - startIdx + 1);
            Match mLo = Regex.Match(appBlock, "\"LaunchOptions\"\\s+\"((?:\\\\.|[^\"\\\\])*)\"");
            string curLo = mLo.Success ? mLo.Groups[1].Value : "";
            string merged = MergeLaunchOptions(curLo, batPath);
            if (merged == null) continue;

            string escapedNew = EscapeVdfString(merged);

            if (merged.Length == 0)
            {
                // Наших параметров быть не должно. Если строка есть — удаляем её целиком
                // (именно она с битой ссылкой на удалённый Shuffle.bat ломала запуск игры).
                if (!mLo.Success) continue;
                int lineStart = appBlock.LastIndexOf('\n', Math.Max(0, mLo.Index - 1));
                if (lineStart < 0) lineStart = mLo.Index;
                int lineEnd = appBlock.IndexOf('\n', mLo.Index + mLo.Length);
                if (lineEnd < 0) lineEnd = appBlock.Length;
                appBlock = appBlock.Substring(0, lineStart) + appBlock.Substring(lineEnd);
            }
            else if (mLo.Success)
            {
                appBlock = appBlock.Substring(0, mLo.Index) + "\"LaunchOptions\"\t\t\"" + escapedNew + "\"" + appBlock.Substring(mLo.Index + mLo.Length);
            }
            else
            {
                continue;   // добавлять нам больше нечего
            }
            string newText = text.Substring(0, startIdx) + appBlock + text.Substring(endIdx + 1);

            // Санитарная проверка: файл не должен «схлопнуться» и обязан сохранить блок приложения.
            int chkS, chkE;
            if (newText.Length < text.Length * 0.5 || !FindVdfBlockBounds(newText, "667970", out chkS, out chkE))
            {
                AppLogger.Log("STEAM", "Запись VDF отменена санитарной проверкой: " + vdf);
                continue;
            }

            try
            {
                CreateVersionedBackup(vdf);
                WriteVdfAtomic(vdf, newText, DetectVdfEncoding(vdf));
                AppLogger.Log("STEAM", "Параметры запуска записаны: " + vdf);
            }
            catch (Exception exW)
            {
                AppLogger.Error("STEAM", "Не удалось обновить VDF: " + vdf, exW);
            }
        }

        // Steam не закрывали и не перезапускаем — параметры применятся при следующем запуске
        // Steam самим пользователем. Так программа физически не может повредить Steam.
        UpdateStatus("Готово. Параметры применятся при следующем запуске Steam.", 100);
    }

    // =====================================================================
    // === ДОСТУП К РАЗДЕЛУ STEAM В РЕЕСТРЕ =================================
    // Steam пишет свои данные в HKCU\Software\Valve\Steam и без этого не
    // работает. Запущенный из программы клиент наследует её токен, поэтому
    // проверять доступ нужно ДО запуска: если запись запрещена, Steam потом
    // покажет «Путь к разделу Steam в реестре недоступен для записи».
    // =====================================================================
    private static bool? steamRegistryWritableCache = null;

    private static bool CanWriteSteamRegistry()
    {
        if (steamRegistryWritableCache.HasValue) return steamRegistryWritableCache.Value;
        string err;
        bool ok = SteamRegistryRepair.CanWrite(out err);
        if (!ok) AppLogger.Log("STEAM", "Раздел Steam в реестре недоступен для записи: " + err);
        steamRegistryWritableCache = ok;
        return ok;
    }

    // Steam бывает запущен «вхолостую»: процесс есть, но клиент так и не записал
    // свои данные в реестр (обычно потому, что его запустила программа с урезанными
    // правами). Такой клиент не обрабатывает команды запуска, а пользователю
    // показывает ошибку «Путь к разделу Steam в реестре недоступен для записи».
    // Возвращает false, если запущенный Steam нерабочий (при согласии — закрывает его).
    private bool CheckRunningSteamIsUsable()
    {
        int pid = 0;
        try
        {
            object pidObj = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam\ActiveProcess", "pid", 0);
            if (pidObj is int) pid = (int)pidObj;
        }
        catch { return true; }   // нет доступа к чтению — не мешаем

        // Клиент записал свой PID и процесс жив — считаем, что Steam рабочий.
        if (pid > 0)
        {
            try
            {
                using (Process live = Process.GetProcessById(pid))
                {
                    if (!live.HasExited) return true;
                }
            }
            catch { }
        }

        // pid пуст или ссылается на мёртвый процесс. Совсем свежий запуск не трогаем:
        // клиент мог просто не успеть записать себя (до ~25 секунд).
        DateTime oldest = DateTime.MaxValue;
        foreach (string name in new string[] { "steam", "steamwebhelper" })
        {
            Process[] arr = Process.GetProcessesByName(name);
            foreach (Process p in arr)
            {
                try { if (p.StartTime < oldest) oldest = p.StartTime; } catch { }
                p.Dispose();
            }
        }
        if (oldest == DateTime.MaxValue) return true;                    // процессов нет
        if ((DateTime.Now - oldest).TotalSeconds < 25) return true;      // ещё стартует

        AppLogger.Log("STEAM", "Запущенный Steam не записал свои данные в реестр (работает "
            + (int)(DateTime.Now - oldest).TotalSeconds + " c, pid в реестре=" + pid + ")");

        DialogResult res = MessageBox.Show(this,
            "Steam сейчас запущен, но так и не записал свои данные в реестр — значит, клиент стартовал "
            + "с урезанными правами и сам не работает. Именно поэтому появляется ошибка про раздел Steam в реестре.\n\n"
            + "Закрыть этот Steam и запустить его заново обычным способом?",
            "Steam запущен неправильно", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (res != DialogResult.Yes) return false;

        UpdateStatus("Перезапускаю Steam...", 30);
        string[] names = new string[] { "steam", "steamwebhelper", "steamerrorreporter64", "steamerrorreporter", "vrdashboard", "vrmonitor", "vrserver", "vrcompositor" };
        foreach (string name in names)
        {
            foreach (Process p in Process.GetProcessesByName(name))
            {
                try { p.Kill(); } catch { }
                p.Dispose();
            }
        }
        Thread.Sleep(4000);
        AppLogger.Log("STEAM", "Нерабочий Steam закрыт, PID в реестре сброшен");
        return true;
    }

    // Может ли программа писать в свой каталог настроек. Если нет — процесс
    // запущен с урезанным токеном (песочница, AppContainer, низкий уровень
    // целостности), и это же ограничение унаследует любой запущенный Steam.
    private static bool CanWriteAppStateFolder()
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL");
            Directory.CreateDirectory(dir);
            string probe = Path.Combine(dir, "qol_probe_" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".tmp");
            File.WriteAllText(probe, "1");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Log("SETTINGS", "Каталог настроек недоступен для записи: " + ex.GetType().Name);
            return false;
        }
    }

    // Можно ли безопасно запускать Steam из этой программы. false — запускать
    // нельзя (объяснение и, при испорченных правах, ремонт уже выполнены).
    private bool PrepareSafeSteamLaunch()
    {
        // Случай 1: урезанный токен. Права в реестре целы, чинить нечего —
        // так работает защита, поэтому просто честно объясняем, что делать.
        if (!CanWriteAppStateFolder())
        {
            AppLogger.Log("STEAM", "Ограниченный режим: нет записи ни в раздел Steam в реестре, ни в свой каталог настроек");
            MessageBox.Show(this,
                "Программа запущена с урезанными правами: у процесса нет доступа на запись ни к разделу Steam в реестре, "
                + "ни к своей папке настроек.\n\n"
                + "Обычно так бывает, когда VTOL_QoL.exe лежит в папке, из которой Windows запускает программы "
                + "с пониженными правами (рабочая папка агента/песочницы). Любой Steam, запущенный оттуда, "
                + "получает те же ограничения и показывает ошибку «Путь к разделу Steam в реестре недоступен для записи».\n\n"
                + "Что сделать:\n"
                + "1) скопируйте папку с программой в обычное место (например, «Документы» или отдельную папку на диске C) "
                + "и запускайте VTOL_QoL.exe оттуда — права будут в порядке;\n"
                + "2) или сначала запустите Steam обычным ярлыком, а уже потом нажмите эту кнопку.\n\n"
                + "Сам раздел реестра при этом не испорчен: игре и клиенту Steam ничего не грозит.",
                "Ограниченный режим", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        // Случай 2: раздела Steam в реестре нет вообще — клиент не установлен
        // или не запускался ни разу. Права тут чинить нечего.
        using (RegistryKey steamKeyProbe = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
        {
            if (steamKeyProbe == null)
            {
                AppLogger.Log("STEAM", "Раздел HKCU\\Software\\Valve\\Steam отсутствует: Steam не установлен");
                MessageBox.Show(this,
                    "В реестре нет раздела Steam (HKCU\\Software\\Valve\\Steam) — клиент Steam не установлен или ещё ни разу не запускался.\n\n"
                    + "Установите Steam и один раз запустите его обычным ярлыком — после этого кнопка СТАРТ VTOL VR заработает.",
                    "Steam не найден", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        // Случай 3: права раздела Steam действительно испорчены (запреты,
        // снятое наследование). Предлагаем восстановить.
        AppLogger.Log("STEAM", "Раздел Steam в реестре защищён от записи, предлагаю восстановление прав");
        DialogResult fix = MessageBox.Show(this,
            "Раздел Steam в реестре защищён от записи — из-за этого Steam при запуске показывает ошибку.\n\n"
            + "Восстановить стандартные права на раздел HKCU\\Software\\Valve\\Steam?\n"
            + "Потребуется подтверждение администратора (UAC). Настройки и значения Steam не меняются.",
            "Права на раздел Steam", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (fix != DialogResult.Yes) return false;

        UpdateStatus("Восстанавливаю права на раздел Steam в реестре...", 30);
        string reportPath = Path.Combine(Path.GetTempPath(), "VTOL_QoL_steam_acl.txt");
        try { if (File.Exists(reportPath)) File.Delete(reportPath); } catch { }
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, "--fix-steam-acl \"" + reportPath + "\"");
            psi.Verb = "runas";
            psi.UseShellExecute = true;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            using (Process fixProc = Process.Start(psi))
            {
                if (fixProc != null) fixProc.WaitForExit(60000);
            }
        }
        catch (Exception exFix)
        {
            AppLogger.Error("STEAM", "Запуск восстановления прав раздела Steam", exFix);
        }

        steamRegistryWritableCache = null;
        string errNow;
        if (SteamRegistryRepair.CanWrite(out errNow))
        {
            AppLogger.Log("STEAM", "Права на раздел Steam восстановлены");
            UpdateStatus("Права на раздел Steam восстановлены.", 40);
            return true;
        }

        AppLogger.Log("STEAM", "Восстановить права на раздел Steam не удалось: " + errNow);
        string report = "";
        try { if (File.Exists(reportPath)) report = File.ReadAllText(reportPath).Trim(); } catch { }
        MessageBox.Show(this,
            "Не удалось восстановить права на раздел Steam в реестре.\n\n"
            + (errNow ?? "")
            + (string.IsNullOrEmpty(report) ? "" : "\n\n" + report)
            + "\n\nЧто можно сделать вручную: запустите Steam обычным ярлыком (тогда кнопка работает с уже "
            + "запущенным клиентом) или запустите VTOL QoL другим способом — не из песочницы и не от администратора.",
            "Не удалось исправить", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    // Надёжное определение того, запущен ли Steam.
    // Одного процесса "steam" мало: клиент может держать только steamwebhelper,
    // окно может быть свёрнуто в трей, а имя процесса зависит от версии клиента.
    private static bool IsSteamRunning()
    {
        try
        {
            foreach (string name in new string[] { "steam", "steamwebhelper", "steamservice", "steamerrorreporter" })
            {
                Process[] arr = Process.GetProcessesByName(name);
                if (arr != null && arr.Length > 0)
                {
                    foreach (Process p in arr) p.Dispose();
                    return true;
                }
            }
            // Окно Steam (в т.ч. свёрнутое в трей — заголовок всё равно существует)
            if (FindWindow(null, "Steam") != IntPtr.Zero) return true;
            // SteamPID из реестра, если процесс ещё жив
            object pidObj = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "SteamPID", 0);
            if (pidObj is int && (int)pidObj > 0)
            {
                try
                {
                    using (Process p = Process.GetProcessById((int)pidObj))
                        if (!p.HasExited) return true;
                }
                catch { }
            }
        }
        catch { }
        return false;
    }

    // Запущена ли программа с повышенными правами.
    private static bool IsRunningAsAdmin()
    {
        try
        {
            using (System.Security.Principal.WindowsIdentity id = System.Security.Principal.WindowsIdentity.GetCurrent())
                return new System.Security.Principal.WindowsPrincipal(id)
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    // Атомарная запись конфигурации Steam: сначала во временный файл рядом, затем замена.
    // Обрыв/падение в середине записи не может оставить localconfig.vdf обрезанным.
    private static void WriteVdfAtomic(string path, string text, System.Text.Encoding enc)
    {
        if (enc == null) enc = System.Text.Encoding.Default;
        string dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir)) dir = ".";
        string tmp = Path.Combine(dir, Path.GetFileName(path) + ".tmp_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        try
        {
            using (FileStream fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (StreamWriter sw = new StreamWriter(fs, enc))
            {
                sw.Write(text);
                sw.Flush();
                fs.Flush(true);
            }
            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, null); }
                catch
                {
                    // ФС без поддержки Replace (сеть, некоторый антивирус) — аккуратная перезапись.
                    File.Copy(tmp, path, true);
                    File.Delete(tmp);
                }
            }
            else
            {
                File.Move(tmp, path);
            }
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            throw;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [STAThread]
    static void Main(string[] args)
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (s, e) =>
        {
            try { File.WriteAllText("crash_last.log", e.Exception.ToString()); } catch { }
        };
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            try { File.WriteAllText("crash_last.log", e.ExceptionObject.ToString()); } catch { }
        };
        bool isShuffleMode = (args != null && args.Length >= 1 && args[0] == "--shuffle");
        if (args != null && args.Length >= 1 && args[0] == "--perf-test")
            DebriefRadarForm.PerfTestActive = true;
        if (args != null && args.Length >= 3 && args[0] == "--dump-replay")
        {
            // Загружает реплей и пишет диагностический отчёт: удобно проверять
            // разбор сторон (союз/враг) без запуска окна.
            diagnosticMode = true;
            string rpPilot = args.Length >= 4 ? args[3] : "";
            try { DebriefEngine.DumpReplay(args[1], rpPilot, args[2]); } catch { }
            return;
        }
        if (args != null && args.Length >= 3 && args[0] == "--scenario-text")
        {
            diagnosticMode = true;
            try { DebriefEngine.ScenarioToText(args[1], args[2]); } catch { }
            return;
        }
        if (args != null && args.Length >= 2 && args[0] == "--find-type")
        {
            diagnosticMode = true;
            string ftOut = args.Length >= 3 ? args[2] : (args[1].Replace(" ", "_") + ".types.txt");
            try { DebriefEngine.FindGameTypes(args[1], ftOut); } catch { }
            return;
        }
        if (args != null && args.Length >= 2 && args[0] == "--livery-fix")
        {
            diagnosticMode = true;
            string lp;
            try { LiveryManagerForm.RunAutoFix(args[1], out lp); } catch { }
            return;
        }
        if (args != null && args.Length >= 4 && args[0] == "--test-teams")
        {
            diagnosticMode = true;
            string tp = args.Length >= 5 ? args[4] : "";
            try { DebriefEngine.TestScenarioTeams(args[1], args[2], tp, args[3]); } catch { }
            return;
        }
        if (args != null && args.Length >= 3 && args[0] == "--prefab-teams")
        {
            diagnosticMode = true;
            try { DebriefEngine.ScanAssetForPrefabTeams(args[1], args[2]); } catch { }
            return;
        }
        if (args != null && args.Length >= 4 && args[0] == "--hexdump")
        {
            diagnosticMode = true;
            long off = 0;
            int len = 512;
            try { off = Convert.ToInt64(args[2], 16); } catch { }
            if (args.Length >= 4) { try { len = Convert.ToInt32(args[3]); } catch { } }
            string ho = args.Length >= 5 ? args[4] : "hexdump.txt";
            try { DebriefEngine.HexDump(args[1], off, len, ho); } catch { }
            return;
        }
        if (args != null && args.Length >= 3 && args[0] == "--grep-file")
        {
            diagnosticMode = true;
            string go = args.Length >= 4 ? args[3] : (args[1] + ".find.txt");
            try { DebriefEngine.FindInFile(args[1], args[2], go); } catch { }
            return;
        }
        if (args != null && args.Length >= 3 && args[0] == "--scenario-teams")
        {
            diagnosticMode = true;
            try { DebriefEngine.DumpScenarioTeams(args[1], args[2]); } catch { }
            return;
        }
        if (args != null && args.Length >= 2 && args[0] == "--il-dump")
        {
            diagnosticMode = true;
            string ilOut = args.Length >= 3 ? args[2] : (args[1] + ".il.txt");
            try { DebriefEngine.DumpTypeIL(args[1], ilOut); } catch { }
            return;
        }
        if (args != null && args.Length >= 2 && args[0] == "--dump-vtr")
        {
            diagnosticMode = true;
            string dumpOut = args.Length >= 3 ? args[2] : (args[1] + ".structure.txt");
            string dumpPilot = args.Length >= 4 ? args[3] : "";
            try { DebriefEngine.DumpNativeStructure(args[1], dumpOut, dumpPilot); } catch { }
            return;
        }
        // Восстановление прав на раздел Steam в реестре (запускается с runas из
        // основной программы, когда раздел защищён от записи). Отчёт — в файл,
        // потому что окно консоли у winexe нет.
        if (args != null && args.Length >= 1 && args[0] == "--fix-steam-acl")
        {
            diagnosticMode = true;
            string aclReport;
            bool aclOk = SteamRegistryRepair.Repair(out aclReport);
            string aclOut = (args.Length >= 2 && !string.IsNullOrEmpty(args[1]))
                ? args[1]
                : Path.Combine(Path.GetTempPath(), "VTOL_QoL_steam_acl.txt");
            try { File.WriteAllText(aclOut, (aclOk ? "OK" : "FAIL") + Environment.NewLine + aclReport, Encoding.UTF8); } catch { }
            return;
        }

        if (args != null && args.Length >= 1 && args[0] == "--perf-test")
            diagnosticMode = true;
        if (!isShuffleMode)
            AppLogger.InitSession();

        AppLogger.Log("CORE", "Запуск приложения. Аргументы: " + (args != null ? string.Join(" ", args) : "нет"));
        // Сразу фиксируем в логе, в каком контексте работает программа: от этого
        // зависит, можно ли запускать Steam (см. PrepareSafeSteamLaunch).
        AppLogger.Log("CORE", "Проверка доступа: раздел Steam в реестре — запись "
            + (CanWriteSteamRegistry() ? "разрешена" : "ЗАПРЕЩЕНА")
            + ", каталог настроек — запись " + (CanWriteAppStateFolder() ? "разрешена" : "ЗАПРЕЩЕНА"));

        if (isShuffleMode)
        {
            string dir = args.Length >= 2 ? args[1] : "";
            if (!string.IsNullOrEmpty(dir))
            {
                DateTime deadline = DateTime.UtcNow.AddSeconds(40);
                while (DateTime.UtcNow < deadline)
                {
                    if (!MusicShuffler.IsGameRunning()) break;
                    Thread.Sleep(500);
                }
                bool smart = SettingsManagerSafeGetShuffleFlag();
                MusicShuffler.ShuffleNow(dir, smart);
            }

            bool createdNewBg;
            using (Mutex bgMutex = new Mutex(true, @"Local\VTOL_QoL_Tracker_SingleInstance", out createdNewBg))
            {
                if (createdNewBg)
                {
                    SettingsManager.Load();
                    GameTracker bgTracker = new GameTracker();
                    bgTracker.DiscordEnabled = SettingsManager.GetBool("Discord", true);
                    bgTracker.DuckingEnabled = SettingsManager.GetBool("Ducking", true);
                    bgTracker.AudioSwitchEnabled = SettingsManager.GetBool("AudioSwitch", true);
                    bgTracker.DiagnosticsEnabled = SettingsManager.GetBool("DiagnosticsEnabled", false);
                    bgTracker.Start();

                    DateTime startWait = DateTime.UtcNow.AddSeconds(30);
                    while (DateTime.UtcNow < startWait && !MusicShuffler.IsGameRunning())
                    {
                        if (FindWindow(null, "VTOL QoL") != IntPtr.Zero) return;
                        Thread.Sleep(500);
                    }

                    while (MusicShuffler.IsGameRunning())
                    {
                        if (FindWindow(null, "VTOL QoL") != IntPtr.Zero) return;
                        Thread.Sleep(2000);
                    }

                    Thread.Sleep(2500);
                }
                else
                {
                    AppLogger.Log("CORE", "Трекер VTOL_QoL уже активен, фоновый дубль пропущен.");
                }
            }
            return;
        }

        bool createdNewMain;
        using (Mutex mainMutex = new Mutex(true, @"Local\VTOL_QoL_MainUI_SingleInstance", out createdNewMain))
        {
            if (!createdNewMain)
            {
                IntPtr existingHwnd = FindWindow(null, "VTOL QoL");
                if (existingHwnd != IntPtr.Zero)
                {
                    ShowWindowAsync(existingHwnd, 9); // SW_RESTORE
                    SetForegroundWindow(existingHwnd);
                }
                return;
            }

            bool createdNewTracker;
            using (Mutex trackerMutex = new Mutex(true, @"Local\VTOL_QoL_Tracker_SingleInstance", out createdNewTracker))
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                SettingsManager.Load();
                Application.Run(new MainForm());
            }
        }
    }

    private static bool SettingsManagerSafeGetShuffleFlag()
    {
        try { SettingsManager.Load(); return SettingsManager.GetBool("Shuffle", true); } catch { return true; }
    }
}

