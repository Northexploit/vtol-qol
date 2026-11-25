using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

public class MainForm : Form
{
    private TextBox txtPath;
    private TextBox txtUrl;
    private Button btnDownloadUrl;
    private Button btnStopDownload;
    private Button btnManageMusic;
    private Panel pnlDropZone;
    private Label lblDropText;
    private CheckBox chkShuffle;
    private CheckBox chkLoudNorm;
    private CheckBox chkDiscord;
    private Button btnInstall;
    private Button btnOpenFolder;
    private Label lblStatus;
    private ProgressBar progressBar;
    private CheckBox chkDucking;
    private CheckBox chkAudioSwitch;
    private Button btnLiveryManager;
    private Button btnLogbook;
    private Button btnSaveVault;
    private NotifyIcon trayIcon;
    private ContextMenuStrip trayMenu;
    private bool isExplicitExit = false;
    private volatile Process currentWorkerProcess = null;
    private volatile bool isCancelRequested = false;

    public MainForm()
    {
        this.Text = "VTOL QoL — Smart Radio & Media Hub | by L.Shutze";
        this.Size = new Size(580, 650);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.FixedSingle;
        this.MaximizeBox = false;
        this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        this.BackColor = Color.FromArgb(18, 22, 20);
        this.ForeColor = Color.FromArgb(0, 255, 102);
        this.Font = new Font("Consolas", 9.5f, FontStyle.Regular);

        // Заголовок и авторство
        Label lblTitle = new Label();
        lblTitle.Text = "/// VTOL QoL — FLIGHT AUDIO && DISCORD SUITE ///";
        lblTitle.Font = new Font("Consolas", 12f, FontStyle.Bold);
        lblTitle.Location = new Point(20, 15);
        lblTitle.AutoSize = true;
        this.Controls.Add(lblTitle);

        Label lblAuthor = new Label();
        lblAuthor.Text = "Created by L.Shutze";
        lblAuthor.Font = new Font("Consolas", 8.5f, FontStyle.Italic);
        lblAuthor.ForeColor = Color.FromArgb(130, 180, 150);
        lblAuthor.Location = new Point(405, 38);
        lblAuthor.AutoSize = true;
        this.Controls.Add(lblAuthor);

        // Путь к папке игры
        Label lblDir = new Label();
        lblDir.Text = "Директория RadioMusic:";
        lblDir.Location = new Point(22, 48);
        lblDir.AutoSize = true;
        lblDir.ForeColor = Color.LightGray;
        this.Controls.Add(lblDir);

        txtPath = new TextBox();
        txtPath.Location = new Point(24, 68);
        txtPath.Size = new Size(415, 24);
        txtPath.BackColor = Color.FromArgb(30, 36, 33);
        txtPath.ForeColor = Color.White;
        txtPath.BorderStyle = BorderStyle.FixedSingle;
        txtPath.Text = FindVtolMusicDir();
        this.Controls.Add(txtPath);

        Button btnBrowse = new Button();
        btnBrowse.Text = "Обзор...";
        btnBrowse.Location = new Point(445, 67);
        btnBrowse.Size = new Size(80, 26);
        btnBrowse.FlatStyle = FlatStyle.Flat;
        btnBrowse.BackColor = Color.FromArgb(35, 45, 40);
        btnBrowse.Click += (s, e) => {
            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
                if (fbd.ShowDialog() == DialogResult.OK) txtPath.Text = fbd.SelectedPath;
        };
        this.Controls.Add(btnBrowse);

        // Зона Drag & Drop
        pnlDropZone = new Panel();
        pnlDropZone.Location = new Point(24, 105);
        pnlDropZone.Size = new Size(501, 95);
        pnlDropZone.BackColor = Color.FromArgb(26, 34, 30);
        pnlDropZone.BorderStyle = BorderStyle.FixedSingle;
        pnlDropZone.AllowDrop = true;
        pnlDropZone.DragEnter += PnlDropZone_DragEnter;
        pnlDropZone.DragLeave += (s, e) => pnlDropZone.BackColor = Color.FromArgb(26, 34, 30);
        pnlDropZone.DragDrop += PnlDropZone_DragDrop;
        this.Controls.Add(pnlDropZone);

        lblDropText = new Label();
        lblDropText.Text = "[ ПЕРЕТАЩИТЕ СЮДА ЛЮБУЮ МУЗЫКУ, ВИДЕО ИЛИ ПАПКИ ]\nПоддержка: MP3, FLAC, WAV, OGG, M4A, OPUS, MP4, MKV\nАвтоматическая конвертация и выравнивание громкости";
        lblDropText.TextAlign = ContentAlignment.MiddleCenter;
        lblDropText.Dock = DockStyle.Fill;
        lblDropText.ForeColor = Color.FromArgb(160, 230, 185);
        lblDropText.AllowDrop = true;
        lblDropText.DragEnter += PnlDropZone_DragEnter;
        lblDropText.DragDrop += PnlDropZone_DragDrop;
        pnlDropZone.Controls.Add(lblDropText);

        // Блок загрузки по ссылкам (YouTube, SoundCloud, Radio)
        Label lblUrl = new Label();
        lblUrl.Text = "Загрузить по ссылке (YouTube / SoundCloud / Плейлисты / Потоки):";
        lblUrl.Location = new Point(22, 212);
        lblUrl.AutoSize = true;
        lblUrl.ForeColor = Color.LightGray;
        this.Controls.Add(lblUrl);

        txtUrl = new TextBox();
        txtUrl.Location = new Point(24, 232);
        txtUrl.Size = new Size(310, 24);
        txtUrl.BackColor = Color.FromArgb(30, 36, 33);
        txtUrl.ForeColor = Color.Cyan;
        txtUrl.BorderStyle = BorderStyle.FixedSingle;
        this.Controls.Add(txtUrl);

        btnDownloadUrl = new Button();
        btnDownloadUrl.Text = "СКАЧАТЬ";
        btnDownloadUrl.Font = new Font("Consolas", 8.5f, FontStyle.Bold);
        btnDownloadUrl.Location = new Point(340, 231);
        btnDownloadUrl.Size = new Size(95, 26);
        btnDownloadUrl.FlatStyle = FlatStyle.Flat;
        btnDownloadUrl.BackColor = Color.FromArgb(0, 110, 140);
        btnDownloadUrl.ForeColor = Color.White;
        btnDownloadUrl.Click += BtnDownloadUrl_Click;
        this.Controls.Add(btnDownloadUrl);

        btnStopDownload = new Button();
        btnStopDownload.Text = "СТОП";
        btnStopDownload.Font = new Font("Consolas", 8.5f, FontStyle.Bold);
        btnStopDownload.Location = new Point(440, 231);
        btnStopDownload.Size = new Size(85, 26);
        btnStopDownload.FlatStyle = FlatStyle.Flat;
        btnStopDownload.BackColor = Color.FromArgb(140, 35, 35);
        btnStopDownload.ForeColor = Color.White;
        btnStopDownload.Enabled = false;
        btnStopDownload.Click += BtnStopDownload_Click;
        this.Controls.Add(btnStopDownload);

        // Настройки фоновых систем
        // Настройки фоновых систем
        // Настройки фоновых систем
        chkShuffle = CreateCheckBox("Умный шаффл при старте игры и каждом респавне", 268, true);
        chkLoudNorm = CreateCheckBox("Выравнивание громкости треков (LoudNorm -14 LUFS)", 294, true);
        chkDiscord = CreateCheckBox("Discord Rich Presence (самолет, позывной, фраги, таймер)", 320, true);
        chkDucking = CreateCheckBox("Авто-затихание радио при радиосвязи (Discord / голосовой чат)", 346, true);
        chkAudioSwitch = CreateCheckBox("Авто-переключение звука и микрофона на VR-шлем при старте", 372, true);

        // Прогресс и статус
        progressBar = new ProgressBar();
        progressBar.Location = new Point(24, 420);
        progressBar.Size = new Size(520, 14);
        progressBar.Style = ProgressBarStyle.Continuous;
        progressBar.Visible = false;
        this.Controls.Add(progressBar);

        lblStatus = new Label();
        lblStatus.Text = "Проверка интеграции со Steam...";
        lblStatus.Location = new Point(22, 440);
        lblStatus.Size = new Size(520, 22);
        lblStatus.ForeColor = Color.Silver;
        this.Controls.Add(lblStatus);

        // --- Главные кнопки управления (Сетка действий) ---
        // Ряд 1: Запуск и Защита сохранений
        btnInstall = new Button();
        btnInstall.Text = "▶ СТАРТ VTOL VR";
        btnInstall.Font = new Font("Consolas", 10f, FontStyle.Bold);
        btnInstall.Location = new Point(24, 470);
        btnInstall.Size = new Size(255, 42);
        btnInstall.FlatStyle = FlatStyle.Flat;
        btnInstall.BackColor = Color.FromArgb(0, 140, 60);
        btnInstall.ForeColor = Color.White;
        btnInstall.Click += (s, e) => {
            SilentSync(false);
            StartNativeDiscordTracker();
            Process.Start("steam://rungameid/667970");
        };
        this.Controls.Add(btnInstall);

        btnSaveVault = new Button();
        btnSaveVault.Text = "🛡 БЭКАП СОХРАНЕНИЙ";
        btnSaveVault.Font = new Font("Consolas", 9.5f, FontStyle.Bold);
        btnSaveVault.Location = new Point(290, 470);
        btnSaveVault.Size = new Size(254, 42);
        btnSaveVault.FlatStyle = FlatStyle.Flat;
        btnSaveVault.BackColor = Color.FromArgb(130, 80, 0);
        btnSaveVault.ForeColor = Color.White;
        btnSaveVault.Click += (s, e) => {
            using (var vault = new SaveVaultForm())
            {
                vault.ShowDialog(this);
            }
        };
        this.Controls.Add(btnSaveVault);

        // Ряд 2: Контентные модули (Музыка, Ливреи, Логбук)
        btnManageMusic = new Button();
        btnManageMusic.Text = "♫ ТРЕКИ";
        btnManageMusic.Font = new Font("Consolas", 9f, FontStyle.Bold);
        btnManageMusic.Location = new Point(24, 520);
        btnManageMusic.Size = new Size(165, 38);
        btnManageMusic.FlatStyle = FlatStyle.Flat;
        btnManageMusic.BackColor = Color.FromArgb(0, 95, 125);
        btnManageMusic.ForeColor = Color.White;
        btnManageMusic.Click += (s, e) => {
            string dir = txtPath.Text.Trim('"');
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                MessageBox.Show("Сначала укажите корректную папку RadioMusic!", "Внимание", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            using (var manager = new MusicManagerForm(dir))
            {
                manager.ShowDialog(this);
            }
        };
        this.Controls.Add(btnManageMusic);

        btnLiveryManager = new Button();
        btnLiveryManager.Text = "✈ ЛИВРЕИ / СКИНЫ";
        btnLiveryManager.Font = new Font("Consolas", 9f, FontStyle.Bold);
        btnLiveryManager.Location = new Point(200, 520);
        btnLiveryManager.Size = new Size(170, 38);
        btnLiveryManager.FlatStyle = FlatStyle.Flat;
        btnLiveryManager.BackColor = Color.FromArgb(70, 50, 110);
        btnLiveryManager.ForeColor = Color.White;
        btnLiveryManager.Click += (s, e) => {
            string dir = txtPath.Text.Trim('"');
            string gameRoot = Directory.Exists(dir) ? Path.GetFullPath(Path.Combine(dir, "..")) : "";
            using (var livery = new LiveryManagerForm(gameRoot))
            {
                livery.ShowDialog(this);
            }
        };
        this.Controls.Add(btnLiveryManager);

        btnLogbook = new Button();
        btnLogbook.Text = "📋 ЛОГБУК ВЫЛЕТОВ";
        btnLogbook.Font = new Font("Consolas", 9f, FontStyle.Bold);
        btnLogbook.Location = new Point(379, 520);
        btnLogbook.Size = new Size(165, 38);
        btnLogbook.FlatStyle = FlatStyle.Flat;
        btnLogbook.BackColor = Color.FromArgb(40, 85, 70);
        btnLogbook.ForeColor = Color.White;
        btnLogbook.Click += (s, e) => {
            using (var logbook = new FlightLogbookForm())
            {
                logbook.ShowDialog(this);
            }
        };
        this.Controls.Add(btnLogbook);

        // Ряд 3: Быстрый доступ к папкам
        btnOpenFolder = new Button();
        btnOpenFolder.Text = "📁 ПАПКА RADIOMUSIC";
        btnOpenFolder.Font = new Font("Consolas", 8.5f, FontStyle.Bold);
        btnOpenFolder.Location = new Point(24, 566);
        btnOpenFolder.Size = new Size(255, 32);
        btnOpenFolder.FlatStyle = FlatStyle.Flat;
        btnOpenFolder.BackColor = Color.FromArgb(30, 38, 34);
        btnOpenFolder.ForeColor = Color.FromArgb(0, 255, 102);
        btnOpenFolder.Click += (s, e) => {
            if (Directory.Exists(txtPath.Text)) Process.Start("explorer.exe", txtPath.Text);
        };
        this.Controls.Add(btnOpenFolder);

        Button btnOpenGameDir = new Button();
        btnOpenGameDir.Text = "📁 ПАПКА VTOL VR";
        btnOpenGameDir.Font = new Font("Consolas", 8.5f, FontStyle.Bold);
        btnOpenGameDir.Location = new Point(290, 566);
        btnOpenGameDir.Size = new Size(254, 32);
        btnOpenGameDir.FlatStyle = FlatStyle.Flat;
        btnOpenGameDir.BackColor = Color.FromArgb(30, 38, 34);
        btnOpenGameDir.ForeColor = Color.FromArgb(0, 255, 102);
        btnOpenGameDir.Click += (s, e) => {
            string gRoot = Directory.Exists(txtPath.Text) ? Path.GetFullPath(Path.Combine(txtPath.Text, "..")) : "";
            if (Directory.Exists(gRoot)) Process.Start("explorer.exe", gRoot);
        };
        this.Controls.Add(btnOpenGameDir);

                InitSystemTray();
        this.FormClosing += MainForm_FormClosing;

        // Тихая авто-установка и синхронизация при каждом открытии окна
        ThreadPool.QueueUserWorkItem(_ => SilentSync(true));
    }



    private void InitSystemTray()
    {
        trayMenu = new ContextMenuStrip();
        var itemRestore = new ToolStripMenuItem("Показать главное окно");
        itemRestore.Click += (s, e) => RestoreFromTray();
        trayMenu.Items.Add(itemRestore);

        var itemShuffle = new ToolStripMenuItem("Перемешать треки сейчас");
        itemShuffle.Click += (s, e) => {
            string dir = txtPath.Text.Trim('\"');
            if (Directory.Exists(dir))
            {
                using (var manager = new MusicManagerForm(dir))
                {
                    manager.TriggerShuffle();
                }
                if (trayIcon != null)
                    trayIcon.ShowBalloonTip(2000, "VTOL QoL", "Плейлист RadioMusic успешно перемешан!", ToolTipIcon.Info);
            }
        };
        trayMenu.Items.Add(itemShuffle);

        var itemOpenFolder = new ToolStripMenuItem("Открыть папку RadioMusic");
        itemOpenFolder.Click += (s, e) => {
            string dir = txtPath.Text.Trim('\"');
            if (Directory.Exists(dir)) Process.Start("explorer.exe", dir);
        };
        trayMenu.Items.Add(itemOpenFolder);

        trayMenu.Items.Add(new ToolStripSeparator());

        var itemExit = new ToolStripMenuItem("Выход из VTOL QoL");
        itemExit.Click += (s, e) => {
            isExplicitExit = true;
            if (trayIcon != null) trayIcon.Visible = false;
            Application.Exit();
        };
        trayMenu.Items.Add(itemExit);

        trayIcon = new NotifyIcon();
        trayIcon.Text = "VTOL QoL — Flight Audio Hub";
        trayIcon.Icon = this.Icon;
        trayIcon.ContextMenuStrip = trayMenu;
        trayIcon.Visible = true;
        trayIcon.DoubleClick += (s, e) => RestoreFromTray();
    }

    private void RestoreFromTray()
    {
        this.Show();
        this.WindowState = FormWindowState.Normal;
        this.BringToFront();
        this.Activate();
    }

    private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
    {
        if (!isExplicitExit && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            this.Hide();
            if (trayIcon != null)
                trayIcon.ShowBalloonTip(2500, "VTOL QoL", "Утилита свернута в системный трей и продолжает работу в фоне игры.", ToolTipIcon.Info);
        }
    }
    private bool isTrackerRunning = false;
    private void StartNativeDiscordTracker()
    {
        if (isTrackerRunning) return;
        isTrackerRunning = true;

        ThreadPool.QueueUserWorkItem(_ => {
            string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"AppData\LocalLow\Boundless Dynamics, LLC\VTOLVR\Player.log");
            string pilotsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Boundless Dynamics, LLC\VTOLVR\SaveData\pilots.cfg");

            string myNick = "";
            if (File.Exists(pilotsFile))
            {
                Match m = Regex.Match(File.ReadAllText(pilotsFile, Encoding.UTF8), @"(?m)^(?:lastPilot|pilotName)\s*=\s*(.+)$");
                if (m.Success) myNick = Regex.Escape(m.Result("$1").Trim());
            }

            string currentAircraft = "В ангаре / Меню";
            string currentCallsign = "В ангаре / Меню";
            int kills = 0;
            long startEpoch = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
            long lastSeekPos = File.Exists(logPath) ? new FileInfo(logPath).Length : 0;

            System.IO.Pipes.NamedPipeClientStream pipe = null;

            Action<int, string> sendPkt = (op, json) => {
                byte[] jb = Encoding.UTF8.GetBytes(json);
                byte[] pkt = new byte[8 + jb.Length];
                BitConverter.GetBytes(op).CopyTo(pkt, 0);
                BitConverter.GetBytes(jb.Length).CopyTo(pkt, 4);
                jb.CopyTo(pkt, 8);
                pipe.Write(pkt, 0, pkt.Length);
                pipe.Flush();
                byte[] buf = new byte[2048];
                pipe.Read(buf, 0, buf.Length);
            };

            while (true)
            {
                try
                {
                    bool useDisc = false;
                    this.Invoke(new Action(() => useDisc = chkDiscord.Checked));

                    Process[] vtolProcs = Process.GetProcessesByName("VTOLVR");
                    if (vtolProcs.Length == 0 || !useDisc)
                    {
                        if (pipe != null) { try { pipe.Dispose(); } catch { } pipe = null; }
                        currentAircraft = "В ангаре / Меню";
                        currentCallsign = "В ангаре / Меню";
                        kills = 0;
                        Thread.Sleep(2000);
                        startEpoch = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
                        continue;
                    }

                    // Читаем свежие строки из Player.log
                    if (File.Exists(logPath))
                    {
                        using (FileStream fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            if (fs.Length < lastSeekPos) lastSeekPos = 0;
                            if (fs.Length > lastSeekPos)
                            {
                                fs.Seek(lastSeekPos, SeekOrigin.Begin);
                                using (StreamReader sr = new StreamReader(fs, Encoding.UTF8))
                                {
                                    string chunk = sr.ReadToEnd();
                                    lastSeekPos = fs.Position;

                                    string csPat = @"((?:Alpha|Bravo|Charlie|Delta|Echo|Foxtrot|Golf|Hotel|India|Juliet|Kilo|Lima|Mike|November|Oscar|Papa|Quebec|Romeo|Sierra|Tango|Uniform|Victor|Whiskey|X-?ray|Yankee|Zulu)\s+\d+-\d+)";
                                    string plPat = @"(F/?A-26B|EF-24G|AV-42C|F-45A|AH-94|T-55)";

                                    Match mCs = !string.IsNullOrEmpty(myNick) ? Regex.Match(chunk, myNick + @"[^\r\n]*?" + csPat) : Regex.Match(chunk, csPat);
                                    if (mCs.Success) currentCallsign = mCs.Result("$1");

                                    if (currentCallsign != "В ангаре / Меню")
                                    {
                                        Match mPl = Regex.Match(chunk, plPat);
                                        if (mPl.Success) currentAircraft = mPl.Result("$1");
                                    }

                                    if (!string.IsNullOrEmpty(myNick) && Regex.IsMatch(chunk, @"was killed by " + myNick))
                                        kills++;

                                    bool hit = !string.IsNullOrEmpty(myNick)
                                        ? Regex.IsMatch(chunk, @"\(" + myNick + @"\) ejected|\(" + myNick + @"\) was killed")
                                        : Regex.IsMatch(chunk, @"ejected|was killed");
                                    if (hit)
                                    {
                                        currentAircraft = "В ангаре / Меню";
                                        currentCallsign = "В ангаре / Меню";
                                    }
                                }
                            }
                        }
                    }

                    // Подключаемся к Discord IPC
                    if (pipe == null || !pipe.IsConnected)
                    {
                        for (int i = 0; i < 10; i++)
                        {
                            try
                            {
                                var p = new System.IO.Pipes.NamedPipeClientStream(".", "discord-ipc-" + i, System.IO.Pipes.PipeDirection.InOut);
                                p.Connect(150);
                                if (p.IsConnected)
                                {
                                    pipe = p;
                                    sendPkt(0, "{\"v\":1,\"client_id\":\"1573385169807872222\"}");
                                    break;
                                }
                            }
                            catch { }
                        }
                    }

                    // Отправляем актуальный статус в Discord
                    if (pipe != null && pipe.IsConnected)
                    {
                        string det = "Борт: " + currentAircraft + (kills > 0 ? " | Сбито: " + kills : "");
                        string st = "Позывной: " + currentCallsign;
                        string json = "{\"cmd\":\"SET_ACTIVITY\",\"args\":{\"pid\":" + Process.GetCurrentProcess().Id +
                                      ",\"activity\":{\"details\":\"" + det.Replace("\"", "\\\"") +
                                      "\",\"state\":\"" + st.Replace("\"", "\\\"") +
                                      "\",\"timestamps\":{\"start\":" + startEpoch +
                                      "},\"assets\":{\"large_text\":\"VTOL QoL by L.Shutze\"}}},\"nonce\":\"" + Guid.NewGuid() + "\"}";
                        sendPkt(1, json);
                    }
                }
                catch
                {
                    if (pipe != null) { try { pipe.Dispose(); } catch { } pipe = null; }
                }
                Thread.Sleep(2000);
            }
        });
    }

    private CheckBox CreateCheckBox(string text, int top, bool isChecked)
    {
        CheckBox cb = new CheckBox();
        cb.Text = text;
        cb.Location = new Point(24, top);
        cb.Size = new Size(505, 24);
        cb.Checked = isChecked;
        cb.ForeColor = Color.FromArgb(210, 240, 220);
        this.Controls.Add(cb);
        return cb;
    }
    private string FindVtolMusicDir()
    {
        string steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", "") as string ?? "";
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
                    string libPath = m.Result("$1").Replace("\\\\", "\\");
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

        foreach (string dir in searchDirs)
        {
            if (Directory.Exists(dir)) return dir;
        }
        return "";
    }

    private void ExtractResource(string resName, string targetPath)
    {
        if (File.Exists(targetPath)) return;
        using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resName))
        {
            if (stream != null)
            {
                using (FileStream fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write))
                {
                    stream.CopyTo(fs);
                }
            }
        }
    }

    private void UpdateStatus(string text, int progress)
    {
        if (this.InvokeRequired)
        {
            this.Invoke(new Action(() => UpdateStatus(text, progress)));
            return;
        }
        lblStatus.Text = text;
        if (progress >= 0 && progress <= 100)
        {
            progressBar.Value = progress;
        }
    }

    private void SetControlsEnabled(bool enabled)
    {
        if (this.InvokeRequired)
        {
            this.Invoke(new Action(() => SetControlsEnabled(enabled)));
            return;
        }
        btnInstall.Enabled = enabled;
        btnDownloadUrl.Enabled = enabled;
        btnManageMusic.Enabled = enabled;
        if (btnLiveryManager != null) btnLiveryManager.Enabled = enabled;
        if (btnLogbook != null) btnLogbook.Enabled = enabled;
        if (btnSaveVault != null) btnSaveVault.Enabled = enabled;
        pnlDropZone.AllowDrop = enabled;
        btnStopDownload.Enabled = !enabled;

        // Шкала видна только во время активного процесса
        progressBar.Visible = !enabled;
        if (enabled)
        {
            progressBar.Value = 0;
            currentWorkerProcess = null;
        }
    }

    private void BtnStopDownload_Click(object sender, EventArgs e)
    {
        isCancelRequested = true;
        try
        {
            if (currentWorkerProcess != null && !currentWorkerProcess.HasExited)
            {
                currentWorkerProcess.Kill();
            }
        }
        catch { }
        UpdateStatus("Остановка процесса...", 0);
    }

    // === ОБРАБОТКА DRAG & DROP (ФАЙЛЫ, ВИДЕО И ПАПКИ) ===
    private void PnlDropZone_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effect = DragDropEffects.Copy;
            pnlDropZone.BackColor = Color.FromArgb(0, 80, 45);
        }
    }

    private void PnlDropZone_DragDrop(object sender, DragEventArgs e)
    {
        pnlDropZone.BackColor = Color.FromArgb(26, 34, 30);
        string gameDir = txtPath.Text.Trim('"');
        if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
        {
            MessageBox.Show("Сначала укажите папку RadioMusic!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string[] droppedItems = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (droppedItems == null || droppedItems.Length == 0) return;

        SetControlsEnabled(false);
        ThreadPool.QueueUserWorkItem(_ => {
            try
            {
                string ffmpegPath = Path.Combine(gameDir, "ffmpeg.exe");
                ExtractResource("ffmpeg.exe", ffmpegPath);

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
                    {
                        filesToProcess.Add(item);
                    }
                }

                if (filesToProcess.Count == 0)
                {
                    UpdateStatus("Не найдено поддерживаемых аудио/видео файлов.", 0);
                    return;
                }

                string normCache = Path.Combine(gameDir, ".normalized_cache.txt");
                isCancelRequested = false;
                for (int i = 0; i < filesToProcess.Count; i++)
                {
                    if (isCancelRequested)
                    {
                        UpdateStatus("Обработка отменена пользователем.", 0);
                        return;
                    }

                    string src = filesToProcess[i];
                    string baseName = Path.GetFileNameWithoutExtension(src);
                    string outMp3 = Path.Combine(gameDir, baseName + ".mp3");
                    int pct = (int)(((i + 1) / (double)filesToProcess.Count) * 100);

                    UpdateStatus(string.Format("Обработка [{0}/{1}]: {2}...", i + 1, filesToProcess.Count, baseName), pct);

                    if (File.Exists(ffmpegPath) && (chkLoudNorm.Checked || Path.GetExtension(src).ToLower() != ".mp3"))
                    {
                        string afFilter = chkLoudNorm.Checked ? "-af loudnorm=I=-14:TP=-1.5:LRA=11 " : "";
                        string args = string.Format("-y -i \"{0}\" -vn {1}-codec:a libmp3lame -qscale:a 2 \"{2}\"", src, afFilter, outMp3);
                        Process p = Process.Start(new ProcessStartInfo(ffmpegPath, args) { WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true });
                        currentWorkerProcess = p;
                        if (p != null) p.WaitForExit();
                        if (File.Exists(outMp3) && chkLoudNorm.Checked && !isCancelRequested)
                            File.AppendAllText(normCache, baseName + ".mp3\r\n", Encoding.UTF8);
                    }
                    else
                    {
                        File.Copy(src, outMp3, true);
                    }
                }

                if (!isCancelRequested)
                    UpdateStatus(string.Format("Готово! Добавлено и обработано треков: {0}", filesToProcess.Count), 100);
            }
            catch (Exception ex)
            {
                UpdateStatus("Ошибка обработки: " + ex.Message, 0);
            }
            finally
            {
                SetControlsEnabled(true);
            }
        });
    }

    // === ЗАГРУЗКА ПО ССЫЛКЕ (YOUTUBE, SOUNDCLOUD, ОНЛАЙН-РАДИО) ===
    private void BtnDownloadUrl_Click(object sender, EventArgs e)
    {
        string url = txtUrl.Text.Trim();
        string gameDir = txtPath.Text.Trim('"');
        if (string.IsNullOrEmpty(url)) return;
        if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
        {
            MessageBox.Show("Сначала укажите папку RadioMusic!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SetControlsEnabled(false);
        UpdateStatus("Подготовка загрузчика и скачивание по ссылке...", 30);

        ThreadPool.QueueUserWorkItem(_ => {
            try
            {
                string ffmpegPath = Path.Combine(gameDir, "ffmpeg.exe");
                string ytdlpPath = Path.Combine(gameDir, "yt-dlp.exe");
                ExtractResource("ffmpeg.exe", ffmpegPath);
                ExtractResource("yt-dlp.exe", ytdlpPath);

                // Сохраняем ссылку в links.txt для истории
                File.AppendAllText(Path.Combine(gameDir, "links.txt"), url + "\r\n", Encoding.UTF8);

                // Если это прямой поток интернет-радио (.m3u8, .pls, stream) — записываем 15-минутный эфирный блок в MP3
                bool isLiveStream = url.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) || url.EndsWith(".pls", StringComparison.OrdinalIgnoreCase) || url.Contains("stream") || url.Contains("radio");

                isCancelRequested = false;
                if (isLiveStream && File.Exists(ffmpegPath))
                {
                    UpdateStatus("Запись эфира интернет-радио (15 мин) в MP3...", 60);
                    string radioName = "RadioStream_" + DateTime.Now.ToString("HHmmss") + ".mp3";
                    string outRadio = Path.Combine(gameDir, radioName);
                    string args = string.Format("-y -i \"{0}\" -t 900 -af loudnorm=I=-14:TP=-1.5:LRA=11 -codec:a libmp3lame -qscale:a 2 \"{1}\"", url, outRadio);
                    Process p = Process.Start(new ProcessStartInfo(ffmpegPath, args) { WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true });
                    currentWorkerProcess = p;
                    if (p != null) p.WaitForExit();
                }
                else if (File.Exists(ytdlpPath))
                {
                    UpdateStatus("Скачивание аудио через yt-dlp (нажмите СТОП для отмены)...", 60);
                    string archiveFile = Path.Combine(gameDir, "downloaded.txt");
                    string postArgs = chkLoudNorm.Checked ? "--postprocessor-args \"ffmpeg:-af loudnorm=I=-14:TP=-1.5:LRA=11\"" : "";
                    string args = string.Format("-x --audio-format mp3 --ffmpeg-location \"{0}\" {1} --download-archive \"{2}\" -o \"{3}\\%(title)s.%(ext)s\" \"{4}\"", gameDir, postArgs, archiveFile, gameDir, url);
                    Process p = Process.Start(new ProcessStartInfo(ytdlpPath, args) { WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true });
                    currentWorkerProcess = p;
                    if (p != null) p.WaitForExit();
                }

                if (isCancelRequested)
                {
                    UpdateStatus("Загрузка отменена пользователем.", 0);
                }
                else
                {
                    this.Invoke(new Action(() => txtUrl.Text = ""));
                    UpdateStatus("Загрузка по ссылке завершена! Треки добавлены в RadioMusic.", 100);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus("Ошибка загрузки: " + ex.Message, 0);
            }
            finally
            {
                SetControlsEnabled(true);
            }
        });
    }
    // === ТИХАЯ АВТО-УСТАНОВКА И СИНХРОНИЗАЦИЯ ===
    private void SilentSync(bool firstRun)
    {
        string gameDir = txtPath.Text.Trim('"');
        if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
        {
            UpdateStatus("Укажите папку RadioMusic через Обзор...", 0);
            return;
        }

        try
        {
            UpdateStatus("Синхронизация модулей VTOL QoL...", 30);
            ExtractResource("ffmpeg.exe", Path.Combine(gameDir, "ffmpeg.exe"));
            ExtractResource("yt-dlp.exe", Path.Combine(gameDir, "yt-dlp.exe"));

            // Удаляем старую папку из директории игры, чтобы Steam не думал, что игра уже запущена
            string oldGameSubfolder = Path.GetFullPath(Path.Combine(gameDir, @"..\VTOL_QoL"));
            if (Directory.Exists(oldGameSubfolder))
            {
                try { Directory.Delete(oldGameSubfolder, true); } catch { }
            }

            // Устанавливаем программу в AppData\Local\VTOL_QoL (вне папки Steam)
            string appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL");
            Directory.CreateDirectory(appFolder);
            string installedExe = Path.Combine(appFolder, "VTOL_QoL.exe");
            string currentExe = Process.GetCurrentProcess().MainModule.FileName;
            if (!string.Equals(currentExe, installedExe, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(currentExe, installedExe, true);
                CreateDesktopShortcut(installedExe, gameDir);
            }

            string batPath = Path.Combine(gameDir, "Shuffle.bat");
            string ps1Path = Path.Combine(gameDir, "SmartShuffle.ps1");

            File.WriteAllText(batPath, "@echo off\r\ncd /d \"%~dp0\"\r\nstart \"VTOL_QoL_Bg\" /min powershell -WindowStyle Hidden -ExecutionPolicy Bypass -File \"%~dp0SmartShuffle.ps1\"\r\n", Encoding.ASCII);

            bool shuf = false, norm = false, disc = false, duck = false, vrAudio = false;
            this.Invoke(new Action(() => {
                shuf = chkShuffle.Checked;
                norm = chkLoudNorm.Checked;
                disc = chkDiscord.Checked;
                duck = chkDucking.Checked;
                vrAudio = chkAudioSwitch.Checked;
            }));

            string ps1Code = BuildPowerShellEngine(shuf, norm, disc, duck, vrAudio);
            File.WriteAllText(ps1Path, ps1Code, Encoding.UTF8);

            if (firstRun)
            {
                ConfigureSteam(batPath);
            }

            UpdateStatus("VTOL QoL готов к работе.", 100);
        }
        catch (Exception ex)
        {
            UpdateStatus("Ошибка синхронизации: " + ex.Message, 0);
        }
    }

    private void ConfigureSteam(string batPath)
    {
        string steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", "") as string ?? "";
        steamPath = steamPath.Replace('/', '\\');
        if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(Path.Combine(steamPath, "userdata"))) return;

        string escapedLaunch = ("cmd /c \"\"" + batPath + "\" & %command%\"").Replace("\\", "\\\\").Replace("\"", "\\\"");
        string[] vdfFiles = Directory.GetFiles(Path.Combine(steamPath, "userdata"), "localconfig.vdf", SearchOption.AllDirectories);

        // Проверяем, нужно ли вообще менять конфиг Steam
        bool needsUpdate = false;
        foreach (string vdf in vdfFiles)
        {
            string text = File.ReadAllText(vdf, Encoding.UTF8);
            if (Regex.IsMatch(text, "\"667970\"\\s*\\{") && !text.Contains(escapedLaunch))
            {
                needsUpdate = true;
                break;
            }
        }

        // Если строчка уже прописана в Steam — выходим, не перезапуская клиент!
        if (!needsUpdate) return;

        bool steamWasRunning = false;
        Process[] steamProcs = Process.GetProcessesByName("steam");
        if (steamProcs.Length > 0)
        {
            steamWasRunning = true;
            try
            {
                // Запрашиваем штатное завершение работы Steam
                Process.Start(new ProcessStartInfo("steam://exit") { UseShellExecute = true });
            }
            catch { }

            // Ждём мягкого завершения до 10 секунд
            DateTime waitDeadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < waitDeadline)
            {
                if (Process.GetProcessesByName("steam").Length == 0) break;
                Thread.Sleep(500);
            }

            // Если процесс всё ещё висит, только тогда принудительно гасим
            foreach (var p in Process.GetProcessesByName("steam"))
            {
                try { p.Kill(); } catch { }
            }
            Thread.Sleep(1000);
        }

        foreach (string vdf in vdfFiles)
        {
            string text = File.ReadAllText(vdf, Encoding.UTF8);
            Match appMatch = Regex.Match(text, "\"667970\"\\s*\\{");
            if (appMatch.Success)
            {
                File.Copy(vdf, vdf + ".bak", true);
                int startIdx = appMatch.Index;
                int braceCount = 0;
                int endIdx = -1;
                for (int i = startIdx; i < text.Length; i++)
                {
                    if (text[i] == '{') braceCount++;
                    else if (text[i] == '}')
                    {
                        braceCount--;
                        if (braceCount == 0) { endIdx = i; break; }
                    }
                }
                if (endIdx > startIdx)
                {
                    string appBlock = text.Substring(startIdx, endIdx - startIdx + 1);
                    if (Regex.IsMatch(appBlock, "\"LaunchOptions\"\\s*\""))
                        appBlock = Regex.Replace(appBlock, "(\"LaunchOptions\"\\s*\")[^\"]*(\")", "${1}" + escapedLaunch + "${2}");
                    else
                        appBlock = Regex.Replace(appBlock, "(\"667970\"\\s*\\{)", "\"667970\"\n\t\t\t\t\t{\n\t\t\t\t\t\t\"LaunchOptions\"\t\t\"" + escapedLaunch + "\"");
                    text = text.Substring(0, startIdx) + appBlock + text.Substring(endIdx + 1);
                    File.WriteAllText(vdf, text, new UTF8Encoding(false));
                }
            }
        }

        if (steamWasRunning && File.Exists(Path.Combine(steamPath, "steam.exe")))
            Process.Start(Path.Combine(steamPath, "steam.exe"));
    }

    private void CreateDesktopShortcut(string installedExe, string gameDir)
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        string lnkPath = Path.Combine(desktop, "VTOL QoL.lnk");
        string exeIcon = installedExe;
        string psShortcut = string.Format(
            "$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut('{0}'); $s.TargetPath = '{1}'; $s.WorkingDirectory = '{2}'; if (Test-Path '{3}') {{ $s.IconLocation = '{3}' }}; $s.Save()",
            lnkPath.Replace("'", "''"), installedExe.Replace("'", "''"), Path.GetDirectoryName(installedExe).Replace("'", "''"), exeIcon.Replace("'", "''")
        );
        string encodedPs = Convert.ToBase64String(Encoding.Unicode.GetBytes(psShortcut));
        Process p = Process.Start(new ProcessStartInfo("powershell", "-NoProfile -EncodedCommand " + encodedPs) { WindowStyle = ProcessWindowStyle.Hidden });
        if (p != null) p.WaitForExit();
    }

    private string BuildPowerShellEngine(bool useShuffle, bool useLoudNorm, bool useDiscord, bool useDucking, bool useAudioSwitch)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("Get-CimInstance Win32_Process -Filter \"Name = 'powershell.exe' AND CommandLine LIKE '%SmartShuffle.ps1%'\" | Where-Object { $_.ProcessId -ne $PID } | Invoke-CimMethod -MethodName Terminate | Out-Null");
        sb.AppendLine("$musicDir = $PSScriptRoot");
        sb.AppendLine("$vtolAppData = \"$env:APPDATA\\Boundless Dynamics, LLC\\VTOLVR\\SaveData\"");
        sb.AppendLine("$logPath = \"$env:USERPROFILE\\AppData\\LocalLow\\Boundless Dynamics, LLC\\VTOLVR\\Player.log\"");
        sb.AppendLine("$currentAircraft = 'В ангаре / Меню'");
        sb.AppendLine("$currentCallsign = 'В ангаре / Меню'");
        sb.AppendLine("$killsCount = 0");
        sb.AppendLine("$startEpoch = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()");


        if (useLoudNorm)
        {
            sb.AppendLine("$ffmpeg = Join-Path $musicDir 'ffmpeg.exe'");
            sb.AppendLine("$normCache = Join-Path $musicDir '.normalized_cache.txt'");
            sb.AppendLine("if (Test-Path $ffmpeg) {");
            sb.AppendLine("    $otherAudio = @(Get-ChildItem -Path $musicDir -File | Where-Object { $_.Extension -match '^\\.(flac|wav|ogg|m4a|aac|wma|opus)$' })");
            sb.AppendLine("    foreach ($track in $otherAudio) {");
            sb.AppendLine("        $outMp3 = Join-Path $musicDir \"$($track.BaseName).mp3\"");
            sb.AppendLine("        Start-Process -FilePath $ffmpeg -ArgumentList \"-y -i `\"$($track.FullName)`\" -af loudnorm=I=-14:TP=-1.5:LRA=11 -codec:a libmp3lame -qscale:a 2 `\"$outMp3`\"\" -WindowStyle Hidden -Wait");
            sb.AppendLine("        if (Test-Path $outMp3) { Remove-Item -LiteralPath $track.FullName -Force -ErrorAction SilentlyContinue; Add-Content -Path $normCache -Value \"$($track.BaseName).mp3\" }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
        }

        sb.AppendLine("$myNick = ''");
        sb.AppendLine("$pilotsFile = Join-Path $vtolAppData 'pilots.cfg'");
        sb.AppendLine("if (Test-Path $pilotsFile) {");
        sb.AppendLine("    $content = Get-Content $pilotsFile -Raw -Encoding UTF8");
        sb.AppendLine("    if ($content -match '(?m)^(?:lastPilot|pilotName)\\s*=\\s*(.+)$') { $myNick = [regex]::Escape($matches.Item(1).Trim()) }");
        sb.AppendLine("}");
        if (useDiscord)
        {
            sb.AppendLine(@"
$discordPipe = $null

function Send-DiscordPacket([int]$opcode, [string]$jsonStr, [bool]$waitReply) {
    try {
        if ($null -eq $script:discordPipe -or -not $script:discordPipe.IsConnected) { return }
        $jsonBytes = [System.Text.Encoding]::UTF8.GetBytes($jsonStr)
        $opBytes = [System.BitConverter]::GetBytes($opcode)
        $lenBytes = [System.BitConverter]::GetBytes([int]$jsonBytes.Length)
        [byte[]]$packet = New-Object byte[] (8 + $jsonBytes.Length)
        [System.Array]::Copy($opBytes, 0, $packet, 0, 4)
        [System.Array]::Copy($lenBytes, 0, $packet, 4, 4)
        [System.Array]::Copy($jsonBytes, 0, $packet, 8, $jsonBytes.Length)
        $script:discordPipe.Write($packet, 0, $packet.Length)
        $script:discordPipe.Flush()
        if ($waitReply) {
            $buf = New-Object byte[] 2048
            $null = $script:discordPipe.Read($buf, 0, $buf.Length)
        }
    } catch {
        try { if ($script:discordPipe) { $script:discordPipe.Dispose() } } catch {}
        $script:discordPipe = $null
    }
}

function Update-DiscordRPC($details, $state) {
    try {
        if ($null -eq $script:discordPipe -or -not $script:discordPipe.IsConnected) {
            for ($i = 0; $i -lt 10; $i++) {
                try {
                    $p = New-Object System.IO.Pipes.NamedPipeClientStream('.', ""discord-ipc-$i"", [System.IO.Pipes.PipeDirection]::InOut)
                    $p.Connect(150)
                    if ($p.IsConnected) {
                        $script:discordPipe = $p
                        $hsObj = @{ v = 1; client_id = '1573385169807872222' } | ConvertTo-Json -Compress
                        Send-DiscordPacket 0 $hsObj $true
                        break
                    }
                } catch {}
            }
        }
            if ($null -ne $script:discordPipe -and $script:discordPipe.IsConnected) {
         $vProc = Get-Process -Name 'VTOLVR' -ErrorAction SilentlyContinue | Select-Object -First 1
         $targetPid = if ($vProc) { $vProc.Id } else { $PID }
         $payload = @{
             cmd = 'SET_ACTIVITY'
             args = @{
                 pid = $targetPid
                 activity = @{
                     details = [string]$details
                     state = [string]$state
                     timestamps = @{ start = [long]$script:startEpoch }
                     assets = @{
                         large_text = 'VTOL QoL by L.Shutze'
                     }
                 }
             }
             nonce = [guid]::NewGuid().ToString()
         } | ConvertTo-Json -Depth 5 -Compress
         Send-DiscordPacket 1 $payload $false
     }
    } catch {}
}");
        }

        if (useAudioSwitch)
        {
            sb.AppendLine(@"
function Switch-VRAudio($toVR) {
    try {
        $vrDevicePatterns = 'Oculus|SteamVR|Virtual Desktop|VIVE|Index|Pico|VR'
        $audioModule = Get-Module -ListAvailable -Name AudioDeviceCmdlets
        if ($audioModule) {
            Import-Module AudioDeviceCmdlets -ErrorAction SilentlyContinue
            if ($toVR) {
                $vrDev = Get-AudioDevice -List | Where-Object { $_.Type -eq 'Playback' -and $_.Name -match $vrDevicePatterns } | Select-Object -First 1
                if ($vrDev) { Set-AudioDevice -Index $vrDev.Index | Out-Null }
                $vrMic = Get-AudioDevice -List | Where-Object { $_.Type -eq 'Recording' -and $_.Name -match $vrDevicePatterns } | Select-Object -First 1
                if ($vrMic) { Set-AudioDevice -Index $vrMic.Index | Out-Null }
            }
        }
    } catch {}
}
Switch-VRAudio $true
");
        }

        sb.AppendLine(@"
$logbookDir = Join-Path $env:LOCALAPPDATA 'VTOL_QoL'
if (!(Test-Path $logbookDir)) { try { New-Item -ItemType Directory -Path $logbookDir -Force | Out-Null } catch {} }
$logbookFile = Join-Path $logbookDir 'flight_logbook.txt'
$script:sortieStart = Get-Date
$script:sortieKills = 0
$script:inSortie = $false

function Get-FlightDurationStr {
    $span = (Get-Date) - $script:sortieStart
    return ('{0:D2}:{1:D2}' -f [int]$span.TotalMinutes, $span.Seconds)
}

function Record-FlightLog($action, $aircraft, $callsign, $dur, $kills, $note) {
    try {
        $timestamp = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
        $entry = ""$timestamp|$action|$aircraft|$callsign|$dur|$kills|$note""
        Add-Content -Path $logbookFile -Value $entry -Encoding UTF8
    } catch {}
}
Record-FlightLog 'SESSION' 'В ангаре / Меню' '—' '00:00' 0 'Запуск VTOL VR'
");
        if (useDucking)
        {
            sb.AppendLine(@"
$script:isDucked = $false
function Check-RadioDucking {
    try {
        $micKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone\NonPackaged'
        $voiceActive = $false
        if (Test-Path $micKey) {
            $apps = Get-ChildItem -Path $micKey -ErrorAction SilentlyContinue
            foreach ($app in $apps) {
                if ($app.PSChildName -match 'Discord|ts3client|VTOLVR') {
                    $stopTime = (Get-ItemProperty -Path $app.PSPath -Name 'LastUsedTimeStop' -ErrorAction SilentlyContinue).LastUsedTimeStop
                    if ($stopTime -eq 0) { $voiceActive = $true; break }
                }
            }
        }
        if ($voiceActive -and !$script:isDucked) {
            $script:isDucked = $true
            (New-Object -ComObject WScript.Shell).SendKeys([char]174)
        } elseif (!$voiceActive -and $script:isDucked) {
            $script:isDucked = $false
            (New-Object -ComObject WScript.Shell).SendKeys([char]175)
        }
    } catch {}
}");
        }

        sb.AppendLine(@"
function Shuffle-Music($doRename) {
    if ($doRename) {
        $allMp3 = @(Get-ChildItem -Path $musicDir -Filter '*.mp3' -File -ErrorAction SilentlyContinue | Sort-Object { Get-Random })
        $idx = 1
        foreach ($file in $allMp3) {
            $clean = $file.Name -replace '^\d{4}_', ''
            $newName = ('{0:D4}_{1}' -f $idx, $clean)
            $idx++
            if ($file.Name -ne $newName) {
                try { Rename-Item -LiteralPath $file.FullName -NewName $newName -ErrorAction Stop } catch {}
            }
        }
    }
    if (Test-Path Function:\Update-DiscordRPC) {
        $killStr = if ($script:killsCount -gt 0) { "" | Сбито: $script:killsCount"" } else { '' }
        Update-DiscordRPC ""Борт: $script:currentAircraft$killStr"" ""Позывной: $script:currentCallsign""
    }
}
");
        sb.AppendLine("Shuffle-Music " + (useShuffle ? "$true" : "$false"));

        sb.AppendLine(@"
for ($w = 0; $w -lt 80; $w++) {
    if (Get-Process -Name 'VTOLVR' -ErrorAction SilentlyContinue) { break }
    Start-Sleep -Milliseconds 500
}
Start-Sleep -Seconds 3
Shuffle-Music $false

if (Test-Path $logPath) {
    $fs = New-Object System.IO.FileStream($logPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    $sr = New-Object System.IO.StreamReader($fs)
    $sr.BaseStream.Seek(0, [System.IO.SeekOrigin]::End) | Out-Null

    while ($true) {
        if (-not (Get-Process -Name 'VTOLVR' -ErrorAction SilentlyContinue)) { break }
        if ($fs.Length -lt $sr.BaseStream.Position) {
            $sr.BaseStream.Seek(0, [System.IO.SeekOrigin]::Begin) | Out-Null
            $sr.DiscardBufferedData()
        }
        $chunk = $sr.ReadToEnd()
        if (-not [string]::IsNullOrEmpty($chunk)) {");

        if (useDiscord)
        {
            sb.AppendLine(@"
            $csPattern = '((?:Alpha|Bravo|Charlie|Delta|Echo|Foxtrot|Golf|Hotel|India|Juliet|Kilo|Lima|Mike|November|Oscar|Papa|Quebec|Romeo|Sierra|Tango|Uniform|Victor|Whiskey|X-?ray|Yankee|Zulu)\s+\d+-\d+)'
            $planePattern = '(F/?A-26B|EF-24G|AV-42C|F-45A|AH-94|T-55)'
            $updated = $false

            if ($myNick -ne '' -and $chunk -match ""$myNick[^\r\n]*?$csPattern"") {
                $script:currentCallsign = $matches.Item(1)
                $updated = $true
            } elseif ($myNick -eq '' -and $chunk -match $csPattern) {
                $script:currentCallsign = $matches.Item(1)
                $updated = $true
            }

            if ($script:currentCallsign -ne 'В ангаре / Меню' -and $chunk -match $planePattern) {
                $newPlane = $matches.Item(1)
                if (-not $script:inSortie -or $script:currentAircraft -ne $newPlane) {
                    $script:currentAircraft = $newPlane
                    $script:inSortie = $true
                    $script:sortieStart = Get-Date
                    $script:sortieKills = 0
                    Record-FlightLog 'SORTIE' $script:currentAircraft $script:currentCallsign '00:00' 0 'Занятие слота и вылет'
                }
                $updated = $true
            }

            if ($myNick -ne '' -and $chunk -match ""([^\r\n(]+)\s+was killed by $myNick"") {
                $victim = $matches.Item(1).Trim()
                $script:killsCount++
                $script:sortieKills++
                Record-FlightLog 'KILL' $script:currentAircraft $script:currentCallsign (Get-FlightDurationStr) $script:sortieKills ""Уничтожен: $victim""
                $updated = $true
            }
            if ($updated) { Shuffle-Music $false }");
        }
        if (useShuffle)
        {
            sb.AppendLine(@"
            $hit = if ($myNick -ne '') { $chunk -match ""\($myNick\) ejected|\($myNick\) was killed"" } else { $chunk -match 'ejected|was killed' }
            if ($hit) {
                $evType = if ($chunk -match 'ejected') { 'EJECT' } else { 'KIA' }
                $evNote = if ($evType -eq 'EJECT') { 'Катапультирование' } else { 'Борт сбит / Потерян' }
                Record-FlightLog $evType $script:currentAircraft $script:currentCallsign (Get-FlightDurationStr) $script:sortieKills $evNote
                $script:inSortie = $false
                $script:currentAircraft = 'В ангаре / Меню'
                $script:currentCallsign = 'В ангаре / Меню'
                Start-Sleep -Milliseconds 2500
                Shuffle-Music $true
                $sr.BaseStream.Seek(0, [System.IO.SeekOrigin]::End) | Out-Null
                $sr.DiscardBufferedData()
            }");
        }

        sb.AppendLine(@"
        }
             if (Test-Path Function:\Check-RadioDucking) { Check-RadioDucking }
     $script:rpcTick++
     if ($script:rpcTick -ge 40) { $script:rpcTick = 0; Shuffle-Music $false }
     Start-Sleep -Milliseconds 250
    }
    if ($script:inSortie) {
        Record-FlightLog 'LANDED' $script:currentAircraft $script:currentCallsign (Get-FlightDurationStr) $script:sortieKills 'Завершение вылета'
    }
    $sr.Close()
    $fs.Close()
}");

        return sb.ToString();
    }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}
// =========================================================================
// === ВСТРОЕННЫЙ МЕНЕДЖЕР ТРЕКОВ И ПЛЕЙЛИСТОВ (БЕЗ ЗАХОДА В ПАПКУ) =======
// =========================================================================
public class MusicManagerForm : Form
{
    private string musicDir;
    private string currentActiveSubdir = "";
    private ListBox lstTracks;
    private ComboBox cmbPlaylists;
    private TextBox txtSearch;
    private Label lblTrackCount;
    private Button btnPlayPause;
    private Button btnDelete;
    private Button btnRename;
    private Button btnNewPlaylist;
    private Button btnMoveToPlaylist;
    private Button btnRefresh;
    private Button btnManualShuffle;
    private List<string> allFiles = new List<string>();
    private Process currentAudioPlayer = null;

    public MusicManagerForm(string dir)
    {
        this.musicDir = dir;
        this.Text = "VTOL QoL — Менеджер треков и плейлистов";
        this.Size = new Size(690, 560);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.BackColor = Color.FromArgb(18, 22, 20);
        this.ForeColor = Color.FromArgb(0, 255, 102);
        this.Font = new Font("Consolas", 9.5f, FontStyle.Regular);

        Label lblPl = new Label();
        lblPl.Text = "Плейлист / Раздел:";
        lblPl.Location = new Point(20, 15);
        lblPl.AutoSize = true;
        lblPl.ForeColor = Color.LightGray;
        this.Controls.Add(lblPl);

        cmbPlaylists = new ComboBox();
        cmbPlaylists.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbPlaylists.Location = new Point(20, 35);
        cmbPlaylists.Size = new Size(340, 24);
        cmbPlaylists.BackColor = Color.FromArgb(30, 36, 33);
        cmbPlaylists.ForeColor = Color.White;
        cmbPlaylists.SelectedIndexChanged += (s, e) => {
            currentActiveSubdir = cmbPlaylists.SelectedItem as string ?? "";
            LoadTracks();
        };
        this.Controls.Add(cmbPlaylists);

        btnNewPlaylist = new Button();
        btnNewPlaylist.Text = "+ Новый раздел";
        btnNewPlaylist.Location = new Point(370, 34);
        btnNewPlaylist.Size = new Size(130, 26);
        btnNewPlaylist.FlatStyle = FlatStyle.Flat;
        btnNewPlaylist.BackColor = Color.FromArgb(0, 90, 70);
        btnNewPlaylist.ForeColor = Color.White;
        btnNewPlaylist.Click += BtnNewPlaylist_Click;
        this.Controls.Add(btnNewPlaylist);

        btnRefresh = new Button();
        btnRefresh.Text = "Обновить";
        btnRefresh.Location = new Point(510, 34);
        btnRefresh.Size = new Size(145, 26);
        btnRefresh.FlatStyle = FlatStyle.Flat;
        btnRefresh.BackColor = Color.FromArgb(40, 50, 45);
        btnRefresh.ForeColor = Color.White;
        btnRefresh.Click += (s, e) => { RefreshPlaylists(); LoadTracks(); };
        this.Controls.Add(btnRefresh);

        Label lblSearch = new Label();
        lblSearch.Text = "Поиск трека:";
        lblSearch.Location = new Point(20, 75);
        lblSearch.AutoSize = true;
        lblSearch.ForeColor = Color.LightGray;
        this.Controls.Add(lblSearch);

        txtSearch = new TextBox();
        txtSearch.Location = new Point(120, 72);
        txtSearch.Size = new Size(380, 24);
        txtSearch.BackColor = Color.FromArgb(30, 36, 33);
        txtSearch.ForeColor = Color.Cyan;
        txtSearch.BorderStyle = BorderStyle.FixedSingle;
        txtSearch.TextChanged += (s, e) => FilterTracks();
        this.Controls.Add(txtSearch);

        lblTrackCount = new Label();
        lblTrackCount.Text = "Треков: 0";
        lblTrackCount.Location = new Point(510, 75);
        lblTrackCount.AutoSize = true;
        lblTrackCount.ForeColor = Color.Silver;
        this.Controls.Add(lblTrackCount);

        lstTracks = new ListBox();
        lstTracks.Location = new Point(20, 105);
        lstTracks.Size = new Size(480, 390);
        lstTracks.BackColor = Color.FromArgb(24, 28, 26);
        lstTracks.ForeColor = Color.White;
        lstTracks.BorderStyle = BorderStyle.FixedSingle;
        lstTracks.Font = new Font("Consolas", 9f);
        lstTracks.DoubleClick += (s, e) => PlaySelectedTrack();
        this.Controls.Add(lstTracks);

        // Панель кнопок справа
        int btnTop = 105;
        int btnW = 145;
        int btnH = 34;
        int gap = 8;

        btnPlayPause = CreateActionButton("Прослушать", ref btnTop, btnW, btnH, gap, Color.FromArgb(0, 110, 140));
        btnPlayPause.Click += (s, e) => PlaySelectedTrack();

        btnRename = CreateActionButton("Переименовать", ref btnTop, btnW, btnH, gap, Color.FromArgb(50, 60, 55));
        btnRename.Click += BtnRename_Click;

        btnDelete = CreateActionButton("Удалить трек", ref btnTop, btnW, btnH, gap, Color.FromArgb(140, 30, 30));
        btnDelete.Click += BtnDelete_Click;

        btnMoveToPlaylist = CreateActionButton("Переместить...", ref btnTop, btnW, btnH, gap, Color.FromArgb(60, 70, 65));
        btnMoveToPlaylist.Click += BtnMoveToPlaylist_Click;

        btnManualShuffle = CreateActionButton("Перемешать порядок", ref btnTop, btnW, btnH, gap, Color.FromArgb(110, 80, 0));
        btnManualShuffle.Click += BtnManualShuffle_Click;

        Button btnOpenInExplorer = CreateActionButton("Открыть в Проводнике", ref btnTop, btnW, btnH, gap, Color.FromArgb(35, 45, 40));
        btnOpenInExplorer.Click += (s, e) => {
            string cur = GetCurrentDir();
            if (Directory.Exists(cur)) Process.Start("explorer.exe", cur);
        };

        this.FormClosing += (s, e) => StopPlayback();

        RefreshPlaylists();
        LoadTracks();
    }

    private Button CreateActionButton(string text, ref int top, int width, int height, int gap, Color backColor)
    {
        Button btn = new Button();
        btn.Text = text;
        btn.Location = new Point(510, top);
        btn.Size = new Size(width, height);
        btn.FlatStyle = FlatStyle.Flat;
        btn.BackColor = backColor;
        btn.ForeColor = Color.White;
        this.Controls.Add(btn);
        top += height + gap;
        return btn;
    }

    private string GetCurrentDir()
    {
        if (string.IsNullOrEmpty(currentActiveSubdir) || currentActiveSubdir == "[Основная папка RadioMusic]")
            return musicDir;
        return Path.Combine(musicDir, currentActiveSubdir);
    }

    private void RefreshPlaylists()
    {
        cmbPlaylists.Items.Clear();
        cmbPlaylists.Items.Add("[Основная папка RadioMusic]");
        if (Directory.Exists(musicDir))
        {
            foreach (string sub in Directory.GetDirectories(musicDir))
            {
                string dirName = Path.GetFileName(sub);
                if (!dirName.StartsWith(".") && !dirName.Equals("VTOL_QoL", StringComparison.OrdinalIgnoreCase))
                {
                    cmbPlaylists.Items.Add(dirName);
                }
            }
        }
        cmbPlaylists.SelectedIndex = 0;
    }

    private void LoadTracks()
    {
        allFiles.Clear();
        string targetDir = GetCurrentDir();
        if (Directory.Exists(targetDir))
        {
            string[] files = Directory.GetFiles(targetDir, "*.mp3", SearchOption.TopDirectoryOnly);
            Array.Sort(files);
            allFiles.AddRange(files);
        }
        FilterTracks();
    }

    private void FilterTracks()
    {
        lstTracks.Items.Clear();
        string q = txtSearch.Text.Trim().ToLower();
        foreach (string f in allFiles)
        {
            string name = Path.GetFileName(f);
            if (string.IsNullOrEmpty(q) || name.ToLower().Contains(q))
            {
                lstTracks.Items.Add(name);
            }
        }
        lblTrackCount.Text = "Треков: " + lstTracks.Items.Count;
    }

    private void PlaySelectedTrack()
    {
        if (lstTracks.SelectedItem == null) return;
        string fName = lstTracks.SelectedItem.ToString();
        string fullPath = Path.Combine(GetCurrentDir(), fName);
        if (!File.Exists(fullPath)) return;

        StopPlayback();
        try
        {
            currentAudioPlayer = Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
        }
        catch { }
    }

    private void StopPlayback()
    {
        try
        {
            if (currentAudioPlayer != null && !currentAudioPlayer.HasExited)
            {
                currentAudioPlayer.Kill();
            }
        }
        catch { }
        finally
        {
            currentAudioPlayer = null;
        }
    }

    private void BtnRename_Click(object sender, EventArgs e)
    {
        if (lstTracks.SelectedItem == null)
        {
            MessageBox.Show("Выберите трек для переименования.", "Внимание", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        string oldName = lstTracks.SelectedItem.ToString();
        string curDir = GetCurrentDir();
        string oldPath = Path.Combine(curDir, oldName);

        string input = PromptInput("Новое имя файла:", "Переименование трека", oldName);
        if (!string.IsNullOrWhiteSpace(input) && input != oldName)
        {
            if (!input.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)) input += ".mp3";
            string newPath = Path.Combine(curDir, input);
            try
            {
                File.Move(oldPath, newPath);
                LoadTracks();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка переименования: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void BtnDelete_Click(object sender, EventArgs e)
    {
        if (lstTracks.SelectedItem == null)
        {
            MessageBox.Show("Выберите трек для удаления.", "Внимание", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        string oldName = lstTracks.SelectedItem.ToString();
        string curDir = GetCurrentDir();
        string oldPath = Path.Combine(curDir, oldName);

        if (MessageBox.Show(string.Format("Удалить файл \"{0}\"?", oldName), "Подтверждение удаления", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            try
            {
                File.Delete(oldPath);
                LoadTracks();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка удаления: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void BtnNewPlaylist_Click(object sender, EventArgs e)
    {
        string name = PromptInput("Имя нового плейлиста / подпапки:", "Создать плейлист", "MyPlaylist");
        if (!string.IsNullOrWhiteSpace(name))
        {
            string newDir = Path.Combine(musicDir, name);
            try
            {
                if (!Directory.Exists(newDir))
                {
                    Directory.CreateDirectory(newDir);
                    RefreshPlaylists();
                    cmbPlaylists.SelectedItem = name;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось создать папку: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void BtnMoveToPlaylist_Click(object sender, EventArgs e)
    {
        if (lstTracks.SelectedItem == null)
        {
            MessageBox.Show("Выберите трек для перемещения.", "Внимание", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        string fileName = lstTracks.SelectedItem.ToString();
        string srcPath = Path.Combine(GetCurrentDir(), fileName);

        using (Form prompt = new Form())
        {
            prompt.Width = 360;
            prompt.Height = 180;
            prompt.FormBorderStyle = FormBorderStyle.FixedDialog;
            prompt.Text = "Переместить трек";
            prompt.StartPosition = FormStartPosition.CenterParent;
            prompt.BackColor = Color.FromArgb(20, 24, 22);
            prompt.ForeColor = Color.White;

            Label textLabel = new Label() { Left = 20, Top = 15, Text = "Выберите целевой раздел:", AutoSize = true };
            ComboBox cmb = new ComboBox() { Left = 20, Top = 40, Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var item in cmbPlaylists.Items) cmb.Items.Add(item);
            cmb.SelectedIndex = 0;

            Button confirmation = new Button() { Text = "Переместить", Left = 190, Width = 130, Top = 80, DialogResult = DialogResult.OK, BackColor = Color.FromArgb(0, 110, 140), FlatStyle = FlatStyle.Flat, ForeColor = Color.White };
            prompt.Controls.Add(textLabel);
            prompt.Controls.Add(cmb);
            prompt.Controls.Add(confirmation);
            prompt.AcceptButton = confirmation;

            if (prompt.ShowDialog() == DialogResult.OK)
            {
                string targetSub = cmb.SelectedItem as string;
                string targetDir = (targetSub == "[Основная папка RadioMusic]") ? musicDir : Path.Combine(musicDir, targetSub);
                string dstPath = Path.Combine(targetDir, fileName);
                try
                {
                    if (File.Exists(dstPath))
                    {
                        MessageBox.Show("Файл с таким именем уже существует в выбранном плейлисте.", "Внимание", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    File.Move(srcPath, dstPath);
                    LoadTracks();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Ошибка: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }

    public void TriggerShuffle()
    {
        BtnManualShuffle_Click(this, EventArgs.Empty);
    }

    private void BtnManualShuffle_Click(object sender, EventArgs e)
    {
        string curDir = GetCurrentDir();
        if (!Directory.Exists(curDir)) return;

        try
        {
            string[] files = Directory.GetFiles(curDir, "*.mp3");
            if (files.Length == 0) return;

            Random rnd = new Random();
            List<string> shuffled = new List<string>(files);
            for (int i = shuffled.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                string temp = shuffled[i];
                shuffled[i] = shuffled[j];
                shuffled[j] = temp;
            }

            for (int i = 0; i < shuffled.Count; i++)
            {
                string oldPath = shuffled[i];
                string name = Path.GetFileName(oldPath);
                string cleanName = Regex.Replace(name, @"^\d{4}_", "");
                string newName = string.Format("{0:D4}_{1}", i + 1, cleanName);
                string newPath = Path.Combine(curDir, newName);

                if (!string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Move(oldPath, newPath); } catch { }
                }
            }

            LoadTracks();
            MessageBox.Show("Треки успешно перемешаны и пронумерованы!", "Шаффл завершен", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Ошибка шаффла: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private string PromptInput(string text, string caption, string defaultValue = "")
    {
        Form prompt = new Form()
        {
            Width = 420,
            Height = 160,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            Text = caption,
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Color.FromArgb(20, 24, 22),
            ForeColor = Color.White
        };
        Label lbl = new Label() { Left = 20, Top = 15, Text = text, AutoSize = true };
        TextBox txt = new TextBox() { Left = 20, Top = 40, Width = 360, Text = defaultValue, BackColor = Color.FromArgb(30, 36, 33), ForeColor = Color.White };
        Button ok = new Button() { Text = "OK", Left = 270, Width = 110, Top = 75, DialogResult = DialogResult.OK, BackColor = Color.FromArgb(0, 110, 140), FlatStyle = FlatStyle.Flat, ForeColor = Color.White };
        prompt.Controls.Add(lbl);
        prompt.Controls.Add(txt);
        prompt.Controls.Add(ok);
        prompt.AcceptButton = ok;
        return prompt.ShowDialog() == DialogResult.OK ? txt.Text.Trim() : "";
    }
}
// =========================================================================
// === 1. МЕНЕДЖЕР КАСТОМНЫХ ЛИВРЕЙ / КАМУФЛЯЖЕЙ (LIVERY MANAGER) ==========
// =========================================================================
public class LiveryManagerForm : Form
{
    private string gameRoot;
    private ComboBox cmbAircraft;
    private ListBox lstLiveries;
    private Label lblStatus;
    private readonly string[] aircrafts = new string[] { "Все самолеты", "FA-26B", "F-45A", "AV-42C", "AH-94", "T-55", "EF-24G" };

    public LiveryManagerForm(string root)
    {
        this.gameRoot = root;
        this.Text = "VTOL QoL — Менеджер кастомных ливрей (CustomLiveries)";
        this.Size = new Size(640, 520);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.BackColor = Color.FromArgb(18, 22, 20);
        this.ForeColor = Color.FromArgb(0, 255, 102);
        this.Font = new Font("Consolas", 9.5f, FontStyle.Regular);

        Label lblAc = new Label() { Text = "Фильтр по летательному аппарату:", Left = 20, Top = 15, AutoSize = true, ForeColor = Color.LightGray };
        this.Controls.Add(lblAc);

        cmbAircraft = new ComboBox();
        cmbAircraft.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbAircraft.Location = new Point(20, 38);
        cmbAircraft.Size = new Size(340, 26);
        cmbAircraft.BackColor = Color.FromArgb(30, 36, 33);
        cmbAircraft.ForeColor = Color.Cyan;
        foreach (string ac in aircrafts) cmbAircraft.Items.Add(ac);
        cmbAircraft.SelectedIndex = 0;
        cmbAircraft.SelectedIndexChanged += (s, e) => LoadLiveries();
        this.Controls.Add(cmbAircraft);

        Button btnRefresh = new Button() { Text = "Обновить", Left = 370, Top = 37, Width = 110, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(40, 50, 45), ForeColor = Color.White };
        btnRefresh.Click += (s, e) => LoadLiveries();
        this.Controls.Add(btnRefresh);

        Label lblList = new Label() { Text = "Установленные ливреи (VTOL VR\\CustomLiveries):", Left = 20, Top = 80, AutoSize = true, ForeColor = Color.LightGray };
        this.Controls.Add(lblList);

        lstLiveries = new ListBox();
        lstLiveries.Location = new Point(20, 105);
        lstLiveries.Size = new Size(425, 330);
        lstLiveries.BackColor = Color.FromArgb(24, 28, 26);
        lstLiveries.ForeColor = Color.White;
        lstLiveries.BorderStyle = BorderStyle.FixedSingle;
        this.Controls.Add(lstLiveries);

        int top = 105;
        int btnW = 155;
        int btnH = 36;
        int gap = 10;

        Button btnInstallFolder = CreateBtn("+ Добавить скин", ref top, btnW, btnH, gap, Color.FromArgb(0, 110, 140));
        btnInstallFolder.Click += BtnInstallFolder_Click;

        Button btnDelete = CreateBtn("Удалить ливрею", ref top, btnW, btnH, gap, Color.FromArgb(140, 35, 35));
        btnDelete.Click += BtnDelete_Click;

        Button btnOpenDir = CreateBtn("Папка CustomLiveries", ref top, btnW, btnH, gap, Color.FromArgb(45, 55, 50));
        btnOpenDir.Click += (s, e) => {
            string dir = GetLiveriesRootDir();
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            Process.Start("explorer.exe", dir);
        };

        Button btnWorkshopTips = CreateBtn("Справка по скинам", ref top, btnW, btnH, gap, Color.FromArgb(70, 50, 100));
        btnWorkshopTips.Click += (s, e) => {
            MessageBox.Show("В VTOL VR кастомные ливреи хранятся в корне игры в папке CustomLiveries\\<Название_Скина>\\.\nВнутри папки скина должен лежать файл livery.cfg и PNG-текстуры.\nНажмите '+ Добавить скин' и выберите распакованную папку с ливреей!", "Справка по ливреям", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        lblStatus = new Label() { Left = 20, Top = 450, Width = 580, Text = "", ForeColor = Color.Silver };
        this.Controls.Add(lblStatus);

        LoadLiveries();
    }

    private Button CreateBtn(string text, ref int top, int width, int height, int gap, Color backColor)
    {
        Button btn = new Button() { Text = text, Location = new Point(455, top), Size = new Size(width, height), FlatStyle = FlatStyle.Flat, BackColor = backColor, ForeColor = Color.White };
        this.Controls.Add(btn);
        top += height + gap;
        return btn;
    }

    private string GetLiveriesRootDir()
    {
        if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot))
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL", "CustomLiveries");
        }
        return Path.Combine(gameRoot, "CustomLiveries");
    }

    private string DetectLiveryAircraft(string liveryFolder)
    {
        string cfg = Path.Combine(liveryFolder, "livery.cfg");
        if (File.Exists(cfg))
        {
            string text = File.ReadAllText(cfg, Encoding.UTF8);
            Match m = Regex.Match(text, @"(?m)^\s*vehicle\s*=\s*(.+)$");
            if (m.Success) return m.Result("$1").Trim().Replace("/", "");
        }
        return "Не указан";
    }

    private void LoadLiveries()
    {
        lstLiveries.Items.Clear();
        string dir = GetLiveriesRootDir();
        string filterAc = cmbAircraft.SelectedItem as string ?? "Все самолеты";

        if (Directory.Exists(dir))
        {
            foreach (string sub in Directory.GetDirectories(dir))
            {
                string folderName = Path.GetFileName(sub);
                string ac = DetectLiveryAircraft(sub);
                if (filterAc == "Все самолеты" || ac.Equals(filterAc, StringComparison.OrdinalIgnoreCase) || folderName.IndexOf(filterAc, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    lstLiveries.Items.Add(string.Format("{0}  [{1}]", folderName, ac));
                }
            }
        }
        lblStatus.Text = string.Format("Путь: {0} | Найдено скинов: {1}", dir, lstLiveries.Items.Count);
    }

    private void BtnInstallFolder_Click(object sender, EventArgs e)
    {
        using (FolderBrowserDialog fbd = new FolderBrowserDialog())
        {
            fbd.Description = "Выберите папку со скином (где лежат livery.cfg и PNG-текстуры)";
            if (fbd.ShowDialog() == DialogResult.OK)
            {
                string src = fbd.SelectedPath;
                string folderName = Path.GetFileName(src);
                string rootDir = GetLiveriesRootDir();
                string dst = Path.Combine(rootDir, folderName);
                try
                {
                    Directory.CreateDirectory(dst);
                    foreach (string f in Directory.GetFiles(src, "*.*", SearchOption.AllDirectories))
                    {
                        string rel = f.Substring(src.Length).TrimStart('\\', '/');
                        string target = Path.Combine(dst, rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        File.Copy(f, target, true);
                    }
                    LoadLiveries();
                    MessageBox.Show(string.Format("Ливрея \"{0}\" успешно добавлена в CustomLiveries!", folderName), "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Ошибка установки скина: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }

    private void BtnDelete_Click(object sender, EventArgs e)
    {
        if (lstLiveries.SelectedItem == null)
        {
            MessageBox.Show("Выберите ливрею для удаления.", "Внимание", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        string raw = lstLiveries.SelectedItem.ToString();
        int bracketIdx = raw.LastIndexOf("  [");
        string folderName = bracketIdx > 0 ? raw.Substring(0, bracketIdx) : raw;
        string targetPath = Path.Combine(GetLiveriesRootDir(), folderName);

        if (MessageBox.Show(string.Format("Удалить камуфляж \"{0}\"?", folderName), "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            try
            {
                if (Directory.Exists(targetPath)) Directory.Delete(targetPath, true);
                LoadLiveries();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
// =========================================================================
// === 2. ТАКТИЧЕСКИЙ ЖУРНАЛ ВЫЛЕТОВ (FLIGHT LOGBOOK & DEBRIEFING) =========
// =========================================================================
public class FlightLogbookForm : Form
{
    private ListView lvLog;
    private Label lblSummary;
    private Label lblSubStats;

    public FlightLogbookForm()
    {
        this.Text = "VTOL QoL — Журнал боевых вылетов (Flight Logbook & Debriefing)";
        this.Size = new Size(840, 560);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.BackColor = Color.FromArgb(18, 22, 20);
        this.ForeColor = Color.FromArgb(0, 255, 102);
        this.Font = new Font("Consolas", 9.5f, FontStyle.Regular);

        Label lblTitle = new Label() { Text = "/// ТАКТИЧЕСКИЙ ЖУРНАЛ БОЕВЫХ ВЫЛЕТОВ (FLIGHT DEBRIEFING) ///", Left = 20, Top = 14, AutoSize = true, Font = new Font("Consolas", 11f, FontStyle.Bold) };
        this.Controls.Add(lblTitle);

        Panel pnlStats = new Panel() { Left = 20, Top = 42, Width = 785, Height = 52, BackColor = Color.FromArgb(25, 32, 28), BorderStyle = BorderStyle.FixedSingle };
        this.Controls.Add(pnlStats);

        lblSummary = new Label() { Left = 12, Top = 6, Width = 760, Height = 20, ForeColor = Color.Cyan, Font = new Font("Consolas", 9.5f, FontStyle.Bold) };
        pnlStats.Controls.Add(lblSummary);

        lblSubStats = new Label() { Left = 12, Top = 27, Width = 760, Height = 20, ForeColor = Color.FromArgb(170, 230, 190), Font = new Font("Consolas", 9f) };
        pnlStats.Controls.Add(lblSubStats);

        lvLog = new ListView();
        lvLog.Location = new Point(20, 104);
        lvLog.Size = new Size(785, 365);
        lvLog.View = View.Details;
        lvLog.FullRowSelect = true;
        lvLog.GridLines = false;
        lvLog.OwnerDraw = true;
        lvLog.BackColor = Color.FromArgb(22, 26, 24);
        lvLog.ForeColor = Color.White;
        lvLog.BorderStyle = BorderStyle.FixedSingle;

        lvLog.Columns.Add("Дата и время", 145);
        lvLog.Columns.Add("Статус", 85);
        lvLog.Columns.Add("Борт", 115);
        lvLog.Columns.Add("Позывной", 115);
        lvLog.Columns.Add("Налет", 65);
        lvLog.Columns.Add("Сбито", 60);
        lvLog.Columns.Add("Детали события", 195);

        lvLog.DrawColumnHeader += (s, e) => {
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(32, 42, 37)))
                e.Graphics.FillRectangle(bg, e.Bounds);
            using (Pen border = new Pen(Color.FromArgb(0, 160, 70)))
                e.Graphics.DrawLine(border, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, new Font("Consolas", 9f, FontStyle.Bold), e.Bounds, Color.FromArgb(0, 255, 102), TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        };
        lvLog.DrawItem += (s, e) => e.DrawDefault = true;
        lvLog.DrawSubItem += (s, e) => e.DrawDefault = true;
        this.Controls.Add(lvLog);

        Button btnRefresh = new Button() { Text = "Обновить", Left = 20, Top = 480, Width = 120, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(35, 45, 40), ForeColor = Color.FromArgb(0, 255, 102) };
        btnRefresh.Click += (s, e) => LoadLog();
        this.Controls.Add(btnRefresh);

        Button btnExport = new Button() { Text = "Экспорт отчета", Left = 150, Top = 480, Width = 145, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 110, 140), ForeColor = Color.White };
        btnExport.Click += BtnExport_Click;
        this.Controls.Add(btnExport);

        Button btnClear = new Button() { Text = "Очистить журнал", Left = 305, Top = 480, Width = 145, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(120, 30, 30), ForeColor = Color.White };
        btnClear.Click += BtnClear_Click;
        this.Controls.Add(btnClear);

        Button btnClose = new Button() { Text = "Закрыть", Left = 675, Top = 480, Width = 130, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(40, 50, 45), ForeColor = Color.White };
        btnClose.Click += (s, e) => this.Close();
        this.Controls.Add(btnClose);

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
        int totalKills = 0;
        int totalSorties = 0;
        int totalLosses = 0;
        Dictionary<string, int> planeUsage = new Dictionary<string, int>();

        if (File.Exists(path))
        {
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                string[] p = line.Split('|');

                string time = p.Length > 0 ? p.GetValue(0).ToString() : "";
                string ev = p.Length > 1 ? p.GetValue(1).ToString() : "";
                string plane = p.Length > 2 ? p.GetValue(2).ToString() : "";
                string cs = p.Length > 3 ? p.GetValue(3).ToString() : "";
                string dur = p.Length >= 7 ? p.GetValue(4).ToString() : "—";
                string killsStr = p.Length >= 7 ? p.GetValue(5).ToString() : (p.Length > 4 ? p.GetValue(4).ToString() : "0");
                string note = p.Length >= 7 ? p.GetValue(6).ToString() : (p.Length > 5 ? p.GetValue(5).ToString() : "");

                ListViewItem item = new ListViewItem(time);
                item.SubItems.Add(ev);
                item.SubItems.Add(plane);
                item.SubItems.Add(cs);
                item.SubItems.Add(dur);
                item.SubItems.Add(killsStr);
                item.SubItems.Add(note);

                if (ev == "KILL") { item.ForeColor = Color.Cyan; totalKills++; }
                else if (ev == "EJECT" || ev == "KIA") { item.ForeColor = Color.OrangeRed; totalLosses++; }
                else if (ev == "SORTIE" || ev == "START")
                {
                    item.ForeColor = Color.LightGreen;
                    totalSorties++;
                    if (!string.IsNullOrEmpty(plane) && !plane.Contains("ангар"))
                    {
                        if (!planeUsage.ContainsKey(plane)) planeUsage[plane] = 0;
                        planeUsage[plane]++;
                    }
                }
                else { item.ForeColor = Color.Silver; }

                lvLog.Items.Add(item);
            }
        }

        string favPlane = "Нет данных";
        int maxCount = 0;
        foreach (var kv in planeUsage)
        {
            if (kv.Value > maxCount) { maxCount = kv.Value; favPlane = kv.Key; }
        }

        double kd = totalLosses > 0 ? Math.Round((double)totalKills / totalLosses, 2) : totalKills;
        lblSummary.Text = string.Format("ВЫЛЕТОВ: {0}  |  СБИТО ЦЕЛЕЙ: {1}  |  ПОТЕРЬ БОРТА: {2}  |  K/D: {3}", totalSorties, totalKills, totalLosses, kd);
        lblSubStats.Text = string.Format("Основной борт: {0} ({1} вылетов)  |  Записей в бортовом самописце: {2}", favPlane, maxCount, lvLog.Items.Count);
    }

    private void BtnClear_Click(object sender, EventArgs e)
    {
        if (MessageBox.Show("Очистить историю всех боевых вылетов?", "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            try
            {
                string path = GetLogFilePath();
                if (File.Exists(path)) File.Delete(path);
                LoadLog();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка: " + ex.Message);
            }
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
                MessageBox.Show("Отчет сохранен в " + sfd.FileName, "Экспорт завершен", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}

// =========================================================================
// === 3. БЭКАП И ЗАЩИТА СОХРАНЕНИЙ (SAVE DATA VAULT) ======================
// =========================================================================
public class SaveVaultForm : Form
{
    private ListBox lstBackups;
    private Label lblSaveStatus;

    public SaveVaultForm()
    {
        this.Text = "VTOL QoL — Резервные копии сохранений (SaveData Vault)";
        this.Size = new Size(620, 480);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.BackColor = Color.FromArgb(18, 22, 20);
        this.ForeColor = Color.FromArgb(0, 255, 102);
        this.Font = new Font("Consolas", 9.5f, FontStyle.Regular);

        Label lbl = new Label() { Text = "АРХИВ СНАПШОТОВ СОХРАНЕНИЙ И ПРОФИЛЕЙ ПИЛОТА:", Left = 20, Top = 15, AutoSize = true, Font = new Font("Consolas", 10f, FontStyle.Bold) };
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

        int top = 68;
        int btnW = 155;
        int btnH = 38;
        int gap = 10;

        Button btnCreate = CreateBtn("+ Создать бэкап", ref top, btnW, btnH, gap, Color.FromArgb(0, 140, 60));
        btnCreate.Click += (s, e) => CreateSnapshot();

        Button btnRestore = CreateBtn("Восстановить", ref top, btnW, btnH, gap, Color.FromArgb(160, 100, 0));
        btnRestore.Click += BtnRestore_Click;

        Button btnDelete = CreateBtn("Удалить архив", ref top, btnW, btnH, gap, Color.FromArgb(140, 35, 35));
        btnDelete.Click += BtnDelete_Click;

        Button btnOpenDir = CreateBtn("Папка бэкапов", ref top, btnW, btnH, gap, Color.FromArgb(40, 50, 45));
        btnOpenDir.Click += (s, e) => {
            string dir = GetBackupsDir();
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            Process.Start("explorer.exe", dir);
        };

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

    private string GetSaveDataDir()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Boundless Dynamics, LLC", "VTOLVR", "SaveData");
    }

    private string GetBackupsDir()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL", "SaveBackups");
    }

    private void RefreshBackups()
    {
        lstBackups.Items.Clear();
        string bDir = GetBackupsDir();
        if (Directory.Exists(bDir))
        {
            string[] dirs = Directory.GetDirectories(bDir);
            Array.Sort(dirs);
            Array.Reverse(dirs);
            foreach (string d in dirs)
            {
                lstBackups.Items.Add(Path.GetFileName(d));
            }
        }
        string sDir = GetSaveDataDir();
        lblSaveStatus.Text = Directory.Exists(sDir) ? "Папка сохранений обнаружена: " + sDir : "Папка сохранений игры пока не создана.";
    }

    private void CreateSnapshot()
    {
        string sDir = GetSaveDataDir();
        if (!Directory.Exists(sDir))
        {
            MessageBox.Show("Папка с сохранениями VTOL VR не найдена по пути:\n" + sDir, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string bDir = GetBackupsDir();
        string snapshotName = "Backup_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string targetDir = Path.Combine(bDir, snapshotName);

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
            MessageBox.Show("Резервная копия сохранений успешно создана: " + snapshotName, "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Ошибка создания бэкапа: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BtnRestore_Click(object sender, EventArgs e)
    {
        if (lstBackups.SelectedItem == null)
        {
            MessageBox.Show("Выберите резервную копию для восстановления.", "Внимание", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string bName = lstBackups.SelectedItem.ToString();
        string bSource = Path.Combine(GetBackupsDir(), bName);
        string sDir = GetSaveDataDir();

        if (MessageBox.Show(string.Format("Восстановить сохранения из снимка \"{0}\"?\r\nТекущий прогресс будет перезаписан выбранной копией.", bName), "Внимание", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
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
                MessageBox.Show("Сохранения успешно восстановлены из архива " + bName + "!", "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка восстановления: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void BtnDelete_Click(object sender, EventArgs e)
    {
        if (lstBackups.SelectedItem == null) return;
        string bName = lstBackups.SelectedItem.ToString();
        string target = Path.Combine(GetBackupsDir(), bName);
        if (MessageBox.Show("Удалить бэкап " + bName + "?", "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            try
            {
                if (Directory.Exists(target)) Directory.Delete(target, true);
                RefreshBackups();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка: " + ex.Message);
            }
        }
    }
}
