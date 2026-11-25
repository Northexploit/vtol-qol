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
// === 3D-ПРОСМОТРЩИК ЛИВРЕЙ (WPF Viewport3D в ElementHost) ================
// =========================================================================
public class Livery3DViewerForm : Form
{
    private System.Windows.Forms.Integration.ElementHost host;
    private System.Windows.Controls.Viewport3D viewport;
    private System.Windows.Media.Media3D.PerspectiveCamera camera;
    private System.Windows.Media.Media3D.GeometryModel3D model;
    private System.Windows.Media.ImageBrush textureBrush;
    private LiveryEntry entry;
    private System.Windows.Point lastMouse;
    private bool dragging = false;
    private double orbitYaw = 45, orbitPitch = 20, zoomDist = 4;

    private ComboBox cmbModelSelect;
    private System.Windows.Controls.TextBlock lblMissingModel;
    private System.Windows.Media.Media3D.ModelVisual3D modelVisual;
    private System.Windows.Media.Media3D.ModelVisual3D cloudsVisual;
    private System.Windows.Media.Media3D.ModelVisual3D rainVisual;
    private System.Windows.Media.Media3D.ModelVisual3D aircraftLightsVisual;
    private List<System.Windows.Media.SolidColorBrush> strobeBrushes = new List<System.Windows.Media.SolidColorBrush>();
    private List<System.Windows.Media.Media3D.PointLight> strobePointLights = new List<System.Windows.Media.Media3D.PointLight>();
    private System.Windows.Forms.Timer navLightTimer;
    private int strobePhaseTick = 0;
    private bool navLightsEnabled = true;
    private bool strobeLightsEnabled = true;
    private bool landingLightEnabled = true;
    private System.Windows.Media.Media3D.TranslateTransform3D rainDropTransform;
    private System.Windows.Media.SolidColorBrush rainMatBrush;
    private System.Windows.Media.Media3D.Model3DGroup lightGroup;
    private System.Windows.Controls.Canvas bgCanvas;
    private System.Windows.Controls.Canvas fgCanvas;
    private int lightPreset = 0; // 0 = Студия, 1 = Солнце, 2 = Ночь

    private class CloudBank3D
    {
        public double Yaw, Pitch, Dist, SpeedYaw, Scale;
        public System.Windows.Controls.Canvas Visual;
    }

    private class PassThroughCloud
    {
        public double Progress; // 0.0 -> 1.0 (пролёт мимо/сквозь самолёт)
        public double OffsetX, OffsetY, Speed, Scale;
        public System.Windows.Controls.Canvas Visual;
    }

    private List<CloudBank3D> activeCloudBanks = new List<CloudBank3D>();
    private List<PassThroughCloud> passClouds = new List<PassThroughCloud>();
    private System.Windows.Forms.Timer cloudAnimTimer;

    // Поля для ночной грозы (lightPreset == 3)
    private System.Windows.Media.Media3D.DirectionalLight stormFlashLight;
    private double lightningFlashAlpha = 0.0; // 0.0 -> 1.0 (яркость текущей вспышки)
    private int lightningPulsesLeft = 0;
    private double lightningYaw = 215.0;
    private double lightningPitch = 8.0;
    private int lightningTicksUntilNext = 90;
    private int lightningSeed = 1337;
    private double rainPhase = 0.0;




    public Livery3DViewerForm(LiveryEntry liveryEntry)
        {
            this.entry = liveryEntry;
        this.Text = "VTOL QoL — 3D просмотр ливреи: " + liveryEntry.Name;
        DarkWindowHelper.Apply(this);
        this.Size = new Size(860, 680);
        this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = Color.FromArgb(18, 22, 20);

            host = new System.Windows.Forms.Integration.ElementHost();
            host.Dock = DockStyle.Fill;
            this.Controls.Add(host);

        System.Windows.Controls.Grid grid = new System.Windows.Controls.Grid();
        grid.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(26, 31, 35));
        bgCanvas = new System.Windows.Controls.Canvas();
        bgCanvas.IsHitTestVisible = false;
        grid.Children.Add(bgCanvas);

        viewport = new System.Windows.Controls.Viewport3D();
        viewport.IsHitTestVisible = false;
        grid.Children.Add(viewport);

        fgCanvas = new System.Windows.Controls.Canvas();
        fgCanvas.IsHitTestVisible = false;
        grid.Children.Add(fgCanvas);

        grid.SizeChanged += (s, e) => RenderBackgroundDecor();
        host.Child = grid;



        Panel pnlBottom = new Panel();
            pnlBottom.Dock = DockStyle.Bottom;
            pnlBottom.Height = 42;
            pnlBottom.BackColor = Color.FromArgb(24, 28, 26);
            this.Controls.Add(pnlBottom);

            Button btnTexture = new Button() { Text = "Открыть свою текстуру", Left = 10, Top = 7, Width = 175, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 110, 140), ForeColor = Color.White };
            btnTexture.Click += (s, e) => OpenCustomTexture();
            pnlBottom.Controls.Add(btnTexture);

            Button btnReset = new Button() { Text = "Сбросить вид", Left = 195, Top = 7, Width = 110, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(40, 50, 45), ForeColor = Color.White };
            btnReset.Click += (s, e) => { orbitYaw = 45; orbitPitch = 20; zoomDist = 3.2; UpdateCamera(); };
            pnlBottom.Controls.Add(btnReset);

            Label lblModel = new Label() { Text = "Борт:", Left = 315, Top = 13, AutoSize = true, ForeColor = Color.LightGray };
            pnlBottom.Controls.Add(lblModel);

            cmbModelSelect = new ComboBox();
            cmbModelSelect.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbModelSelect.Location = new Point(360, 9);
            cmbModelSelect.Size = new Size(115, 24);
            cmbModelSelect.BackColor = Color.FromArgb(30, 36, 33);
            cmbModelSelect.ForeColor = Color.Cyan;
            string[] planes = new string[] { "FA-26B", "F-45A", "AV-42C", "AH-94", "T-55", "EF-24G" };
            foreach (string p in planes) cmbModelSelect.Items.Add(p);
            int foundIdx = Array.IndexOf(planes, entry.Aircraft);
            cmbModelSelect.SelectedIndex = foundIdx >= 0 ? foundIdx : 0;
        cmbModelSelect.SelectedIndexChanged += (s, e) => {
            entry.Aircraft = cmbModelSelect.SelectedItem.ToString();
            LiveryParser.SaveUserAircraftMapping(entry.FolderPath, entry.Aircraft);
            LoadModelAndTexture();
        };

        pnlBottom.Controls.Add(cmbModelSelect);

        Button btnUvLayer = new Button() { Text = "UV слой", Left = 485, Top = 7, Width = 80, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 75, 60), ForeColor = Color.White };
        btnUvLayer.Click += (s, e) => { FbxLoader.UseSecondUV = !FbxLoader.UseSecondUV; LoadModelAndTexture(); };
        pnlBottom.Controls.Add(btnUvLayer);

        Button btnFlipY = new Button() { Text = "Flip Y", Left = 572, Top = 7, Width = 70, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 75, 60), ForeColor = Color.White };
        btnFlipY.Click += (s, e) => { FbxLoader.FlipUVY = !FbxLoader.FlipUVY; LoadModelAndTexture(); };
        pnlBottom.Controls.Add(btnFlipY);

        Button btnLight = new Button() { Text = "☀ Свет", Left = 648, Top = 7, Width = 72, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(95, 75, 25), ForeColor = Color.White };
        btnLight.Click += (s, e) => { lightPreset = (lightPreset + 1) % 4; ApplyLightingPreset(); };
        pnlBottom.Controls.Add(btnLight);

        Button btnNavToggle = new Button() { Text = "NAV", Left = 726, Top = 7, Width = 46, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 75), ForeColor = Color.White };
        btnNavToggle.Click += (s, e) => {
            navLightsEnabled = !navLightsEnabled;
            btnNavToggle.BackColor = navLightsEnabled ? Color.FromArgb(0, 120, 75) : Color.FromArgb(40, 46, 44);
            if (lastLoadedMeshData != null) BuildAircraftLights(lastLoadedMeshData);
        };
        pnlBottom.Controls.Add(btnNavToggle);

        Button btnStrobeToggle = new Button() { Text = "STRB", Left = 776, Top = 7, Width = 50, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 75), ForeColor = Color.White };
        btnStrobeToggle.Click += (s, e) => {
            strobeLightsEnabled = !strobeLightsEnabled;
            btnStrobeToggle.BackColor = strobeLightsEnabled ? Color.FromArgb(0, 120, 75) : Color.FromArgb(40, 46, 44);
            if (lastLoadedMeshData != null) BuildAircraftLights(lastLoadedMeshData);
        };
        pnlBottom.Controls.Add(btnStrobeToggle);

        Button btnLandToggle = new Button() { Text = "LAND", Left = 830, Top = 7, Width = 52, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 75), ForeColor = Color.White };
        btnLandToggle.Click += (s, e) => {
            landingLightEnabled = !landingLightEnabled;
            btnLandToggle.BackColor = landingLightEnabled ? Color.FromArgb(0, 120, 75) : Color.FromArgb(40, 46, 44);
            if (lastLoadedMeshData != null) BuildAircraftLights(lastLoadedMeshData);
        };
        pnlBottom.Controls.Add(btnLandToggle);

        Label lblHint = new Label() { Text = "ЛКМ", Left = 888, Top = 13, AutoSize = true, ForeColor = Color.Silver };
        pnlBottom.Controls.Add(lblHint);



        grid.MouseLeftButtonDown += (s, e) => { dragging = true; lastMouse = e.GetPosition(grid); grid.CaptureMouse(); };
            grid.MouseLeftButtonUp += (s, e) => { dragging = false; grid.ReleaseMouseCapture(); };
            grid.MouseMove += (s, e) => {
                if (!dragging) return;
                System.Windows.Point pos = e.GetPosition(grid);
                orbitYaw -= (pos.X - lastMouse.X) * 0.45;
                orbitPitch += (pos.Y - lastMouse.Y) * 0.45;
                if (orbitPitch > 89) orbitPitch = 89;
                if (orbitPitch < -89) orbitPitch = -89;
                lastMouse = pos;
                UpdateCamera();
            };
            grid.MouseWheel += (s, e) => {
                zoomDist -= e.Delta / 240.0;
                if (zoomDist < 0.8) zoomDist = 0.8;
                if (zoomDist > 30) zoomDist = 30;
                UpdateCamera();
            };

        this.FormClosing += (s, e) => {
            if (cloudAnimTimer != null) { cloudAnimTimer.Stop(); cloudAnimTimer.Dispose(); cloudAnimTimer = null; }
            if (navLightTimer != null) { navLightTimer.Stop(); navLightTimer.Dispose(); navLightTimer = null; }
        };

        InitScene();
        entry.Aircraft = cmbModelSelect.SelectedItem.ToString();
        LoadModelAndTexture();
    }

    private void InitScene()
    {
        camera = new System.Windows.Media.Media3D.PerspectiveCamera();
        camera.FieldOfView = 45;
        viewport.Camera = camera;

        System.Windows.Media.Media3D.ModelVisual3D lightVisual = new System.Windows.Media.Media3D.ModelVisual3D();
        lightGroup = new System.Windows.Media.Media3D.Model3DGroup();
        lightVisual.Content = lightGroup;
        viewport.Children.Add(lightVisual);

        ApplyLightingPreset();
        UpdateCamera();
    }

    private void ApplyLightingPreset()
    {
        if (lightGroup == null) return;
        lightGroup.Children.Clear();
        if (fgCanvas != null) fgCanvas.Children.Clear();
        System.Windows.Controls.Grid grid = (System.Windows.Controls.Grid)host.Child;

        if (lightPreset == 0)
        {
            System.Windows.Media.RadialGradientBrush rad = new System.Windows.Media.RadialGradientBrush(
                System.Windows.Media.Color.FromRgb(34, 42, 46),
                System.Windows.Media.Color.FromRgb(18, 22, 24));
            grid.Background = rad;

            lightGroup.Children.Add(new System.Windows.Media.Media3D.AmbientLight(System.Windows.Media.Color.FromRgb(95, 102, 110)));
            lightGroup.Children.Add(new System.Windows.Media.Media3D.DirectionalLight(
                System.Windows.Media.Color.FromRgb(255, 248, 235),
                new System.Windows.Media.Media3D.Vector3D(-1.2, -2.0, -1.5)));
            lightGroup.Children.Add(new System.Windows.Media.Media3D.DirectionalLight(
                System.Windows.Media.Color.FromRgb(150, 180, 220),
                new System.Windows.Media.Media3D.Vector3D(1.8, -0.5, 1.0)));
            lightGroup.Children.Add(new System.Windows.Media.Media3D.DirectionalLight(
                System.Windows.Media.Color.FromRgb(130, 145, 165),
                new System.Windows.Media.Media3D.Vector3D(0.3, 1.4, 1.8)));
        }
        else if (lightPreset == 1)
        {
            System.Windows.Media.LinearGradientBrush sky = new System.Windows.Media.LinearGradientBrush();
            sky.StartPoint = new System.Windows.Point(0.5, 0);
            sky.EndPoint = new System.Windows.Point(0.5, 1);
            sky.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(16, 44, 84), 0.0));
            sky.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(42, 92, 148), 0.52));
            sky.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(118, 162, 202), 0.85));
            sky.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(176, 198, 214), 1.0));
            grid.Background = sky;


            lightGroup.Children.Add(new System.Windows.Media.Media3D.AmbientLight(System.Windows.Media.Color.FromRgb(90, 105, 125)));
            lightGroup.Children.Add(new System.Windows.Media.Media3D.DirectionalLight(
                System.Windows.Media.Color.FromRgb(255, 244, 214),
                new System.Windows.Media.Media3D.Vector3D(1.5, -2.2, -1.2)));
            lightGroup.Children.Add(new System.Windows.Media.Media3D.DirectionalLight(
                System.Windows.Media.Color.FromRgb(125, 165, 215),
                new System.Windows.Media.Media3D.Vector3D(-1.2, -0.8, 1.4)));
            lightGroup.Children.Add(new System.Windows.Media.Media3D.DirectionalLight(
                System.Windows.Media.Color.FromRgb(105, 100, 90),
                new System.Windows.Media.Media3D.Vector3D(0.0, 2.0, 0.0)));
        }
        else if (lightPreset == 2)
        {
            System.Windows.Media.LinearGradientBrush night = new System.Windows.Media.LinearGradientBrush();
            night.StartPoint = new System.Windows.Point(0.5, 0);
            night.EndPoint = new System.Windows.Point(0.5, 1);
            night.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(7, 10, 18), 0.0));
            night.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(14, 22, 38), 1.0));
            grid.Background = night;

            lightGroup.Children.Add(new System.Windows.Media.Media3D.AmbientLight(System.Windows.Media.Color.FromRgb(42, 52, 68)));
            lightGroup.Children.Add(new System.Windows.Media.Media3D.DirectionalLight(
                System.Windows.Media.Color.FromRgb(195, 220, 255),
                new System.Windows.Media.Media3D.Vector3D(-1.2, -2.2, -1.0)));
            lightGroup.Children.Add(new System.Windows.Media.Media3D.DirectionalLight(
                System.Windows.Media.Color.FromRgb(70, 110, 160),
                new System.Windows.Media.Media3D.Vector3D(1.5, 0.5, 1.5)));
        }
        else
        {
            // 3: Ночная гроза (свинцовое небо + вспышки 3D-молний на самолёте)
            System.Windows.Media.LinearGradientBrush stormSky = new System.Windows.Media.LinearGradientBrush();
            stormSky.StartPoint = new System.Windows.Point(0.5, 0);
            stormSky.EndPoint = new System.Windows.Point(0.5, 1);
            stormSky.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(9, 12, 19), 0.0));
            stormSky.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(18, 25, 36), 0.65));
            stormSky.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(26, 35, 48), 1.0));
            grid.Background = stormSky;

            lightGroup.Children.Add(new System.Windows.Media.Media3D.AmbientLight(System.Windows.Media.Color.FromRgb(32, 40, 52)));
            lightGroup.Children.Add(new System.Windows.Media.Media3D.DirectionalLight(
                System.Windows.Media.Color.FromRgb(95, 125, 165),
                new System.Windows.Media.Media3D.Vector3D(-1.0, -2.0, -1.0)));
            lightGroup.Children.Add(new System.Windows.Media.Media3D.DirectionalLight(
                System.Windows.Media.Color.FromRgb(45, 65, 95),
                new System.Windows.Media.Media3D.Vector3D(1.5, 0.8, 1.2)));

            // Динамический источник света от вспышки молнии (в обычное время выключен = 0,0,0)
            stormFlashLight = new System.Windows.Media.Media3D.DirectionalLight(
                System.Windows.Media.Color.FromRgb(0, 0, 0),
                new System.Windows.Media.Media3D.Vector3D(0.5, -1.5, -1.0));
            lightGroup.Children.Add(stormFlashLight);
        }
        if (lastLoadedMeshData != null)
            BuildAircraftLights(lastLoadedMeshData);
        UpdateVolumetricClouds();
        RenderBackgroundDecor();

    }
    private System.Windows.Controls.Canvas BuildCumulusCloudElement(double scale, bool isPassThrough, bool isStorm = false)
    {
        System.Windows.Controls.Canvas cloud = new System.Windows.Controls.Canvas();
        cloud.Width = 960 * scale;
        cloud.Height = 380 * scale;
        cloud.IsHitTestVisible = false;

        // Широкий многослойный облачный фронт (28 пересекающихся клубов без резких границ "отдельных плюшек")
        double[,] puffs = new double[,]
        {
            // Нижняя плотная теневая основа (широкое основание гряды)
            { 160, 255, 190, 72, 1, 135 },
            { 310, 262, 220, 78, 1, 150 },
            { 480, 265, 240, 82, 1, 155 },
            { 650, 260, 215, 76, 1, 145 },
            { 800, 252, 185, 68, 1, 130 },
            { 240, 240, 210, 75, 1, 130 },
            { 400, 242, 230, 80, 1, 140 },
            { 570, 242, 225, 78, 1, 140 },
            { 730, 240, 195, 72, 1, 130 },

            // Широкая средняя облачная пелена (соединяет всё в единый массив)
            { 130, 215, 180, 95,  0, 175 },
            { 250, 195, 210, 115, 0, 205 },
            { 390, 180, 235, 130, 0, 225 },
            { 540, 182, 240, 132, 0, 225 },
            { 685, 195, 210, 115, 0, 205 },
            { 820, 215, 175, 92,  0, 170 },
            { 320, 205, 220, 118, 0, 210 },
            { 470, 202, 245, 125, 0, 220 },
            { 615, 205, 220, 118, 0, 210 },

            // Верхние пушистые кучевые гребни
            { 220, 155, 145, 95,  0, 205 },
            { 330, 130, 165, 112, 0, 230 },
            { 450, 112, 185, 125, 0, 242 },
            { 565, 122, 175, 118, 0, 235 },
            { 670, 148, 150, 98,  0, 210 },
            { 400, 145, 170, 108, 0, 225 },
            { 515, 142, 175, 110, 0, 228 },
            { 485, 88,  130, 92,  0, 225 },
            { 375, 102, 120, 86,  0, 210 },
            { 595, 100, 125, 88,  0, 212 }
        };

        double alphaMul = isPassThrough ? 0.58 : 1.0;

        for (int i = 0; i < puffs.GetLength(0); i++)
        {
            double cx = puffs[i, 0] * scale;
            double cy = puffs[i, 1] * scale;
            double rx = puffs[i, 2] * scale;
            double ry = puffs[i, 3] * scale;
            bool isShadow = (puffs[i, 4] > 0.5);
            byte maxAlpha = (byte)(puffs[i, 5] * alphaMul);

            byte r = isStorm ? (isShadow ? (byte)16 : (byte)36) : (isShadow ? (byte)138 : (byte)255);
            byte g = isStorm ? (isShadow ? (byte)21 : (byte)45) : (isShadow ? (byte)162 : (byte)254);
            byte b = isStorm ? (isShadow ? (byte)30 : (byte)62) : (isShadow ? (byte)192 : (byte)252);


            System.Windows.Media.RadialGradientBrush rg = new System.Windows.Media.RadialGradientBrush();
            rg.GradientOrigin = new System.Windows.Point(0.45, 0.35);
            rg.Center = new System.Windows.Point(0.5, 0.5);
            rg.GradientStops.Add(new System.Windows.Media.GradientStop(
                System.Windows.Media.Color.FromArgb(maxAlpha, r, g, b), 0.0));
            rg.GradientStops.Add(new System.Windows.Media.GradientStop(
                System.Windows.Media.Color.FromArgb((byte)(maxAlpha * 0.58), r, g, b), 0.45));
            rg.GradientStops.Add(new System.Windows.Media.GradientStop(
                System.Windows.Media.Color.FromArgb((byte)(maxAlpha * 0.16), r, g, b), 0.76));
            rg.GradientStops.Add(new System.Windows.Media.GradientStop(
                System.Windows.Media.Color.FromArgb(0, r, g, b), 1.0));
            rg.Freeze();

            System.Windows.Shapes.Ellipse el = new System.Windows.Shapes.Ellipse()
            {
                Width = rx * 2.0,
                Height = ry * 2.0,
                Fill = rg
            };
            System.Windows.Controls.Canvas.SetLeft(el, cx - rx);
            System.Windows.Controls.Canvas.SetTop(el, cy - ry);
            cloud.Children.Add(el);
        }

        return cloud;
    }

    private void UpdateVolumetricClouds()
    {
        if (cloudAnimTimer != null)
        {
            cloudAnimTimer.Stop();
            cloudAnimTimer.Dispose();
            cloudAnimTimer = null;
        }
        activeCloudBanks.Clear();
        passClouds.Clear();
        if (fgCanvas != null) fgCanvas.Children.Clear();

        if (viewport != null && cloudsVisual != null)
        {
            viewport.Children.Remove(cloudsVisual);
            cloudsVisual = null;
        }
        if (viewport != null && rainVisual != null)
        {
            viewport.Children.Remove(rainVisual);
            rainVisual = null;
            rainDropTransform = null;
            rainMatBrush = null;
        }

        if (lightPreset != 1 && lightPreset != 3) return;
        bool isStorm = (lightPreset == 3);

        if (isStorm && viewport != null)
            Build3DRainVolume();


        // Сплошные перекрывающиеся облачные гряды по всему небу (от горизонта -4° до зенита +62°)
        double[,] presets = new double[,]
        {
            // Нижний сплошной облачный горизонт (перекрываются каждые ~30°)
            {   0.0, -3.5, 1.35, 0.035, 1.0 },
            {  32.0, -2.0, 1.45, 0.038, 1.0 },
            {  65.0, -4.0, 1.40, 0.032, 1.0 },
            {  98.0, -1.5, 1.50, 0.040, 1.0 },
            { 130.0, -3.5, 1.35, 0.034, 1.0 },
            { 162.0, -2.0, 1.45, 0.036, 1.0 },
            { 195.0, -4.2, 1.55, 0.030, 1.0 },
            { 228.0, -1.8, 1.40, 0.038, 1.0 },
            { 260.0, -3.5, 1.45, 0.033, 1.0 },
            { 292.0, -2.2, 1.50, 0.037, 1.0 },
            { 325.0, -3.8, 1.40, 0.035, 1.0 },

            // Средний ярус облачных полей (12°..26° высоты)
            {  18.0, 14.0, 1.25, 0.052, 1.1 },
            {  75.0, 19.0, 1.30, 0.055, 1.1 },
            { 135.0, 13.0, 1.25, 0.050, 1.1 },
            { 195.0, 21.0, 1.35, 0.058, 1.1 },
            { 255.0, 15.0, 1.25, 0.052, 1.1 },
            { 312.0, 20.0, 1.30, 0.055, 1.1 },

            // Верхний ярус над головой (36°..62° высоты — плотное небо при взгляде снизу вверх)
            {  25.0, 38.0, 1.20, 0.068, 1.2 },
            {  95.0, 46.0, 1.25, 0.072, 1.2 },
            { 165.0, 36.0, 1.20, 0.066, 1.2 },
            { 235.0, 52.0, 1.30, 0.075, 1.2 },
            { 305.0, 42.0, 1.22, 0.070, 1.2 },
            { 120.0, 62.0, 1.35, 0.080, 1.3 }
        };

        for (int i = 0; i < presets.GetLength(0); i++)
        {
            double sc = presets[i, 2];
            activeCloudBanks.Add(new CloudBank3D
            {
                Yaw = presets[i, 0],
                Pitch = presets[i, 1],
                Scale = sc,
                SpeedYaw = presets[i, 3],
                Dist = presets[i, 4],
                Visual = BuildCumulusCloudElement(sc, false, isStorm)
            });
        }

        if (!isStorm)
        {
            passClouds.Add(new PassThroughCloud
            {
                Progress = -1.2,
                OffsetX = 65,
                OffsetY = -25,
                Speed = 0.0022,
                Scale = 1.05,
                Visual = BuildCumulusCloudElement(1.05, true, false)
            });
        }



        cloudAnimTimer = new System.Windows.Forms.Timer();
        cloudAnimTimer.Interval = 33;
        cloudAnimTimer.Tick += (s, e) => SortAndSyncClouds(true);
        cloudAnimTimer.Start();
    }

    private void SortAndSyncClouds(bool advanceMovement)
    {
        if (lightPreset != 1 && lightPreset != 3)
        {
            if (fgCanvas != null && fgCanvas.Children.Count > 0) fgCanvas.Children.Clear();
            return;
        }

        for (int i = 0; i < activeCloudBanks.Count; i++)
        {
            if (advanceMovement)
                activeCloudBanks[i].Yaw = (activeCloudBanks[i].Yaw + activeCloudBanks[i].SpeedYaw * (lightPreset == 3 ? 1.6 : 1.0)) % 360.0;
        }

        if (lightPreset == 3 && advanceMovement)
        {
            rainPhase = (rainPhase + 22.0) % 1000.0;
            if (lightningFlashAlpha > 0.0)
            {
                lightningFlashAlpha -= 0.16;
                if (lightningFlashAlpha <= 0.25 && lightningPulsesLeft > 0)
                {
                    lightningPulsesLeft--;
                    lightningFlashAlpha = 0.88; // однократный повторный импульс без зацикливания
                }
                if (lightningFlashAlpha < 0.0) lightningFlashAlpha = 0.0;
            }
            else
            {
                lightningTicksUntilNext--;
                if (lightningTicksUntilNext <= 0)
                {
                    Random rnd = new Random(Environment.TickCount);
                    lightningFlashAlpha = 1.0;
                    lightningPulsesLeft = rnd.Next(0, 2); // 0 или 1 повторная вспышка
                    lightningSeed = rnd.Next(1, 99999);
                    lightningYaw = (orbitYaw + 180.0 + (rnd.NextDouble() * 140.0 - 70.0) + 360.0) % 360.0;
                    lightningPitch = 6.0 + rnd.NextDouble() * 22.0;
                    lightningTicksUntilNext = 90 + rnd.Next(130);
                }
            }


            if (stormFlashLight != null)
            {
                byte flashVal = (byte)(lightningFlashAlpha * 235);
                byte flashBlue = (byte)(lightningFlashAlpha * 255);
                stormFlashLight.Color = System.Windows.Media.Color.FromRgb(flashVal, (byte)(flashVal * 0.96), flashBlue);
                double rad = lightningYaw * Math.PI / 180.0;
                stormFlashLight.Direction = new System.Windows.Media.Media3D.Vector3D(-Math.Sin(rad), -1.2, -Math.Cos(rad));
            }
        }

        if (advanceMovement)
        {
            RenderBackgroundDecor();
            if (lightPreset == 1) RenderPassThroughClouds();
            else if (lightPreset == 3) Step3DRain();
        }

    }

    private void RenderPassThroughClouds()
    {
        if (fgCanvas == null) return;
        fgCanvas.Children.Clear();
        if (lightPreset != 1 || passClouds.Count == 0) return;

        double w = fgCanvas.ActualWidth > 0 ? fgCanvas.ActualWidth : 840;
        double h = fgCanvas.ActualHeight > 0 ? fgCanvas.ActualHeight : 600;

        for (int i = 0; i < passClouds.Count; i++)
        {
            PassThroughCloud pc = passClouds[i];
            pc.Progress += pc.Speed;
            if (pc.Progress > 1.0)
            {
                // Случайная пауза ~20-40 секунд до следующего пролёта
                pc.Progress = -1.4 - ((DateTime.UtcNow.Ticks % 150) / 100.0);
                pc.OffsetX = ((DateTime.UtcNow.Millisecond % 180) - 90);
                pc.OffsetY = ((DateTime.UtcNow.Second % 90) - 45);
                continue;
            }
            if (pc.Progress < 0.0) continue;

            double t = pc.Progress;
            double perspScale = 0.65 + t * 0.95;

            // Лёгкая, ненавязчивая полупрозрачность (0.38 вместо 0.85), чтобы самолёт всегда хорошо читался
            double opacity = Math.Sin(t * Math.PI);
            opacity = Math.Pow(Math.Max(0, opacity), 0.85) * 0.38;


            double cx = w * 0.5 + pc.OffsetX + (0.5 - t) * (w * 0.65);
            double cy = h * 0.5 + pc.OffsetY + (t - 0.5) * (h * 0.35);

            pc.Visual.Opacity = opacity;
            pc.Visual.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            pc.Visual.RenderTransform = new System.Windows.Media.ScaleTransform(perspScale, perspScale);

            System.Windows.Controls.Canvas.SetLeft(pc.Visual, cx - pc.Visual.Width * 0.5);
            System.Windows.Controls.Canvas.SetTop(pc.Visual, cy - pc.Visual.Height * 0.5);
            fgCanvas.Children.Add(pc.Visual);
        }
    }

    private double WrapAngle180(double deg)
    {
        deg = (deg + 180.0) % 360.0;
        if (deg < 0) deg += 360.0;
        return deg - 180.0;
    }

    private bool ProjectSkyObject(double objYaw, double objPitch, double w, double h, out double sx, out double sy)
    {
        double camLookYaw = orbitYaw + 180.0;
        double camLookPitch = -orbitPitch;

        double dYaw = WrapAngle180(objYaw - camLookYaw);
        double dPitch = objPitch - camLookPitch;

        double pxPerDeg = h / 52.0;
        sx = w * 0.5 - dYaw * pxPerDeg;
        sy = h * 0.5 - dPitch * pxPerDeg;

        return (sx > -750 && sx < w + 750 && sy > -550 && sy < h + 550);
    }


    private void RenderBackgroundDecor()
    {
        if (bgCanvas == null) return;
        bgCanvas.Children.Clear();
        double w = bgCanvas.ActualWidth > 0 ? bgCanvas.ActualWidth : 840;
        double h = bgCanvas.ActualHeight > 0 ? bgCanvas.ActualHeight : 600;

        if (lightPreset == 0)
        {
            System.Windows.Media.Brush gridBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(24, 0, 255, 140));
            double step = 48.0;
            double offX = ((orbitYaw * 4.5) % step + step) % step;
            double offY = ((orbitPitch * 4.5) % step + step) % step;


            for (double x = offX; x < w; x += step)
            {
                System.Windows.Shapes.Line ln = new System.Windows.Shapes.Line() { X1 = x, Y1 = 0, X2 = x, Y2 = h, Stroke = gridBrush, StrokeThickness = 1 };
                bgCanvas.Children.Add(ln);
            }
            for (double y = offY; y < h; y += step)
            {
                System.Windows.Shapes.Line ln = new System.Windows.Shapes.Line() { X1 = 0, Y1 = y, X2 = w, Y2 = y, Stroke = gridBrush, StrokeThickness = 1 };
                bgCanvas.Children.Add(ln);
            }

            double cx = w / 2.0 + WrapAngle180(-orbitYaw) * 0.35;
            double cy = h / 2.0 + orbitPitch * 0.35;
            System.Windows.Media.Brush ringBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(38, 0, 255, 140));
            double[] radii = new double[] { 145, 245 };
            foreach (double r in radii)
            {
                System.Windows.Shapes.Ellipse el = new System.Windows.Shapes.Ellipse()
                {
                    Width = r * 2,
                    Height = r * 2,
                    Stroke = ringBrush,
                    StrokeThickness = 1
                };
                System.Windows.Controls.Canvas.SetLeft(el, cx - r);
                System.Windows.Controls.Canvas.SetTop(el, cy - r);
                bgCanvas.Children.Add(el);
            }
        }
        else if (lightPreset == 1)
        {
            // 1. Атмосферная дымка и нижняя облачная/морская пелена ниже линии горизонта
            double hx, hy;
            ProjectSkyObject(orbitYaw + 180.0, -3.0, w, h, out hx, out hy);
            if (hy < h + 120)
            {
                double deckTop = Math.Max(-100, hy);
                System.Windows.Media.LinearGradientBrush haze = new System.Windows.Media.LinearGradientBrush();
                haze.StartPoint = new System.Windows.Point(0.5, 0);
                haze.EndPoint = new System.Windows.Point(0.5, 1);
                haze.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0, 195, 215, 230), 0.0));
                haze.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(145, 185, 206, 222), 0.25));
                haze.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(215, 130, 158, 184), 1.0));

                System.Windows.Shapes.Rectangle groundDeck = new System.Windows.Shapes.Rectangle()
                {
                    Width = w + 4,
                    Height = Math.Max(0, h - deckTop + 100),
                    Fill = haze
                };
                System.Windows.Controls.Canvas.SetLeft(groundDeck, -2);
                System.Windows.Controls.Canvas.SetTop(groundDeck, deckTop);
                bgCanvas.Children.Add(groundDeck);
            }

            // 2. Всегда считаем экранную проекцию солнца (210°, 14°) без жёсткой обрезки по краям окна
            double sx, sy;
            ProjectSkyObject(210.0, 14.0, w, h, out sx, out sy);
            double sunAngleFromView = Math.Abs(WrapAngle180(210.0 - (orbitYaw + 180.0)));
            double sunVisibility = Math.Max(0.0, Math.Min(1.0, (140.0 - sunAngleFromView) / 45.0));

            double sunOcclusion = 0.0;
            for (int i = 0; i < activeCloudBanks.Count; i++)
            {
                CloudBank3D cb = activeCloudBanks[i];
                double dy = WrapAngle180(cb.Yaw - 210.0);
                double dp = cb.Pitch - 14.0;
                double d = Math.Sqrt(dy * dy + dp * dp);
                if (d < 22.0)
                    sunOcclusion = Math.Max(sunOcclusion, 1.0 - (d / 22.0));
            }

            if (sunVisibility > 0.01 && sx > -1200 && sx < w + 1200 && sy > -1200 && sy < h + 1200)
            {
                byte atmoAlpha = (byte)((140 + sunOcclusion * 55) * sunVisibility);
                System.Windows.Media.RadialGradientBrush atmoGlow = new System.Windows.Media.RadialGradientBrush();
                atmoGlow.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(atmoAlpha, 255, 246, 225), 0.0));
                atmoGlow.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb((byte)((60 + sunOcclusion * 45) * sunVisibility), 255, 232, 190), 0.35));
                atmoGlow.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb((byte)(18 * sunVisibility), 220, 235, 255), 0.70));
                atmoGlow.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0, 220, 235, 255), 1.0));

                System.Windows.Shapes.Ellipse bigGlow = new System.Windows.Shapes.Ellipse() { Width = 640, Height = 640, Fill = atmoGlow };
                System.Windows.Controls.Canvas.SetLeft(bigGlow, sx - 320);
                System.Windows.Controls.Canvas.SetTop(bigGlow, sy - 320);
                bgCanvas.Children.Add(bigGlow);

                byte coreAlpha = (byte)((255 - sunOcclusion * 90) * sunVisibility);
                System.Windows.Media.RadialGradientBrush coreBrush = new System.Windows.Media.RadialGradientBrush();
                coreBrush.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(coreAlpha, 255, 255, 255), 0.0));
                coreBrush.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb((byte)(coreAlpha * 0.85), 255, 252, 238), 0.35));
                coreBrush.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0, 255, 245, 210), 1.0));

                System.Windows.Shapes.Ellipse sunCore = new System.Windows.Shapes.Ellipse() { Width = 58, Height = 58, Fill = coreBrush };
                System.Windows.Controls.Canvas.SetLeft(sunCore, sx - 29);
                System.Windows.Controls.Canvas.SetTop(sunCore, sy - 29);
                bgCanvas.Children.Add(sunCore);
            }

            // 3. Облака + подповерхностное рассеивание (Silver Lining)
            List<System.Windows.Rect> cloudScreenBounds = new List<System.Windows.Rect>();

            for (int i = 0; i < activeCloudBanks.Count; i++)
            {
                CloudBank3D cb = activeCloudBanks[i];
                double cx, cy;
                if (ProjectSkyObject(cb.Yaw, cb.Pitch, w, h, out cx, out cy))
                {
                    // Сохраняем экранный контур плотного ядра облака для расчёта щелей
                    double coreW = cb.Visual.Width * 0.56;
                    double coreH = cb.Visual.Height * 0.44;
                    cloudScreenBounds.Add(new System.Windows.Rect(cx - coreW * 0.5, cy - coreH * 0.5, coreW, coreH));

                    double dYawSun = WrapAngle180(cb.Yaw - 210.0);
                    double dPitchSun = cb.Pitch - 14.0;
                    double angDist = Math.Sqrt(dYawSun * dYawSun + dPitchSun * dPitchSun);

                    if (angDist < 65.0)
                    {
                        double scatter = 1.0 - (angDist / 65.0);
                        byte glowAlpha = (byte)(scatter * scatter * 155);

                        System.Windows.Media.RadialGradientBrush scatterBrush = new System.Windows.Media.RadialGradientBrush();
                        scatterBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                            System.Windows.Media.Color.FromArgb(glowAlpha, 255, 249, 226), 0.0));
                        scatterBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                            System.Windows.Media.Color.FromArgb((byte)(glowAlpha * 0.55), 255, 236, 185), 0.50));
                        scatterBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                            System.Windows.Media.Color.FromArgb(0, 255, 236, 185), 1.0));

                        double sw = cb.Visual.Width * 1.18;
                        double sh = cb.Visual.Height * 1.18;
                        System.Windows.Shapes.Ellipse scatterGlow = new System.Windows.Shapes.Ellipse()
                        {
                            Width = sw,
                            Height = sh,
                            Fill = scatterBrush
                        };
                        System.Windows.Controls.Canvas.SetLeft(scatterGlow, cx - sw * 0.5);
                        System.Windows.Controls.Canvas.SetTop(scatterGlow, cy - sh * 0.5);
                        bgCanvas.Children.Add(scatterGlow);
                    }

                    System.Windows.Controls.Canvas.SetLeft(cb.Visual, cx - cb.Visual.Width * 0.5);
                    System.Windows.Controls.Canvas.SetTop(cb.Visual, cy - cb.Visual.Height * 0.5);
                    bgCanvas.Children.Add(cb.Visual);
                }
            }

            // 4. Плавные переливающиеся саншафты (мягкие световые конусы с рассеиванием вдоль и поперёк луча)
            if (sunVisibility > 0.02 && cloudScreenBounds.Count > 0)
            {
                System.Windows.Controls.Canvas shaftsLayer = new System.Windows.Controls.Canvas();
                shaftsLayer.Width = w;
                shaftsLayer.Height = h;
                shaftsLayer.IsHitTestVisible = false;
                shaftsLayer.Effect = new System.Windows.Media.Effects.BlurEffect() { Radius = 28.0 };

                int numRays = 72;
                double maxRayLen = Math.Sqrt(w * w + h * h) * 1.25;

                for (int r = 0; r < numRays; r++)
                {
                    double theta = (2.0 * Math.PI * r) / numRays;
                    double dirX = Math.Cos(theta);
                    double dirY = Math.Sin(theta);

                    double blockScore = 0.0;
                    double edgeProximity = 0.0;

                    for (int step = 1; step <= 7; step++)
                    {
                        double dist = step * 55.0;
                        double px = sx + dirX * dist;
                        double py = sy + dirY * dist;

                        for (int c = 0; c < cloudScreenBounds.Count; c++)
                        {
                            System.Windows.Rect rect = cloudScreenBounds[c];
                            double rcx = rect.X + rect.Width * 0.5;
                            double rcy = rect.Y + rect.Height * 0.5;
                            double nx = (px - rcx) / (rect.Width * 0.5);
                            double ny = (py - rcy) / (rect.Height * 0.5);
                            double normDist = Math.Sqrt(nx * nx + ny * ny);

                            if (normDist < 0.72)
                            {
                                blockScore += (0.72 - normDist) * 1.6;
                            }
                            else if (normDist >= 0.72 && normDist < 1.45)
                            {
                                double edgeFactor = 1.0 - Math.Abs(normDist - 1.02) / 0.45;
                                if (edgeFactor > 0) edgeProximity = Math.Max(edgeProximity, edgeFactor);
                            }
                        }
                    }

                    double intensity = edgeProximity * Math.Max(0.0, 1.0 - blockScore) * sunVisibility;
                    if (intensity > 0.14)
                    {
                        double angleDeg = theta * 180.0 / Math.PI;
                        double beamLen = maxRayLen * (0.78 + 0.22 * intensity);
                        double beamHeight = 76.0 + intensity * 115.0;

                        System.Windows.Media.RadialGradientBrush softBeamBrush = new System.Windows.Media.RadialGradientBrush();
                        softBeamBrush.GradientOrigin = new System.Windows.Point(0.04, 0.5);
                        softBeamBrush.Center = new System.Windows.Point(0.18, 0.5);
                        softBeamBrush.RadiusX = 0.85;
                        softBeamBrush.RadiusY = 0.50;

                        byte aCore = (byte)Math.Min(46, intensity * 44);
                        byte aMid = (byte)(aCore * 0.48);
                        byte aEdge = (byte)(aCore * 0.16);

                        softBeamBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                            System.Windows.Media.Color.FromArgb(aCore, 255, 250, 226), 0.0));
                        softBeamBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                            System.Windows.Media.Color.FromArgb(aMid, 255, 243, 204), 0.38));
                        softBeamBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                            System.Windows.Media.Color.FromArgb(aEdge, 240, 244, 255), 0.72));
                        softBeamBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                            System.Windows.Media.Color.FromArgb(0, 240, 244, 255), 1.0));

                        System.Windows.Shapes.Ellipse beamEllipse = new System.Windows.Shapes.Ellipse()
                        {
                            Width = beamLen,
                            Height = beamHeight,
                            Fill = softBeamBrush,
                            RenderTransformOrigin = new System.Windows.Point(0.0, 0.5),
                            RenderTransform = new System.Windows.Media.RotateTransform(angleDeg)
                        };

                        System.Windows.Controls.Canvas.SetLeft(beamEllipse, sx);
                        System.Windows.Controls.Canvas.SetTop(beamEllipse, sy - beamHeight * 0.5);
                        shaftsLayer.Children.Add(beamEllipse);
                    }
                }

                if (shaftsLayer.Children.Count > 0)
                    bgCanvas.Children.Add(shaftsLayer);
            }
        }

        else if (lightPreset == 2)
        {
            Random rnd = new Random(42667);

            for (int i = 0; i < 260; i++)
            {
                double starYaw = rnd.NextDouble() * 360.0;
                double starPitch = (rnd.NextDouble() * 170.0) - 85.0;
                double sz = (i % 9 == 0) ? 2.7 : ((i % 3 == 0) ? 1.8 : 1.1);
                byte alpha = (byte)(120 + rnd.Next(135));
                byte blueTint = (byte)(i % 4 == 0 ? 255 : 225);

                double sx, sy;
                if (ProjectSkyObject(starYaw, starPitch, w, h, out sx, out sy))
                {
                    System.Windows.Shapes.Ellipse star = new System.Windows.Shapes.Ellipse()
                    {
                        Width = sz,
                        Height = sz,
                        Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, 225, 235, blueTint))
                    };
                    System.Windows.Controls.Canvas.SetLeft(star, sx);
                    System.Windows.Controls.Canvas.SetTop(star, sy);
                    bgCanvas.Children.Add(star);
                }
            }

            double mx, my;
            if (ProjectSkyObject(242.0, 15.0, w, h, out mx, out my))
            {
                System.Windows.Media.RadialGradientBrush moonGlow = new System.Windows.Media.RadialGradientBrush();
                moonGlow.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(95, 170, 205, 255), 0.0));
                moonGlow.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0, 170, 205, 255), 1.0));
                System.Windows.Shapes.Ellipse glow = new System.Windows.Shapes.Ellipse() { Width = 170, Height = 170, Fill = moonGlow };
                System.Windows.Controls.Canvas.SetLeft(glow, mx - 85);
                System.Windows.Controls.Canvas.SetTop(glow, my - 85);
                bgCanvas.Children.Add(glow);

                System.Windows.Shapes.Ellipse moon = new System.Windows.Shapes.Ellipse()
                {
                    Width = 44,
                    Height = 44,
                    Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 232, 250))
                };
                System.Windows.Controls.Canvas.SetLeft(moon, mx - 22);
                System.Windows.Controls.Canvas.SetTop(moon, my - 22);
                bgCanvas.Children.Add(moon);
            }
        }
        else
        {
            double lx = 0, ly = 0;
            bool flashVisible = (lightningFlashAlpha > 0.02) && ProjectSkyObject(lightningYaw, lightningPitch, w, h, out lx, out ly);

            // 1. Глубокое сине-фиолетовое свечение внутри туч (как на фотореференсе)
            if (flashVisible)
            {
                System.Windows.Media.RadialGradientBrush deepBlueGlow = new System.Windows.Media.RadialGradientBrush();
                deepBlueGlow.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb((byte)(lightningFlashAlpha * 235), 165, 195, 255), 0.0));
                deepBlueGlow.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb((byte)(lightningFlashAlpha * 185), 55, 105, 245), 0.30));
                deepBlueGlow.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb((byte)(lightningFlashAlpha * 95), 25, 55, 175), 0.65));
                deepBlueGlow.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(0, 15, 30, 110), 1.0));

                System.Windows.Shapes.Ellipse flashHalo = new System.Windows.Shapes.Ellipse() { Width = 1150, Height = 820, Fill = deepBlueGlow };
                System.Windows.Controls.Canvas.SetLeft(flashHalo, lx - 575);
                System.Windows.Controls.Canvas.SetTop(flashHalo, ly - 380);
                bgCanvas.Children.Add(flashHalo);
            }

            // 2. Грозовые тучи
            for (int i = 0; i < activeCloudBanks.Count; i++)
            {
                CloudBank3D cb = activeCloudBanks[i];
                double cx, cy;
                if (ProjectSkyObject(cb.Yaw, cb.Pitch, w, h, out cx, out cy))
                {
                    System.Windows.Controls.Canvas.SetLeft(cb.Visual, cx - cb.Visual.Width * 0.5);
                    System.Windows.Controls.Canvas.SetTop(cb.Visual, cy - cb.Visual.Height * 0.5);
                    bgCanvas.Children.Add(cb.Visual);
                }
            }

            // 3. Тонкий, дёрганый микро-сегментный канал молнии + подсвеченное облачное ядро
            if (flashVisible)
            {
                // Яркое локальное пятно пробоя в облаке
                System.Windows.Media.RadialGradientBrush breakGlow = new System.Windows.Media.RadialGradientBrush();
                breakGlow.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb((byte)(lightningFlashAlpha * 210), 190, 215, 255), 0.0));
                breakGlow.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb((byte)(lightningFlashAlpha * 110), 65, 115, 250), 0.45));
                breakGlow.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(0, 30, 60, 180), 1.0));

                System.Windows.Shapes.Ellipse cloudBreak = new System.Windows.Shapes.Ellipse() { Width = 460, Height = 340, Fill = breakGlow };
                System.Windows.Controls.Canvas.SetLeft(cloudBreak, lx - 230);
                System.Windows.Controls.Canvas.SetTop(cloudBreak, ly - 150);
                bgCanvas.Children.Add(cloudBreak);

                Random boltRnd = new Random(lightningSeed);
                System.Windows.Media.PointCollection mainPts = new System.Windows.Media.PointCollection();
                double curX = lx, curY = Math.Max(-10, ly - 75);
                mainPts.Add(new System.Windows.Point(curX, curY));

                double drift = (boltRnd.NextDouble() - 0.5) * 3.5;
                int segments = 46;
                for (int s = 0; s < segments; s++)
                {
                    curX += drift + (boltRnd.NextDouble() - 0.5) * 18.0;
                    curY += 8.0 + boltRnd.NextDouble() * 8.5;
                    mainPts.Add(new System.Windows.Point(curX, curY));

                    // Тонкие капиллярные ответвления
                    if (s == 9 || s == 19 || s == 31)
                    {
                        System.Windows.Media.PointCollection branchPts = new System.Windows.Media.PointCollection();
                        double bx = curX, by = curY;
                        branchPts.Add(new System.Windows.Point(bx, by));
                        double side = (s % 2 == 1) ? -1.0 : 1.0;
                        for (int b = 0; b < 14; b++)
                        {
                            bx += side * (4.0 + boltRnd.NextDouble() * 9.0) + (boltRnd.NextDouble() - 0.5) * 8.0;
                            by += 5.0 + boltRnd.NextDouble() * 7.0;
                            branchPts.Add(new System.Windows.Point(bx, by));
                        }

                        System.Windows.Shapes.Polyline branchGlow = new System.Windows.Shapes.Polyline()
                        {
                            Points = branchPts,
                            Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)(lightningFlashAlpha * 110), 80, 130, 255)),
                            StrokeThickness = 2.4
                        };
                        bgCanvas.Children.Add(branchGlow);

                        System.Windows.Shapes.Polyline branchCore = new System.Windows.Shapes.Polyline()
                        {
                            Points = branchPts,
                            Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)(lightningFlashAlpha * 215), 225, 238, 255)),
                            StrokeThickness = 0.85
                        };
                        bgCanvas.Children.Add(branchCore);
                    }
                }

                // Сине-фиолетовый электрический ореол
                System.Windows.Shapes.Polyline outerGlow = new System.Windows.Shapes.Polyline()
                {
                    Points = mainPts,
                    Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)(lightningFlashAlpha * 125), 65, 115, 255)),
                    StrokeThickness = 6.5,
                    StrokeLineJoin = System.Windows.Media.PenLineJoin.Round
                };
                bgCanvas.Children.Add(outerGlow);

                // Тонкое ослепительно-белое ядро молнии (1.5 px)
                System.Windows.Shapes.Polyline coreBolt = new System.Windows.Shapes.Polyline()
                {
                    Points = mainPts,
                    Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)(lightningFlashAlpha * 255), 248, 252, 255)),
                    StrokeThickness = 1.5,
                    StrokeLineJoin = System.Windows.Media.PenLineJoin.Round
                };
                bgCanvas.Children.Add(coreBolt);
            }
        }
    }

    private void Build3DRainVolume()
    {
        System.Windows.Media.Media3D.MeshGeometry3D mesh = new System.Windows.Media.Media3D.MeshGeometry3D();
        double spanY = 7.0;

        for (int copy = 0; copy < 2; copy++)
        {
            double baseOffsetY = copy * spanY;
            Random layerRnd = new Random(8842);

            for (int i = 0; i < 110; i++)
            {
                double x = (layerRnd.NextDouble() - 0.5) * 9.0;
                double y = (layerRnd.NextDouble() - 0.5) * spanY + baseOffsetY;
                double z = (layerRnd.NextDouble() - 0.5) * 9.0;

                if (x * x + y * y + z * z < 1.2) continue;

                double len = 0.28 + layerRnd.NextDouble() * 0.24;
                double tiltX = -0.055;
                double r = 0.0032;

                int idx = mesh.Positions.Count;
                mesh.Positions.Add(new System.Windows.Media.Media3D.Point3D(x - r, y, z));
                mesh.Positions.Add(new System.Windows.Media.Media3D.Point3D(x + r, y, z));
                mesh.Positions.Add(new System.Windows.Media.Media3D.Point3D(x - tiltX, y + len, z));

                mesh.Positions.Add(new System.Windows.Media.Media3D.Point3D(x, y, z - r));
                mesh.Positions.Add(new System.Windows.Media.Media3D.Point3D(x, y, z + r));
                mesh.Positions.Add(new System.Windows.Media.Media3D.Point3D(x - tiltX, y + len, z));

                mesh.TriangleIndices.Add(idx);
                mesh.TriangleIndices.Add(idx + 1);
                mesh.TriangleIndices.Add(idx + 2);

                mesh.TriangleIndices.Add(idx + 3);
                mesh.TriangleIndices.Add(idx + 4);
                mesh.TriangleIndices.Add(idx + 5);
            }
        }
        mesh.Freeze();

        rainMatBrush = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromArgb(65, 175, 205, 240));
        System.Windows.Media.Media3D.EmissiveMaterial rainMat = new System.Windows.Media.Media3D.EmissiveMaterial(rainMatBrush);

        System.Windows.Media.Media3D.GeometryModel3D rainModel = new System.Windows.Media.Media3D.GeometryModel3D(mesh, rainMat);
        rainModel.BackMaterial = rainMat;

        rainDropTransform = new System.Windows.Media.Media3D.TranslateTransform3D(0, 0, 0);
        rainModel.Transform = rainDropTransform;

        rainVisual = new System.Windows.Media.Media3D.ModelVisual3D();
        rainVisual.Content = rainModel;
        viewport.Children.Add(rainVisual);
    }

    private void Step3DRain()
    {
        if (rainDropTransform == null || rainMatBrush == null || lightPreset != 3) return;
        double spanY = 7.0;
        double offset = (rainPhase * 0.018) % spanY;
        rainDropTransform.OffsetY = -offset;
        rainDropTransform.OffsetX = -offset * 0.14;


        byte alpha = (byte)Math.Min(195, 58 + lightningFlashAlpha * 115);
        rainMatBrush.Color = System.Windows.Media.Color.FromArgb(alpha, 185, 212, 248);
    }


    private void UpdateCamera()


    {
        double yawRad = orbitYaw * Math.PI / 180.0;
        double pitchRad = orbitPitch * Math.PI / 180.0;
        double x = zoomDist * Math.Cos(pitchRad) * Math.Sin(yawRad);
        double y = zoomDist * Math.Sin(pitchRad);
        double z = zoomDist * Math.Cos(pitchRad) * Math.Cos(yawRad);
        camera.Position = new System.Windows.Media.Media3D.Point3D(x, y, z);
        System.Windows.Media.Media3D.Vector3D look = new System.Windows.Media.Media3D.Vector3D(-x, -y, -z);
        look.Normalize();
        camera.LookDirection = look;
    camera.UpDirection = new System.Windows.Media.Media3D.Vector3D(0, 1, 0);
    SortAndSyncClouds(false);
    RenderBackgroundDecor();
}


private void LoadModelAndTexture()
        {
            System.Windows.Controls.Grid grid = (System.Windows.Controls.Grid)host.Child;
            if (lblMissingModel != null) { grid.Children.Remove(lblMissingModel); lblMissingModel = null; }
            if (modelVisual != null) { viewport.Children.Remove(modelVisual); modelVisual = null; }

            string fbxPath = FindReferenceModel(entry.Aircraft);
            if (string.IsNullOrEmpty(fbxPath) || !File.Exists(fbxPath))
            {
                lblMissingModel = new System.Windows.Controls.TextBlock();
                lblMissingModel.Text = "Референс-модель для борта \"" + entry.Aircraft + "\" не найдена.\n" +
                          "Ожидаемый путь: %LOCALAPPDATA%\\VTOL_QoL\\models\\" + entry.Aircraft + ".fbx\n" +
                          "Положите туда экспортированную модель самолета (FBX), чтобы включить 3D-просмотр.";
                lblMissingModel.Foreground = System.Windows.Media.Brushes.OrangeRed;
                lblMissingModel.FontSize = 16;
                lblMissingModel.TextWrapping = System.Windows.TextWrapping.Wrap;
                lblMissingModel.Margin = new System.Windows.Thickness(20);
                grid.Children.Add(lblMissingModel);
                return;
            }

        FbxMeshData meshData = FbxLoader.Load(fbxPath);
        if (meshData == null)
        {
            MessageBox.Show("Не удалось прочитать FBX-файл модели: " + fbxPath, "Ошибка 3D-просмотра", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        NormalizeToUnitCube(meshData);

        System.Windows.Media.Brush matBrush;
        string texPath = !string.IsNullOrEmpty(entry.TexturePath) && File.Exists(entry.TexturePath) ? entry.TexturePath : null;
        if (texPath != null)
        {
            try
            {
                System.Windows.Media.Imaging.BitmapImage bmp = LiveryParser.LoadTextureBitmap(texPath);
                textureBrush = new System.Windows.Media.ImageBrush(bmp);
                textureBrush.ViewportUnits = System.Windows.Media.BrushMappingMode.Absolute;
                textureBrush.Viewport = new System.Windows.Rect(0, 0, 1, 1);
                textureBrush.TileMode = System.Windows.Media.TileMode.Tile;
                matBrush = textureBrush;
            }
            catch
            {
                matBrush = System.Windows.Media.Brushes.LightGray;
            }
        }
        else
        {
            matBrush = System.Windows.Media.Brushes.LightGray;
        }

        System.Windows.Media.Media3D.Model3DGroup group = new System.Windows.Media.Media3D.Model3DGroup();

        System.Windows.Media.Media3D.MeshGeometry3D bodyGeom = BuildWpfMesh(meshData.Body);
        System.Windows.Media.Media3D.MaterialGroup bodyMatGroup = new System.Windows.Media.Media3D.MaterialGroup();
        bodyMatGroup.Children.Add(new System.Windows.Media.Media3D.DiffuseMaterial(matBrush));
        bodyMatGroup.Children.Add(new System.Windows.Media.Media3D.SpecularMaterial(
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(65, 70, 75)), 22));
        model = new System.Windows.Media.Media3D.GeometryModel3D(bodyGeom, bodyMatGroup);
        model.BackMaterial = bodyMatGroup;
        group.Children.Add(model);


        if (meshData.Details.Vertices.Count > 0)
        {
            System.Windows.Media.Media3D.MeshGeometry3D detailsGeom = BuildWpfMesh(meshData.Details);
            System.Windows.Media.Media3D.MaterialGroup detailsMat = new System.Windows.Media.Media3D.MaterialGroup();
            detailsMat.Children.Add(new System.Windows.Media.Media3D.DiffuseMaterial(
                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(42, 46, 51))));
            detailsMat.Children.Add(new System.Windows.Media.Media3D.SpecularMaterial(
                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(90, 95, 100)), 25));
            group.Children.Add(new System.Windows.Media.Media3D.GeometryModel3D(detailsGeom, detailsMat));
        }

        if (meshData.Glass.Vertices.Count > 0)
        {
            System.Windows.Media.Media3D.MeshGeometry3D glassGeom = BuildWpfMesh(meshData.Glass);
            System.Windows.Media.Media3D.MaterialGroup glassMat = new System.Windows.Media.Media3D.MaterialGroup();
            glassMat.Children.Add(new System.Windows.Media.Media3D.DiffuseMaterial(
                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(115, 35, 65, 85))));
            glassMat.Children.Add(new System.Windows.Media.Media3D.SpecularMaterial(
                System.Windows.Media.Brushes.White, 60));
            group.Children.Add(new System.Windows.Media.Media3D.GeometryModel3D(glassGeom, glassMat));
        }

        modelVisual = new System.Windows.Media.Media3D.ModelVisual3D();
        modelVisual.Content = group;
        viewport.Children.Add(modelVisual);

        BuildAircraftLights(meshData);
    }

    private FbxMeshData lastLoadedMeshData;


    private void BuildAircraftLights(FbxMeshData meshData)
    {
        lastLoadedMeshData = meshData;
        if (navLightTimer != null)
        {
            navLightTimer.Stop();
            navLightTimer.Dispose();
            navLightTimer = null;
        }
        strobeBrushes.Clear();
        strobePointLights.Clear();

        if (viewport == null) return;
        if (aircraftLightsVisual != null)
        {
            viewport.Children.Remove(aircraftLightsVisual);
            aircraftLightsVisual = null;
        }

        if (meshData == null || meshData.Lights.Count == 0) return;

        bool isNightOrStorm = (lightPreset == 2 || lightPreset == 3);
        bool isDay = (lightPreset == 1);

        System.Windows.Media.Media3D.Model3DGroup lightsGroup = new System.Windows.Media.Media3D.Model3DGroup();
        System.Windows.Media.Media3D.MeshGeometry3D bulbMesh = BuildLightSphereMesh(10, 8);

        for (int i = 0; i < meshData.Lights.Count; i++)
        {
            FbxLightMarker lm = meshData.Lights[i];
            System.Windows.Media.Media3D.Point3D rawPos = lm.Position;
            System.Windows.Media.Media3D.Point3D pos = SnapToWingSurface(meshData.Body.Vertices, rawPos, lm.Kind);
            AppLogger.Log("LIGHT_SNAP", string.Format("[{0}] {1}: Raw=({2:F3},{3:F3},{4:F3}) -> Snapped=({5:F3},{6:F3},{7:F3})",
                entry != null ? entry.Aircraft : "?", lm.Kind,
                rawPos.X, rawPos.Y, rawPos.Z, pos.X, pos.Y, pos.Z));



            if ((lm.Kind == "NavRed" || lm.Kind == "NavGreen" || lm.Kind == "NavWhite") && navLightsEnabled)
            {
                System.Windows.Media.Color coreCol = lm.Kind == "NavRed"
                    ? System.Windows.Media.Color.FromRgb(255, 35, 35)
                    : (lm.Kind == "NavGreen"
                        ? System.Windows.Media.Color.FromRgb(30, 255, 95)
                        : System.Windows.Media.Color.FromRgb(255, 248, 230));

                byte haloAlpha = isNightOrStorm ? (byte)115 : (isDay ? (byte)35 : (byte)75);
                System.Windows.Media.Color haloCol = System.Windows.Media.Color.FromArgb(haloAlpha, coreCol.R, coreCol.G, coreCol.B);

                AddLightBulb(lightsGroup, bulbMesh, pos, 0.013, coreCol);
                AddLightBulb(lightsGroup, bulbMesh, pos, isNightOrStorm ? 0.042 : 0.026, haloCol);

                if (isNightOrStorm)
                {
                    System.Windows.Media.Color ptCol = lm.Kind == "NavRed"
                        ? System.Windows.Media.Color.FromRgb(160, 20, 20)
                        : (lm.Kind == "NavGreen"
                            ? System.Windows.Media.Color.FromRgb(20, 160, 55)
                            : System.Windows.Media.Color.FromRgb(140, 135, 120));
                    System.Windows.Media.Media3D.PointLight pt = new System.Windows.Media.Media3D.PointLight(ptCol, pos);
                    pt.Range = 0.65;
                    pt.ConstantAttenuation = 0.6;
                    pt.LinearAttenuation = 2.2;
                    lightsGroup.Children.Add(pt);
                }
            }
            else if ((lm.Kind == "Strobe" || lm.Kind == "RedStrobe") && strobeLightsEnabled)
            {
                bool isRed = (lm.Kind == "RedStrobe");
                System.Windows.Media.Media3D.Point3D stPos = pos;

                // Если строб на крыле — ставим его строго по середине торца законцовки относительно БАНО этого крыла
                if (!isRed && Math.Abs(pos.X) > 0.35)
                {
                    for (int n = 0; n < meshData.Lights.Count; n++)
                    {
                        FbxLightMarker nav = meshData.Lights[n];
                        if ((nav.Kind == "NavRed" || nav.Kind == "NavGreen") && Math.Sign(nav.Position.X) == Math.Sign(pos.X))
                        {
                            System.Windows.Media.Media3D.Point3D navSnapped = SnapToWingSurface(meshData.Body.Vertices, nav.Position, nav.Kind);
                            stPos = new System.Windows.Media.Media3D.Point3D(navSnapped.X, navSnapped.Y, navSnapped.Z - 0.028);
                            break;
                        }
                    }
                }



                System.Windows.Media.SolidColorBrush coreBr = new System.Windows.Media.SolidColorBrush(
                    isRed ? System.Windows.Media.Color.FromArgb(0, 255, 45, 45) : System.Windows.Media.Color.FromArgb(0, 255, 255, 255));
                System.Windows.Media.SolidColorBrush haloBr = new System.Windows.Media.SolidColorBrush(
                    isRed ? System.Windows.Media.Color.FromArgb(0, 255, 30, 30) : System.Windows.Media.Color.FromArgb(0, 200, 230, 255));

                strobeBrushes.Add(coreBr);
                strobeBrushes.Add(haloBr);

                AddLightBulbBrush(lightsGroup, bulbMesh, stPos, 0.011, coreBr);
                AddLightBulbBrush(lightsGroup, bulbMesh, stPos, 0.036, haloBr);

                System.Windows.Media.Media3D.Point3D lightOrigin = new System.Windows.Media.Media3D.Point3D(
                    stPos.X * 0.92, stPos.Y + 0.04, stPos.Z);
                System.Windows.Media.Media3D.PointLight stPt = new System.Windows.Media.Media3D.PointLight(
                    System.Windows.Media.Color.FromRgb(0, 0, 0), lightOrigin);
                stPt.Range = 0.85;
                stPt.ConstantAttenuation = 0.6;
                stPt.LinearAttenuation = 2.0;
                lightsGroup.Children.Add(stPt);
                strobePointLights.Add(stPt);
            }
            else if (lm.Kind == "Landing" && landingLightEnabled)
            {
                if (!isDay)
                {
                    System.Windows.Media.Color lampCol = System.Windows.Media.Color.FromRgb(255, 250, 232);
                    AddLightBulb(lightsGroup, bulbMesh, pos, 0.014, lampCol);
                    AddLightBulb(lightsGroup, bulbMesh, pos, 0.034,
                        System.Windows.Media.Color.FromArgb(isNightOrStorm ? (byte)95 : (byte)45, 255, 244, 205));

                    if (isNightOrStorm)
                    {
                        System.Windows.Media.Media3D.PointLight nosePt = new System.Windows.Media.Media3D.PointLight(
                            System.Windows.Media.Color.FromRgb(210, 198, 165),
                            new System.Windows.Media.Media3D.Point3D(pos.X, pos.Y - 0.04, pos.Z + 0.12));
                        nosePt.Range = 0.85;
                        nosePt.ConstantAttenuation = 0.5;
                        nosePt.LinearAttenuation = 1.8;
                        lightsGroup.Children.Add(nosePt);
                    }
                }
            }
        }


        aircraftLightsVisual = new System.Windows.Media.Media3D.ModelVisual3D();
        aircraftLightsVisual.Content = lightsGroup;
        viewport.Children.Add(aircraftLightsVisual);

        if (strobeBrushes.Count > 0)
        {
            navLightTimer = new System.Windows.Forms.Timer();
            navLightTimer.Interval = 40;
            navLightTimer.Tick += (s, e) => StepAircraftLights();
            navLightTimer.Start();
        }
    }

    private System.Windows.Media.Media3D.Point3D SnapToWingSurface(
        List<System.Windows.Media.Media3D.Point3D> verts,
        System.Windows.Media.Media3D.Point3D pt,
        string kind)
    {
        if (verts == null || verts.Count == 0 || kind == "Landing") return pt;
        if (Math.Abs(pt.X) < 0.25) return pt;

        double bestDistSq = double.MaxValue;
        System.Windows.Media.Media3D.Point3D bestVert = pt;

        for (int i = 0; i < verts.Count; i++)
        {
            System.Windows.Media.Media3D.Point3D v = verts[i];
            double dx = v.X - pt.X;
            double dy = v.Y - pt.Y;
            double dz = v.Z - pt.Z;
            double dSq = dx * dx + dy * dy + dz * dz;
            if (dSq < bestDistSq)
            {
                bestDistSq = dSq;
                bestVert = v;
            }
        }

        if (bestDistSq < 0.032)
        {
            double outOffset = pt.X > 0 ? 0.006 : -0.006;
            return new System.Windows.Media.Media3D.Point3D(bestVert.X + outOffset, bestVert.Y, bestVert.Z);
        }
        return pt;
    }

    private void StepAircraftLights()
    {
        if (strobeBrushes.Count == 0) return;
        strobePhaseTick = (strobePhaseTick + 1) % 38;

        bool isFlashOn = (strobePhaseTick == 0 || strobePhaseTick == 1 || strobePhaseTick == 4 || strobePhaseTick == 5);
        byte coreA = isFlashOn ? (byte)255 : (byte)0;
        byte haloA = isFlashOn ? (byte)(lightPreset == 1 ? 75 : 155) : (byte)0;

    for (int i = 0; i < strobeBrushes.Count; i += 2)
    {
        System.Windows.Media.Color baseC = strobeBrushes[i].Color;
        bool isRed = (baseC.G < 100);
        strobeBrushes[i].Color = isRed
            ? System.Windows.Media.Color.FromArgb(coreA, 255, 45, 45)
            : System.Windows.Media.Color.FromArgb(coreA, 255, 255, 255);
        if (i + 1 < strobeBrushes.Count)
        {
            strobeBrushes[i + 1].Color = isRed
                ? System.Windows.Media.Color.FromArgb(haloA, 255, 30, 30)
                : System.Windows.Media.Color.FromArgb(haloA, 200, 230, 255);
        }

        int ptIdx = i / 2;
        if (ptIdx < strobePointLights.Count)
        {
            byte ptVal = (isFlashOn && (lightPreset == 2 || lightPreset == 3)) ? (byte)210 : (byte)0;
            strobePointLights[ptIdx].Color = isRed
                ? System.Windows.Media.Color.FromRgb(ptVal, (byte)(ptVal * 0.12), (byte)(ptVal * 0.12))
                : System.Windows.Media.Color.FromRgb(ptVal, ptVal, (byte)Math.Min(255, ptVal + 25));
        }
    }
}

private void AddLightBulb(System.Windows.Media.Media3D.Model3DGroup group, System.Windows.Media.Media3D.MeshGeometry3D sphere, System.Windows.Media.Media3D.Point3D pos, double radius, System.Windows.Media.Color color)
    {
        AddLightBulbBrush(group, sphere, pos, radius, new System.Windows.Media.SolidColorBrush(color));
    }

    private void AddLightBulbBrush(System.Windows.Media.Media3D.Model3DGroup group, System.Windows.Media.Media3D.MeshGeometry3D sphere, System.Windows.Media.Media3D.Point3D pos, double radius, System.Windows.Media.SolidColorBrush brush)
    {
        System.Windows.Media.Media3D.EmissiveMaterial mat = new System.Windows.Media.Media3D.EmissiveMaterial(brush);
        System.Windows.Media.Media3D.GeometryModel3D gm = new System.Windows.Media.Media3D.GeometryModel3D(sphere, mat);
        System.Windows.Media.Media3D.Transform3DGroup tg = new System.Windows.Media.Media3D.Transform3DGroup();
        tg.Children.Add(new System.Windows.Media.Media3D.ScaleTransform3D(radius, radius, radius));
        tg.Children.Add(new System.Windows.Media.Media3D.TranslateTransform3D(pos.X, pos.Y, pos.Z));
        gm.Transform = tg;
        group.Children.Add(gm);
    }

    private System.Windows.Media.Media3D.MeshGeometry3D BuildLightSphereMesh(int slices, int stacks)
    {
        System.Windows.Media.Media3D.MeshGeometry3D m = new System.Windows.Media.Media3D.MeshGeometry3D();
        for (int st = 0; st <= stacks; st++)
        {
            double phi = Math.PI * st / stacks;
            double y = Math.Cos(phi);
            double r = Math.Sin(phi);
            for (int sl = 0; sl <= slices; sl++)
            {
                double th = 2.0 * Math.PI * sl / slices;
                m.Positions.Add(new System.Windows.Media.Media3D.Point3D(r * Math.Cos(th), y, r * Math.Sin(th)));
            }
        }
        int stride = slices + 1;
        for (int st = 0; st < stacks; st++)
        {
            for (int sl = 0; sl < slices; sl++)
            {
                int a = st * stride + sl, b = a + stride, c = a + 1, d = b + 1;
                m.TriangleIndices.Add(a); m.TriangleIndices.Add(b); m.TriangleIndices.Add(c);
                m.TriangleIndices.Add(c); m.TriangleIndices.Add(b); m.TriangleIndices.Add(d);
            }
        }
        m.Freeze();
        return m;
    }

    private System.Windows.Media.Media3D.MeshGeometry3D BuildSpotlightConeMesh(System.Windows.Media.Media3D.Point3D origin, double r0, double r1, double length, int sides)
    {
        System.Windows.Media.Media3D.MeshGeometry3D m = new System.Windows.Media.Media3D.MeshGeometry3D();
        for (int i = 0; i <= sides; i++)
        {
            double a = 2.0 * Math.PI * i / sides;
            double ca = Math.Cos(a), sa = Math.Sin(a);
            m.Positions.Add(new System.Windows.Media.Media3D.Point3D(origin.X + r0 * ca, origin.Y + r0 * sa, origin.Z));
            m.Positions.Add(new System.Windows.Media.Media3D.Point3D(origin.X + r1 * ca, origin.Y + r1 * sa - 0.14, origin.Z + length));
        }
        for (int i = 0; i < sides; i++)
        {
            int b = i * 2;
            m.TriangleIndices.Add(b); m.TriangleIndices.Add(b + 1); m.TriangleIndices.Add(b + 2);
            m.TriangleIndices.Add(b + 2); m.TriangleIndices.Add(b + 1); m.TriangleIndices.Add(b + 3);
        }
        m.Freeze();
        return m;
    }

    private System.Windows.Media.Media3D.MeshGeometry3D BuildWpfMesh(FbxSubMesh sub)
    {
        System.Windows.Media.Media3D.MeshGeometry3D geom = new System.Windows.Media.Media3D.MeshGeometry3D();
        foreach (System.Windows.Media.Media3D.Point3D p in sub.Vertices) geom.Positions.Add(p);
        foreach (int idx in sub.Indices) geom.TriangleIndices.Add(idx);
        if (sub.UVs.Count == sub.Vertices.Count)
            foreach (System.Windows.Point uv in sub.UVs) geom.TextureCoordinates.Add(uv);
        return geom;
    }


    private void NormalizeToUnitCube(FbxMeshData mesh)
    {
        if (mesh.TotalVertices == 0) return;
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

        FbxSubMesh[] subs = new FbxSubMesh[] { mesh.Body, mesh.Details, mesh.Glass };
        foreach (FbxSubMesh s in subs)
        {
            foreach (System.Windows.Media.Media3D.Point3D p in s.Vertices)
            {
                if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
                if (p.Z < minZ) minZ = p.Z; if (p.Z > maxZ) maxZ = p.Z;
            }
        }

        double cx = (minX + maxX) / 2.0, cy = (minY + maxY) / 2.0, cz = (minZ + maxZ) / 2.0;
        double sizeX = maxX - minX, sizeY = maxY - minY, sizeZ = maxZ - minZ;
        double maxSize = Math.Max(sizeX, Math.Max(sizeY, sizeZ));
        if (maxSize < 0.0001) maxSize = 1;
        double scale = 2.0 / maxSize;

        foreach (FbxSubMesh s in subs)
        {
            for (int i = 0; i < s.Vertices.Count; i++)
            {
                System.Windows.Media.Media3D.Point3D p = s.Vertices[i];
                s.Vertices[i] = new System.Windows.Media.Media3D.Point3D((p.X - cx) * scale, (p.Y - cy) * scale, (p.Z - cz) * scale);
            }
        }

        for (int i = 0; i < mesh.Lights.Count; i++)
        {
            System.Windows.Media.Media3D.Point3D p = mesh.Lights[i].Position;
            mesh.Lights[i].Position = new System.Windows.Media.Media3D.Point3D((p.X - cx) * scale, (p.Y - cy) * scale, (p.Z - cz) * scale);
            if (mesh.Lights[i].Kind == "NavAuto")
            {
                if (Math.Abs(mesh.Lights[i].Position.X) < 0.08) mesh.Lights[i].Kind = "NavWhite";
                else mesh.Lights[i].Kind = mesh.Lights[i].Position.X > 0 ? "NavRed" : "NavGreen";
            }
            AppLogger.Log("LIGHTS_NORM", string.Format("[{0}] Kind={1} -> Pos=({2:F3}, {3:F3}, {4:F3})",
                entry != null ? entry.Aircraft : "?", mesh.Lights[i].Kind,
                mesh.Lights[i].Position.X, mesh.Lights[i].Position.Y, mesh.Lights[i].Position.Z));
        }
    }

    private string FindReferenceModel(string aircraft)
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL", "models");
        try { Directory.CreateDirectory(dir); } catch { }
        string candidate = Path.Combine(dir, aircraft + ".fbx");
        if (!File.Exists(candidate))
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(aircraft + ".fbx"))
            {
                if (s != null)
                {
                    using (FileStream fs = new FileStream(candidate, FileMode.Create, FileAccess.Write))
                        s.CopyTo(fs);
                }
            }
        }
        if (File.Exists(candidate)) return candidate;
        return null;
    }

    private void OpenCustomTexture()
    {
        using (OpenFileDialog ofd = new OpenFileDialog() { Filter = "Изображения (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg" })
        {
            if (ofd.ShowDialog() == DialogResult.OK && model != null)
            {
                try
                {
                    System.Windows.Media.Imaging.BitmapImage bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(ofd.FileName, UriKind.Absolute);
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    System.Windows.Media.ImageBrush br = new System.Windows.Media.ImageBrush(bmp);
                    br.ViewportUnits = System.Windows.Media.BrushMappingMode.Absolute;
                    br.Viewport = new System.Windows.Rect(0, 0, 1, 1);
                    br.TileMode = System.Windows.Media.TileMode.Tile;
                    System.Windows.Media.Media3D.DiffuseMaterial mat = new System.Windows.Media.Media3D.DiffuseMaterial(br);
                    model.Material = mat;
                    model.BackMaterial = mat;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Не удалось загрузить текстуру: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}

