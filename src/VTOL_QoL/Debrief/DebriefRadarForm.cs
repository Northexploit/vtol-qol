using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Windows.Forms;
using OpenFileDialog = System.Windows.Forms.OpenFileDialog;


// =========================================================================
// === ТАКТИЧЕСКИЙ РАДАР-ПЛАНШЕТ ДЕБРИФИНГА И РАЗБОР ОШИБОК ПИЛОТА ==========
// =========================================================================
public class DebriefRadarForm : Form
{
    // Детерминированный псевдослучайный шум 0..1 для визуализации помех
    private static double Hash01(int k)
    {
        unchecked
        {
            uint x = (uint)(k * 747796405 + 2891336453);
            x ^= x >> 15;
            x *= 2246822519u;
            x ^= x >> 13;
            x *= 3266489917u;
            x ^= x >> 16;
            return (x & 0xFFFFFF) / 16777216.0;
        }
    }

    private class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
    }

    private ComboBox cmbReplays;
    private Button btnBrowseFile;
    private DoubleBufferedPanel pnlRadar;
    private OpenGLRadarControl glRadar3D;
    private DoubleBufferedPanel pnlTimeline;
    private Button btnPlayPause;
    private ComboBox cmbSpeed;
    private Label lblTimeHud;
    private Label lblTelemetryHud;
    private Label lblSummary;
    private ListBox lstMistakes;
    private TextBox txtCoachAdvice;
    private Button btnFilterAll;
    private Button btnFilterThreats;
    private Button btnFilterMistakes;
    private Button btnFilterSuccess;
    private string eventFilter = "ALL";
    private List<TacticalEvent> currentEvents = new List<TacticalEvent>();

    private List<string> replayFiles = new List<string>();
    private DebriefSession session = null;
    private double currentTime = 0.0;
    private bool isPlaying = false;
    private double playSpeed = 2.0;
    private System.Windows.Forms.Timer playTimer;

    // Параметры камеры тактического планшета
    private double camCenterX = 0.0;
    private double camCenterY = 0.0;
    private double targetCamX = 0.0;
    private double targetCamY = 0.0;
    private bool hasSmoothCamTarget = false;
    private double pixelsPerMeter = 0.0085;
    // Пределы масштаба карты (пикселей на метр). Верхний предел задаёт максимальное
    // приближение: 2D — ~0.8 км на экран, 3D — камера в ~1.9 км от цели.
    private const double ZoomMin2D = 0.0008;
    private const double ZoomMax2D = 1.2;
    private const double ZoomMin3D = 0.00003;
    private const double ZoomMax3D = 5.0;
    private const double CamDistMinKm = 1.9;
    private const double CamDistMaxKm = 350.0;

    // Дистанция 3D-камеры (км) из текущего масштаба
    private double CamDistKm()
    {
        return Math.Max(CamDistMinKm, Math.Min(CamDistMaxKm, 9.5 / Math.Max(ZoomMin3D, pixelsPerMeter)));
    }
    private bool isPanning = false;
    private Point lastPanMouse;
    private bool followPlayer = false;
    private CheckBox chkFollowPlayer;
    private ComboBox cmbFollowTarget;
    private string followEntityId = "";
    private CheckBox chkShowTerrain;
    private CheckBox chkShowBeams;
    private CheckBox chkShowAllies;
    private bool showTerrain = true;
    private bool showBeams = true;
    private bool showAllies = true;
    private bool is3DMode = false;
    private double camYawDeg = 35.0;
    private double camPitchDeg = 38.0;
    private Button btnToggle3D;
    private Dictionary<string, PointF> smoothBadgeOffsets = new Dictionary<string, PointF>();
    private float beamDashPhase = 0f;
    private System.Diagnostics.Stopwatch frameSw = System.Diagnostics.Stopwatch.StartNew();
    private double lastTickSec = 0.0;
    private double lastJamSurgeTime = -99.0;
    private string lastJamBannerActor = "";
    private HashSet<string> triggeredJamEvents = new HashSet<string>();
    // Запомненная цель направленного луча DRFM/SAS: ключ луча -> id юнита
    private Dictionary<string, string> dirJamLock = new Dictionary<string, string>();

    // Линейка (измерение расстояния и азимута по карте)
    private bool rulerActive = false;
    private bool rulerHasA = false;
    private bool rulerHasB = false;
    private PointF rulerA = PointF.Empty;
    private PointF rulerB = PointF.Empty;
    private Button btnRuler;

    // Аналитика пусков
    private ListBox lstLaunches;
    private List<DebriefLaunch> launchList = new List<DebriefLaunch>();
    private Button btnTabChrono;
    private Button btnTabLaunches;
    private bool launchesMode = false;
    private Panel pnlFiltersRef;
    private Label lblChronoTitle;
    private FlightLogbookForm openLogbook = null;


    public DebriefRadarForm()
    {
        this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        this.DoubleBuffered = true;
        this.Text = "VTOL QoL — Тактический дебрифинг и 2D-карта боя";
        DarkWindowHelper.Apply(this);
        this.Size = new Size(1240, 745);
        this.MinimumSize = new Size(1020, 580);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.Sizable;
        this.MaximizeBox = true;
        this.KeyPreview = true;
        this.BackColor = Color.FromArgb(16, 20, 18);
        this.SizeChanged += (s, e) => { this.PerformLayout(); if (pnlRadar != null) pnlRadar.Invalidate(); if (pnlTimeline != null) pnlTimeline.Invalidate(); };

        // Верхняя панель выбора повтора и слоёв карты
        Panel pnlTop = new Panel() { Left = 14, Top = 10, Width = 1196, Height = 38, BackColor = Color.FromArgb(24, 30, 27), BorderStyle = BorderStyle.FixedSingle };
        pnlTop.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        this.Controls.Add(pnlTop);

        Label lblFile = new Label() { Text = "Вылет:", Left = 10, Top = 10, AutoSize = true, ForeColor = Color.LightGray };
        pnlTop.Controls.Add(lblFile);

        cmbReplays = new ComboBox() { Left = 64, Top = 6, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(30, 38, 34), ForeColor = Color.Cyan };
        cmbReplays.SelectedIndexChanged += (s, e) => LoadSelectedReplay();
        pnlTop.Controls.Add(cmbReplays);

        btnBrowseFile = new Button() { Text = "📂 Файл...", Left = 270, Top = 5, Width = 88, Height = 26, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 105, 135), ForeColor = Color.White };
        btnBrowseFile.Click += BtnBrowseFile_Click;
        pnlTop.Controls.Add(btnBrowseFile);

        Button btnFitAll = new Button() { Text = "🌐 Бой", Left = 366, Top = 5, Width = 62, Height = 26, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(38, 78, 62), ForeColor = Color.White };
        btnFitAll.Click += (s, e) => { followPlayer = false; chkFollowPlayer.Checked = false; FitCameraToBattle(); pnlRadar.Invalidate(); };
        pnlTop.Controls.Add(btnFitAll);

        // Крупный переключатель 2D/3D — основной элемент управления видом
        btnToggle3D = new Button() { Text = "📐 3D-ПРОСМОТР", Left = 434, Top = 3, Width = 140, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(40, 52, 65), ForeColor = Color.LightGray, Font = new Font("Consolas", 9f, FontStyle.Bold) };
        btnToggle3D.FlatAppearance.BorderSize = 1;
        btnToggle3D.Click += (s, e) =>
        {
            is3DMode = !is3DMode;
            btnToggle3D.Text = is3DMode ? "🗺 2D-КАРТА" : "📐 3D-ПРОСМОТР";
            btnToggle3D.BackColor = is3DMode ? Color.FromArgb(0, 125, 165) : Color.FromArgb(40, 52, 65);
            btnToggle3D.ForeColor = is3DMode ? Color.White : Color.LightGray;
            pnlRadar.Visible = !is3DMode;
            glRadar3D.Visible = is3DMode;
            if (is3DMode) glRadar3D.Invalidate();
            else pnlRadar.Invalidate();
        };
        pnlTop.Controls.Add(btnToggle3D);

        chkFollowPlayer = new CheckBox() { Text = "Следить:", Left = 586, Top = 9, AutoSize = true, Checked = false, ForeColor = Color.FromArgb(180, 240, 200) };
        chkFollowPlayer.CheckedChanged += (s, e) => { followPlayer = chkFollowPlayer.Checked; UpdateFrame(); };
        pnlTop.Controls.Add(chkFollowPlayer);

        cmbFollowTarget = new ComboBox() { Left = 666, Top = 6, Width = 115, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(30, 38, 34), ForeColor = Color.FromArgb(180, 240, 200), Font = new Font("Consolas", 8f) };
        cmbFollowTarget.SelectedIndexChanged += (s, e) =>
        {
            if (cmbFollowTarget.SelectedIndex <= 0) followEntityId = "";
            else
            {
                string sel = cmbFollowTarget.SelectedItem.ToString();
                int idxBr = sel.LastIndexOf('[');
                if (idxBr >= 0 && sel.EndsWith("]"))
                    followEntityId = sel.Substring(idxBr + 1, sel.Length - idxBr - 2);
            }
            UpdateFrame();
        };
        pnlTop.Controls.Add(cmbFollowTarget);

        chkShowTerrain = new CheckBox() { Text = "Рельеф", Left = 787, Top = 8, AutoSize = true, Checked = true, ForeColor = Color.Cyan };
        chkShowTerrain.CheckedChanged += (s, e) => { showTerrain = chkShowTerrain.Checked; pnlRadar.Invalidate(); };
        pnlTop.Controls.Add(chkShowTerrain);

        chkShowBeams = new CheckBox() { Text = "RWR", Left = 856, Top = 8, AutoSize = true, Checked = true, ForeColor = Color.Gold };
        chkShowBeams.CheckedChanged += (s, e) => { showBeams = chkShowBeams.Checked; pnlRadar.Invalidate(); };
        pnlTop.Controls.Add(chkShowBeams);

        chkShowAllies = new CheckBox() { Text = "Союз", Left = 908, Top = 8, AutoSize = true, Checked = true, ForeColor = Color.DeepSkyBlue };
        chkShowAllies.CheckedChanged += (s, e) => { showAllies = chkShowAllies.Checked; pnlRadar.Invalidate(); };
        pnlTop.Controls.Add(chkShowAllies);

        Button btnInspector = new Button() { Text = "🔍 Дамп", Left = 966, Top = 5, Width = 84, Height = 26, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(65, 55, 110), ForeColor = Color.White, Font = new Font("Consolas", 8f, FontStyle.Bold) };
        btnInspector.Click += BtnInspector_Click;
        pnlTop.Controls.Add(btnInspector);

        Button btnCleanOld = new Button() { Text = "🗑", Left = 1056, Top = 5, Width = 28, Height = 26, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(120, 38, 38), ForeColor = Color.White };
        btnCleanOld.Click += (s, e) =>
        {
            if (MessageBox.Show(this, "Удалить все старые/дублирующие повторы из папки Replays?", "Очистка повторов", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                string dir = DebriefEngine.GetReplaysDir();
                foreach (string f in Directory.GetFiles(dir, "*.vtolrep"))
                {
                    try { File.Delete(f); File.Delete(f + ".debug.txt"); } catch { }
                }
                RefreshReplayList();
            }
        };
        pnlTop.Controls.Add(btnCleanOld);

        Button btnLogbook = new Button() { Text = "📋 ЛОГБУК", Left = 1090, Top = 5, Width = 100, Height = 26, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(40, 85, 70), ForeColor = Color.White, Font = new Font("Consolas", 8f, FontStyle.Bold) };
        btnLogbook.Click += (s, e) =>
        {
            if (openLogbook != null && !openLogbook.IsDisposed) { openLogbook.Activate(); openLogbook.BringToFront(); return; }
            openLogbook = new FlightLogbookForm();
            openLogbook.FormClosed += (s2, e2) => { openLogbook = null; };
            openLogbook.Show(this);
        };
        pnlTop.Controls.Add(btnLogbook);

        // Левая основная зона: тактический радар-планшет
        pnlRadar = new DoubleBufferedPanel();
        pnlRadar.Location = new Point(14, 56);
        pnlRadar.Size = new Size(810, 565);
        pnlRadar.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        pnlRadar.BackColor = Color.FromArgb(11, 20, 26);
        pnlRadar.BorderStyle = BorderStyle.FixedSingle;
        pnlRadar.Paint += PnlRadar_Paint;
        pnlRadar.MouseDown += (s, e) =>
        {
            // Линейка: режим включён кнопкой или зажат Ctrl — ставим точки, не трогая камеру
            if (e.Button == MouseButtons.Left && session != null &&
                (rulerActive || (Control.ModifierKeys & Keys.Control) == Keys.Control))
            {
                if (!rulerHasA || rulerHasB)
                {
                    rulerA = e.Location;
                    rulerHasA = true;
                    rulerHasB = false;
                }
                else
                {
                    rulerB = e.Location;
                    rulerHasB = true;
                }
                pnlRadar.Invalidate();
                return;
            }

            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right)
            {
                bool hitAircraft = false;
                if (e.Button == MouseButtons.Left && session != null)
                    hitAircraft = InspectNearestObjectAt(e.Location);

                if (hitAircraft)
                {
                    if (!chkFollowPlayer.Checked) chkFollowPlayer.Checked = true;
                }
                else
                {
                    hasSmoothCamTarget = false;
                    if (followPlayer || chkFollowPlayer.Checked)
                    {
                        followPlayer = false;
                        chkFollowPlayer.Checked = false;
                    }
                }

                isPanning = true;
                lastPanMouse = e.Location;
            }
        };

        pnlRadar.MouseUp += (s, e) => { isPanning = false; };
        pnlRadar.MouseMove += (s, e) =>
        {
            if (!isPanning) return;
            double dx = e.X - lastPanMouse.X;
            double dy = e.Y - lastPanMouse.Y;
            if (Math.Abs(dx) < 1 && Math.Abs(dy) < 1) return;

            if (is3DMode && (e.Button & MouseButtons.Right) == MouseButtons.Right)
            {
                camYawDeg = (camYawDeg + dx * 0.45 + 360.0) % 360.0;
                camPitchDeg = Math.Max(5.0, Math.Min(89.0, camPitchDeg + dy * 0.40));
                lastPanMouse = e.Location;
                pnlRadar.Invalidate();
                return;
            }

            hasSmoothCamTarget = false;
            if (followPlayer)
            {
                followPlayer = false;
                chkFollowPlayer.Checked = false;
            }

            if (is3DMode)
            {
                double yawRad = camYawDeg * Math.PI / 180.0;
                double sinP = Math.Max(0.25, Math.Sin(camPitchDeg * Math.PI / 180.0));
                double wdx = (dx * Math.Cos(yawRad) - (dy / sinP) * Math.Sin(yawRad)) / pixelsPerMeter;
                double wdy = (dx * Math.Sin(yawRad) + (dy / sinP) * Math.Cos(yawRad)) / pixelsPerMeter;
                camCenterX -= wdx;
                camCenterY += wdy;
            }
            else
            {
                camCenterX -= dx / pixelsPerMeter;
                camCenterY += dy / pixelsPerMeter;
            }
            lastPanMouse = e.Location;
            pnlRadar.Invalidate();
        };
        pnlRadar.MouseWheel += (s, e) =>
        {
            double factor = e.Delta > 0 ? 1.25 : 0.8;
            pixelsPerMeter = Math.Max(ZoomMin2D, Math.Min(ZoomMax2D, pixelsPerMeter * factor));
            pnlRadar.Invalidate();
        };
        this.Controls.Add(pnlRadar);

        // Кнопка-переключатель линейки (места в верхней панели нет — держим поверх карты)
        btnRuler = new Button()
        {
            Text = "📏",
            Left = pnlRadar.Width - 44,
            Top = 8,
            Width = 34,
            Height = 26,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(32, 40, 36),
            ForeColor = Color.FromArgb(190, 215, 205),
            Font = new Font("Consolas", 9f, FontStyle.Bold),
            TabStop = false
        };
        btnRuler.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnRuler.FlatAppearance.BorderSize = 1;
        btnRuler.FlatAppearance.BorderColor = Color.FromArgb(55, 70, 62);
        btnRuler.Click += (s, e) => SetRulerMode(!rulerActive);
        pnlRadar.Controls.Add(btnRuler);
        glRadar3D = new OpenGLRadarControl();
        glRadar3D.Location = pnlRadar.Location;
        glRadar3D.Size = pnlRadar.Size;
        glRadar3D.Anchor = pnlRadar.Anchor;
        glRadar3D.Visible = false;
        glRadar3D.GetSession = () => session;
        glRadar3D.GetCurrentTime = () => currentTime;
        glRadar3D.GetCamX = () => camCenterX;
        glRadar3D.GetCamY = () => camCenterY;
        glRadar3D.GetCamDist = () => CamDistKm();
        glRadar3D.GetYaw = () => camYawDeg;
        glRadar3D.GetPitch = () => camPitchDeg;
        glRadar3D.GetRoll = () => 0.0;
        glRadar3D.GetShowTerrain = () => showTerrain;
        glRadar3D.GetShowBeams = () => showBeams;
        glRadar3D.GetShowAllies = () => showAllies;
        glRadar3D.GetFollowId = () => followEntityId;
        glRadar3D.GetIsPlaying = () => isPlaying;
        glRadar3D.MouseDown += (s, e) => { glRadar3D.Focus(); isPanning = true; lastPanMouse = e.Location; };
        glRadar3D.MouseUp += (s, e) => { isPanning = false; };
        glRadar3D.MouseMove += (s, e) =>
        {
            if (!glRadar3D.Focused) glRadar3D.Focus();
            if (!isPanning) return;
            double dx = e.X - lastPanMouse.X;
            double dy = e.Y - lastPanMouse.Y;
            if ((e.Button & MouseButtons.Left) == MouseButtons.Left)
            {
                camYawDeg = (camYawDeg + dx * 0.45 + 360.0) % 360.0;
                camPitchDeg = Math.Max(3.0, Math.Min(89.0, camPitchDeg + dy * 0.40));
            }
            else if ((e.Button & MouseButtons.Right) == MouseButtons.Right)
            {
                hasSmoothCamTarget = false;
                if (followPlayer) { followPlayer = false; chkFollowPlayer.Checked = false; }
                double yawRad = camYawDeg * Math.PI / 180.0;
                double distKm = CamDistKm();
                double metersPerPx = (distKm * 1000.0) / Math.Max(300.0, glRadar3D.Height * 0.85);
                double sinP = Math.Max(0.32, Math.Sin(camPitchDeg * Math.PI / 180.0));
                double wdx = (dx * Math.Cos(yawRad) - (dy / sinP) * Math.Sin(yawRad)) * metersPerPx;
                double wdy = (dx * Math.Sin(yawRad) + (dy / sinP) * Math.Cos(yawRad)) * metersPerPx;
                camCenterX -= wdx;
                camCenterY += wdy;
            }
            lastPanMouse = e.Location;
            glRadar3D.Invalidate();
        };
        glRadar3D.MouseWheel += (s, e) =>
        {
            double factor = e.Delta > 0 ? 1.28 : 0.78;
            pixelsPerMeter = Math.Max(ZoomMin3D, Math.Min(ZoomMax3D, pixelsPerMeter * factor));
            glRadar3D.Invalidate();
        };
        this.Controls.Add(glRadar3D);

        // Правая панель: HUD телеметрии + Разбор ошибок пилота
        Panel pnlRight = new Panel();
        pnlRight.Location = new Point(832, 56);
        pnlRight.Size = new Size(378, 565);
        pnlRight.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;
        pnlRight.BackColor = Color.FromArgb(20, 25, 23);
        pnlRight.BorderStyle = BorderStyle.FixedSingle;
        this.Controls.Add(pnlRight);

        lblTelemetryHud = new Label()
        {
            Left = 10,
            Top = 8,
            Width = 356,
            Height = 68,
            BackColor = Color.FromArgb(15, 20, 18),
            ForeColor = Color.FromArgb(0, 255, 102),
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(6),
            Font = new Font("Consolas", 8.5f),
            Text = "Загрузите вылет для просмотра телеметрии..."
        };
        pnlRight.Controls.Add(lblTelemetryHud);

        // Компактная сводка боя: только реальные цифры из файла
        lblSummary = new Label()
        {
            Left = 10,
            Top = 84,
            Width = 356,
            Height = 62,
            BackColor = Color.FromArgb(18, 24, 21),
            ForeColor = Color.FromArgb(200, 235, 210),
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(6),
            Font = new Font("Consolas", 8f),
            Text = "Сводка появится после загрузки повтора."
        };
        pnlRight.Controls.Add(lblSummary);

        lblChronoTitle = new Label() { Text = "/// ХРОНОЛОГИЯ БОЯ ///", Left = 10, Top = 152, AutoSize = true, Font = new Font("Consolas", 9.5f, FontStyle.Bold), ForeColor = Color.Gold, Visible = false };
        pnlRight.Controls.Add(lblChronoTitle);

        btnTabChrono = new Button()
        {
            Text = "ХРОНОЛОГИЯ",
            Left = 10,
            Top = 152,
            Width = 112,
            Height = 21,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0, 110, 80),
            ForeColor = Color.White,
            Font = new Font("Consolas", 8f, FontStyle.Bold),
            TabStop = false
        };
        btnTabChrono.FlatAppearance.BorderSize = 1;
        btnTabChrono.FlatAppearance.BorderColor = Color.FromArgb(55, 70, 62);
        btnTabChrono.Click += (s, e) => SetLaunchesMode(false);
        pnlRight.Controls.Add(btnTabChrono);

        btnTabLaunches = new Button()
        {
            Text = "ПУСКИ",
            Left = 128,
            Top = 152,
            Width = 80,
            Height = 21,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(32, 40, 36),
            ForeColor = Color.FromArgb(170, 190, 180),
            Font = new Font("Consolas", 8f, FontStyle.Bold),
            TabStop = false
        };
        btnTabLaunches.FlatAppearance.BorderSize = 1;
        btnTabLaunches.FlatAppearance.BorderColor = Color.FromArgb(55, 70, 62);
        btnTabLaunches.Click += (s, e) => SetLaunchesMode(true);
        pnlRight.Controls.Add(btnTabLaunches);

        // Фильтры хронологии
        Panel pnlFilters = new Panel() { Left = 8, Top = 178, Width = 360, Height = 24, BackColor = Color.FromArgb(20, 25, 23) };
        pnlFilters.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        pnlFiltersRef = pnlFilters;
        pnlRight.Controls.Add(pnlFilters);

        btnFilterAll = MakeFilterButton("ВСЕ", 0, true);
        btnFilterThreats = MakeFilterButton("УГРОЗЫ", 1, false);
        btnFilterMistakes = MakeFilterButton("ОШИБКИ", 2, false);
        btnFilterSuccess = MakeFilterButton("УСПЕХИ", 3, false);
        pnlFilters.Controls.Add(btnFilterAll);
        pnlFilters.Controls.Add(btnFilterThreats);
        pnlFilters.Controls.Add(btnFilterMistakes);
        pnlFilters.Controls.Add(btnFilterSuccess);

        lstMistakes = new ListBox();
        lstMistakes.Location = new Point(10, 206);
        lstMistakes.Size = new Size(356, 184);
        lstMistakes.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        lstMistakes.BackColor = Color.FromArgb(16, 20, 18);
        lstMistakes.ForeColor = Color.White;
        lstMistakes.BorderStyle = BorderStyle.FixedSingle;
        lstMistakes.DrawMode = DrawMode.OwnerDrawFixed;
        lstMistakes.ItemHeight = 34;
        lstMistakes.IntegralHeight = false;
        lstMistakes.DrawItem += LstMistakes_DrawItem;
        lstMistakes.SelectedIndexChanged += LstMistakes_SelectedIndexChanged;
        pnlRight.Controls.Add(lstMistakes);

        // Второй список — аналитика пусков (тот же прямоугольник, виден только во вкладке «ПУСКИ»)
        lstLaunches = new ListBox();
        lstLaunches.Location = lstMistakes.Location;
        lstLaunches.Size = lstMistakes.Size;
        lstLaunches.Anchor = lstMistakes.Anchor;
        lstLaunches.BackColor = Color.FromArgb(16, 20, 18);
        lstLaunches.ForeColor = Color.White;
        lstLaunches.BorderStyle = BorderStyle.FixedSingle;
        lstLaunches.DrawMode = DrawMode.OwnerDrawFixed;
        lstLaunches.ItemHeight = 34;
        lstLaunches.IntegralHeight = false;
        lstLaunches.DrawItem += LstLaunches_DrawItem;
        lstLaunches.SelectedIndexChanged += LstLaunches_SelectedIndexChanged;
        lstLaunches.Visible = false;
        pnlRight.Controls.Add(lstLaunches);

        Label lblAdvTitle = new Label() { Text = "/// РАЗБОР МОМЕНТА ///", Left = 10, Top = 396, AutoSize = true, Font = new Font("Consolas", 9f, FontStyle.Bold), ForeColor = Color.FromArgb(0, 255, 102) };
        lblAdvTitle.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        pnlRight.Controls.Add(lblAdvTitle);

        txtCoachAdvice = new TextBox()
        {
            Left = 10,
            Top = 415,
            Width = 356,
            Height = 140,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(15, 20, 18),
            ForeColor = Color.FromArgb(225, 245, 230),
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 8.5f),
            Text = "Выберите событие в хронологии, чтобы перейти к этому моменту боя на карте и увидеть разбор."
        };
        pnlRight.Controls.Add(txtCoachAdvice);
        // Нижняя панель плеера
        Panel pnlBottom = new Panel() { Left = 14, Top = 629, Width = 1196, Height = 64, BackColor = Color.FromArgb(24, 30, 27), BorderStyle = BorderStyle.FixedSingle };
        pnlBottom.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        this.Controls.Add(pnlBottom);
        btnPlayPause = new Button() { Text = "▶ ПУСК", Left = 10, Top = 16, Width = 92, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 130, 65), ForeColor = Color.White, Font = new Font("Consolas", 9.5f, FontStyle.Bold) };
        btnPlayPause.Click += (s, e) => TogglePlay();
        pnlBottom.Controls.Add(btnPlayPause);
        cmbSpeed = new ComboBox() { Left = 110, Top = 19, Width = 68, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(30, 38, 34), ForeColor = Color.Cyan };
        cmbSpeed.Items.AddRange(new object[] { "0.25x", "0.5x", "1x", "2x", "5x", "10x", "20x" });
        cmbSpeed.SelectedIndex = 3;
        cmbSpeed.SelectedIndexChanged += (s, e) =>
        {
            string sVal = cmbSpeed.SelectedItem.ToString().Replace("x", "");
            double sp;
            if (double.TryParse(sVal, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sp)) playSpeed = sp;
        };
        pnlBottom.Controls.Add(cmbSpeed);
        lblTimeHud = new Label() { Left = 186, Top = 22, Width = 135, Height = 20, ForeColor = Color.Cyan, Font = new Font("Consolas", 9.5f, FontStyle.Bold), Text = "00:00 / 00:00" };
        pnlBottom.Controls.Add(lblTimeHud);
        pnlTimeline = new DoubleBufferedPanel();
        pnlTimeline.Location = new Point(328, 12);
        pnlTimeline.Size = new Size(854, 40);
        pnlTimeline.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        pnlTimeline.BackColor = Color.FromArgb(14, 18, 16);
        pnlTimeline.BorderStyle = BorderStyle.FixedSingle;
        pnlTimeline.Cursor = Cursors.Hand;
        pnlTimeline.Paint += PnlTimeline_Paint;
        pnlTimeline.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) SeekTimelineByX(e.X); };
        pnlTimeline.MouseMove += (s, e) => { if (e.Button == MouseButtons.Left) SeekTimelineByX(e.X); };
        pnlBottom.Controls.Add(pnlTimeline);
        playTimer = new System.Windows.Forms.Timer();
        playTimer.Interval = 16; // 60 FPS
        playTimer.Tick += (s, e) =>
        {
            double nowSec = frameSw.Elapsed.TotalSeconds;
            double dt = nowSec - lastTickSec;
            lastTickSec = nowSec;
            if (dt <= 0 || dt > 0.25) dt = 0.016;

            if (session != null && hasSmoothCamTarget && !isPanning)
            {
                double dx = targetCamX - camCenterX;
                double dy = targetCamY - camCenterY;
                if (Math.Abs(dx) < 1.5 && Math.Abs(dy) < 1.5)
                {
                    camCenterX = targetCamX;
                    camCenterY = targetCamY;
                    if (!followPlayer) hasSmoothCamTarget = false;
                }
                else
                {
                    camCenterX += dx * 0.20;
                    camCenterY += dy * 0.20;
                }
                if (!isPlaying) pnlRadar.Invalidate();
            }

            if (!isPlaying || session == null) return;
            currentTime += dt * playSpeed;
            beamDashPhase = (beamDashPhase - (float)(dt * 30.0) + 100f) % 100f;
            if (currentTime >= session.Duration)
            {
                currentTime = session.Duration;
                isPlaying = false;
                btnPlayPause.Text = "▶ ПУСК";
            }
            UpdateFrame();
        };
        playTimer.Start();
        this.FormClosing += (s, e) =>
        {
            if (playTimer != null) { playTimer.Stop(); playTimer.Dispose(); }
            if (session != null && session.TerrainBitmap != null) { session.TerrainBitmap.Dispose(); session.TerrainBitmap = null; }
            DebriefSoundPlayer.StopContinuousLoop();
        };
        RefreshReplayList();
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (session == null) return base.ProcessCmdKey(ref msg, keyData);
        if (ActiveControl is TextBoxBase) return base.ProcessCmdKey(ref msg, keyData);
        if (ActiveControl is ComboBox) return base.ProcessCmdKey(ref msg, keyData);

        Keys key = keyData & Keys.KeyCode;
        bool shift = (keyData & Keys.Shift) == Keys.Shift;

        if (key == Keys.Space)
        {
            TogglePlay();
            return true;
        }
        if (key == Keys.Left || key == Keys.Right)
        {
            ResetJamEvents();
            double step = shift ? 5.0 : 1.0;
            currentTime += (key == Keys.Left ? -step : step);
            if (currentTime < 0) currentTime = 0;
            if (currentTime > session.Duration) currentTime = session.Duration;
            UpdateFrame();
            return true;
        }
        if (key == Keys.Home)
        {
            ResetJamEvents();
            currentTime = 0;
            UpdateFrame();
            return true;
        }
        if (key == Keys.End)
        {
            ResetJamEvents();
            currentTime = session.Duration;
            UpdateFrame();
            return true;
        }
        if (key == Keys.Escape)
        {
            SetRulerMode(false);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void SetRulerMode(bool on)
    {
        rulerActive = on;
        if (!on) { rulerHasA = false; rulerHasB = false; }
        if (btnRuler != null)
        {
            btnRuler.BackColor = on ? Color.FromArgb(0, 110, 80) : Color.FromArgb(32, 40, 36);
            btnRuler.ForeColor = on ? Color.White : Color.FromArgb(190, 215, 205);
        }
        if (pnlRadar != null) pnlRadar.Invalidate();
    }

    private void SetLaunchesMode(bool on)
    {
        launchesMode = on;
        if (lstMistakes != null) lstMistakes.Visible = !on;
        if (lstLaunches != null) lstLaunches.Visible = on;
        if (pnlFiltersRef != null) pnlFiltersRef.Visible = !on;
        if (btnTabChrono != null)
        {
            btnTabChrono.BackColor = on ? Color.FromArgb(32, 40, 36) : Color.FromArgb(0, 110, 80);
            btnTabChrono.ForeColor = on ? Color.FromArgb(170, 190, 180) : Color.White;
        }
        if (btnTabLaunches != null)
        {
            btnTabLaunches.BackColor = on ? Color.FromArgb(0, 110, 80) : Color.FromArgb(32, 40, 36);
            btnTabLaunches.ForeColor = on ? Color.White : Color.FromArgb(170, 190, 180);
        }
        if (on) RefreshLaunchList();
    }

    private void RefreshLaunchList()
    {
        if (lstLaunches == null) return;
        lstLaunches.BeginUpdate();
        lstLaunches.Items.Clear();
        foreach (DebriefLaunch dl in launchList)
            lstLaunches.Items.Add(dl);
        lstLaunches.EndUpdate();
    }

    private void LstLaunches_DrawItem(object sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= lstLaunches.Items.Count) return;
        DebriefLaunch dl = (DebriefLaunch)lstLaunches.Items[e.Index];
        bool sel = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        Color bg = sel ? Color.FromArgb(30, 62, 50) : (e.Index % 2 == 0 ? Color.FromArgb(16, 20, 18) : Color.FromArgb(19, 24, 21));

        Color fg = Color.LightGray;
        Color accent = Color.Gray;
        if (dl.Side == "МОЙ") { fg = Color.FromArgb(140, 235, 175); accent = Color.FromArgb(0, 200, 110); }
        else if (dl.Side == "СОЮЗ") { fg = Color.FromArgb(150, 205, 235); accent = Color.FromArgb(90, 160, 210); }
        else if (dl.Side == "ВРАГ") { fg = Color.FromArgb(255, 170, 140); accent = Color.FromArgb(220, 90, 60); }

        using (SolidBrush br = new SolidBrush(bg)) e.Graphics.FillRectangle(br, e.Bounds);
        using (SolidBrush accentBr = new SolidBrush(accent))
            e.Graphics.FillRectangle(accentBr, e.Bounds.Left, e.Bounds.Top, 3, e.Bounds.Height);

        Rectangle line1 = new Rectangle(e.Bounds.Left + 8, e.Bounds.Top + 2, e.Bounds.Width - 12, 16);
        Rectangle line2 = new Rectangle(e.Bounds.Left + 8, e.Bounds.Top + 17, e.Bounds.Width - 12, 15);

        using (Font f1 = new Font("Consolas", 8.5f, FontStyle.Bold))
        using (Font f2 = new Font("Consolas", 7.5f))
        {
            string head = string.Format("{0}  🚀 {1}  {2}", FormatSec(dl.Time), dl.MissileName,
                string.IsNullOrEmpty(dl.Launcher) ? "" : "· " + dl.Launcher);
            using (SolidBrush fbr = new SolidBrush(fg))
                e.Graphics.DrawString(head, f1, fbr, line1);

            StringBuilder sub = new StringBuilder();
            sub.AppendFormat("{0}  ", dl.Side);
            if (!double.IsNaN(dl.LaunchDist)) sub.AppendFormat("D {0:F1} км  ", dl.LaunchDist / 1000.0);
            sub.AppendFormat("T {0:F0} с  ", dl.FlightTime);
            sub.AppendFormat("Цель: {0}  ", string.IsNullOrEmpty(dl.TargetName) ? "—" : dl.TargetName);
            sub.Append(dl.Result);
            using (SolidBrush fbr = new SolidBrush(Color.FromArgb(150, 170, 162)))
                e.Graphics.DrawString(sub.ToString(), f2, fbr, line2);
        }
    }

    private void LstLaunches_SelectedIndexChanged(object sender, EventArgs e)
    {
        DebriefLaunch dl = lstLaunches.SelectedItem as DebriefLaunch;
        if (dl == null || session == null) return;

        currentTime = dl.Time;
        foreach (DebriefEntity ent in session.Entities)
        {
            if (ent.Id != dl.MissileId || ent.Track.Count == 0) continue;
            camCenterX = ent.Track[0].X;
            camCenterY = ent.Track[0].Y;
            break;
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine(string.Format("[{0}]  ПУСК · {1}", FormatSec(dl.Time), dl.Side));
        sb.AppendLine(string.Format("Ракета: {0}", dl.MissileName));
        sb.AppendLine(string.Format("Носитель: {0}", string.IsNullOrEmpty(dl.Launcher) ? "—" : dl.Launcher));
        sb.AppendLine(string.Format("Цель: {0}", string.IsNullOrEmpty(dl.TargetName) ? "по треку не определена" : dl.TargetName));
        sb.AppendLine(string.Format("Дистанция пуска: {0}", double.IsNaN(dl.LaunchDist) ? "—" : string.Format("{0:F1} км", dl.LaunchDist / 1000.0)));
        sb.AppendLine(string.Format("Время полёта: {0:F0} с", dl.FlightTime));
        sb.AppendLine(string.Format("Результат: {0}", dl.Result));
        txtCoachAdvice.Text = sb.ToString();
        UpdateFrame();
    }

    private void FitCameraToBattle()
    {
        if (session == null) return;
        camCenterX = (session.BoundsMinX + session.BoundsMaxX) * 0.5;
        camCenterY = (session.BoundsMinY + session.BoundsMaxY) * 0.5;
        double spanX = Math.Max(15000.0, session.BoundsMaxX - session.BoundsMinX);
        double spanY = Math.Max(15000.0, session.BoundsMaxY - session.BoundsMinY);
        double ppmX = (pnlRadar.Width * 0.80) / spanX;
        double ppmY = (pnlRadar.Height * 0.80) / spanY;
        pixelsPerMeter = Math.Max(0.00008, Math.Min(0.06, Math.Min(ppmX, ppmY)));
    }
    private void TogglePlay()
    {
        if (session == null) return;
        if (currentTime >= session.Duration) currentTime = 0;
        isPlaying = !isPlaying;
        btnPlayPause.Text = isPlaying ? "⏸ ПАУЗА" : "▶ ПУСК";
        if (glRadar3D != null)
        {
            if (isPlaying) glRadar3D.ResumeJamContinuous();
            else glRadar3D.PauseJamContinuous();
        }
    }
    private void ResetJamEvents()
    {
        triggeredJamEvents.Clear();
        dirJamLock.Clear();
        if (glRadar3D != null) glRadar3D.ResetJamTriggers();
    }
    private void SeekTimelineByX(int mouseX)
    {
        if (session == null || pnlTimeline.Width <= 4) return;
        ResetJamEvents();
        double ratio = Math.Max(0.0, Math.Min(1.0, (double)mouseX / pnlTimeline.Width));
        currentTime = ratio * session.Duration;
        UpdateFrame();
    }
    private Button MakeFilterButton(string text, int index, bool active)
    {
        Button b = new Button()
        {
            Text = text,
            Left = 2 + index * 90,
            Top = 1,
            Width = 86,
            Height = 22,
            FlatStyle = FlatStyle.Flat,
            BackColor = active ? Color.FromArgb(0, 110, 80) : Color.FromArgb(32, 40, 36),
            ForeColor = active ? Color.White : Color.FromArgb(170, 190, 180),
            Font = new Font("Consolas", 8f, FontStyle.Bold),
            Tag = text
        };
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.BorderColor = Color.FromArgb(55, 70, 62);
        b.Click += (s, e) => SetEventFilter(text);
        return b;
    }

    private void SetEventFilter(string filter)
    {
        eventFilter = filter;
        Button[] btns = new Button[] { btnFilterAll, btnFilterThreats, btnFilterMistakes, btnFilterSuccess };
        foreach (Button b in btns)
        {
            if (b == null) continue;
            bool active = (string)b.Tag == filter;
            b.BackColor = active ? Color.FromArgb(0, 110, 80) : Color.FromArgb(32, 40, 36);
            b.ForeColor = active ? Color.White : Color.FromArgb(170, 190, 180);
        }
        ApplyEventFilter();
    }

    private void ApplyEventFilter()
    {
        if (lstMistakes == null) return;
        lstMistakes.BeginUpdate();
        lstMistakes.Items.Clear();
        foreach (TacticalEvent ev in currentEvents)
        {
            if (eventFilter == "ALL") { }
            else if (eventFilter == "УГРОЗЫ" && ev.Category != "THREAT") continue;
            else if (eventFilter == "ОШИБКИ" && ev.Category != "MISTAKE") continue;
            else if (eventFilter == "УСПЕХИ" && ev.Category != "SUCCESS") continue;
            lstMistakes.Items.Add(ev);
        }
        lstMistakes.EndUpdate();
    }

    private void RefreshReplayList()
    {
        cmbReplays.Items.Clear();
        replayFiles = DebriefEngine.FindAvailableReplays();
        foreach (string f in replayFiles)
        {
            bool isNativeSave = f.EndsWith(".vtr", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(f).Equals("info.vts", StringComparison.OrdinalIgnoreCase);
            string tag = isNativeSave ? "[VTOL-SAVE]" : (f.EndsWith(".acmi", StringComparison.OrdinalIgnoreCase) ? "[ACMI]" : "[КАРТА+ЛОГ]");
            string dispName = Path.GetFileName(f).Equals("info.vts", StringComparison.OrdinalIgnoreCase) ? Path.GetFileName(Path.GetDirectoryName(f)) : Path.GetFileName(f);
            cmbReplays.Items.Add(string.Format("{0} {1} ({2})", tag, dispName, File.GetLastWriteTime(f).ToString("dd.MM HH:mm")));
        }
        if (cmbReplays.Items.Count > 0)
            cmbReplays.SelectedIndex = 0;
        else
            lblTelemetryHud.Text = "Повторы пока не найдены.\nНажмите «📥 Из Player.log» или полетайте с включённым VTOL QoL.";
    }
    private void BtnBrowseFile_Click(object sender, EventArgs e)
    {
        OpenFileDialog ofd = new OpenFileDialog();
        string defRep = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Boundless Dynamics, LLC\VTOLVR\SaveData\Replays");
        if (System.IO.Directory.Exists(defRep)) ofd.InitialDirectory = defRep;
        ofd.Filter = "Все повторы VTOL (info.vts;*.vtr;*.vtolrep;*.acmi)|info.vts;*.vtr;*.vtolrep;*.acmi|Все файлы (*.*)|*.*";
        using (ofd)
        {
            if (ofd.ShowDialog(this) == DialogResult.OK)
            {
                replayFiles.Insert(0, ofd.FileName);
                cmbReplays.Items.Insert(0, "[FILE] " + Path.GetFileName(ofd.FileName));
                cmbReplays.SelectedIndex = 0;
            }
        }
    }

    private void LoadSelectedReplay()
    {
        int idx = cmbReplays.SelectedIndex;
        if (idx < 0 || idx >= replayFiles.Count) return;
        isPlaying = false;
        btnPlayPause.Text = "▶ ПУСК";
        if (session != null && session.TerrainBitmap != null)
        {
            session.TerrainBitmap.Dispose();
            session.TerrainBitmap = null;
        }
        try
        {
            string myNick = SettingsManager.Get("LastPilotNick", "");
            session = DebriefEngine.LoadSession(replayFiles[idx], myNick);
            currentTime = 0;
            smoothBadgeOffsets.Clear();
            triggeredJamEvents.Clear();
            launchList = new List<DebriefLaunch>(session.Launches ?? new List<DebriefLaunch>());
            rulerHasA = false;
            rulerHasB = false;
            lastJamSurgeTime = -99.0;
            lastJamBannerActor = "";
            if (glRadar3D != null) glRadar3D.StopJamContinuous();
            followEntityId = "";
            followPlayer = false;
            chkFollowPlayer.Checked = false;
            cmbFollowTarget.Items.Clear();
            cmbFollowTarget.Items.Add("Мой борт (авто)");
            foreach (DebriefEntity ent in session.Entities)
            {
                if (ent.Type == "Air")
                {
                    string side = ent.IsPlayer ? "ВЫ" : (ent.Coalition == "Allies" ? "СОЮЗ" : "ВРАГ");
                    cmbFollowTarget.Items.Add(string.Format("[{0}] {1} [{2}]", side, ent.Name, ent.Id));
                }
            }
            cmbFollowTarget.SelectedIndex = 0;
            FitCameraToBattle();

            currentEvents = new List<TacticalEvent>(session.Events);
            currentEvents.Sort((a, b) => a.Time.CompareTo(b.Time));
            SetEventFilter(eventFilter);
            RefreshLaunchList();
            SetLaunchesMode(false);
            UpdateSummary();
            UpdateCoachIntro();
            UpdateFrame();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Ошибка загрузки повтора: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    private void LstMistakes_DrawItem(object sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= lstMistakes.Items.Count) return;
        TacticalEvent ev = (TacticalEvent)lstMistakes.Items[e.Index];
        bool sel = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        Color bg = sel ? Color.FromArgb(30, 62, 50) : (e.Index % 2 == 0 ? Color.FromArgb(16, 20, 18) : Color.FromArgb(19, 24, 21));
        Color fg = Color.LightGray;
        Color accent = Color.Gray;
        string badge = "•";

        if (ev.Category == "THREAT") { fg = Color.FromArgb(255, 175, 90); accent = Color.FromArgb(230, 120, 60); }
        if (ev.Category == "MISTAKE") { fg = Color.FromArgb(255, 120, 110); accent = Color.FromArgb(220, 70, 70); }
        if (ev.Category == "SUCCESS") { fg = Color.FromArgb(110, 240, 150); accent = Color.FromArgb(0, 200, 110); }
        if (ev.Category == "INFO") { fg = Color.FromArgb(170, 200, 220); accent = Color.FromArgb(90, 130, 160); }

        if (ev.Kind == "THREAT") badge = "⚠ УГРОЗА";
        else if (ev.Kind == "RWR") badge = "📡 ОБЛУЧЕНИЕ";
        else if (ev.Kind == "JAMMER") badge = "⚡ РЭБ";
        else if (ev.Kind == "LAUNCH" && ev.Severity == "GOOD") badge = "🚀 ПУСК";
        else if (ev.Kind == "LAUNCH") badge = "🚀 ПУСК СОЮЗНИКА";
        else if (ev.Kind == "KILL") badge = "★ СБИТ";
        else if (ev.Kind == "GROUND_KILL") badge = "★ ЦЕЛЬ";
        else if (ev.Kind == "KIA" && ev.Severity == "WARNING") badge = "✚ ПОТЕРЯ";
        else if (ev.Kind == "KIA") badge = "✖ ПОТЕРЯ БОРТА";
        else if (ev.Kind == "CFIT") badge = "✖ CFIT";
        else if (ev.Kind == "DAMAGE") badge = "💥 ПОВРЕЖДЕНИЕ";
        else if (ev.Kind == "MISTAKE") badge = "⚠ ОШИБКА";
        else if (ev.Kind == "EJECT") badge = "🪂 КАТАПУЛЬТ";

        using (SolidBrush br = new SolidBrush(bg)) e.Graphics.FillRectangle(br, e.Bounds);
        using (SolidBrush accentBr = new SolidBrush(accent))
            e.Graphics.FillRectangle(accentBr, e.Bounds.Left, e.Bounds.Top, 3, e.Bounds.Height);

        Rectangle line1 = new Rectangle(e.Bounds.Left + 8, e.Bounds.Top + 2, e.Bounds.Width - 12, 16);
        Rectangle line2 = new Rectangle(e.Bounds.Left + 8, e.Bounds.Top + 17, e.Bounds.Width - 12, 15);

        using (Font f1 = new Font("Consolas", 8.5f, FontStyle.Bold))
        using (Font f2 = new Font("Consolas", 7.5f))
        {
            string head = string.Format("{0}  {1}  {2}", FormatSec(ev.Time), badge, ev.Title);
            using (SolidBrush fbr = new SolidBrush(fg))
                e.Graphics.DrawString(head, f1, fbr, line1);

            StringBuilder sub = new StringBuilder();
            if (!double.IsNaN(ev.Altitude)) sub.AppendFormat("H {0:F0} м  ", ev.Altitude);
            if (!double.IsNaN(ev.SpeedKmh)) sub.AppendFormat("V {0:F0} км/ч  ", ev.SpeedKmh);
            if (!string.IsNullOrEmpty(ev.Description))
            {
                string d = ev.Description.Replace("\r", " ").Replace("\n", " ");
                if (d.Length > 78) d = d.Substring(0, 75) + "...";
                sub.Append(d);
            }
            using (SolidBrush fbr = new SolidBrush(Color.FromArgb(150, 170, 162)))
                e.Graphics.DrawString(sub.ToString(), f2, fbr, line2);
        }
    }

    private void UpdateSummary()
    {
        if (lblSummary == null) return;
        if (session == null)
        {
            lblSummary.Text = "Сводка появится после загрузки повтора.";
            return;
        }
        int threats = 0, mistakes = 0, kills = 0;
        foreach (TacticalEvent ev in session.Events)
        {
            if (ev.Category == "THREAT") threats++;
            else if (ev.Category == "MISTAKE") mistakes++;
            else if (ev.Kind == "KILL" || ev.Kind == "GROUND_KILL") kills++;
        }
        StringBuilder sb = new StringBuilder();
        sb.AppendFormat("Сбито: {0}   Целей: {1}   Пусков: {2}\r\n", session.PlayerKills, session.AlliedKills, session.PlayerLaunches);
        sb.AppendFormat("Угроз: {0}   Ошибок: {1}   RWR: {2}\r\n", threats, mistakes, session.RwrSpikesCount);
        if (session.PlayerMaxG > 0.5)
            sb.AppendFormat("Макс. G: {0:F1}   Макс. V: {1:F0} км/ч   Взрывов: {2}", session.PlayerMaxG, session.PlayerMaxSpeed * 3.6,
                session.Explosions != null ? session.Explosions.Count : 0);
        else
            sb.AppendFormat("Длительность: {0}   Взрывов: {1}", FormatSec(session.Duration),
                session.Explosions != null ? session.Explosions.Count : 0);
        lblSummary.Text = sb.ToString();
    }

    private void UpdateCoachIntro()
    {
        if (txtCoachAdvice == null) return;
        if (session == null) return;
        int mistakes = 0;
        foreach (TacticalEvent ev in session.Events)
            if (ev.Category == "MISTAKE") mistakes++;

        if (mistakes == 0)
            txtCoachAdvice.Text = "Грубых ошибок пилота не зафиксировано.\r\nВыберите любое событие в хронологии, чтобы перейти к нему на карте.";
        else
            txtCoachAdvice.Text = string.Format("Зафиксировано ошибок пилота: {0}.\r\nВыберите запись с меткой «ОШИБКА» или «CFIT», чтобы перейти к моменту и увидеть разбор.", mistakes);
    }
    private void LstMistakes_SelectedIndexChanged(object sender, EventArgs e)
    {
        TacticalEvent ev = lstMistakes.SelectedItem as TacticalEvent;
        if (ev == null) return;
        ResetJamEvents();
        currentTime = ev.Time;
        if (Math.Abs(ev.WorldX) > 1.0 || Math.Abs(ev.WorldY) > 1.0)
        {
            camCenterX = ev.WorldX;
            camCenterY = ev.WorldY;
        }
        StringBuilder sb = new StringBuilder();
        string catLabel = ev.Category == "THREAT" ? "УГРОЗА"
                        : ev.Category == "MISTAKE" ? "ОШИБКА ПИЛОТА"
                        : ev.Category == "SUCCESS" ? "РЕЗУЛЬТАТ"
                        : "ИНФОРМАЦИЯ";
        sb.AppendLine(string.Format("[{0}]  {1}   ·   {2}", FormatSec(ev.Time), catLabel, ev.Title));
        if (!double.IsNaN(ev.Altitude) || !double.IsNaN(ev.SpeedKmh))
        {
            sb.Append("Параметры в этот момент: ");
            if (!double.IsNaN(ev.Altitude)) sb.AppendFormat("высота {0:F0} м; ", ev.Altitude);
            if (!double.IsNaN(ev.SpeedKmh)) sb.AppendFormat("скорость {0:F0} км/ч; ", ev.SpeedKmh);
            sb.Length -= 2;
            sb.AppendLine();
        }
        sb.AppendLine();
        sb.AppendLine(ev.Description);
        if (!string.IsNullOrEmpty(ev.Advice))
        {
            sb.AppendLine();
            sb.AppendLine("РЕКОМЕНДАЦИЯ:");
            sb.AppendLine(ev.Advice);
        }
        txtCoachAdvice.Text = sb.ToString();
        UpdateFrame();
    }
    private void UpdateFrame()
    {
        if (session == null) return;
        lblTimeHud.Text = string.Format("{0} / {1}", FormatSec(currentTime), FormatSec(session.Duration));

        DebriefEntity trackedEnt = null;
        if (!string.IsNullOrEmpty(followEntityId))
        {
            foreach (DebriefEntity ent in session.Entities)
                if (ent.Id == followEntityId) { trackedEnt = ent; break; }
        }
        if (trackedEnt == null)
        {
            foreach (DebriefEntity ent in session.Entities)
                if (ent.IsPlayer && ent.GetStateAt(currentTime) != null) { trackedEnt = ent; break; }
        }
        if (trackedEnt == null)
        {
            foreach (DebriefEntity ent in session.Entities)
                if (ent.IsPlayer) { trackedEnt = ent; break; }
        }

        if (trackedEnt != null)
        {
            DebriefSample st = trackedEnt.GetStateAt(currentTime);
            if (st != null)
            {
                if (followPlayer)
                {
                    camCenterX = st.X;
                    camCenterY = st.Y;
                }
                string modeTag = session.HasExactTelemetry ? "[3D-ТРЕК]" : "[ЛОГ+КАРТА]";
                lblTelemetryHud.Text = string.Format(
                    "{0} {1}\r\n" +
                    "H {2:F0} м   V {3:F0} км/ч\r\n" +
                    "HDG {4:F0}°   PITCH {5:+0;-0;0}°   BANK {6:+0;-0;0}°",
                    trackedEnt.Name, modeTag,
                    st.Alt, st.Speed * 3.6,
                    st.Heading, st.Pitch, st.Roll);
            }
        }

        if (is3DMode && glRadar3D != null) glRadar3D.Invalidate();
        else pnlRadar.Invalidate();
        pnlTimeline.Invalidate();
    }
    private PointF WorldToScreen(double wx, double wy, int w, int h)
    {
        return WorldToScreen3D(wx, wy, 0.0, w, h);
    }

    private PointF WorldToScreen3D(double wx, double wy, double alt, int w, int h)
    {
        double sx, sy;
        if (!is3DMode)
        {
            sx = w * 0.5 + (wx - camCenterX) * pixelsPerMeter;
            sy = h * 0.5 - (wy - camCenterY) * pixelsPerMeter;
        }
        else
        {
            double dx = wx - camCenterX;
            double dy = wy - camCenterY;
            double yawRad = camYawDeg * Math.PI / 180.0;
            double pitchRad = camPitchDeg * Math.PI / 180.0;

            double rx = dx * Math.Cos(yawRad) - dy * Math.Sin(yawRad);
            double ry = dx * Math.Sin(yawRad) + dy * Math.Cos(yawRad);

            double altScale = 1.0; // Строгие пропорции 1:1 (метры высоты == метры по горизонтали)
            sx = w * 0.5 + rx * pixelsPerMeter;
            sy = h * 0.52 - (ry * Math.Sin(pitchRad) + alt * altScale * Math.Cos(pitchRad)) * pixelsPerMeter;
        }

        if (double.IsNaN(sx) || double.IsInfinity(sx)) sx = 0;
        if (double.IsNaN(sy) || double.IsInfinity(sy)) sy = 0;
        float fx = (float)Math.Max(-15000.0, Math.Min(15000.0, sx));
        float fy = (float)Math.Max(-15000.0, Math.Min(15000.0, sy));
        return new PointF(fx, fy);
    }
    private void PnlRadar_Paint(object sender, PaintEventArgs e)
    {
        int w = pnlRadar.Width;
        int h = pnlRadar.Height;
        if (w <= 10 || h <= 10 || double.IsNaN(camCenterX) || double.IsNaN(camCenterY) || pixelsPerMeter <= 0) return;
        try
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.Bilinear;
            // 0. Отрисовка 2D-карты рельефа или 3D-каркаса гор
            if (!is3DMode && session != null && showTerrain && session.TerrainBitmap != null && session.MapSizeMeters > 100)
            {
                // Подложка рисуется в границах heightmap, а не всей карты
                double tMinX = session.TerrainSizeMeters > 100.0 ? session.TerrainMinX : session.MapMinX;
                double tMinY = session.TerrainSizeMeters > 100.0 ? session.TerrainMinY : session.MapMinY;
                double tSize = session.TerrainSizeMeters > 100.0 ? session.TerrainSizeMeters : session.MapSizeMeters;
                PointF topLeft = WorldToScreen(tMinX, tMinY + tSize, w, h);
                PointF botRight = WorldToScreen(tMinX + tSize, tMinY, w, h);
                float mapW = botRight.X - topLeft.X;
                float mapH = botRight.Y - topLeft.Y;
                if (mapW > 10 && mapH > 10)
                {
                    g.DrawImage(session.TerrainBitmap, topLeft.X, topLeft.Y, mapW, mapH);
                    using (Pen borderPen = new Pen(Color.FromArgb(65, 0, 255, 140), 1f))
                        g.DrawRectangle(borderPen, topLeft.X, topLeft.Y, mapW, mapH);
                }
            }
            else if (is3DMode && session != null && showTerrain && session.TerrainHeights != null && session.MapSizeMeters > 100)
            {
                // Высокодетальный 3D-ландшафт (130x130 полигонов по видимой области карты) с отсечением за границами экрана
                double viewSpan = Math.Min(session.MapSizeMeters, Math.Max(14000.0, Math.Max(w, h) * 1.35 / Math.Max(0.0008, pixelsPerMeter)));
                int meshN = 130;
                double stepM = viewSpan / meshN;
                double baseX = Math.Floor((camCenterX - viewSpan * 0.5) / stepM) * stepM;
                double baseY = Math.Floor((camCenterY - viewSpan * 0.5) / stepM) * stepM;

                PointF[,] pts = new PointF[meshN + 1, meshN + 1];
                double[,] alts = new double[meshN + 1, meshN + 1];
                for (int iy = 0; iy <= meshN; iy++)
                {
                    double wy = baseY + iy * stepM;
                    for (int ix = 0; ix <= meshN; ix++)
                    {
                        double wx = baseX + ix * stepM;
                        double gz = session.GetGroundAltitudeAt(wx, wy);
                        alts[ix, iy] = gz;
                        pts[ix, iy] = WorldToScreen3D(wx, wy, gz, w, h);
                    }
                }

                double yawRad = camYawDeg * Math.PI / 180.0;
                bool revX = Math.Sin(yawRad) < 0;
                bool revY = Math.Cos(yawRad) > 0;

                using (Pen contourPen = new Pen(Color.FromArgb(40, 95, 225, 160), 1.0f))
                {
                    for (int sy = 0; sy < meshN; sy++)
                    {
                        int iy = revY ? (meshN - 1 - sy) : sy;
                        for (int sx = 0; sx < meshN; sx++)
                        {
                            int ix = revX ? (meshN - 1 - sx) : sx;

                            PointF p00 = pts[ix, iy];
                            PointF p10 = pts[ix + 1, iy];
                            PointF p11 = pts[ix + 1, iy + 1];
                            PointF p01 = pts[ix, iy + 1];

                            if ((p00.X < -60 && p10.X < -60 && p11.X < -60 && p01.X < -60) ||
                                (p00.X > w + 60 && p10.X > w + 60 && p11.X > w + 60 && p01.X > w + 60) ||
                                (p00.Y < -60 && p10.Y < -60 && p11.Y < -60 && p01.Y < -60) ||
                                (p00.Y > h + 60 && p10.Y > h + 60 && p11.Y > h + 60 && p01.Y > h + 60))
                                continue;

                            double h00 = alts[ix, iy];
                            double h10 = alts[ix + 1, iy];
                            double h11 = alts[ix + 1, iy + 1];
                            double h01 = alts[ix, iy + 1];
                            double hAvg = (h00 + h10 + h11 + h01) * 0.25;

                            PointF[] quad = new PointF[] { p00, p10, p11, p01 };

                            Color fillCol;
                            if (hAvg <= 2.0)
                            {
                                fillCol = Color.FromArgb(245, 11, 25, 39);
                            }
                            else
                            {
                                double slopeX = (h10 - h00) / stepM;
                                double slopeY = (h01 - h00) / stepM;
                                double shade = Math.Max(-0.42, Math.Min(0.55, (-slopeX * 0.72 + slopeY * 0.72) * 2.1));

                                double norm = Math.Min(1.0, hAvg / 2000.0);
                                int r = (int)((22 + norm * 86) * (1.0 + shade));
                                int gr = (int)((52 + norm * 88) * (1.0 + shade));
                                int b = (int)((34 + norm * 62) * (1.0 + shade));

                                if (hAvg < 38.0) { r += 10; gr += 24; b += 20; }
                                if (hAvg > 1450.0) { r += 26; gr += 26; b += 30; }

                                r = Math.Max(8, Math.Min(230, r));
                                gr = Math.Max(16, Math.Min(240, gr));
                                b = Math.Max(14, Math.Min(225, b));
                                fillCol = Color.FromArgb(245, r, gr, b);
                            }

                            using (SolidBrush polyBr = new SolidBrush(fillCol))
                            using (Pen matchPen = new Pen(fillCol, 1.0f))
                            {
                                g.FillPolygon(polyBr, quad);
                                g.DrawPolygon(matchPen, quad);
                            }

                            int bandA = (int)(h00 / 250.0);
                            int bandB = (int)(h10 / 250.0);
                            int bandC = (int)(h01 / 250.0);
                            if (bandA != bandB) g.DrawLine(contourPen, p00, p10);
                            if (bandA != bandC) g.DrawLine(contourPen, p00, p01);
                        }
                    }
                }
            }


            // 1. Координатная сетка (уровень моря Z=0)
            double gridStepMeters = 10000.0;
            if (pixelsPerMeter > 0.04) gridStepMeters = 2000.0;
            else if (pixelsPerMeter < 0.004) gridStepMeters = 25000.0;
            double spanGrid = is3DMode ? Math.Max(w, h) * 1.1 / pixelsPerMeter : (w * 0.5) / pixelsPerMeter;
            double leftWorld = camCenterX - spanGrid;
            double rightWorld = camCenterX + spanGrid;
            double bottomWorld = camCenterY - spanGrid;
            double topWorld = camCenterY + spanGrid;
            double startX = Math.Floor(leftWorld / gridStepMeters) * gridStepMeters;
            double startY = Math.Floor(bottomWorld / gridStepMeters) * gridStepMeters;
            using (Pen gridPen = new Pen(Color.FromArgb(is3DMode ? 24 : 38, 0, 220, 120), 1))
            using (SolidBrush scaleBrush = new SolidBrush(Color.FromArgb(145, 205, 180)))
            {
                for (double gx = startX; gx <= rightWorld; gx += gridStepMeters)
                {
                    PointF p1 = WorldToScreen3D(gx, bottomWorld, 0, w, h);
                    PointF p2 = WorldToScreen3D(gx, topWorld, 0, w, h);
                    g.DrawLine(gridPen, p1, p2);
                }
                for (double gy = startY; gy <= topWorld; gy += gridStepMeters)
                {
                    PointF p1 = WorldToScreen3D(leftWorld, gy, 0, w, h);
                    PointF p2 = WorldToScreen3D(rightWorld, gy, 0, w, h);
                    g.DrawLine(gridPen, p1, p2);
                }
                string hint = is3DMode
                    ? string.Format("3D-РЕЖИМ (Yaw {0:F0}° / Pitch {1:F0}°) | ПКМ — вращение, ЛКМ — сдвиг | {2}", camYawDeg, camPitchDeg, session != null ? (session.MapTitle ?? "") : "")
                    : string.Format("Сетка: {0:F0} км | Колесо — зум, ЛКМ — сдвиг | {1}", gridStepMeters / 1000.0, session != null ? (session.MapTitle ?? "") : "");
                g.DrawString(hint, this.Font, scaleBrush, 10, h - 22);
            }
            if (session == null) return;
        if (!session.HasFullRadarCoords && !session.HasExactTelemetry)
        {
            string msg = "3D-КООРДИНАТЫ В ЭТОМ ФАЙЛЕ ОТСУТСТВУЮТ (ИМПОРТ ИЗ PLAYER.LOG)\n\n" +
                         "В текстовом логе игры нет координат юнитов (X, Y, Z).\n" +
                         "• Справа доступен полный разбор событий, пусков, RWR и ошибок пилота.\n" +
                         "• Для отображения 2D-карты загрузите файл Tacview (.acmi) или сохранённый в игре повтор (.vtr).";
            using (SolidBrush warnBr = new SolidBrush(Color.FromArgb(200, 255, 210, 90)))
                g.DrawString(msg, new Font("Consolas", 10.5f, FontStyle.Bold), warnBr, 36, h * 0.42f);
            return;
        }
        // Плавное инерционное размещение тактических HUD-плашек без рывков + плавное затухание (alpha)
        List<RectangleF> occupiedRects = new List<RectangleF>();
        Action<string, string, Font, Color, Color, float, float, bool, float> drawSmartBadge = (key, text, fnt, fgCol, borderCol, anchorX, anchorY, drawLeader, alphaFactor) =>
        {
            int aText = Math.Max(0, Math.Min(255, (int)(255 * alphaFactor)));
            int aBg = Math.Max(0, Math.Min(255, (int)(195 * alphaFactor)));
            int aBox = Math.Max(0, Math.Min(255, (int)(120 * alphaFactor)));
            if (aText < 12) return;

            SizeF sz = g.MeasureString(text, fnt);
            float padX = 5f, padY = 2f;
            float bw = sz.Width + padX * 2f;
            float bh = sz.Height + padY * 2f;

            // Компактные слоты вокруг объекта (справа, сверху, снизу, слева) — чтобы выносная линия всегда была короткой
            float[] dxOffsets = new float[] { 14f, -bw * 0.5f, -bw * 0.5f, -bw - 14f, 14f, 14f, -bw - 14f, -bw - 14f };
            float[] dyOffsets = new float[] { -bh * 0.5f, -bh - 14f, 14f, -bh * 0.5f, bh + 6f, -bh - 8f, bh + 6f, -bh - 8f };

            float targetDx = 14f;
            float targetDy = -bh * 0.5f;

            // Сначала проверяем прошлое смещение объекта (гистерезис — не меняем слот, пока старый не занят!)
            PointF prevOff;
            bool hasPrev = smoothBadgeOffsets.TryGetValue(key, out prevOff);
            bool prevValid = false;
            if (hasPrev)
            {
                RectangleF prevRect = new RectangleF(anchorX + prevOff.X, anchorY + prevOff.Y, bw, bh);
                if (prevRect.Left >= 4 && prevRect.Right <= w - 4 && prevRect.Top >= 4 && prevRect.Bottom <= h - 26)
                {
                    bool hit = false;
                    for (int r = 0; r < occupiedRects.Count; r++)
                    {
                        RectangleF ex = occupiedRects[r];
                        ex.Inflate(2f, 1f);
                        if (prevRect.IntersectsWith(ex)) { hit = true; break; }
                    }
                    if (!hit)
                    {
                        targetDx = prevOff.X;
                        targetDy = prevOff.Y;
                        prevValid = true;
                    }
                }
            }

            if (!prevValid)
            {
                for (int c = 0; c < dxOffsets.Length; c++)
                {
                    RectangleF cand = new RectangleF(anchorX + dxOffsets[c], anchorY + dyOffsets[c], bw, bh);
                    if (cand.Left < 4 || cand.Right > w - 4 || cand.Top < 4 || cand.Bottom > h - 26) continue;
                    bool hit = false;
                    for (int r = 0; r < occupiedRects.Count; r++)
                    {
                        RectangleF ex = occupiedRects[r];
                        ex.Inflate(3f, 2f);
                        if (cand.IntersectsWith(ex)) { hit = true; break; }
                    }
                    if (!hit)
                    {
                        targetDx = dxOffsets[c];
                        targetDy = dyOffsets[c];
                        break;
                    }
                }
            }

            // Плавная интерполяция смещения (Lerp) против резких скачков
            float curDx = targetDx;
            float curDy = targetDy;
            if (hasPrev)
            {
                curDx = prevOff.X + (targetDx - prevOff.X) * 0.25f;
                curDy = prevOff.Y + (targetDy - prevOff.Y) * 0.25f;
            }
            smoothBadgeOffsets[key] = new PointF(curDx, curDy);

            RectangleF chosen = new RectangleF(anchorX + curDx, anchorY + curDy, bw, bh);
            occupiedRects.Add(chosen);

            float distFromAnchor = Math.Abs((chosen.Left + chosen.Width * 0.5f) - anchorX) + Math.Abs((chosen.Top + chosen.Height * 0.5f) - anchorY);
            if (drawLeader || distFromAnchor > bw * 0.65f + 24f)
            {
                float targetX = chosen.Left > anchorX ? chosen.Left : chosen.Right;
                float targetY = chosen.Top + chosen.Height * 0.5f;
                using (Pen leadPen = new Pen(Color.FromArgb((int)(145 * alphaFactor), borderCol), 1.0f))
                    g.DrawLine(leadPen, anchorX, anchorY, targetX, targetY);
            }

            bool isWeaponTag = key.StartsWith("msl_");
            if (isWeaponTag)
            {
                // Компактный лёгкий бейдж для вооружения (без рамки и акцентной полосы)
                using (SolidBrush wBg = new SolidBrush(Color.FromArgb((int)(145 * alphaFactor), 8, 12, 11)))
                    g.FillRectangle(wBg, chosen.X + 1f, chosen.Y + 1f, chosen.Width - 2f, chosen.Height - 2f);
                using (SolidBrush txtBr = new SolidBrush(Color.FromArgb((int)(230 * alphaFactor), fgCol)))
                    g.DrawString(text, fnt, txtBr, chosen.X + padX, chosen.Y + padY);
            }
            else
            {
                // Полная тактическая карточка для самолётов, ЗРК, кораблей и боевых событий
                using (SolidBrush bgBr = new SolidBrush(Color.FromArgb(aBg, 11, 16, 15)))
                    g.FillRectangle(bgBr, chosen);
                using (Pen boxPen = new Pen(Color.FromArgb(aBox, borderCol), 1.0f))
                    g.DrawRectangle(boxPen, chosen.X, chosen.Y, chosen.Width, chosen.Height);
                using (SolidBrush accentBr = new SolidBrush(Color.FromArgb(aText, borderCol)))
                    g.FillRectangle(accentBr, chosen.X, chosen.Y, 2.5f, chosen.Height);
                using (SolidBrush txtBr = new SolidBrush(Color.FromArgb(aText, fgCol)))
                    g.DrawString(text, fnt, txtBr, chosen.X + padX, chosen.Y + padY);
            }
        };

        // 1.5 Базы, авианосцы, зоны ПВО и Bullseye
        using (Font poiFont = new Font("Consolas", 7.5f, FontStyle.Bold))
        {
            int poiIdx = 0;
            foreach (MapPoiMarker poi in session.MapMarkers)
            {
                poiIdx++;
                if (poi.RadiusMeters < 0 && currentTime > -poi.RadiusMeters) continue;
                if (!showAllies && poi.Coalition == "Allies" && poi.Kind != "Base") continue;
                    double poiZ = is3DMode ? session.GetGroundAltitudeAt(poi.X, poi.Y) : 0.0;
                    PointF pt = WorldToScreen3D(poi.X, poi.Y, poiZ, w, h);
                    if (poi.RadiusMeters > 100 && poi.Coalition == "Enemies")
                    {
                        using (Pen zPen = new Pen(Color.FromArgb(75, 255, 155, 35), 1.1f) { DashStyle = DashStyle.Dot })
                        {
                            if (!is3DMode)
                            {
                                float rPx = (float)(poi.RadiusMeters * pixelsPerMeter);
                                g.DrawEllipse(zPen, pt.X - rPx, pt.Y - rPx, rPx * 2, rPx * 2);
                            }
                            else
                            {
                                PointF[] ringPts = new PointF[36];
                                for (int k = 0; k < 36; k++)
                                {
                                    double ang = k * (Math.PI * 2.0 / 36.0);
                                    double rx = poi.X + Math.Cos(ang) * poi.RadiusMeters;
                                    double ry = poi.Y + Math.Sin(ang) * poi.RadiusMeters;
                                    ringPts[k] = WorldToScreen3D(rx, ry, poiZ, w, h);
                                }
                                g.DrawPolygon(zPen, ringPts);
                            }
                        }
                    }

                    if (pt.X < -60 || pt.X > w + 60 || pt.Y < -60 || pt.Y > h + 60) continue;

                if (poi.Kind == "Bullseye")
                {
                    using (Pen bPen = new Pen(Color.FromArgb(165, 255, 215, 0), 1.4f))
                    {
                        g.DrawEllipse(bPen, pt.X - 8, pt.Y - 8, 16, 16);
                        g.DrawLine(bPen, pt.X - 12, pt.Y, pt.X + 12, pt.Y);
                        g.DrawLine(bPen, pt.X, pt.Y - 12, pt.X, pt.Y + 12);
                    }
                    drawSmartBadge("poi_" + poiIdx, poi.Label, poiFont, Color.Gold, Color.Gold, pt.X, pt.Y, false, 1.0f);
                }
                else if (poi.Kind == "SAM")
                {
                    Color sc = poi.Coalition == "Enemies" ? Color.FromArgb(255, 155, 35) : Color.DeepSkyBlue;
                    using (Pen sPen = new Pen(sc, 1.5f))
                        g.DrawPolygon(sPen, new PointF[] { new PointF(pt.X, pt.Y - 6), new PointF(pt.X - 6, pt.Y + 5), new PointF(pt.X + 6, pt.Y + 5) });
                    if (pixelsPerMeter > 0.015)
                        drawSmartBadge("poi_" + poiIdx, poi.Label, poiFont, Color.FromArgb(220, sc), sc, pt.X, pt.Y, false, 1.0f);
                }
            }

            // 0.9 Аэродромы: ВПП, осевая и пороги
            if (session.Airfields != null && session.Airfields.Count > 0)
            {
                using (Font afFont = new Font("Consolas", 6.6f, FontStyle.Bold))
                {
                    int afIdx = 0;
                    foreach (MapPoiMarker af in session.Airfields)
                    {
                        afIdx++;
                        double hdg = af.Heading * Math.PI / 180.0;
                        double fx = Math.Sin(hdg), fz = Math.Cos(hdg);
                        double rx = Math.Cos(hdg), rz = -Math.Sin(hdg);
                        double L = 2400.0, W = 70.0;
                        Func<double, double, PointF> afPt = (along, side) =>
                            WorldToScreen3D(af.X + fx * along + rx * side, af.Y + fz * along + rz * side,
                                is3DMode ? session.GetGroundAltitudeAt(af.X + fx * along + rx * side, af.Y + fz * along + rz * side) : 0.0, w, h);
                        PointF a0 = afPt(-L * 0.5, -W * 0.5), a1 = afPt(L * 0.5, -W * 0.5);
                        PointF a2 = afPt(L * 0.5, W * 0.5), a3 = afPt(-L * 0.5, W * 0.5);
                        PointF[] strip = new PointF[] { a0, a1, a2, a3 };
                        using (SolidBrush fill = new SolidBrush(Color.FromArgb(190, 52, 56, 58)))
                            g.FillPolygon(fill, strip);
                        using (Pen edge = new Pen(Color.FromArgb(190, 200, 210, 205), 1.3f))
                            g.DrawPolygon(edge, strip);
                        // Осевая
                        using (Pen mid = new Pen(Color.FromArgb(150, 235, 240, 235), 1.1f) { DashStyle = DashStyle.Dash, DashPattern = new float[] { 6f, 5f } })
                            g.DrawLine(mid, afPt(-L * 0.45, 0), afPt(L * 0.45, 0));
                        // Пороги
                        using (Pen thr = new Pen(Color.FromArgb(190, 240, 245, 240), 1.6f))
                        {
                            g.DrawLine(thr, afPt(-L * 0.46, -W * 0.28), afPt(-L * 0.40, -W * 0.28));
                            g.DrawLine(thr, afPt(-L * 0.46, W * 0.28), afPt(-L * 0.40, W * 0.28));
                            g.DrawLine(thr, afPt(L * 0.40, -W * 0.28), afPt(L * 0.46, -W * 0.28));
                            g.DrawLine(thr, afPt(L * 0.40, W * 0.28), afPt(L * 0.46, W * 0.28));
                        }
                        PointF center = afPt(0, 0);
                        drawSmartBadge("af_" + afIdx, "🛫 " + af.Label, afFont,
                            Color.FromArgb(225, 220, 230), Color.FromArgb(170, 200, 215), center.X, center.Y, false, 1.0f);
                    }
                }
            }
        }

        // 1.75 Реальные взрывы из eventTrack реплея: короткая вспышка в момент t,
        // затем затухающий маркер, чтобы было видно, где именно сработало.
        if (session != null && session.Explosions != null && session.Explosions.Count > 0)
        {
            using (Font exFont = new Font("Consolas", 6.4f, FontStyle.Bold))
            {
                foreach (DebriefExplosion ex in session.Explosions)
                {
                    double age = currentTime - ex.Time;
                    if (age < 0.0 || age > 2.5) continue;

                    PointF ep = WorldToScreen3D(ex.X, ex.Y, ex.Alt, w, h);
                    if (ep.X < -40 || ep.X > w + 40 || ep.Y < -40 || ep.Y > h + 40) continue;

                    double fade = Math.Max(0.0, 1.0 - age / 2.5);
                    int a = (int)(235 * fade);
                    if (a <= 4) continue;

                    float flashR = age <= 0.35
                        ? (float)(4.0 + age * 60.0)
                        : (float)(6.0 + Math.Min(4.0, age));

                    if (age <= 0.35)
                    {
                        using (SolidBrush core = new SolidBrush(Color.FromArgb(Math.Min(255, a + 20), 255, 240, 180)))
                            g.FillEllipse(core, ep.X - flashR * 0.45f, ep.Y - flashR * 0.45f, flashR * 0.9f, flashR * 0.9f);
                    }
                    using (Pen ring = new Pen(Color.FromArgb(a, 255, 165, 40), age <= 0.35 ? 2.0f : 1.2f))
                        g.DrawEllipse(ring, ep.X - flashR, ep.Y - flashR, flashR * 2f, flashR * 2f);
                    using (Pen cross = new Pen(Color.FromArgb(a, 255, 120, 30), 1.0f))
                    {
                        g.DrawLine(cross, ep.X - flashR * 0.55f, ep.Y, ep.X + flashR * 0.55f, ep.Y);
                        g.DrawLine(cross, ep.X, ep.Y - flashR * 0.55f, ep.X, ep.Y + flashR * 0.55f);
                    }

                    if (age <= 0.8 && pixelsPerMeter > 0.008)
                    {
                        string lbl = "ВЗРЫВ " + ex.Time.ToString("F0") + "с";
                        SizeF ls = g.MeasureString(lbl, exFont);
                        using (SolidBrush lbg = new SolidBrush(Color.FromArgb((int)(170 * fade), 30, 18, 6)))
                            g.FillRectangle(lbg, ep.X + flashR + 2f, ep.Y - ls.Height * 0.5f, ls.Width + 4f, ls.Height);
                        using (SolidBrush lfg = new SolidBrush(Color.FromArgb(a, 255, 205, 130)))
                            g.DrawString(lbl, exFont, lfg, ep.X + flashR + 4f, ep.Y - ls.Height * 0.5f);
                    }
                }
            }
        }

        // 1.76 Отстрел ЛТЦ/ДО из eventTrack (eventType 2 = Flare, 3 = Chaff).
        // В текущих реплеях VTOL VR таких кадров нет — блок молча ничего не рисует,
        // но готов, если игра начнёт писать их в файл.
        if (session != null && session.Flares != null && session.Flares.Count > 0)
        {
            using (Font flFont = new Font("Consolas", 6.0f, FontStyle.Bold))
            {
                foreach (DebriefFlare fl in session.Flares)
                {
                    double age = currentTime - fl.Time;
                    if (age < 0.0 || age > 4.0) continue;

                    PointF fp = WorldToScreen3D(fl.X, fl.Y, fl.Alt, w, h);
                    if (fp.X < -30 || fp.X > w + 30 || fp.Y < -30 || fp.Y > h + 30) continue;

                    double fade = Math.Max(0.0, 1.0 - age / 4.0);
                    int a = (int)(220 * fade);
                    if (a <= 4) continue;

                    float r = 3.5f + (float)(age * 2.5);
                    Color c = fl.IsChaff ? Color.FromArgb(a, 200, 210, 225) : Color.FromArgb(a, 255, 120, 60);
                    using (Pen p = new Pen(c, 1.3f))
                        g.DrawEllipse(p, fp.X - r, fp.Y - r, r * 2f, r * 2f);
                    if (age <= 1.5)
                    {
                        string lbl = fl.IsChaff ? "ДО" : "ЛТЦ";
                        using (SolidBrush fg = new SolidBrush(c))
                            g.DrawString(lbl, flFont, fg, fp.X + r + 2f, fp.Y - 6f);
                    }
                }
            }
        }

        // 1.8 Анимированные лучи радаров, захватов АРЛ-ракет с индикатором дистанции (km) и подлётного времени (TTI)
        float pulse01 = (float)(0.5 + 0.5 * Math.Sin(currentTime * 6.5));
            Dictionary<string, string> jammedTargets = new Dictionary<string, string>(); // EntityId -> уровень помех
            if (showBeams)
            {
                using (Font beamTagFont = new Font("Consolas", 6.6f, FontStyle.Bold))
                {
                    Dictionary<string, DebriefSample> liveById = new Dictionary<string, DebriefSample>();
                    foreach (DebriefEntity ent in session.Entities)
                    {
                        DebriefSample st = ent.GetStateAt(currentTime);
                        if (st != null) liveById[ent.Id] = st;
                    }

                    foreach (TacticalBeam bm in session.Beams)
                    {
                        if (currentTime < bm.StartTime || currentTime > bm.EndTime) continue;

                        double fx = bm.FromX, fy = bm.FromY;
                        double tx = bm.ToX, ty = bm.ToY;
                        double dirTargetAlt = double.NaN;
                        // Ось NOISE-конуса и её разрешение (выставляются в блоке JAMMER)
                        double jamDirX = bm.DirX, jamDirY = bm.DirY, jamDirZ = bm.DirZ;
                        bool jamDirResolved = false;
                        double maxRay = 250000.0;              // предел поиска цели вдоль оси
                        double coneLenM = JammerModel.NoiseConeRangeM;

                        DebriefSample sFrom = null, sTo = null;
                        if (!string.IsNullOrEmpty(bm.FromId) && liveById.TryGetValue(bm.FromId, out sFrom))
                        {
                            fx = sFrom.X;
                            fy = sFrom.Y;
                        }
                        if (bm.Kind == "JAMMER")
                        {
                            foreach (TacticalEvent ev in session.Events)
                            {
                                if (ev.Kind != "JAMMER") continue;
                                double evAge = currentTime - ev.Time;
                                string evId = ev.Time.ToString("F1") + "_" + ev.Title;
                                if (isPlaying && evAge >= 0.0 && evAge <= 0.6 && !triggeredJamEvents.Contains(evId))
                                {
                                    triggeredJamEvents.Add(evId);
                                    lastJamSurgeTime = currentTime;
                                    lastJamBannerActor = ev.Title;
                                    DebriefSoundPlayer.Play("JAMMER_ON");
                                }
                            }
                            double horizLen = Math.Sqrt(bm.DirX * bm.DirX + bm.DirY * bm.DirY);
                            double ux2d = horizLen > 1e-4 ? (bm.DirX / horizLen) : 0.0;
                            double uy2d = horizLen > 1e-4 ? (bm.DirY / horizLen) : 0.0;

                            // Дальность станции РЭБ большая — конус не обрезаем по карте
                            maxRay = 250000.0;

                            string jamModeName = bm.JamMode ?? "";
                            if (jamModeName.Length == 0) jamModeName = bm.ToName ?? "";
                            bool isDirMode = jamModeName.IndexOf("DRFM", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                             jamModeName.IndexOf("SAS", StringComparison.OrdinalIgnoreCase) >= 0;

                            string srcCoalition = "Allies";
                            foreach (DebriefEntity se in session.Entities)
                                if (se.Id == bm.FromId) { srcCoalition = se.Coalition; break; }

                            // Логика 0.7.0: DRFM/SAS работают по конкретной цели, поэтому луч обрезаем
                            // на первом радиолокационном юните в коридоре вдоль оси, а бейдж ставим только ему.
                            // NOISE — зона подавления: бейджи на юнитах не ставим (по решению пользователя).
                            Func<DebriefEntity, bool> hasRadarSystem = (ent) =>
                            {
                                if (ent.Class == "Air" || ent.Class == "Chopper") return true;
                                if (ent.Class != "Ground") return false;
                                string nm = ent.Name ?? "";
                                if (nm.IndexOf("MANPADS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    nm.IndexOf("Infantry", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    nm.IndexOf("Artillery", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    nm.IndexOf("IFV", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    nm.IndexOf("Tank", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    nm.IndexOf("APC", StringComparison.OrdinalIgnoreCase) >= 0)
                                    return false;
                                return ent.RoleTag == "SAM" ||
                                       nm.IndexOf("Radar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       nm.IndexOf("Watchman", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       nm.IndexOf("SAM", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       nm.IndexOf("SAAW", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       nm.IndexOf("Ship", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       nm.IndexOf("Cruiser", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       nm.IndexOf("Carrier", StringComparison.OrdinalIgnoreCase) >= 0;
                            };

                            // Ось NOISE-конуса: в игре антенна наведена на подавляемую цель.
                            // Восстанавливаем цель по оси луча и удерживаем её, чтобы конус не «ехал» за бортом.
                            jamDirX = bm.DirX; jamDirY = bm.DirY; jamDirZ = bm.DirZ;
                            jamDirResolved = false;

                            if (isDirMode)
                            {
                                // Цель запоминается по ключу луча, чтобы пунктир не перескакивал
                                // между юнитами и не уходил с цели при её смещении.
                                string beamKey = (bm.FromId ?? "") + "|" + bm.StartTime.ToString("F1");
                                string hitEntityId = null;
                                string lockedId;
                                if (dirJamLock.TryGetValue(beamKey, out lockedId))
                                {
                                    if (liveById.ContainsKey(lockedId)) hitEntityId = lockedId;
                                    else dirJamLock.Remove(beamKey);
                                }
                                if (hitEntityId == null)
                                {
                                    double bestProj = double.MaxValue;
                                    foreach (DebriefEntity cand in session.Entities)
                                    {
                                        if (cand.Id == bm.FromId || cand.Coalition == srcCoalition || !hasRadarSystem(cand)) continue;
                                        DebriefSample cs;
                                        if (!liveById.TryGetValue(cand.Id, out cs)) continue;

                                        double dx = cs.X - fx;
                                        double dy = cs.Y - fy;
                                        double proj = dx * ux2d + dy * uy2d;
                                        if (proj <= 500.0 || proj >= maxRay) continue;

                                        double perp = Math.Abs(dx * (-uy2d) + dy * ux2d);
                                        double hitRadius = Math.Max(2500.0, proj * 0.10);
                                        if (perp > hitRadius) continue;
                                        if (proj < bestProj) { bestProj = proj; hitEntityId = cand.Id; }
                                    }
                                    if (hitEntityId != null) dirJamLock[beamKey] = hitEntityId;
                                }
                                DebriefSample tgtSmp;
                                if (hitEntityId == null || !liveById.TryGetValue(hitEntityId, out tgtSmp))
                                    continue;   // цель не найдена — луч DRFM/SAS «в никуда» не рисуем
                                tx = tgtSmp.X;
                                ty = tgtSmp.Y;
                                dirTargetAlt = tgtSmp.Alt;
                                jammedTargets[hitEntityId] = jamModeName.ToUpperInvariant();
                            }
                            else
                            {
                                // NOISE: одна цель на постановщик, пока она жива.
                                string noiseKey = (bm.FromId ?? "") + "|NOISE";
                                string noiseTargetId = null;
                                string lockedNoise;
                                if (dirJamLock.TryGetValue(noiseKey, out lockedNoise))
                                {
                                    if (liveById.ContainsKey(lockedNoise)) noiseTargetId = lockedNoise;
                                    else dirJamLock.Remove(noiseKey);
                                }
                                if (noiseTargetId == null)
                                {
                                    double bestProj = double.MaxValue;
                                    foreach (DebriefEntity cand in session.Entities)
                                    {
                                        if (cand.Id == bm.FromId || cand.Coalition == srcCoalition || !hasRadarSystem(cand)) continue;
                                        DebriefSample cs;
                                        if (!liveById.TryGetValue(cand.Id, out cs)) continue;
                                        double dx = cs.X - fx;
                                        double dy = cs.Y - fy;
                                        double proj = dx * ux2d + dy * uy2d;
                                        if (proj <= 500.0 || proj >= maxRay) continue;
                                        double perp = Math.Abs(dx * (-uy2d) + dy * ux2d);
                                        double hitRadius = Math.Max(3500.0, proj * 0.12);
                                        if (perp > hitRadius) continue;
                                        if (proj < bestProj) { bestProj = proj; noiseTargetId = cand.Id; }
                                    }
                                    if (noiseTargetId != null) dirJamLock[noiseKey] = noiseTargetId;
                                }
                                DebriefSample nTgt;
                                if (noiseTargetId != null && liveById.TryGetValue(noiseTargetId, out nTgt))
                                {
                                    double fz0 = sFrom != null ? sFrom.Alt : session.GetGroundAltitudeAt(fx, fy);
                                    jamDirX = nTgt.X - fx;
                                    jamDirY = nTgt.Y - fy;
                                    jamDirZ = nTgt.Alt - fz0;
                                    jamDirResolved = true;
                                    double d3 = Math.Sqrt(jamDirX * jamDirX + jamDirY * jamDirY + jamDirZ * jamDirZ);
                                    coneLenM = Math.Min(JammerModel.NoiseConeRangeM, Math.Max(8000.0, d3 * 1.25));
                                }
                                tx = fx + ux2d * coneLenM;
                                ty = fy + uy2d * coneLenM;
                            }
                        }
                        else if (!string.IsNullOrEmpty(bm.ToId) && liveById.TryGetValue(bm.ToId, out sTo))
                        {
                            tx = sTo.X;
                            ty = sTo.Y;
                        }

                        double fz = sFrom != null ? sFrom.Alt : session.GetGroundAltitudeAt(fx, fy);
                        double tz = fz;
                        if (bm.Kind == "JAMMER")
                        {
                            if (!double.IsNaN(dirTargetAlt))
                            {
                                tz = dirTargetAlt;   // направленный режим: строго в цель
                            }
                            else if (jamDirResolved)
                            {
                                // NOISE: конец луча — вдоль направления на подавляемую цель
                                double dl = Math.Sqrt(jamDirX * jamDirX + jamDirY * jamDirY + jamDirZ * jamDirZ);
                                if (dl > 1e-6)
                                {
                                    tx = fx + jamDirX / dl * coneLenM;
                                    ty = fy + jamDirY / dl * coneLenM;
                                    tz = fz + jamDirZ / dl * coneLenM;
                                }
                            }
                            else
                            {
                                double horizLen = Math.Sqrt(bm.DirX * bm.DirX + bm.DirY * bm.DirY);
                                double rayDist2d = Math.Sqrt(Math.Pow(tx - fx, 2) + Math.Pow(ty - fy, 2));
                                double ray3dLen = horizLen > 0.05 ? (rayDist2d / horizLen) : rayDist2d;
                                tz = Math.Max(session.GetGroundAltitudeAt(tx, ty), fz + bm.DirZ * ray3dLen);
                            }
                        }
                        else if (!string.IsNullOrEmpty(bm.ToId) && sTo != null)
                        {
                            tz = sTo.Alt;
                        }
                        
                        PointF p1 = WorldToScreen3D(fx, fy, fz, w, h);
                        PointF p2 = WorldToScreen3D(tx, ty, tz, w, h);

                        Color tagColor = Color.Empty;
                        if (bm.Kind == "RWR_SPIKE")
                        {
                            using (Pen rwrPen = new Pen(Color.FromArgb(215, 255, 50, 50), 1.7f) { DashStyle = DashStyle.Custom, DashPattern = new float[] { 6f, 4f }, DashOffset = beamDashPhase })
                                g.DrawLine(rwrPen, p1, p2);
                            float ringR = 14f + pulse01 * 9f;
                            int ringAlpha = (int)(220 - pulse01 * 90);
                            using (Pen ringPen = new Pen(Color.FromArgb(ringAlpha, 255, 50, 50), 2f))
                                g.DrawEllipse(ringPen, p2.X - ringR, p2.Y - ringR, ringR * 2f, ringR * 2f);
                            tagColor = Color.FromArgb(255, 85, 85);
                        }
                        else if (bm.Kind == "MY_LOCK" || bm.Kind == "PLAYER_LOCK")
                        {
                            using (Pen lockPen = new Pen(Color.FromArgb(220, 55, 255, 85), 1.9f) { DashStyle = DashStyle.Custom, DashPattern = new float[] { 8f, 3f }, DashOffset = beamDashPhase })
                                g.DrawLine(lockPen, p1, p2);
                            tagColor = Color.FromArgb(75, 255, 105);
                        }
                        else if (bm.Kind == "ALLY_LOCK")
                        {
                            using (Pen allyPen = new Pen(Color.FromArgb(205, 0, 215, 255), 1.7f) { DashStyle = DashStyle.Custom, DashPattern = new float[] { 8f, 3f }, DashOffset = beamDashPhase })
                                g.DrawLine(allyPen, p1, p2);
                            tagColor = Color.FromArgb(40, 220, 255);
                        }
                        else if (bm.Kind == "JAMMER")
                        {
                            float vx = p2.X - p1.X;
                            float vy = p2.Y - p1.Y;
                            float screenLen = (float)Math.Sqrt(vx * vx + vy * vy);
                            if (screenLen > 10f)
                            {
                                float ux = vx / screenLen;
                                float uy = vy / screenLen;
                                float px = -uy;
                                float py = ux;

                                string jamModeName = bm.JamMode ?? "";
                                if (jamModeName.Length == 0) jamModeName = bm.ToName ?? "";
                                bool isDrfm = jamModeName.IndexOf("DRFM", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                              jamModeName.IndexOf("SAS", StringComparison.OrdinalIgnoreCase) >= 0;

                                if (isDrfm)
                                {
                                    using (Pen glowPen = new Pen(Color.FromArgb(55, 215, 100, 255), 5.0f))
                                        g.DrawLine(glowPen, p1, p2);
                                    using (Pen corePen = new Pen(Color.FromArgb(230, 225, 130, 255), 1.8f) { DashStyle = DashStyle.Custom, DashPattern = new float[] { 10f, 4f, 2f, 4f }, DashOffset = beamDashPhase })
                                        g.DrawLine(corePen, p1, p2);
                                }
                                else
                                {
                                    // Конус сильного подавления: узкий и длинный, как реальный лепесток РЭБ.
                                    float tanCone = (float)Math.Tan(JammerModel.NoiseConeHalfAngleDeg * Math.PI / 180.0);
                                    float endHalfW = screenLen * tanCone;

                                    float jamPulse = (float)(0.5 + 0.5 * Math.Sin(currentTime * 1.6));
                                    int fillAlpha = (int)(12 + 10 * jamPulse);
                                    int edgeAlpha = (int)(70 + 30 * jamPulse);

                                    PointF apex = p1;
                                    PointF e1 = new PointF(p2.X + px * endHalfW, p2.Y + py * endHalfW);
                                    PointF e2 = new PointF(p2.X - px * endHalfW, p2.Y - py * endHalfW);
                                    using (SolidBrush coneFill = new SolidBrush(Color.FromArgb(fillAlpha, 160, 95, 255)))
                                        g.FillPolygon(coneFill, new PointF[] { apex, e1, e2 });

                                    // Шум внутри конуса: мерцающие штрихи помех.
                                    // Плотность и яркость падают к кромке (cos(theta)^8) и вдаль.
                                    {
                                        int aNear = Math.Min(225, (int)(45 + 95 * jamPulse));
                                        int aFar = Math.Min(150, (int)(22 + 55 * jamPulse));
                                        using (Pen nzNear = new Pen(Color.FromArgb(aNear, 205, 135, 255), 1.1f))
                                        using (Pen nzFar = new Pen(Color.FromArgb(aFar, 180, 110, 240), 1.0f))
                                        {
                                            for (int ni = 0; ni < 90; ni++)
                                            {
                                                double nh1 = Hash01(ni * 3 + 11);
                                                double nh2 = Hash01(ni * 3 + 12);
                                                double nh3 = Hash01(ni * 3 + 13);

                                                float nt = (float)(0.05 + 0.95 * nh1);
                                                float nlat = (float)((nh2 * 2.0 - 1.0) * nt);
                                                float nbx = apex.X + (p2.X - apex.X) * nt + px * nlat * endHalfW;
                                                float nby = apex.Y + (p2.Y - apex.Y) * nt + py * nlat * endHalfW;

                                                float ncos = (float)(1.0 / Math.Sqrt(1.0 + nlat * nlat * tanCone * tanCone));
                                                float nsig = (float)Math.Pow(ncos, 8.0) * (1f - 0.40f * nt);
                                                float nflick = (float)(0.30 + 0.70 * (0.5 + 0.5 * Math.Sin(currentTime * 17.0 + nh2 * 40.0)));
                                                if (nsig * nflick < 0.05f) continue;

                                                float nlen = 2.5f + 9.0f * (float)nh3 * (0.4f + nt);
                                                Pen npen = nsig * nflick > 0.40f ? nzNear : nzFar;
                                                g.DrawLine(npen,
                                                    nbx - px * nlen * 0.5f, nby - py * nlen * 0.5f,
                                                    nbx + px * nlen * 0.5f, nby + py * nlen * 0.5f);
                                            }
                                        }
                                    }

                                    using (Pen coneEdge = new Pen(Color.FromArgb(edgeAlpha, 205, 130, 255), 1.2f))
                                    {
                                        g.DrawLine(coneEdge, apex, e1);
                                        g.DrawLine(coneEdge, apex, e2);
                                        g.DrawLine(coneEdge, e1, e2);
                                    }
                                    using (Pen axisPen = new Pen(Color.FromArgb(edgeAlpha / 2, 225, 150, 255), 1.0f) { DashStyle = DashStyle.Dot })
                                        g.DrawLine(axisPen, apex, p2);
                                }
                            }
                        }
                        else if (bm.Kind == "AWACS_LINK")
                        {
                            using (Pen awPen = new Pen(Color.FromArgb(115, 175, 130, 255), 1.1f) { DashStyle = DashStyle.Dot })
                                g.DrawLine(awPen, p1, p2);
                        }
                        else
                        {
                            using (Pen scPen = new Pen(Color.FromArgb(100, 0, 255, 140), 1.1f) { DashStyle = DashStyle.Dot })
                                g.DrawLine(scPen, p1, p2);
                        }

                        // На лучах ракет ([АРЛ]) рисуем компактный индикатор дистанции (km) и подлётного времени (TTI)
                        if (!tagColor.IsEmpty && bm.FromName.StartsWith("[АРЛ]") && pixelsPerMeter > 0.005)
                        {
                            double distMeters = Math.Sqrt(Math.Pow(tx - fx, 2) + Math.Pow(ty - fy, 2));
                            double distKm = distMeters / 1000.0;
                        int ttiSec = Math.Max(1, (int)Math.Round(distMeters / 850.0)); // Оценка времени подлёта при средней скорости сближения ~850 м/с

                        float mx = (p1.X + p2.X) * 0.5f;
                        float my = (p1.Y + p2.Y) * 0.5f;
                        if (mx >= 10 && mx <= w - 10 && my >= 10 && my <= h - 25)
                        {
                            string ttiLabel = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F1}km · {1}s", distKm, ttiSec);
                            SizeF tSz = g.MeasureString(ttiLabel, beamTagFont);
                            RectangleF tRect = new RectangleF(mx - tSz.Width * 0.5f - 3f, my - tSz.Height * 0.5f - 1f, tSz.Width + 6f, tSz.Height + 2f);

                            using (SolidBrush tBg = new SolidBrush(Color.FromArgb(185, 10, 15, 14)))
                                g.FillRectangle(tBg, tRect);
                            using (Pen tPen = new Pen(Color.FromArgb(110, tagColor), 1f))
                                g.DrawRectangle(tPen, tRect.X, tRect.Y, tRect.Width, tRect.Height);
                            using (SolidBrush tFg = new SolidBrush(tagColor))
                                g.DrawString(ttiLabel, beamTagFont, tFg, tRect.X + 3f, tRect.Y + 1f);
                        }
                    }
                }
            }
        }


            // 2. Отрисовка траекторий, иконок, реального сектора БРЛС и одиночных непересекающихся плашек
            using (Font acFont = new Font("Consolas", 7.8f, FontStyle.Bold))
            using (Font wpnFont = new Font("Consolas", 6.8f, FontStyle.Italic))
            using (Font wpnBoldFont = new Font("Consolas", 6.8f, FontStyle.Bold | FontStyle.Italic))
            using (Font jamTagFont = new Font("Consolas", 6.8f, FontStyle.Bold))
            {
                Action<PointF, string> drawJamBadge = (pt, mode) =>
                {
                    float pulse = (float)(0.5 + 0.5 * Math.Sin(currentTime * 10.0));
                    int alpha = 185 + (int)(65 * pulse);
                    float ringR = 12f + pulse * 3f;

                    using (Pen ringPen = new Pen(Color.FromArgb(alpha, 210, 110, 255), 1.3f) { DashStyle = DashStyle.Dot })
                        g.DrawEllipse(ringPen, pt.X - ringR, pt.Y - ringR, ringR * 2f, ringR * 2f);

                    string txt = "⚡ ПОДАВЛЕНИЕ · " + mode;
                    SizeF sz = g.MeasureString(txt, jamTagFont);
                    float bx = pt.X - sz.Width * 0.5f;
                    float by = pt.Y - 26f;
                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(195, 22, 10, 36)))
                        g.FillRectangle(bg, bx - 4f, by - 1f, sz.Width + 8f, sz.Height + 2f);
                    using (SolidBrush bar = new SolidBrush(Color.FromArgb(alpha, 215, 115, 255)))
                        g.FillRectangle(bar, bx - 4f, by - 1f, 2.5f, sz.Height + 2f);
                    using (Pen bd = new Pen(Color.FromArgb(alpha / 2, 215, 115, 255), 1f))
                        g.DrawRectangle(bd, bx - 4f, by - 1f, sz.Width + 8f, sz.Height + 2f);
                    using (SolidBrush fg = new SolidBrush(Color.FromArgb(alpha, 238, 180, 255)))
                        g.DrawString(txt, jamTagFont, fg, bx, by);
                };

                foreach (DebriefEntity ent in session.Entities)
                {
                    if (!showAllies && !ent.IsPlayer && ent.Coalition == "Allies") continue;
                DebriefSample cur = ent.GetStateAt(currentTime);
                if (cur == null) continue;
                bool isMyMissile = (ent.Type == "Missile" && ent.Pilot != null && ent.Pilot.StartsWith("MY_MISSILE:"));
                Color col = ent.IsPlayer || isMyMissile
                    ? Color.FromArgb(55, 255, 85)
                    : (ent.Type == "Missile"
                        ? (ent.Coalition == "Enemies" ? Color.FromArgb(255, 175, 50) : Color.FromArgb(0, 215, 255))
                        : (ent.Coalition == "Enemies" ? Color.FromArgb(255, 155, 35) : Color.DeepSkyBlue));

                if (ent.Type == "Ground")
                {
                        PointF gp = WorldToScreen3D(cur.X, cur.Y,
                            Math.Max(cur.Alt, session.GetGroundAltitudeAt(cur.X, cur.Y)), w, h);
                        if (gp.X >= -30 && gp.X <= w + 30 && gp.Y >= -30 && gp.Y <= h + 30)
                    {
                        string uName = ent.Name ?? "";
                        bool isRadar = uName.IndexOf("Radar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       uName.IndexOf("Watchman", StringComparison.OrdinalIgnoreCase) >= 0;
                        bool isSam = !isRadar && (ent.RoleTag == "SAM" ||
                                     uName.IndexOf("SAM", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     uName.IndexOf("SAAW", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     uName.IndexOf("SLAM", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     uName.IndexOf("SRAD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     uName.IndexOf("CIWS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     uName.IndexOf("Anti-Air", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     uName.IndexOf("MANPADS", StringComparison.OrdinalIgnoreCase) >= 0);
                        bool isShip = ent.Class == "Sea" ||
                                      uName.IndexOf("Ship", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      uName.IndexOf("Cruiser", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      uName.IndexOf("Carrier", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      uName.IndexOf("Boat", StringComparison.OrdinalIgnoreCase) >= 0;
                        bool isArmor = uName.IndexOf("IFV", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       uName.IndexOf("Tank", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       uName.IndexOf("APC", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       uName.IndexOf("Truck", StringComparison.OrdinalIgnoreCase) >= 0;

                        using (SolidBrush bgFill = new SolidBrush(Color.FromArgb(180, 12, 18, 16)))
                        using (SolidBrush mainBr = new SolidBrush(col))
                        using (Pen mainPen = new Pen(col, 1.5f))
                        {
                            if (isShip)
                            {
                                PointF[] hull = new PointF[]
                                {
                                    new PointF(gp.X - 8f, gp.Y - 1f),
                                    new PointF(gp.X + 8f, gp.Y - 1f),
                                    new PointF(gp.X + 5f, gp.Y + 4f),
                                    new PointF(gp.X - 5f, gp.Y + 4f)
                                };
                                g.FillPolygon(bgFill, hull);
                                g.DrawPolygon(mainPen, hull);
                                g.FillRectangle(mainBr, gp.X - 2.5f, gp.Y - 4.5f, 5f, 3.5f);
                            }
                            else if (isRadar)
                            {
                                g.DrawArc(mainPen, gp.X - 6f, gp.Y - 6f, 12f, 12f, 200f, 140f);
                                g.DrawArc(mainPen, gp.X - 9f, gp.Y - 9f, 18f, 18f, 215f, 110f);
                                g.FillEllipse(mainBr, gp.X - 2.5f, gp.Y - 1f, 5f, 5f);
                            }
                            else if (isSam)
                            {
                                PointF[] tri = new PointF[]
                                {
                                    new PointF(gp.X, gp.Y - 7f),
                                    new PointF(gp.X - 6.5f, gp.Y + 5f),
                                    new PointF(gp.X + 6.5f, gp.Y + 5f)
                                };
                                g.FillPolygon(bgFill, tri);
                                g.DrawPolygon(mainPen, tri);
                                g.DrawLine(mainPen, gp.X, gp.Y - 4f, gp.X, gp.Y + 3f);
                            }
                            else if (isArmor)
                            {
                                g.FillRectangle(bgFill, gp.X - 6f, gp.Y - 4f, 12f, 8f);
                                g.DrawRectangle(mainPen, gp.X - 6f, gp.Y - 4f, 12f, 8f);
                                g.DrawEllipse(mainPen, gp.X - 3f, gp.Y - 2f, 6f, 4f);
                            }
                            else
                            {
                                PointF[] dia = new PointF[]
                                {
                                    new PointF(gp.X, gp.Y - 5.5f),
                                    new PointF(gp.X + 5.5f, gp.Y),
                                    new PointF(gp.X, gp.Y + 5.5f),
                                    new PointF(gp.X - 5.5f, gp.Y)
                                };
                                g.FillPolygon(bgFill, dia);
                                g.DrawPolygon(mainPen, dia);
                            }
                        }
                            string jModeTag;
                            if (jammedTargets.TryGetValue(ent.Id, out jModeTag))
                                drawJamBadge(gp, jModeTag);

                            if (pixelsPerMeter > 0.012 && !isSam)
                        {
                            using (Font gFont = new Font("Consolas", 7f, FontStyle.Regular))
                                drawSmartBadge(ent.Id, uName, gFont, Color.FromArgb(220, col), col, gp.X, gp.Y, false, 0.9f);
                        }
                    }
                    continue;
                }

                bool isSelectedUnit = (!string.IsNullOrEmpty(followEntityId) && ent.Id == followEntityId) ||
                      (string.IsNullOrEmpty(followEntityId) && ent.IsPlayer);
                List<PointF> trail = new List<PointF>();
                List<PointF> hotTrail = new List<PointF>();
                double minTrailT = (isSelectedUnit || ent.IsPlayer || isMyMissile) ? ent.FirstTime : Math.Max(ent.FirstTime, currentTime - 65.0);
                double hotWindow = ent.Type == "Missile" ? 4.0 : (isSelectedUnit ? 22.0 : 14.0);


                    for (int i = 0; i < ent.Track.Count; i++)
                    {
                        DebriefSample s = ent.Track[i];
                        if (s.Time < minTrailT) continue;
                        if (s.Time > currentTime) break;
                        PointF pt = WorldToScreen3D(s.X, s.Y, s.Alt, w, h);
                        trail.Add(pt);
                        if (currentTime - s.Time <= hotWindow)
                            hotTrail.Add(pt);
                    }
                    PointF curPt = WorldToScreen3D(cur.X, cur.Y, cur.Alt, w, h);


                    trail.Add(curPt);
                hotTrail.Add(curPt);

                if (trail.Count >= 2)
                {
                    using (Pen trailPen = new Pen(Color.FromArgb(ent.Type == "Missile" ? 95 : 110, col), ent.Type == "Missile" ? 1.2f : 1.7f))
                    {
                        if (ent.Type == "Missile") trailPen.DashStyle = DashStyle.Dash;
                        g.DrawLines(trailPen, trail.ToArray());
                    }
                }
                if (hotTrail.Count >= 2)
                {
                    using (Pen hotPen = new Pen(Color.FromArgb(225, col), ent.Type == "Missile" ? 1.9f : 2.5f))
                        g.DrawLines(hotPen, hotTrail.ToArray());
                }
            }

            for (int pass = 0; pass < 2; pass++)
            {
                foreach (DebriefEntity ent in session.Entities)
                {
                    if (ent.Type == "Ground") continue;
                    if (pass == 0 && ent.Type == "Missile") continue;
                    if (pass == 1 && ent.Type != "Missile") continue;
                    if (!showAllies && !ent.IsPlayer && ent.Coalition == "Allies") continue;

                        DebriefSample cur = ent.GetStateAt(currentTime);
                        if (cur == null) continue;
                        PointF pos = WorldToScreen3D(cur.X, cur.Y, cur.Alt, w, h);
                        if (pos.X < -40 || pos.X > w + 40 || pos.Y < -40 || pos.Y > h + 40) continue;

                        if (is3DMode)
                        {
                            Color stemCol = (ent.IsPlayer || (ent.Type == "Missile" && ent.Pilot != null && ent.Pilot.StartsWith("MY_MISSILE:")))
                                ? Color.FromArgb(55, 255, 85)
                                : (ent.Coalition == "Enemies" ? Color.FromArgb(255, 155, 35) : Color.DeepSkyBlue);
                            double groundZ = session.GetGroundAltitudeAt(cur.X, cur.Y);
                            PointF gndPt = WorldToScreen3D(cur.X, cur.Y, groundZ, w, h);
                            using (Pen dropPen = new Pen(Color.FromArgb(ent.Type == "Missile" ? 55 : 95, stemCol), 1.0f) { DashStyle = DashStyle.Dot })
                                g.DrawLine(dropPen, pos, gndPt);
                            using (SolidBrush dotBr = new SolidBrush(Color.FromArgb(130, stemCol)))
                                g.FillEllipse(dotBr, gndPt.X - 2f, gndPt.Y - 2f, 4f, 4f);
                        }

                        bool isMyMissile = (ent.Type == "Missile" && ent.Pilot != null && ent.Pilot.StartsWith("MY_MISSILE:"));
                    Color col = ent.IsPlayer || isMyMissile
                        ? Color.FromArgb(55, 255, 85)
                        : (ent.Type == "Missile"
                            ? (ent.Coalition == "Enemies" ? Color.FromArgb(255, 175, 50) : Color.FromArgb(0, 215, 255))
                            : (ent.Coalition == "Enemies" ? Color.FromArgb(255, 155, 35) : Color.DeepSkyBlue));

                    if (ent.Type == "Missile")
                    {
                        using (SolidBrush glowBr = new SolidBrush(Color.FromArgb(80, col)))
                            g.FillEllipse(glowBr, pos.X - 6.5f, pos.Y - 6.5f, 13f, 13f);
                        using (SolidBrush mBr = new SolidBrush(col))
                            g.FillEllipse(mBr, pos.X - 3.5f, pos.Y - 3.5f, 7f, 7f);
                        double tLeft = ent.LastTime - currentTime;
                        if (tLeft >= 0 && tLeft <= 2.5 && ent.LastTime < session.Duration - 1.5)
                        {
                            float rSize = 7f + (float)((2.5 - tLeft) * 3.0);
                            using (Pen termPen = new Pen(Color.FromArgb(210, col), 1.4f))
                            {
                                g.DrawPolygon(termPen, new PointF[]
                                {
new PointF(pos.X, pos.Y - rSize),
new PointF(pos.X + rSize, pos.Y),
new PointF(pos.X, pos.Y + rSize),
new PointF(pos.X - rSize, pos.Y)
                                });
                            }
                        }
                        drawSmartBadge("msl_" + ent.Id, ent.Name, isMyMissile ? wpnBoldFont : wpnFont, col, col, pos.X, pos.Y, false, 1.0f);
                    }
                    else
                    {
                        bool isSelectedUnit = (!string.IsNullOrEmpty(followEntityId) && ent.Id == followEntityId) ||
                                              (string.IsNullOrEmpty(followEntityId) && ent.IsPlayer);

                            double rad = cur.Heading * Math.PI / 180.0;
                            float dx = (float)Math.Sin(rad);
                            float dy = -(float)Math.Cos(rad);
                            if (is3DMode)
                            {
                                double aheadX = cur.X + Math.Sin(rad) * 600.0;
                                double aheadY = cur.Y + Math.Cos(rad) * 600.0;
                                PointF pAhead = WorldToScreen3D(aheadX, aheadY, cur.Alt, w, h);
                                float vx = pAhead.X - pos.X;
                                float vy = pAhead.Y - pos.Y;
                                float vLen = (float)Math.Sqrt(vx * vx + vy * vy);
                                if (vLen > 0.5f)
                                {
                                    dx = vx / vLen;
                                    dy = vy / vLen;
                                }
                            }


                            if (isSelectedUnit)
                        {
                            // Строгая уголковая рамка фокуса выбранного борта (в стиле MFD / AWACS)
                            Color selCol = Color.FromArgb(220, 245, 250, 248);
                            float box = 16f;
                            float arm = 5f;
                            using (Pen selPen = new Pen(selCol, 1.4f))
                            {
                                g.DrawLine(selPen, pos.X - box, pos.Y - box, pos.X - box + arm, pos.Y - box);
                                g.DrawLine(selPen, pos.X - box, pos.Y - box, pos.X - box, pos.Y - box + arm);

                                g.DrawLine(selPen, pos.X + box, pos.Y - box, pos.X + box - arm, pos.Y - box);
                                g.DrawLine(selPen, pos.X + box, pos.Y - box, pos.X + box, pos.Y - box + arm);

                                g.DrawLine(selPen, pos.X - box, pos.Y + box, pos.X - box + arm, pos.Y + box);
                                g.DrawLine(selPen, pos.X - box, pos.Y + box, pos.X - box, pos.Y + box - arm);

                                g.DrawLine(selPen, pos.X + box, pos.Y + box, pos.X + box - arm, pos.Y + box);
                                g.DrawLine(selPen, pos.X + box, pos.Y + box, pos.X + box, pos.Y + box - arm);
                            }
                        }

                        // Силуэт истребителя: нос, стреловидные крылья и хвостовое оперение
                        Func<float, float, PointF> acPt = (f, r) =>
                            new PointF(pos.X + dx * f - dy * r, pos.Y + dy * f + dx * r);
                        PointF[] acSil = new PointF[]
                        {
                            acPt(13f, 0f), acPt(-1f, -3.5f), acPt(-2f, -9f), acPt(-10f, -4f),
                            acPt(-8f, 0f), acPt(-10f, 4f), acPt(-2f, 9f), acPt(-1f, 3.5f)
                        };
                        using (SolidBrush acBr = new SolidBrush(col))
                            g.FillPolygon(acBr, acSil);
                        using (Pen acPen = new Pen(Color.FromArgb(210, 12, 18, 16), 1f))
                            g.DrawPolygon(acPen, acSil);
                        using (Pen vecPen = new Pen(Color.FromArgb(150, col), 1.2f))
                            g.DrawLine(vecPen, pos.X, pos.Y, pos.X + dx * 24, pos.Y + dy * 24);
                            string jModeTag;
                            if (jammedTargets.TryGetValue(ent.Id, out jModeTag))
                                drawJamBadge(pos, jModeTag);

                            string vsTag = "";
                        DebriefSample prevS = ent.GetStateAt(Math.Max(ent.FirstTime, currentTime - 1.0));
                        if (prevS != null && currentTime - prevS.Time > 0.2)
                        {
                            double vs = (cur.Alt - prevS.Alt) / (currentTime - prevS.Time);
                            if (vs > 15.0) vsTag = " ▲";
                            else if (vs < -15.0) vsTag = " ▼";
                        }

                        string unitTitle = !string.IsNullOrEmpty(ent.Pilot) && ent.Pilot != "AI Bandit" && ent.Pilot != ent.Name ? ent.Pilot : ent.Name;
                        string label = string.Format("{0}\n{1:F0}m{2} · {3:F0}km/h", unitTitle, cur.Alt, vsTag, cur.Speed * 3.6);
                        Color badgeBorder = isSelectedUnit ? Color.FromArgb(230, 245, 250, 248) : col;
                        drawSmartBadge(ent.Id, label, acFont, col, badgeBorder, pos.X, pos.Y, false, 1.0f);
                    }
                }
            }
        }
        // 4. Живой тактический HUD-виджет в левом верхнем углу планшета
        int myActiveMsl = 0, allyActiveMsl = 0, enemyActiveMsl = 0;
        DebriefEntity activeTracked = null;
        DebriefSample activeTrackedSample = null;

        foreach (DebriefEntity ent in session.Entities)
        {
            DebriefSample st = ent.GetStateAt(currentTime);
            if (st == null) continue;
            if (!string.IsNullOrEmpty(followEntityId) && ent.Id == followEntityId)
            {
                activeTracked = ent;
                activeTrackedSample = st;
            }
            else if (string.IsNullOrEmpty(followEntityId) && ent.IsPlayer)
            {
                activeTracked = ent;
                activeTrackedSample = st;
            }

            if (ent.Type == "Missile")
            {
                bool isMine = ent.Pilot != null && ent.Pilot.StartsWith("MY_MISSILE:");
                if (isMine) myActiveMsl++;
                else if (ent.Coalition == "Allies") allyActiveMsl++;
                else enemyActiveMsl++;
            }
        }

        int activeRwrLocks = 0;
        if (activeTracked != null)
        {
            foreach (TacticalBeam bm in session.Beams)
            {
                if (currentTime >= bm.StartTime && currentTime <= bm.EndTime &&
                    bm.Kind == "RWR_SPIKE" && bm.ToName.Equals(activeTracked.Name, StringComparison.OrdinalIgnoreCase))
                    activeRwrLocks++;
            }
        }

        using (Font hudTitleFont = new Font("Consolas", 8f, FontStyle.Bold))
        using (Font hudBodyFont = new Font("Consolas", 7.8f, FontStyle.Regular))
        {
            float hx = 12f, hy = 12f, hw = 315f;
            float hh = activeTrackedSample != null ? (activeRwrLocks > 0 ? 102f : 86f) : 52f;

            using (SolidBrush hudBg = new SolidBrush(Color.FromArgb(195, 10, 16, 15)))
                g.FillRectangle(hudBg, hx, hy, hw, hh);
            Color hudBorder = activeRwrLocks > 0 ? Color.FromArgb(220, 255, 55, 55) : Color.FromArgb(110, 0, 255, 130);
            using (Pen hudPen = new Pen(hudBorder, activeRwrLocks > 0 ? 1.4f : 1.0f))
                g.DrawRectangle(hudPen, hx, hy, hw, hh);
            using (SolidBrush sideBar = new SolidBrush(hudBorder))
                g.FillRectangle(sideBar, hx, hy, 3f, hh);

            float curY = hy + 7f;
            if (activeRwrLocks > 0)
            {
                using (SolidBrush rwrBr = new SolidBrush(Color.FromArgb(255, 75, 75)))
                    g.DrawString(string.Format("⚠ ЗАХВАТ БОРТА ПО RWR: {0} ИСТОЧН.", activeRwrLocks), hudTitleFont, rwrBr, hx + 10f, curY);
                curY += 17f;
            }

            string mslSummary = string.Format("РАКЕТЫ В ВОЗДУХЕ: МОИ {0} · СОЮЗ {1} · ВРАГ {2}", myActiveMsl, allyActiveMsl, enemyActiveMsl);
            using (SolidBrush hdrBr = new SolidBrush(Color.FromArgb(0, 255, 135)))
                g.DrawString(mslSummary, hudTitleFont, hdrBr, hx + 10f, curY);
            curY += 19f;

            if (activeTrackedSample != null)
            {
                double mach = activeTrackedSample.Speed / 315.0;
                string machStr = mach.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                string roleTag = activeTracked.IsPlayer ? "[ВЫ]" : (activeTracked.Coalition == "Allies" ? "[СОЮЗ]" : "[ВРАГ]");
                string line1 = string.Format("БОРТ {0}: {1}", roleTag, activeTracked.Name);
                string line2 = string.Format("HDG {0:000}° · ALT {1:F0}m · {2:F0}km/h (M{3})",
                    activeTrackedSample.Heading, activeTrackedSample.Alt, activeTrackedSample.Speed * 3.6, machStr);
                using (SolidBrush b1 = new SolidBrush(Color.FromArgb(225, 240, 230)))
                    g.DrawString(line1, hudBodyFont, b1, hx + 10f, curY);
                using (SolidBrush b2 = new SolidBrush(Color.FromArgb(170, 235, 195)))
                    g.DrawString(line2, hudBodyFont, b2, hx + 10f, curY + 17f);
                using (SolidBrush b3 = new SolidBrush(Color.FromArgb(120, 180, 150)))
                    g.DrawString("Пробел — пауза | Ctrl+ЛКМ — линейка", hudBodyFont, b3, hx + 10f, curY + 35f);
            }
            else
            {
                using (SolidBrush bOff = new SolidBrush(Color.FromArgb(150, 175, 165)))
                    g.DrawString("ВЫБРАННЫЙ БОРТ ВНЕ ВОЗДУХА", hudBodyFont, bOff, hx + 10f, curY);
            }
        }



        // 3. Анимированные маркеры взрывов, сбитий и оповещения (с волной детонации и плавным затуханием)
        using (Font evFont = new Font("Consolas", 8f, FontStyle.Bold))
        {
            int evIdx = 0;
            foreach (TacticalEvent ev in session.Events)
            {
                evIdx++;
                string evKey = "ev_" + evIdx + "_" + ev.Time.ToString("F1");
                double age = currentTime - ev.Time;
                if (ev.Kind == "GROUND_KILL" && currentTime > 1.5 && age >= 0.0 && age <= 5.0 && (Math.Abs(ev.WorldX) > 1.0 || Math.Abs(ev.WorldY) > 1.0))
                {
                    PointF gp = WorldToScreen(ev.WorldX, ev.WorldY, w, h);
                    if (gp.X >= -40 && gp.X <= w + 40 && gp.Y >= -40 && gp.Y <= h + 40)
                    {
                        float fade = age > 3.8 ? (float)((5.0 - age) / 1.2) : 1.0f;
                        if (age < 1.4)
                        {
                            float blastR = (float)(6.0 + age * 26.0);
                            int blastAlpha = Math.Max(0, (int)(220 * (1.0 - age / 1.4)));
                            using (Pen blastPen = new Pen(Color.FromArgb(blastAlpha, 255, 190, 60), 2.2f))
                                g.DrawEllipse(blastPen, gp.X - blastR, gp.Y - blastR, blastR * 2f, blastR * 2f);
                        }
                        using (Pen kPen = new Pen(Color.FromArgb((int)(255 * fade), 55, 255, 85), 2.2f))
                        {
                            g.DrawLine(kPen, gp.X - 8, gp.Y - 8, gp.X + 8, gp.Y + 8);
                            g.DrawLine(kPen, gp.X + 8, gp.Y - 8, gp.X - 8, gp.Y + 8);
                        }
                        drawSmartBadge(evKey, "💥 " + ev.Title, evFont, Color.FromArgb(55, 255, 85), Color.FromArgb(55, 255, 85), gp.X, gp.Y, true, fade);
                    }
                    continue;
                }
                if (currentTime > 1.5 && age >= 0.0 && age <= 5.0 && (Math.Abs(ev.WorldX) > 1.0 || Math.Abs(ev.WorldY) > 1.0))
                {
                    PointF ep = WorldToScreen(ev.WorldX, ev.WorldY, w, h);
                    if (ep.X < -60 || ep.X > w + 60 || ep.Y < -60 || ep.Y > h + 60) continue;
                    float fade = age > 3.8 ? (float)((5.0 - age) / 1.2) : 1.0f;
                    if (ev.Kind == "KILL")
                    {
                        if (age < 1.5)
                        {
                            float blastR = (float)(8.0 + age * 28.0);
                            int blastAlpha = Math.Max(0, (int)(230 * (1.0 - age / 1.5)));
                            using (Pen blastPen = new Pen(Color.FromArgb(blastAlpha, 55, 255, 85), 2.2f))
                                g.DrawEllipse(blastPen, ep.X - blastR, ep.Y - blastR, blastR * 2f, blastR * 2f);
                        }
                        using (Pen kPen = new Pen(Color.FromArgb((int)(255 * fade), 55, 255, 85), 2.2f))
                        {
                            g.DrawLine(kPen, ep.X - 8, ep.Y - 8, ep.X + 8, ep.Y + 8);
                            g.DrawLine(kPen, ep.X + 8, ep.Y - 8, ep.X - 8, ep.Y + 8);
                        }
                        drawSmartBadge(evKey, "★ " + ev.Title, evFont, Color.FromArgb(55, 255, 85), Color.FromArgb(55, 255, 85), ep.X, ep.Y, true, fade);
                    }
                    else if (ev.Kind == "DAMAGE")
                    {
                        using (Pen dPen = new Pen(Color.FromArgb((int)(255 * fade), Color.Orange), 1.8f))
                            g.DrawRectangle(dPen, ep.X - 8, ep.Y - 8, 16, 16);
                        drawSmartBadge(evKey, "💥 " + ev.Title, evFont, Color.Orange, Color.Orange, ep.X, ep.Y, true, fade);
                    }
                    else if (ev.Kind == "MISTAKE" || ev.Kind == "KIA" || ev.Kind == "THREAT")
                    {
                        Color ec = ev.Severity == "CRITICAL" ? Color.FromArgb(255, 55, 55) : Color.Gold;
                        float waveR = 12f + (float)((age % 1.0) * 18.0);
                        int waveA = Math.Max(0, (int)(220 * (1.0 - (age % 1.0)) * fade));
                            using (Pen ringPen = new Pen(Color.FromArgb(waveA, ec), 2f))
                                g.DrawEllipse(ringPen, ep.X - waveR, ep.Y - waveR, waveR * 2f, waveR * 2f);
                            drawSmartBadge(evKey, "⚠ " + ev.Title, evFont, ec, ec, ep.X, ep.Y, true, fade);
                        }
                    }
                }
            }
            double surgeElapsed = currentTime - lastJamSurgeTime;
            if (surgeElapsed >= 0.0 && surgeElapsed <= 0.75)
            {
                // Плавный синусоидальный импульс перепада напряжения (нарастает и мягко затухает без дёргания)
                float wave = (float)Math.Sin((surgeElapsed / 0.75) * Math.PI);
                using (SolidBrush tintBr = new SolidBrush(Color.FromArgb((int)(16 * wave), 185, 110, 255)))
                    g.FillRectangle(tintBr, 0, 0, w, h);

                // Мягкая виньетка по краям экрана и двойная неоновая рамка
                int edgeAlpha = (int)(110 * wave);
                using (Pen outerPen = new Pen(Color.FromArgb(edgeAlpha, 210, 120, 255), 3f))
                    g.DrawRectangle(outerPen, 1.5f, 1.5f, w - 4f, h - 4f);
                using (Pen innerPen = new Pen(Color.FromArgb(edgeAlpha / 2, 160, 90, 255), 8f))
                    g.DrawRectangle(innerPen, 5f, 5f, w - 11f, h - 11f);
            }

            // Информационное оповещение «Внимание: электронная атака!» в верхней центральной части экрана (3.5 сек)
            if (surgeElapsed >= 0.0 && surgeElapsed <= 3.5 && !string.IsNullOrEmpty(lastJamBannerActor))
            {
                float bannerAlpha = surgeElapsed > 2.7 ? (float)((3.5 - surgeElapsed) / 0.8) : 1.0f;
                int aText = Math.Max(0, Math.Min(255, (int)(245 * bannerAlpha)));
                int aBg = Math.Max(0, Math.Min(255, (int)(195 * bannerAlpha)));
                int aBorder = Math.Max(0, Math.Min(255, (int)(170 * bannerAlpha)));

                if (aText > 10)
                {
                    using (Font warnFont = new Font("Consolas", 8.8f, FontStyle.Bold))
                    {
                        string bannerText = "⚡ " + lastJamBannerActor.ToUpperInvariant();
                        SizeF bSz = g.MeasureString(bannerText, warnFont);
                        float bx = Math.Max(340f, (w - bSz.Width) * 0.5f);
                        float by = 14f;
                        RectangleF bRect = new RectangleF(bx - 10f, by, bSz.Width + 20f, bSz.Height + 8f);

                        using (SolidBrush bgBr = new SolidBrush(Color.FromArgb(aBg, 22, 12, 34)))
                            g.FillRectangle(bgBr, bRect);
                        using (Pen bdPen = new Pen(Color.FromArgb(aBorder, 210, 120, 255), 1.3f))
                            g.DrawRectangle(bdPen, bRect.X, bRect.Y, bRect.Width, bRect.Height);
                        using (SolidBrush barBr = new SolidBrush(Color.FromArgb(aText, 215, 130, 255)))
                            g.FillRectangle(barBr, bRect.X, bRect.Y, 3f, bRect.Height);
                        using (SolidBrush txtBr = new SolidBrush(Color.FromArgb(aText, 235, 185, 255)))
                            g.DrawString(bannerText, warnFont, txtBr, bx, by + 4f);
                    }
                }
            }
            // Линейка: измерение расстояния и азимута по карте (рисуется поверх всех слоёв)
            if (rulerHasA)
            {
                PointF pa = rulerA;
                PointF pb = rulerHasB ? rulerB : pa;
                if (rulerHasB)
                {
                    using (Pen rp = new Pen(Color.FromArgb(230, 255, 215, 90), 1.8f))
                    {
                        rp.DashStyle = DashStyle.Dash;
                        g.DrawLine(rp, pa, pb);
                    }
                }
                using (SolidBrush dotBr = new SolidBrush(Color.FromArgb(255, 255, 215, 90)))
                {
                    g.FillEllipse(dotBr, pa.X - 4, pa.Y - 4, 8, 8);
                    if (rulerHasB) g.FillEllipse(dotBr, pb.X - 4, pb.Y - 4, 8, 8);
                }
                if (rulerHasB && pixelsPerMeter > 0)
                {
                    double mdx = (pb.X - pa.X) / pixelsPerMeter;
                    double mdy = -(pb.Y - pa.Y) / pixelsPerMeter;
                    double distM = Math.Sqrt(mdx * mdx + mdy * mdy);
                    double az = (Math.Atan2(mdx, mdy) * 180.0 / Math.PI + 360.0) % 360.0;
                    string label = distM >= 1000.0
                        ? string.Format("{0:F1} км · {1:000}°", distM / 1000.0, az)
                        : string.Format("{0:F0} м · {1:000}°", distM, az);
                    using (Font rf = new Font("Consolas", 8.5f, FontStyle.Bold))
                    {
                        SizeF sz = g.MeasureString(label, rf);
                        float lx = (pa.X + pb.X) * 0.5f - sz.Width * 0.5f;
                        float ly = (pa.Y + pb.Y) * 0.5f - sz.Height - 4f;
                        using (SolidBrush bgB = new SolidBrush(Color.FromArgb(210, 18, 22, 18)))
                            g.FillRectangle(bgB, lx - 4f, ly - 2f, sz.Width + 8f, sz.Height + 4f);
                        using (SolidBrush txB = new SolidBrush(Color.FromArgb(255, 255, 230, 130)))
                            g.DrawString(label, rf, txB, lx, ly);
                    }
                }
            }
        }
        catch { /* Защита от редких OverflowException GDI+ при экстремальном зуме */ }
    }
    private void PnlTimeline_Paint(object sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        int w = pnlTimeline.Width;
        int h = pnlTimeline.Height;
        if (session == null || session.Duration <= 0.1) return;
        foreach (TacticalEvent ev in session.Events)
        {
            float x = (float)((ev.Time / session.Duration) * w);
            Color mc = Color.Gray;
            if (ev.Severity == "CRITICAL") mc = Color.OrangeRed;
            else if (ev.Kind == "RWR" || ev.Kind == "THREAT") mc = Color.Orange;
            else if (ev.Kind == "RADAR") mc = Color.DeepSkyBlue;
            else if (ev.Severity == "WARNING") mc = Color.Gold;
            else if (ev.Severity == "GOOD") mc = Color.FromArgb(0, 255, 102);
            else if (ev.Kind == "LAUNCH") mc = Color.Cyan;
            else if (ev.Kind == "JAMMER") mc = Color.FromArgb(200, 110, 255);
            using (Pen mp = new Pen(mc, (ev.Kind == "MISTAKE" || ev.Kind == "KIA" || ev.Kind == "THREAT") ? 2.5f : 1.5f))
                g.DrawLine(mp, x, 4, x, h - 4);
        }
        float curX = (float)((currentTime / session.Duration) * w);
        using (Pen curPen = new Pen(Color.White, 2f))
            g.DrawLine(curPen, curX, 0, curX, h);
    }

    private bool InspectNearestObjectAt(Point mousePt)
    {
        if (session == null) return false;
        int w = pnlRadar.Width;
        int h = pnlRadar.Height;
        double bestDist = 26.0;
        string report = null;
        bool hitAir = false;

        foreach (DebriefEntity ent in session.Entities)
        {
            DebriefSample st = ent.GetStateAt(currentTime);
            if (st == null) continue;
            PointF sp = WorldToScreen3D(st.X, st.Y, st.Alt, w, h);
            double d = Math.Sqrt(Math.Pow(sp.X - mousePt.X, 2) + Math.Pow(sp.Y - mousePt.Y, 2));
            if (d < bestDist)
            {
                bestDist = d;
                hitAir = (ent.Type == "Air");
                if (hitAir && cmbFollowTarget != null)
                {
                    for (int idx = 1; idx < cmbFollowTarget.Items.Count; idx++)
                    {
                        if (cmbFollowTarget.Items[idx].ToString().EndsWith("[" + ent.Id + "]"))
                        {
                            cmbFollowTarget.SelectedIndex = idx;
                            break;
                        }
                    }
                }
                report = string.Format(
                    "🔍 ИНСПЕКТОР ЮНИТА:\r\n" +
                    "Имя: {0} ({1})\r\n" +
                    "Тип / Роль: {2} / {3} ({4})\r\n" +
                    "Время жизни: {5} .. {6}\r\n" +
                    "Координаты: X={7:F0} м, Y={8:F0} м\r\n" +
                    "Высота: {9:F0} м | Скорость: {10:F0} км/ч | Курс: {11:F0}°\r\n" +
                    "Точек в треке: {12}",
                    ent.Name, ent.Pilot ?? "-", ent.Type, ent.RoleTag ?? "-", ent.Coalition,
                    FormatSec(ent.FirstTime), FormatSec(ent.LastTime),
                    st.X, st.Y, st.Alt, st.Speed * 3.6, st.Heading, ent.Track.Count);

                // Дистанция и азимут до борта игрока — только по координатам из трека
                DebriefEntity playerEnt = null;
                foreach (DebriefEntity pe in session.Entities)
                    if (pe.IsPlayer) { playerEnt = pe; break; }
                if (playerEnt != null && playerEnt != ent)
                {
                    DebriefSample ps = playerEnt.GetStateAt(currentTime);
                    if (ps != null)
                    {
                        double ddx = st.X - ps.X;
                        double ddy = st.Y - ps.Y;
                        double distM = Math.Sqrt(ddx * ddx + ddy * ddy);
                        double az = (Math.Atan2(ddx, ddy) * 180.0 / Math.PI + 360.0) % 360.0;
                        report += string.Format("\r\nДо вас: {0:F1} км · азимут {1:000}°", distM / 1000.0, az);
                    }
                }
            }
        }


        foreach (TacticalEvent ev in session.Events)
        {
            double age = currentTime - ev.Time;
            if (age < 0 || age > 12.0) continue;
            if (Math.Abs(ev.WorldX) < 1.0 && Math.Abs(ev.WorldY) < 1.0) continue;
            PointF sp = WorldToScreen(ev.WorldX, ev.WorldY, w, h);
            double d = Math.Sqrt(Math.Pow(sp.X - mousePt.X, 2) + Math.Pow(sp.Y - mousePt.Y, 2));
            if (d < bestDist)
            {
                bestDist = d;
                report = string.Format(
                    "🔍 ИНСПЕКТОР СОБЫТИЯ:\r\n" +
                    "Таймкод: {0} ({1:F1} сек)\r\n" +
                    "Тип: [{2} / {3}] {4}\r\n" +
                    "Координаты: X={5:F0} м, Y={6:F0} м\r\n" +
                    "Описание: {7}",
                    FormatSec(ev.Time), ev.Time, ev.Kind, ev.Severity, ev.Title,
                    ev.WorldX, ev.WorldY, ev.Description);
            }
        }

        if (!string.IsNullOrEmpty(report))
            txtCoachAdvice.Text = report;
        else
            UpdateCoachIntro();
        return hitAir;
    }

    private void BtnInspector_Click(object sender, EventArgs e)
    {
        if (session == null)
        {
            MessageBox.Show(this, "Сначала загрузите или импортируйте вылет.", "Инспектор", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        string dump = DebriefEngine.BuildDebugReport(session);
        Form dlg = new Form()
        {
            Text = "Инспектор данных повтора — " + (session.Title ?? ""),
            Size = new Size(880, 620),
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Color.FromArgb(18, 22, 20),
            ForeColor = Color.FromArgb(0, 255, 102),
            Font = new Font("Consolas", 9.5f)
        };
        DarkWindowHelper.Apply(dlg);

        Button btnCopy = new Button()
        {
            Text = "📋 Скопировать дамп в буфер обмена",
            Left = 12,
            Top = 10,
            Width = 290,
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0, 120, 75),
            ForeColor = Color.White,
            Font = new Font("Consolas", 9f, FontStyle.Bold)
        };
        btnCopy.Click += (s2, e2) => {
            Clipboard.SetText(dump);
            btnCopy.Text = "✔ Скопировано!";
        };
        dlg.Controls.Add(btnCopy);

        TextBox txtDump = new TextBox()
        {
            Left = 12,
            Top = 46,
            Width = 840,
            Height = 524,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            BackColor = Color.FromArgb(14, 18, 16),
            ForeColor = Color.FromArgb(215, 245, 225),
            Text = dump
        };
        dlg.Controls.Add(txtDump);
        dlg.ShowDialog(this);
    }

    private string FormatSec(double sec)
    {
        if (sec < 0) sec = 0;
        int total = (int)sec;
        return string.Format("{0:D2}:{1:D2}", total / 60, total % 60);
    }
}

