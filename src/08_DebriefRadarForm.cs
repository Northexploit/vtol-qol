using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Windows.Forms;

// =========================================================================
// === ТАКТИЧЕСКИЙ РАДАР-ПЛАНШЕТ ДЕБРИФИНГА И РАЗБОР ОШИБОК ПИЛОТА ==========
// =========================================================================
public class DebriefRadarForm : Form
{
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
    private DoubleBufferedPanel pnlTimeline;
    private Button btnPlayPause;
    private ComboBox cmbSpeed;
    private Label lblTimeHud;
    private Label lblTelemetryHud;
    private ListBox lstMistakes;
    private TextBox txtCoachAdvice;

    private List<string> replayFiles = new List<string>();
    private DebriefSession session = null;
    private double currentTime = 0.0;
    private bool isPlaying = false;
    private double playSpeed = 2.0;
    private System.Windows.Forms.Timer playTimer;

    // Параметры камеры тактического планшета
    private double camCenterX = 0.0;
    private double camCenterY = 0.0;
    private double pixelsPerMeter = 0.012; // ~12 px на 1 км по умолчанию
    private bool isPanning = false;
    private Point lastPanMouse;
    private bool followPlayer = true;
    private CheckBox chkFollowPlayer;

    public DebriefRadarForm()
    {
        this.Text = "VTOL QoL — Тактический дебрифинг и разбор ошибок пилота";
        DarkWindowHelper.Apply(this);
        this.Size = new Size(1160, 720);
        this.MinimumSize = new Size(960, 580);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.Sizable;
        this.MaximizeBox = true;
        this.BackColor = Color.FromArgb(16, 20, 18);
        this.ForeColor = Color.FromArgb(0, 255, 102);
        this.Font = new Font("Consolas", 9.5f);

        // Верхняя панель выбора повтора
        Panel pnlTop = new Panel() { Left = 14, Top = 10, Width = 1116, Height = 38, BackColor = Color.FromArgb(24, 30, 27), BorderStyle = BorderStyle.FixedSingle };
        pnlTop.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        this.Controls.Add(pnlTop);

        Label lblFile = new Label() { Text = "Вылет / Повтор:", Left = 10, Top = 10, AutoSize = true, ForeColor = Color.LightGray };
        pnlTop.Controls.Add(lblFile);

        cmbReplays = new ComboBox() { Left = 128, Top = 6, Width = 540, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(30, 38, 34), ForeColor = Color.Cyan };
        cmbReplays.SelectedIndexChanged += (s, e) => LoadSelectedReplay();
        pnlTop.Controls.Add(cmbReplays);

        btnBrowseFile = new Button() { Text = "📂 Открыть .acmi / .vtolrep", Left = 678, Top = 5, Width = 215, Height = 26, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 105, 135), ForeColor = Color.White };
        btnBrowseFile.Click += BtnBrowseFile_Click;
        pnlTop.Controls.Add(btnBrowseFile);

        chkFollowPlayer = new CheckBox() { Text = "Следить за пилотом", Left = 905, Top = 8, AutoSize = true, Checked = true, ForeColor = Color.FromArgb(180, 240, 200) };
        chkFollowPlayer.CheckedChanged += (s, e) => { followPlayer = chkFollowPlayer.Checked; pnlRadar.Invalidate(); };
        pnlTop.Controls.Add(chkFollowPlayer);

        // Левая основная зона: тактический радар-планшет
        pnlRadar = new DoubleBufferedPanel();
        pnlRadar.Location = new Point(14, 56);
        pnlRadar.Size = new Size(740, 540);
        pnlRadar.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        pnlRadar.BackColor = Color.FromArgb(12, 16, 15);
        pnlRadar.BorderStyle = BorderStyle.FixedSingle;
        pnlRadar.Paint += PnlRadar_Paint;
        pnlRadar.MouseDown += (s, e) => {
            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right)
            {
                isPanning = true;
                followPlayer = false;
                chkFollowPlayer.Checked = false;
                lastPanMouse = e.Location;
            }
        };
        pnlRadar.MouseUp += (s, e) => { isPanning = false; };
        pnlRadar.MouseMove += (s, e) => {
            if (!isPanning) return;
            double dx = e.X - lastPanMouse.X;
            double dy = e.Y - lastPanMouse.Y;
            camCenterX -= dx / pixelsPerMeter;
            camCenterY += dy / pixelsPerMeter;
            lastPanMouse = e.Location;
            pnlRadar.Invalidate();
        };
        pnlRadar.MouseWheel += (s, e) => {
            double factor = e.Delta > 0 ? 1.25 : 0.8;
            pixelsPerMeter = Math.Max(0.0008, Math.Min(0.25, pixelsPerMeter * factor));
            pnlRadar.Invalidate();
        };
        this.Controls.Add(pnlRadar);

        // Правая панель: HUD телеметрии + Разбор ошибок пилота
        Panel pnlRight = new Panel();
        pnlRight.Location = new Point(762, 56);
        pnlRight.Size = new Size(368, 540);
        pnlRight.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;
        pnlRight.BackColor = Color.FromArgb(20, 25, 23);
        pnlRight.BorderStyle = BorderStyle.FixedSingle;
        this.Controls.Add(pnlRight);

        Label lblTelTitle = new Label() { Text = "/// ТЕЛЕМЕТРИЯ БОРТА ///", Left = 10, Top = 8, AutoSize = true, Font = new Font("Consolas", 9.5f, FontStyle.Bold), ForeColor = Color.Cyan };
        pnlRight.Controls.Add(lblTelTitle);

        lblTelemetryHud = new Label()
        {
            Left = 10,
            Top = 28,
            Width = 346,
            Height = 64,
            BackColor = Color.FromArgb(15, 20, 18),
            ForeColor = Color.FromArgb(0, 255, 102),
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(6),
            Text = "Загрузите вылет для просмотра телеметрии..."
        };
        pnlRight.Controls.Add(lblTelemetryHud);

        Label lblErrTitle = new Label() { Text = "/// РАЗБОР ОШИБОК И СОБЫТИЙ ///", Left = 10, Top = 102, AutoSize = true, Font = new Font("Consolas", 9.5f, FontStyle.Bold), ForeColor = Color.Gold };
        pnlRight.Controls.Add(lblErrTitle);

        lstMistakes = new ListBox();
        lstMistakes.Location = new Point(10, 124);
        lstMistakes.Size = new Size(346, 235);
        lstMistakes.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        lstMistakes.BackColor = Color.FromArgb(16, 20, 18);
        lstMistakes.ForeColor = Color.White;
        lstMistakes.BorderStyle = BorderStyle.FixedSingle;
        lstMistakes.DrawMode = DrawMode.OwnerDrawFixed;
        lstMistakes.ItemHeight = 20;
        lstMistakes.DrawItem += LstMistakes_DrawItem;
        lstMistakes.SelectedIndexChanged += LstMistakes_SelectedIndexChanged;
        pnlRight.Controls.Add(lstMistakes);

        Label lblAdvTitle = new Label() { Text = "РЕКОМЕНДАЦИЯ ИНСТРУКТОРА:", Left = 10, Top = 366, AutoSize = true, Font = new Font("Consolas", 9f, FontStyle.Bold), ForeColor = Color.FromArgb(0, 255, 102) };
        lblAdvTitle.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        pnlRight.Controls.Add(lblAdvTitle);

        txtCoachAdvice = new TextBox()
        {
            Left = 10,
            Top = 386,
            Width = 346,
            Height = 142,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(15, 20, 18),
            ForeColor = Color.FromArgb(225, 245, 230),
            BorderStyle = BorderStyle.FixedSingle,
            Text = "Выберите событие или ошибку в списке выше, чтобы перейти к моменту боя и увидеть тактический разбор."
        };
        pnlRight.Controls.Add(txtCoachAdvice);

        // Нижняя панель плеера (таймлайн + управление временем)
        Panel pnlBottom = new Panel() { Left = 14, Top = 604, Width = 1116, Height = 64, BackColor = Color.FromArgb(24, 30, 27), BorderStyle = BorderStyle.FixedSingle };
        pnlBottom.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        this.Controls.Add(pnlBottom);

        btnPlayPause = new Button() { Text = "▶ ПУСК", Left = 10, Top = 16, Width = 92, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 130, 65), ForeColor = Color.White, Font = new Font("Consolas", 9.5f, FontStyle.Bold) };
        btnPlayPause.Click += (s, e) => TogglePlay();
        pnlBottom.Controls.Add(btnPlayPause);

        cmbSpeed = new ComboBox() { Left = 110, Top = 19, Width = 68, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(30, 38, 34), ForeColor = Color.Cyan };
        cmbSpeed.Items.AddRange(new object[] { "1x", "2x", "5x", "10x", "20x" });
        cmbSpeed.SelectedIndex = 1;
        cmbSpeed.SelectedIndexChanged += (s, e) => {
            string sVal = cmbSpeed.SelectedItem.ToString().Replace("x", "");
            double sp;
            if (double.TryParse(sVal, out sp)) playSpeed = sp;
        };
        pnlBottom.Controls.Add(cmbSpeed);

        lblTimeHud = new Label() { Left = 186, Top = 22, Width = 135, Height = 20, ForeColor = Color.Cyan, Font = new Font("Consolas", 9.5f, FontStyle.Bold), Text = "00:00 / 00:00" };
        pnlBottom.Controls.Add(lblTimeHud);

        pnlTimeline = new DoubleBufferedPanel();
        pnlTimeline.Location = new Point(328, 12);
        pnlTimeline.Size = new Size(774, 40);
        pnlTimeline.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        pnlTimeline.BackColor = Color.FromArgb(14, 18, 16);
        pnlTimeline.BorderStyle = BorderStyle.FixedSingle;
        pnlTimeline.Cursor = Cursors.Hand;
        pnlTimeline.Paint += PnlTimeline_Paint;
        pnlTimeline.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) SeekTimelineByX(e.X); };
        pnlTimeline.MouseMove += (s, e) => { if (e.Button == MouseButtons.Left) SeekTimelineByX(e.X); };
        pnlBottom.Controls.Add(pnlTimeline);
        playTimer = new System.Windows.Forms.Timer();
        playTimer.Interval = 40;
        playTimer.Tick += (s, e) => {
            if (!isPlaying || session == null) return;
            currentTime += 0.04 * playSpeed;
            if (currentTime >= session.Duration)
            {
                currentTime = session.Duration;
                isPlaying = false;
                btnPlayPause.Text = "▶ ПУСК";
            }
            UpdateFrame();
        };
        playTimer.Start();
        this.FormClosing += (s, e) => { if (playTimer != null) { playTimer.Stop(); playTimer.Dispose(); } };
        RefreshReplayList();
    }
    private void TogglePlay()
    {
        if (session == null) return;
        if (currentTime >= session.Duration) currentTime = 0;
        isPlaying = !isPlaying;
        btnPlayPause.Text = isPlaying ? "⏸ ПАУЗА" : "▶ ПУСК";
    }
    private void SeekTimelineByX(int mouseX)
    {
        if (session == null || pnlTimeline.Width <= 4) return;
        double ratio = Math.Max(0.0, Math.Min(1.0, (double)mouseX / pnlTimeline.Width));
        currentTime = ratio * session.Duration;
        UpdateFrame();
    }
    private void RefreshReplayList()
    {
        cmbReplays.Items.Clear();
        replayFiles = DebriefEngine.FindAvailableReplays();
        foreach (string f in replayFiles)
        {
            string tag = f.EndsWith(".acmi", StringComparison.OrdinalIgnoreCase) ? "[ACMI 3D]" : "[LOG]";
            cmbReplays.Items.Add(string.Format("{0} {1} ({2})", tag, Path.GetFileName(f), File.GetLastWriteTime(f).ToString("dd.MM HH:mm")));
        }
        if (cmbReplays.Items.Count > 0)
            cmbReplays.SelectedIndex = 0;
        else
            lblTelemetryHud.Text = "Повторы пока не найдены.\nПолетайте с включённым VTOL QoL или откройте любой файл Tacview (.acmi).";
    }
    private void BtnBrowseFile_Click(object sender, EventArgs e)
    {
        using (OpenFileDialog ofd = new OpenFileDialog() { Filter = "Файлы повторов (*.acmi;*.zip.acmi;*.vtolrep)|*.acmi;*.vtolrep|Все файлы (*.*)|*.*" })
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
        try
        {
            string myNick = SettingsManager.Get("LastPilotNick", "");
            session = DebriefEngine.LoadSession(replayFiles[idx], myNick);
            currentTime = 0;
            followPlayer = true;
            chkFollowPlayer.Checked = true;
            lstMistakes.Items.Clear();
            foreach (TacticalEvent ev in session.Events)
                lstMistakes.Items.Add(ev);
            if (session.Mistakes.Count > 0)
            {
                txtCoachAdvice.Text = string.Format("Обнаружено тактических ошибок: {0}.\r\nВыберите любую запись с пометкой [ОШИБКА], чтобы увидеть разбор и рекомендацию.", session.Mistakes.Count);
            }
            else
            {
                txtCoachAdvice.Text = "Грубых тактических ошибок за этот вылет не зафиксировано! Отличная работа.";
            }
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
        Color bg = sel ? Color.FromArgb(35, 65, 52) : Color.FromArgb(16, 20, 18);
        Color fg = Color.LightGray;
        string badge = "•";
        if (ev.Severity == "CRITICAL") { fg = Color.OrangeRed; badge = "✖ [ОШИБКА]"; }
        else if (ev.Severity == "WARNING") { fg = Color.Gold; badge = "⚠ [ОШИБКА]"; }
        else if (ev.Severity == "GOOD") { fg = Color.FromArgb(0, 255, 102); badge = "★ [СБИТИЕ]"; }
        else if (ev.Kind == "LAUNCH") { fg = Color.Cyan; badge = "🚀 [ПУСК]"; }
        using (SolidBrush br = new SolidBrush(bg)) e.Graphics.FillRectangle(br, e.Bounds);
        string line = string.Format("[{0}] {1} {2}", FormatSec(ev.Time), badge, ev.Title);
        using (SolidBrush fbr = new SolidBrush(fg))
            e.Graphics.DrawString(line, lstMistakes.Font, fbr, e.Bounds.Left + 4, e.Bounds.Top + 2);
    }
    private void LstMistakes_SelectedIndexChanged(object sender, EventArgs e)
    {
        TacticalEvent ev = lstMistakes.SelectedItem as TacticalEvent;
        if (ev == null) return;
        currentTime = ev.Time;
        if (Math.Abs(ev.WorldX) > 1.0 || Math.Abs(ev.WorldY) > 1.0)
        {
            camCenterX = ev.WorldX;
            camCenterY = ev.WorldY;
        }
        StringBuilder sb = new StringBuilder();
        sb.AppendLine(string.Format("Таймкод: {0} — {1}", FormatSec(ev.Time), ev.Title));
        sb.AppendLine();
        sb.AppendLine(ev.Description);
        if (!string.IsNullOrEmpty(ev.Advice))
        {
            sb.AppendLine();
            sb.AppendLine("💡 КАК ИСПРАВИТЬ:");
            sb.AppendLine(ev.Advice);
        }
        txtCoachAdvice.Text = sb.ToString();
        UpdateFrame();
    }
    private void UpdateFrame()
    {
        if (session == null) return;
        lblTimeHud.Text = string.Format("{0} / {1}", FormatSec(currentTime), FormatSec(session.Duration));
        DebriefEntity player = null;
        foreach (DebriefEntity ent in session.Entities)
            if (ent.IsPlayer) { player = ent; break; }
        if (player != null)
        {
            DebriefSample st = player.GetStateAt(currentTime);
            if (st != null)
            {
                if (followPlayer && session.HasFullRadarCoords)
                {
                    camCenterX = st.X;
                    camCenterY = st.Y;
                }
                lblTelemetryHud.Text = string.Format(
                "Борт: {0} ({1})\nALT: {2:F0} м  |  SPD: {3:F0} км/ч\nHDG: {4:F0}°  |  Ошибок в бою: {5}",
                player.Name, player.Pilot, st.Alt, st.Speed * 3.6, st.Heading, session.Mistakes.Count);
            }
            else
            {
                lblTelemetryHud.Text = string.Format("Пилот: {0} | Ошибок в бою: {1}\n(Повтор записан из Player.log без 2D-координат)",
                session.PilotName, session.Mistakes.Count);
            }
        }
        pnlRadar.Invalidate();
        pnlTimeline.Invalidate();
    }
    private PointF WorldToScreen(double wx, double wy, int w, int h)
    {
        float sx = (float)(w * 0.5 + (wx - camCenterX) * pixelsPerMeter);
        float sy = (float)(h * 0.5 - (wy - camCenterY) * pixelsPerMeter);
        return new PointF(sx, sy);
    }
    private void PnlRadar_Paint(object sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int w = pnlRadar.Width;
        int h = pnlRadar.Height;
        // 1. Тактическая координатная сетка (шаг 10 км)
        double gridStepMeters = 10000.0;
        if (pixelsPerMeter > 0.04) gridStepMeters = 2000.0;
        else if (pixelsPerMeter < 0.004) gridStepMeters = 25000.0;
        double leftWorld = camCenterX - (w * 0.5) / pixelsPerMeter;
        double rightWorld = camCenterX + (w * 0.5) / pixelsPerMeter;
        double bottomWorld = camCenterY - (h * 0.5) / pixelsPerMeter;
        double topWorld = camCenterY + (h * 0.5) / pixelsPerMeter;
        double startX = Math.Floor(leftWorld / gridStepMeters) * gridStepMeters;
        double startY = Math.Floor(bottomWorld / gridStepMeters) * gridStepMeters;
        using (Pen gridPen = new Pen(Color.FromArgb(24, 38, 32), 1))
        using (SolidBrush scaleBrush = new SolidBrush(Color.FromArgb(80, 140, 110)))
        {
            for (double gx = startX; gx <= rightWorld; gx += gridStepMeters)
            {
                PointF p = WorldToScreen(gx, camCenterY, w, h);
                g.DrawLine(gridPen, p.X, 0, p.X, h);
            }
            for (double gy = startY; gy <= topWorld; gy += gridStepMeters)
            {
                PointF p = WorldToScreen(camCenterX, gy, w, h);
                g.DrawLine(gridPen, 0, p.Y, w, p.Y);
            }
            g.DrawString(string.Format("Сетка: {0:F0} км | Колесо мыши — зум, ЛКМ — сдвиг", gridStepMeters / 1000.0),
            this.Font, scaleBrush, 10, h - 22);
        }
        if (session == null) return;
        if (!session.HasFullRadarCoords)
        {
            using (SolidBrush br = new SolidBrush(Color.Cyan))
                g.DrawString("Режим хронологии вылета (.vtolrep) — кликайте по событиям справа или на таймлайне.\nДля отображения 2D-радара траекторий откройте любой файл Tacview (.acmi).",
                this.Font, br, 20, 20);
            return;
        }
        // 2. Отрисовка траекторий (шлейфов) и тактических символов
        foreach (DebriefEntity ent in session.Entities)
        {
            DebriefSample cur = ent.GetStateAt(currentTime);
            if (cur == null) continue;
            Color col = ent.IsPlayer
            ? Color.FromArgb(0, 255, 102)
            : (ent.Type == "Missile"
            ? Color.Orange
            : (ent.Coalition == "Enemies" ? Color.OrangeRed : Color.DeepSkyBlue));
            // Шлейф за последние 45 секунд
            List<PointF> trail = new List<PointF>();
            double minTrailT = Math.Max(ent.FirstTime, currentTime - 45.0);
            for (int i = 0; i < ent.Track.Count; i++)
            {
                DebriefSample s = ent.Track[i];
                if (s.Time < minTrailT) continue;
                if (s.Time > currentTime) break;
                trail.Add(WorldToScreen(s.X, s.Y, w, h));
            }
            trail.Add(WorldToScreen(cur.X, cur.Y, w, h));
            if (trail.Count >= 2)
            {
                using (Pen trailPen = new Pen(Color.FromArgb(145, col), ent.Type == "Missile" ? 1.3f : 2.0f))
                {
                    if (ent.Type == "Missile") trailPen.DashStyle = DashStyle.Dash;
                    g.DrawLines(trailPen, trail.ToArray());
                }
            }
            PointF pos = WorldToScreen(cur.X, cur.Y, w, h);
            if (pos.X < -40 || pos.X > w + 40 || pos.Y < -40 || pos.Y > h + 40) continue;
            // Символ цели
            double rad = cur.Heading * Math.PI / 180.0;
            float dx = (float)Math.Sin(rad);
            float dy = -(float)Math.Cos(rad);
            if (ent.Type == "Missile")
            {
                using (SolidBrush mBr = new SolidBrush(Color.Orange))
                    g.FillEllipse(mBr, pos.X - 3, pos.Y - 3, 6, 6);
            }
            else
            {
                PointF nose = new PointF(pos.X + dx * 11, pos.Y + dy * 11);
                PointF leftWing = new PointF(pos.X - dx * 7 - dy * 7, pos.Y - dy * 7 + dx * 7);
                PointF rightWing = new PointF(pos.X - dx * 7 + dy * 7, pos.Y - dy * 7 - dx * 7);
                using (SolidBrush acBr = new SolidBrush(col))
                    g.FillPolygon(acBr, new PointF[] { nose, leftWing, rightWing });
                // Вектор скорости
                using (Pen vecPen = new Pen(col, 1.5f))
                    g.DrawLine(vecPen, pos.X, pos.Y, pos.X + dx * 22, pos.Y + dy * 22);
                string label = string.Format("{0}\n{1:F0}m | {2:F0}km/h",
                !string.IsNullOrEmpty(ent.Pilot) ? ent.Pilot : ent.Name, cur.Alt, cur.Speed * 3.6);
                using (SolidBrush txtBr = new SolidBrush(col))
                    g.DrawString(label, new Font("Consolas", 8f, FontStyle.Bold), txtBr, pos.X + 12, pos.Y - 10);
            }
        }
        // 3. Маркеры тактических ошибок прямо на карте
        foreach (TacticalEvent err in session.Mistakes)
        {
            if (Math.Abs(err.Time - currentTime) <= 12.0 && (Math.Abs(err.WorldX) > 1.0 || Math.Abs(err.WorldY) > 1.0))
            {
                PointF ep = WorldToScreen(err.WorldX, err.WorldY, w, h);
                Color ec = err.Severity == "CRITICAL" ? Color.OrangeRed : Color.Gold;
                using (Pen ringPen = new Pen(ec, 2f))
                    g.DrawEllipse(ringPen, ep.X - 16, ep.Y - 16, 32, 32);
                using (SolidBrush eBr = new SolidBrush(ec))
                    g.DrawString("⚠ " + err.Title, new Font("Consolas", 8.5f, FontStyle.Bold), eBr, ep.X + 18, ep.Y - 8);
            }
        }
    }
    private void PnlTimeline_Paint(object sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        int w = pnlTimeline.Width;
        int h = pnlTimeline.Height;
        if (session == null || session.Duration <= 0.1) return;
        // Маркеры событий и ошибок на полосе времени
        foreach (TacticalEvent ev in session.Events)
        {
            float x = (float)((ev.Time / session.Duration) * w);
            Color mc = Color.Gray;
            if (ev.Severity == "CRITICAL") mc = Color.OrangeRed;
            else if (ev.Severity == "WARNING") mc = Color.Gold;
            else if (ev.Severity == "GOOD") mc = Color.FromArgb(0, 255, 102);
            else if (ev.Kind == "LAUNCH") mc = Color.Cyan;
            using (Pen mp = new Pen(mc, ev.Kind == "MISTAKE" ? 2.5f : 1.5f))
                g.DrawLine(mp, x, 4, x, h - 4);
        }
        // Бегунок текущего времени
        float curX = (float)((currentTime / session.Duration) * w);
        using (Pen curPen = new Pen(Color.White, 2f))
            g.DrawLine(curPen, curX, 0, curX, h);
    }
    private string FormatSec(double sec)
    {
        if (sec < 0) sec = 0;
        int total = (int)sec;
        return string.Format("{0:D2}:{1:D2}", total / 60, total % 60);
    }
}