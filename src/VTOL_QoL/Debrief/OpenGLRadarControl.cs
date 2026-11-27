using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;


public class OpenGLRadarControl : Control
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PIXELFORMATDESCRIPTOR
    {
        public ushort nSize;
        public ushort nVersion;
        public uint dwFlags;
        public byte iPixelType;
        public byte cColorBits;
        public byte cRedBits, cRedShift, cGreenBits, cGreenShift, cBlueBits, cBlueShift;
        public byte cAlphaBits, cAlphaShift, cAccumBits, cAccumRedBits, cAccumGreenBits, cAccumBlueBits, cAccumAlphaBits;
        public byte cDepthBits, cStencilBits, cAuxBuffers, iLayerType, bReserved;
        public uint dwLayerMask, dwVisibleMask, dwDamageMask;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern int ChoosePixelFormat(IntPtr hdc, ref PIXELFORMATDESCRIPTOR pfd);
    [DllImport("gdi32.dll")] private static extern bool SetPixelFormat(IntPtr hdc, int iPixelFormat, ref PIXELFORMATDESCRIPTOR pfd);
    [DllImport("gdi32.dll")] private static extern bool SwapBuffers(IntPtr hdc);

    [DllImport("opengl32.dll")] private static extern IntPtr wglCreateContext(IntPtr hdc);
    [DllImport("opengl32.dll")] private static extern bool wglMakeCurrent(IntPtr hdc, IntPtr hglrc);
    [DllImport("opengl32.dll")] private static extern bool wglDeleteContext(IntPtr hglrc);

    [DllImport("opengl32.dll")] private static extern void glViewport(int x, int y, int width, int height);
    [DllImport("opengl32.dll")] private static extern void glMatrixMode(uint mode);
    [DllImport("opengl32.dll")] private static extern void glLoadIdentity();
    [DllImport("opengl32.dll")] private static extern void glFrustum(double left, double right, double bottom, double top, double zNear, double zFar);
    [DllImport("opengl32.dll")] private static extern void glOrtho(double left, double right, double bottom, double top, double zNear, double zFar);
    [DllImport("opengl32.dll")] private static extern void glTranslated(double x, double y, double z);
    [DllImport("opengl32.dll")] private static extern void glRotated(double angle, double x, double y, double z);
    [DllImport("opengl32.dll")] private static extern void glClearColor(float red, float green, float blue, float alpha);
    [DllImport("opengl32.dll")] private static extern void glClear(uint mask);
    [DllImport("opengl32.dll")] private static extern void glEnable(uint cap);
    [DllImport("opengl32.dll")] private static extern void glDisable(uint cap);
    [DllImport("opengl32.dll")] private static extern void glDepthMask(bool flag);
    [DllImport("opengl32.dll")] private static extern void glBlendFunc(uint sfactor, uint dfactor);
    [DllImport("opengl32.dll")] private static extern void glLineWidth(float width);
    [DllImport("opengl32.dll")] private static extern void glLineStipple(int factor, ushort pattern);
    [DllImport("opengl32.dll")] private static extern void glBegin(uint mode);
    [DllImport("opengl32.dll")] private static extern void glEnd();
    [DllImport("opengl32.dll")] private static extern void glVertex3d(double x, double y, double z);
    [DllImport("opengl32.dll")] private static extern void glVertex2f(float x, float y);
    [DllImport("opengl32.dll")] private static extern void glTexCoord2f(float s, float t);
    [DllImport("opengl32.dll")] private static extern void glColor3ub(byte r, byte g, byte b);
    [DllImport("opengl32.dll")] private static extern void glColor4ub(byte r, byte g, byte b, byte a);
    [DllImport("opengl32.dll")] private static extern uint glGenLists(int range);
    [DllImport("opengl32.dll")] private static extern void glNewList(uint list, uint mode);
    [DllImport("opengl32.dll")] private static extern void glEndList();
    [DllImport("opengl32.dll")] private static extern void glCallList(uint list);
    [DllImport("opengl32.dll")] private static extern void glDeleteLists(uint list, int range);
    [DllImport("opengl32.dll")] private static extern void glFogi(uint pname, int param);
    [DllImport("opengl32.dll")] private static extern void glFogf(uint pname, float param);
    [DllImport("opengl32.dll")] private static extern void glFogfv(uint pname, float[] paramsVal);
    [DllImport("opengl32.dll")] private static extern void glGenTextures(int n, uint[] textures);
    [DllImport("opengl32.dll")] private static extern void glBindTexture(uint target, uint texture);
    [DllImport("opengl32.dll")] private static extern void glTexParameteri(uint target, uint pname, int param);
    [DllImport("opengl32.dll")] private static extern void glTexImage2D(uint target, int level, int internalformat, int width, int height, int border, uint format, uint type, IntPtr pixels);
    [DllImport("opengl32.dll")] private static extern void glTexSubImage2D(uint target, int level, int xoffset, int yoffset, int width, int height, uint format, uint type, IntPtr pixels);
    [DllImport("opengl32.dll")] private static extern void glDeleteTextures(int n, uint[] textures);

    private const uint GL_COLOR_BUFFER_BIT = 0x00004000;
    private const uint GL_DEPTH_BUFFER_BIT = 0x00000100;
    private const uint GL_DEPTH_TEST = 0x0B71;
    private const uint GL_BLEND = 0x0BE2;
    private const uint GL_LINE_SMOOTH = 0x0B20;
    private const uint GL_LINE_STIPPLE = 0x0B24;
    private const uint GL_FOG = 0x0B60;
    private const uint GL_FOG_MODE = 0x0B65;
    private const uint GL_FOG_DENSITY = 0x0B62;
    private const uint GL_FOG_COLOR = 0x0B66;
    private const int GL_EXP2 = 0x0801;
    private const uint GL_SRC_ALPHA = 0x0302;
    private const uint GL_ONE_MINUS_SRC_ALPHA = 0x0303;
    private const uint GL_MODELVIEW = 0x1700;
    private const uint GL_PROJECTION = 0x1701;
    private const uint GL_LINES = 0x0001;
    private const uint GL_LINE_LOOP = 0x0002;
    private const uint GL_LINE_STRIP = 0x0003;
    private const uint GL_TRIANGLES = 0x0004;
    private const uint GL_QUADS = 0x0007;
    private const uint GL_COMPILE = 0x1300;
    private const uint GL_TEXTURE_2D = 0x0DE1;
    private const uint GL_TEXTURE_MIN_FILTER = 0x2801;
    private const uint GL_TEXTURE_MAG_FILTER = 0x2800;
    private const int GL_NEAREST = 0x2600;
    private const int GL_RGBA8 = 0x8058;
    private const uint GL_BGRA_EXT = 0x80E1;
    private const uint GL_UNSIGNED_BYTE = 0x1401;

    private IntPtr hdc = IntPtr.Zero;
    private IntPtr hrc = IntPtr.Zero;
    private uint terrainListId = 0;
    private uint hudTexId = 0;
    private Bitmap hudBmp = null;
    private int hudTexW = 0, hudTexH = 0;
    private DebriefSession cachedSession = null;
    private Dictionary<string, PointF> smoothOffsets = new Dictionary<string, PointF>();
    private HashSet<string> triggeredJams = new HashSet<string>();
    // Захваченная цель для направленного подавления (DRFM/SAS): ключ луча -> id цели.
    // Нужно, чтобы пунктир не перескакивал между юнитами и не уходил с цели.
    private Dictionary<string, string> dirJamLock = new Dictionary<string, string>();
    private double lastJamSurgeT = -99.0;
    private string lastJamBanner = "";

    public Func<DebriefSession> GetSession;
    public Func<double> GetCurrentTime;
    public Func<double> GetCamX;
    public Func<double> GetCamY;
    public Func<double> GetCamDist;
    public Func<double> GetYaw;
    public Func<double> GetPitch;
    public Func<double> GetRoll;
    public Func<bool> GetShowTerrain;
    public Func<bool> GetShowBeams;
    public Func<bool> GetShowAllies;
    public Func<string> GetFollowId;
    public Func<bool> GetIsPlaying;

    public OpenGLRadarControl()
    {
        this.SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Selectable, true);
        this.TabStop = true;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        if (this.CanFocus && !this.Focused) this.Focus();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (this.CanFocus && !this.Focused) this.Focus();
        base.OnMouseDown(e);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        hdc = GetDC(this.Handle);
        PIXELFORMATDESCRIPTOR pfd = new PIXELFORMATDESCRIPTOR();
        pfd.nSize = (ushort)Marshal.SizeOf(typeof(PIXELFORMATDESCRIPTOR));
        pfd.nVersion = 1;
        pfd.dwFlags = 0x00000004 | 0x00000020 | 0x00000001;
        pfd.iPixelType = 0;
        pfd.cColorBits = 32;
        pfd.cDepthBits = 24;

        int pf = ChoosePixelFormat(hdc, ref pfd);
        SetPixelFormat(hdc, pf, ref pfd);
        hrc = wglCreateContext(hdc);
        wglMakeCurrent(hdc, hrc);

        uint[] tex = new uint[1];
        glGenTextures(1, tex);
        hudTexId = tex[0];
        glBindTexture(GL_TEXTURE_2D, hudTexId);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (hudBmp != null) { hudBmp.Dispose(); hudBmp = null; }
        if (hrc != IntPtr.Zero)
        {
            wglMakeCurrent(hdc, hrc);
            if (hudTexId != 0) glDeleteTextures(1, new uint[] { hudTexId });
            if (terrainListId != 0) glDeleteLists(terrainListId, 1);
            wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
            wglDeleteContext(hrc);
            hrc = IntPtr.Zero;
        }
        if (hdc != IntPtr.Zero)
        {
            ReleaseDC(this.Handle, hdc);
            hdc = IntPtr.Zero;
        }
        base.OnHandleDestroyed(e);
    }

    protected override void OnPaintBackground(PaintEventArgs pevent) { }

    /// <summary>Сбрасывает отметки сыгранных JAMMER-событий, чтобы звук проигрался заново после перемотки.</summary>
    public void ResetJamTriggers()
    {
        triggeredJams.Clear();
        dirJamLock.Clear();
    }

    // ---------------------------------------------------------------------
    // Непрерывный звук NOISE-помех: работает только в 3D-режиме (этот контрол
    // виден лишь в нём). Громкость зависит от расстояния от камеры до вершины
    // конуса (позиции носителя глушилки):
    //   d = |камера - вершина|,  vol = (1 - d/R) * JamContMaxVolume,  d < R
    // У вершины — громче всего, на расстоянии R и дальше — тишина (пауза).
    // Вне зоны — пауза (позиция сохраняется), при смене реплея/выходе — стоп.
    // ---------------------------------------------------------------------
    private const double JamContRadiusM = 30000.0;   // радиус слышимости NOISE-конуса
    private const float  JamContMaxVolume = 0.7f;    // громкость у самой вершины
    private const float  JamContMinVolume = 0.02f;   // ниже — считаем «вне зоны»
    private bool  jamContInZone = false;             // геометрия: камера в зоне NOISE
    private bool  jamContAppliedPlaying = false;     // фактически играет (не на паузе)
    private float jamContAppliedVolume = -1f;        // последняя применённая громкость

    /// <summary>Полная остановка непрерывного звука NOISE (сброс в начало).</summary>
    public void StopJamContinuous()
    {
        jamContInZone = false;
        jamContAppliedPlaying = false;
        jamContAppliedVolume = -1f;
        DebriefSoundPlayer.StopContinuousLoop();
    }

    /// <summary>Пауза непрерывного звука (для паузы таймлайна / ухода в 2D).</summary>
    public void PauseJamContinuous()
    {
        jamContAppliedPlaying = false;
        DebriefSoundPlayer.PauseContinuousLoop();
    }

    /// <summary>Возобновить непрерывный звук, если камера находится в зоне NOISE.</summary>
    public void ResumeJamContinuous()
    {
        if (!jamContInZone) return;
        jamContAppliedPlaying = true;
        DebriefSoundPlayer.ResumeContinuousLoop();
    }

    private void UpdateJamContinuous(DebriefSession s, double curT, bool isPlaying, bool showBeams,
                                     double camXM, double camYM, double camAltM,
                                     Dictionary<string, DebriefSample> liveById)
    {
        double d2 = double.MaxValue;   // квадрат расстояния до ближайшей вершины конуса

        if (s != null && s.Beams != null && showBeams)
        {
            foreach (TacticalBeam bm in s.Beams)
            {
                if (bm.Kind != "JAMMER") continue;
                if (curT < bm.StartTime || curT > bm.EndTime) continue;

                string mode = bm.JamMode ?? "";
                if (mode.Length == 0) mode = bm.ToName ?? "";
                // Только NOISE: DRFM/SAS — направленное подавление по цели, а не зона помех
                if (mode.IndexOf("DRFM", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    mode.IndexOf("SAS", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                // Вершина конуса = фактическая позиция носителя в текущий момент
                double jx = bm.FromX, jy = bm.FromY;
                DebriefSample st;
                if (!string.IsNullOrEmpty(bm.FromId) && liveById != null && liveById.TryGetValue(bm.FromId, out st))
                { jx = st.X; jy = st.Y; }
                double jz = s.GetGroundAltitudeAt(jx, jy);

                double dx = jx - camXM, dy = jy - camYM, dz = jz - camAltM;
                double cand = dx * dx + dy * dy + dz * dz;
                if (cand < d2) d2 = cand;
            }
        }

        bool inZone = d2 != double.MaxValue && d2 < JamContRadiusM * JamContRadiusM;
        float vol = 0f;
        if (inZone)
        {
            vol = (float)((1.0 - Math.Sqrt(d2) / JamContRadiusM) * JamContMaxVolume);
            if (vol < JamContMinVolume) inZone = false;
        }

        // --- Применяем только изменения (вызовы идут в отдельный поток — не спамим каждый кадр) ---
        if (inZone)
        {
            bool wasInZone = jamContInZone;
            jamContInZone = true;

            if (!wasInZone)
            {
                // Вход в зону: запускаем (или продолжаем с сохранённой позиции)
                DebriefSoundPlayer.PlayContinuousLoop("JAMMER_CONTINUOUS");
                DebriefSoundPlayer.SetContinuousLoopVolume(vol);
                jamContAppliedVolume = vol;
                if (isPlaying)
                {
                    DebriefSoundPlayer.ResumeContinuousLoop();
                    jamContAppliedPlaying = true;
                }
                else
                {
                    DebriefSoundPlayer.PauseContinuousLoop();
                    jamContAppliedPlaying = false;
                }
                return;
            }

            // Внутри зоны: подстраиваем громкость и play/pause
            if (Math.Abs(vol - jamContAppliedVolume) > 0.01f)
            {
                DebriefSoundPlayer.SetContinuousLoopVolume(vol);
                jamContAppliedVolume = vol;
            }
            if (isPlaying != jamContAppliedPlaying)
            {
                if (isPlaying) DebriefSoundPlayer.ResumeContinuousLoop();
                else DebriefSoundPlayer.PauseContinuousLoop();
                jamContAppliedPlaying = isPlaying;
            }
        }
        else if (jamContInZone)
        {
            // Вышли из зоны: пауза, позиция сохраняется
            jamContInZone = false;
            jamContAppliedPlaying = false;
            DebriefSoundPlayer.PauseContinuousLoop();
        }
    }

    private void RebuildTerrainOnGpu(DebriefSession s)
    {
        if (terrainListId != 0) { glDeleteLists(terrainListId, 1); terrainListId = 0; }
        cachedSession = s;
        smoothOffsets.Clear();
        triggeredJams.Clear();
        dirJamLock.Clear();
        lastJamSurgeT = -99.0;
        lastJamBanner = "";
        if (s == null || s.TerrainHeights == null || s.MapSizeMeters <= 100.0) return;

        int srcW = s.TerrainHeights.GetLength(0);
        int srcH = s.TerrainHeights.GetLength(1);
        // Сетка строится по разрешению исходной heightmap (до 1024 узлов на сторону),
        // а не по фиксированным 768: иначе рельеф заметно огрубляется.
        int meshN = Math.Min(1024, Math.Max(2, Math.Max(srcW, srcH) - 1));
        double terrainSize = s.TerrainSizeMeters > 100.0 ? s.TerrainSizeMeters : s.MapSizeMeters;
        double stepM = terrainSize / meshN;
        double minX = s.TerrainSizeMeters > 100.0 ? s.TerrainMinX : s.MapMinX;
        double minY = s.TerrainSizeMeters > 100.0 ? s.TerrainMinY : s.MapMinY;

        double[,] hCache = new double[meshN + 1, meshN + 1];
        for (int iy = 0; iy <= meshN; iy++)
            for (int ix = 0; ix <= meshN; ix++)
                hCache[ix, iy] = s.GetGroundAltitudeAt(minX + ix * stepM, minY + iy * stepM);

        // Heightmap в реплее квантована (шаг ~24 м по высоте). Без сглаживания это даёт
        // характерные «террасы». Один проход box-фильтра убирает ступени, сохраняя рельеф.
        double[,] hSmooth = new double[meshN + 1, meshN + 1];
        for (int iy = 0; iy <= meshN; iy++)
        {
            for (int ix = 0; ix <= meshN; ix++)
            {
                double sum = 0.0;
                int cnt = 0;
                for (int dy = -1; dy <= 1; dy++)
                {
                    int yy = iy + dy;
                    if (yy < 0 || yy > meshN) continue;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = ix + dx;
                        if (xx < 0 || xx > meshN) continue;
                        sum += hCache[xx, yy];
                        cnt++;
                    }
                }
                // Мягкое смешивание: центр важнее среднего, резкие хребты не размываются
                hSmooth[ix, iy] = hCache[ix, iy] * 0.55 + (sum / Math.Max(1, cnt)) * 0.45;
            }
        }
        hCache = hSmooth;

        // Плавная палитра по высоте и мягкое освещение по нормали
        Func<double, double, byte[]> colorAt = (h, shade) =>
        {
            if (h <= 1.5) return new byte[] { 10, 26, 42 };
            double t = Math.Max(0.0, Math.Min(1.0, h / 2600.0));
            double r = 24 + t * 112 + Math.Max(0.0, h - 1500.0) * 0.020;
            double g = 52 + t * 92 + Math.Max(0.0, h - 1500.0) * 0.014;
            double b = 34 + t * 62;
            if (h < 30.0) { r += 10; g += 26; b += 22; }        // пляж/низменность
            else if (h < 120.0) { r += 6; g += 16; b += 8; }
            double k = 1.0 + shade;
            return new byte[]
            {
                (byte)Math.Max(8, Math.Min(245, r * k)),
                (byte)Math.Max(14, Math.Min(250, g * k)),
                (byte)Math.Max(14, Math.Min(240, b * k))
            };
        };

        terrainListId = glGenLists(1);
        glNewList(terrainListId, GL_COMPILE);
        const uint GL_QUAD_STRIP = 0x0008;
        for (int iy = 0; iy < meshN; iy++)
        {
            double z0 = -(minY + iy * stepM) * 0.001;
            double z1 = -(minY + (iy + 1) * stepM) * 0.001;
            glBegin(GL_QUAD_STRIP);
            for (int ix = 0; ix <= meshN; ix++)
            {
                double x0 = (minX + ix * stepM) * 0.001;
                double h00 = hCache[ix, iy];
                double h01 = hCache[ix, iy + 1];
                int ixL = Math.Max(0, ix - 1);
                int ixR = Math.Min(meshN, ix + 1);
                int iyD = Math.Max(0, iy - 1);
                int iyU = Math.Min(meshN, iy + 1);
                // Нормаль по центральным разностям через две ячейки — мягче и без «мозаики»
                double dxh = (hCache[ixR, iy] - hCache[ixL, iy]) / Math.Max(1, ixR - ixL);
                double dyh = (hCache[ix, iyU] - hCache[ix, iyD]) / Math.Max(1, iyU - iyD);
                double shade0 = Math.Max(-0.42, Math.Min(0.55, (-dxh * 0.72 + dyh * 0.72) / stepM * 2.6));

                double dxh1 = (hCache[ixR, Math.Min(meshN, iy + 1)] - hCache[ixL, Math.Min(meshN, iy + 1)]) / Math.Max(1, ixR - ixL);
                double dyh1 = (hCache[ix, Math.Min(meshN, iy + 2)] - hCache[ix, iy]) / Math.Max(1, Math.Min(meshN, iy + 2) - iy);
                double shade1 = Math.Max(-0.42, Math.Min(0.55, (-dxh1 * 0.72 + dyh1 * 0.72) / stepM * 2.6));

                byte[] c0 = colorAt(h00, shade0);
                byte[] c1 = colorAt(h01, shade1);
                glColor3ub(c0[0], c0[1], c0[2]);
                glVertex3d(x0, h00 * 0.001, z0);
                glColor3ub(c1[0], c1[1], c1[2]);
                glVertex3d(x0, h01 * 0.001, z1);
            }
            glEnd();
        }
        glEndList();
    }
    private PointF Project3DToScreen(double wx, double wy, double alt, double cx, double cy, double groundCamAlt, double distKm, double yawDeg, double pitchDeg, int w, int h)
    {
        double px = wx * 0.001 - cx;
        double py = alt * 0.001 - (groundCamAlt + 1.2);
        double pz = -wy * 0.001 + cy;
        double yRad = yawDeg * Math.PI / 180.0;
        double pRad = pitchDeg * Math.PI / 180.0;
        double x1 = px * Math.Cos(yRad) + pz * Math.Sin(yRad);
        double y1 = py;
        double z1 = -px * Math.Sin(yRad) + pz * Math.Cos(yRad);
        double x2 = x1;
        double y2 = y1 * Math.Cos(pRad) - z1 * Math.Sin(pRad);
        double z2 = y1 * Math.Sin(pRad) + z1 * Math.Cos(pRad) - distKm;
        if (z2 >= -0.25) return new PointF(-9999f, -9999f);
        double fovY = 46.0;
        double f = 1.0 / Math.Tan(fovY * Math.PI / 360.0);
        double aspect = (double)w / Math.Max(1, h);
        double ndcX = (x2 * f / aspect) / (-z2);
        double ndcY = (y2 * f) / (-z2);
        return new PointF((float)((ndcX + 1.0) * 0.5 * w), (float)((1.0 - ndcY) * 0.5 * h));
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (hrc == IntPtr.Zero || this.Width <= 4 || this.Height <= 4) return;
        wglMakeCurrent(hdc, hrc);
        int w = this.Width;
        int h = this.Height;
        glViewport(0, 0, w, h);
        glClearColor(0.035f, 0.065f, 0.085f, 1.0f);
        glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);
        glEnable(GL_DEPTH_TEST);
        glEnable(GL_BLEND);
        glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
        glEnable(GL_LINE_SMOOTH);
        glEnable(GL_FOG);
        glFogi(GL_FOG_MODE, GL_EXP2);
        glFogf(GL_FOG_DENSITY, 0.0028f);
        glFogfv(GL_FOG_COLOR, new float[] { 0.035f, 0.065f, 0.085f, 1.0f });
        double aspect = (double)w / Math.Max(1, h);
        double zNear = 0.2;
        double zFar = 650.0;
        double fovY = 46.0;
        double top = zNear * Math.Tan(fovY * Math.PI / 360.0);
        double right = top * aspect;
        glMatrixMode(GL_PROJECTION);
        glLoadIdentity();
        glFrustum(-right, right, -top, top, zNear, zFar);
        double cx = GetCamX != null ? GetCamX() * 0.001 : 0.0;
        double cy = GetCamY != null ? GetCamY() * 0.001 : 0.0;
        double distKm = GetCamDist != null ? GetCamDist() : 45.0;
        double yaw = GetYaw != null ? GetYaw() : 35.0;
        double pitch = GetPitch != null ? GetPitch() : 35.0;
        double roll = GetRoll != null ? GetRoll() : 0.0;
        DebriefSession s = GetSession != null ? GetSession() : null;
        double groundCamAlt = (s != null) ? s.GetGroundAltitudeAt(cx * 1000.0, cy * 1000.0) * 0.001 : 0.0;
        glMatrixMode(GL_MODELVIEW);
        glLoadIdentity();
        glTranslated(0.0, 0.0, -distKm);
        glRotated(roll, 0.0, 0.0, 1.0);
        glRotated(pitch, 1.0, 0.0, 0.0);
        glRotated(yaw, 0.0, 1.0, 0.0);
        glTranslated(-cx, -(groundCamAlt + 1.2), cy);
        List<Tuple<string, string, PointF, Color>> arlLabels = new List<Tuple<string, string, PointF, Color>>();
        Dictionary<string, string> jammedTargets = new Dictionary<string, string>();
        // Эти значения нужны и после блока отрисовки 3D-сцены (для непрерывного звука NOISE)
        double curT = GetCurrentTime != null ? GetCurrentTime() : 0.0;
        bool showBeams = GetShowBeams == null || GetShowBeams();
        bool isPlaying = GetIsPlaying != null && GetIsPlaying();
        Dictionary<string, DebriefSample> liveById = new Dictionary<string, DebriefSample>();
        if (s != null)
        {
            if (s != cachedSession) RebuildTerrainOnGpu(s);
            bool showTerr = GetShowTerrain == null || GetShowTerrain();
            if (showTerr && terrainListId != 0)
                glCallList(terrainListId);
            // ВПП аэродромов лежат на рельефе и участвуют в тесте глубины
            DrawAirfields3D(s, distKm);
            // Сетка координат уровня моря
            glLineWidth(1.0f);
            glColor4ub(0, 220, 130, 32);
            glBegin(GL_LINES);
            double gMinX = s.MapMinX * 0.001;
            double gMaxX = (s.MapMinX + s.MapSizeMeters) * 0.001;
            double gMinY = s.MapMinY * 0.001;
            double gMaxY = (s.MapMinY + s.MapSizeMeters) * 0.001;
            for (double gx = Math.Floor(gMinX / 10.0) * 10.0; gx <= gMaxX; gx += 10.0)
            { glVertex3d(gx, 0.02, -gMinY); glVertex3d(gx, 0.02, -gMaxY); }
            for (double gy = Math.Floor(gMinY / 10.0) * 10.0; gy <= gMaxY; gy += 10.0)
            { glVertex3d(gMinX, 0.02, -gy); glVertex3d(gMaxX, 0.02, -gy); }
            glEnd();
            bool showAllies = GetShowAllies == null || GetShowAllies();
            string followId = GetFollowId != null ? GetFollowId() : "";
            // 3D-кольца зон ПВО (SAM) и Bullseye на рельефе
            glEnable(GL_LINE_STIPPLE);
            glLineStipple(2, 0xAAAA);
            foreach (MapPoiMarker poi in s.MapMarkers)
            {
                if (poi.RadiusMeters < 0 && curT > -poi.RadiusMeters) continue;
                if (!showAllies && poi.Coalition == "Allies" && poi.Kind != "Base") continue;
                double pz = s.GetGroundAltitudeAt(poi.X, poi.Y) + 35.0;
                if (poi.RadiusMeters > 100 && poi.Coalition == "Enemies")
                {
                    glLineWidth(1.2f);
                    glColor4ub(255, 155, 35, 95);
                    glBegin(GL_LINE_LOOP);
                    for (int k = 0; k < 48; k++)
                    {
                        double a = k * (Math.PI * 2.0 / 48.0);
                        double rx = poi.X + Math.Cos(a) * poi.RadiusMeters;
                        double ry = poi.Y + Math.Sin(a) * poi.RadiusMeters;
                        double rz = s.GetGroundAltitudeAt(rx, ry) + 35.0;
                        glVertex3d(rx * 0.001, rz * 0.001, -ry * 0.001);
                    }
                    glEnd();
                }
            }
            glDisable(GL_LINE_STIPPLE);
            // Заполняем liveById (liveById уже объявлен выше)
            foreach (DebriefEntity ent in s.Entities)
            {
                DebriefSample st = ent.GetStateAt(curT);
                if (st != null) liveById[ent.Id] = st;
            }
            // Анимированный бегущий битовый шаблон для радарных захватов и РЭБ
            int shiftBits = ((int)(curT * 18.0)) & 15;
            ushort animPattern = (ushort)((0xF0F0 << shiftBits) | (0xF0F0 >> (16 - shiftBits)));
            float pulse01 = (float)(0.5 + 0.5 * Math.Sin(curT * 7.0));
            if (showBeams)
            {
                // Лучи захвата — тактическая разметка поверх рельефа, а не физический объект.
                // Рисуем их без теста глубины, чтобы пунктир не «уходил под террейн».
                glDisable(GL_DEPTH_TEST);
                foreach (TacticalBeam bm in s.Beams)
                {
                    if (curT < bm.StartTime || curT > bm.EndTime) continue;
                    double fx = bm.FromX, fy = bm.FromY;
                    double tx = bm.ToX, ty = bm.ToY;
                    DebriefSample sFrom = null, sTo = null;
                    if (!string.IsNullOrEmpty(bm.FromId) && liveById.TryGetValue(bm.FromId, out sFrom))
                    { fx = sFrom.X; fy = sFrom.Y; }
                    double fz = sFrom != null ? sFrom.Alt : s.GetGroundAltitudeAt(fx, fy);
                    double tz = fz;
                    if (bm.Kind == "JAMMER")
                    {
                        foreach (TacticalEvent ev in s.Events)
                        {
                            if (ev.Kind != "JAMMER") continue;
                            double evAge = curT - ev.Time;
                            string evId = ev.Time.ToString("F1") + "_" + ev.Title;
                            if (isPlaying && evAge >= 0.0 && evAge <= 0.6 && !triggeredJams.Contains(evId))
                            {
                                triggeredJams.Add(evId);
                                lastJamSurgeT = curT;
                                lastJamBanner = ev.Title;
                                DebriefSoundPlayer.Play("JAMMER_ON");
                            }
                        }
                        double horizLen = Math.Sqrt(bm.DirX * bm.DirX + bm.DirY * bm.DirY);
                        double ux2d = horizLen > 1e-4 ? (bm.DirX / horizLen) : 0.0;
                        double uy2d = horizLen > 1e-4 ? (bm.DirY / horizLen) : 0.0;
                        // Дальность станции РЭБ большая — не обрезаем по карте
                        double maxRay = 250000.0;
                        string jamModeName = bm.JamMode ?? "";
                        if (jamModeName.Length == 0) jamModeName = bm.ToName ?? "";
                        bool isDirMode = jamModeName.IndexOf("DRFM", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         jamModeName.IndexOf("SAS", StringComparison.OrdinalIgnoreCase) >= 0;
                        string srcCoal = "Allies";
                        foreach (DebriefEntity se in s.Entities)
                            if (se.Id == bm.FromId) { srcCoal = se.Coalition; break; }

                        // DRFM/SAS работают по конкретной цели: обрезаем луч на первом радиолокационном
                        // юните в коридоре вдоль оси и ставим бейдж только ему (логика 0.7.0).
                        // NOISE — зона подавления, бейджи на юнитах не ставим.
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

                        // Ось NOISE-конуса. В игре антенна наведена на подавляемую цель,
                        // а не смотрит по курсу — восстанавливаем цель по оси луча и удерживаем её.
                        double jamDirX = bm.DirX, jamDirY = bm.DirY, jamDirZ = bm.DirZ;
                        double coneLenM = JammerModel.NoiseConeRangeM;

                        if (isDirMode)
                        {
                            // Цель запоминается по ключу луча: пока она существует, луч идёт
                            // строго в неё, даже если она сместилась в сторону от оси антенны.
                            string beamKey = (bm.FromId ?? "") + "|" + bm.StartTime.ToString("F1");
                            string hitId = null;
                            string lockedId;
                            if (dirJamLock.TryGetValue(beamKey, out lockedId))
                            {
                                if (liveById.ContainsKey(lockedId)) hitId = lockedId;
                                else dirJamLock.Remove(beamKey);
                            }
                            if (hitId == null)
                            {
                                double bestProj = double.MaxValue;
                                foreach (DebriefEntity cand in s.Entities)
                                {
                                    if (cand.Id == bm.FromId || cand.Coalition == srcCoal || !hasRadarSystem(cand)) continue;
                                    DebriefSample cs;
                                    if (!liveById.TryGetValue(cand.Id, out cs)) continue;
                                    double dx = cs.X - fx;
                                    double dy = cs.Y - fy;
                                    double proj = dx * ux2d + dy * uy2d;
                                    if (proj <= 500.0 || proj >= maxRay) continue;
                                    double perp = Math.Abs(dx * (-uy2d) + dy * ux2d);
                                    double hitRadius = Math.Max(2500.0, proj * 0.10);
                                    if (perp > hitRadius) continue;
                                    if (proj < bestProj) { bestProj = proj; hitId = cand.Id; }
                                }
                                if (hitId != null) dirJamLock[beamKey] = hitId;
                            }
                            DebriefSample tgtSmp;
                            if (hitId == null || !liveById.TryGetValue(hitId, out tgtSmp))
                                continue;   // цели нет — луч DRFM/SAS «в никуда» не рисуем
                            tx = tgtSmp.X;
                            ty = tgtSmp.Y;
                            tz = tgtSmp.Alt;
                            jammedTargets[hitId] = jamModeName.ToUpperInvariant();
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
                                foreach (DebriefEntity cand in s.Entities)
                                {
                                    if (cand.Id == bm.FromId || cand.Coalition == srcCoal || !hasRadarSystem(cand)) continue;
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
                                jamDirX = nTgt.X - fx;
                                jamDirY = nTgt.Y - fy;
                                jamDirZ = nTgt.Alt - fz;
                                double d3 = Math.Sqrt(jamDirX * jamDirX + jamDirY * jamDirY + jamDirZ * jamDirZ);
                                // Конус дотягивается до цели с запасом, но не выходит за предел станции
                                coneLenM = Math.Min(JammerModel.NoiseConeRangeM, Math.Max(8000.0, d3 * 1.25));
                            }

                            double dl = Math.Sqrt(jamDirX * jamDirX + jamDirY * jamDirY + jamDirZ * jamDirZ);
                            if (dl > 1e-6)
                            {
                                tx = fx + jamDirX / dl * coneLenM;
                                ty = fy + jamDirY / dl * coneLenM;
                                tz = fz + jamDirZ / dl * coneLenM;
                            }
                            else
                            {
                                tx = fx + ux2d * coneLenM;
                                ty = fy + uy2d * coneLenM;
                                tz = fz;
                            }
                        }

                        if (!isDirMode)
                        {
                            // Настоящий объёмный конус помех: поверхность вращения вокруг оси антенны
                            // по той же диаграмме направленности, что в игре (pow(cos, 8)).
                            // Конус — физический объём, поэтому он корректно прячется за рельефом.
                            glEnable(GL_DEPTH_TEST);
                            DrawNoiseCone(s, fx, fy, fz, jamDirX, jamDirY, jamDirZ, coneLenM, curT);
                            glDisable(GL_DEPTH_TEST);
                        }
                        else
                        {
                            glEnable(GL_LINE_STIPPLE);
                            glLineStipple(2, animPattern);
                            glLineWidth(2.2f);
                            glColor4ub(225, 130, 255, 235);
                            glBegin(GL_LINES);
                            glVertex3d(fx * 0.001, fz * 0.001, -fy * 0.001);
                            glVertex3d(tx * 0.001, tz * 0.001, -ty * 0.001);
                            glEnd();
                            glDisable(GL_LINE_STIPPLE);
                        }
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(bm.ToId) && liveById.TryGetValue(bm.ToId, out sTo))
                        { tx = sTo.X; ty = sTo.Y; tz = sTo.Alt; }
                        glEnable(GL_LINE_STIPPLE);
                        glLineStipple(2, animPattern);
                        glLineWidth(2.0f);
                        Color lc = Color.DeepSkyBlue;
                        if (bm.Kind == "RWR_SPIKE") { glColor4ub(255, 55, 55, 230); lc = Color.FromArgb(255, 85, 85); }
                        else if (bm.Kind == "MY_LOCK" || bm.Kind == "PLAYER_LOCK") { glColor4ub(55, 255, 85, 235); lc = Color.FromArgb(75, 255, 105); }
                        else { glColor4ub(0, 215, 255, 210); lc = Color.FromArgb(40, 220, 255); }
                        glBegin(GL_LINES);
                        glVertex3d(fx * 0.001, fz * 0.001, -fy * 0.001);
                        glVertex3d(tx * 0.001, tz * 0.001, -ty * 0.001);
                        glEnd();
                        glDisable(GL_LINE_STIPPLE);
                        // Пульсирующее 3D-кольцо захвата RWR вокруг облучаемой цели
                        if (bm.Kind == "RWR_SPIKE")
                        {
                            double rRing = 0.85 + pulse01 * 0.55;
                            glLineWidth(1.8f);
                            glColor4ub(255, 55, 55, (byte)(210 - pulse01 * 80));
                            glBegin(GL_LINE_LOOP);
                            for (int k = 0; k < 24; k++)
                            {
                                double a = k * (Math.PI * 2.0 / 24.0);
                                glVertex3d(tx * 0.001 + Math.Cos(a) * rRing, tz * 0.001, -ty * 0.001 + Math.Sin(a) * rRing);
                            }
                            glEnd();
                        }
                        // Подпись дистанции и TTI на лучах ракет [АРЛ]
                        if (bm.FromName.StartsWith("[АРЛ]"))
                        {
                            double distM = Math.Sqrt(Math.Pow(tx - fx, 2) + Math.Pow(ty - fy, 2) + Math.Pow(tz - fz, 2));
                            int tti = Math.Max(1, (int)Math.Round(distM / 850.0));
                            PointF midPt = Project3DToScreen((fx + tx) * 0.5, (fy + ty) * 0.5, (fz + tz) * 0.5, cx, cy, groundCamAlt, distKm, yaw, pitch, w, h);
                            arlLabels.Add(Tuple.Create("arl_" + bm.FromId, string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F1}km · {1}s", distM / 1000.0, tti), midPt, lc));
                        }
                    }
                }
                glEnable(GL_DEPTH_TEST);
            }
            // 3D-траектории, стойки высоты и силуэты бортов/ракет
            foreach (DebriefEntity ent in s.Entities)
            {
                if (!showAllies && !ent.IsPlayer && ent.Coalition == "Allies") continue;
                DebriefSample cur = ent.GetStateAt(curT);
                if (cur == null) continue;
                bool isMine = ent.IsPlayer || (ent.Type == "Missile" && ent.Pilot != null && ent.Pilot.StartsWith("MY_MISSILE:"));
                bool isNeutral = ent.Coalition == "Neutral";
                byte cr = isMine ? (byte)65 : (isNeutral ? (byte)185 : (ent.Coalition == "Enemies" ? (byte)255 : (byte)40));
                byte cg = isMine ? (byte)255 : (isNeutral ? (byte)185 : (ent.Coalition == "Enemies" ? (byte)155 : (byte)200));
                byte cb = isMine ? (byte)95 : (isNeutral ? (byte)185 : (ent.Coalition == "Enemies" ? (byte)40 : (byte)255));
                if (ent.Type == "Ground")
                {
                    DrawGroundUnit3D(s, ent, cur, cr, cg, cb, distKm, curT);
                    continue;
                }

                bool isSel = (!string.IsNullOrEmpty(followId) && ent.Id == followId) || (string.IsNullOrEmpty(followId) && ent.IsPlayer);
                double minT = (isSel || isMine) ? ent.FirstTime : Math.Max(ent.FirstTime, curT - 75.0);
                if (ent.Type == "Missile")
                {
                    glEnable(GL_LINE_STIPPLE);
                    glLineStipple(1, 0x0F0F);
                }
                glLineWidth(isSel ? 2.4f : (ent.Type == "Missile" ? 1.4f : 1.8f));
                glColor4ub(cr, cg, cb, ent.Type == "Missile" ? (byte)175 : (byte)215);
                glBegin(GL_LINE_STRIP);
                for (int i = 0; i < ent.Track.Count; i++)
                {
                    DebriefSample smp = ent.Track[i];
                    if (smp.Time < minT) continue;
                    if (smp.Time > curT) break;
                    glVertex3d(smp.X * 0.001, smp.Alt * 0.001, -smp.Y * 0.001);
                }
                glVertex3d(cur.X * 0.001, cur.Alt * 0.001, -cur.Y * 0.001);
                glEnd();
                if (ent.Type == "Missile") glDisable(GL_LINE_STIPPLE);

                // Пунктирная стойка высоты от борта до земли
                double gAlt = s.GetGroundAltitudeAt(cur.X, cur.Y);
                glEnable(GL_LINE_STIPPLE);
                glLineStipple(1, 0x3333);
                glLineWidth(1.0f);
                glColor4ub(cr, cg, cb, 85);
                glBegin(GL_LINES);
                glVertex3d(cur.X * 0.001, cur.Alt * 0.001, -cur.Y * 0.001);
                glVertex3d(cur.X * 0.001, gAlt * 0.001, -cur.Y * 0.001);
                glEnd();
                glDisable(GL_LINE_STIPPLE);

                // Объёмная 3D-модель самолёта или ракеты с полным учётом Heading, Pitch и Roll
                double hRad = cur.Heading * Math.PI / 180.0;
                double pRad = cur.Pitch * Math.PI / 180.0;
                double rRad = cur.Roll * Math.PI / 180.0;

                // Базис самолёта (Forward, Right, Up) с учётом курса, тангажа и крена
                double cosH = Math.Cos(hRad), sinH = Math.Sin(hRad);
                double cosP = Math.Cos(pRad), sinP = Math.Sin(pRad);
                double cosR = Math.Cos(rRad), sinR = Math.Sin(rRad);

                double fx = sinH * cosP, fy = sinP, fz = -cosH * cosP;
                double rx0 = cosH, ry0 = 0.0, rz0 = sinH;
                double ux0 = -sinH * sinP, uy0 = cosP, uz0 = cosH * sinP;

                double rx = rx0 * cosR - ux0 * sinR;
                double ry = ry0 * cosR - uy0 * sinR;
                double rz = rz0 * cosR - uz0 * sinR;

                double ux = rx0 * sinR + ux0 * cosR;
                double uy = ry0 * sinR + uy0 * cosR;
                double uz = rz0 * sinR + uz0 * cosR;

                double zoomScale = Math.Max(0.30, Math.Min(1.0, distKm / 22.0));
                double sc = (ent.Type == "Missile" ? 0.10 : 0.16) * zoomScale;
                double px = cur.X * 0.001, py = cur.Alt * 0.001, pz = -cur.Y * 0.001;

                Func<double, double, double, double[]> toWorld = (lf, lr, lu) => new double[]
                {
                    px + (fx * lf + rx * lr + ux * lu) * sc,
                    py + (fy * lf + ry * lr + uy * lu) * sc,
                    pz + (fz * lf + rz * lr + uz * lu) * sc
                };

                glBegin(GL_TRIANGLES);
                if (ent.Type == "Missile")
                {
                    // Узкий 3D-корпус ракеты/бомбы с обтекателем ГСН и крестообразным оперением
                    double[] mTip = toWorld(1.65, 0.0, 0.0);
                    double[] mBodyT = toWorld(0.65, 0.0, 0.10);
                    double[] mBodyB = toWorld(0.65, 0.0, -0.10);
                    double[] mBodyL = toWorld(0.65, -0.10, 0.0);
                    double[] mBodyR = toWorld(0.65, 0.10, 0.0);
                    double[] mTailT = toWorld(-1.15, 0.0, 0.09);
                    double[] mTailB = toWorld(-1.15, 0.0, -0.09);
                    double[] mTailL = toWorld(-1.15, -0.09, 0.0);
                    double[] mTailR = toWorld(-1.15, 0.09, 0.0);

                    // Светлый носовой обтекатель ГСН
                    glColor4ub(245, 250, 248, 255);
                    glVertex3d(mTip[0], mTip[1], mTip[2]); glVertex3d(mBodyT[0], mBodyT[1], mBodyT[2]); glVertex3d(mBodyL[0], mBodyL[1], mBodyL[2]);
                    glVertex3d(mTip[0], mTip[1], mTip[2]); glVertex3d(mBodyR[0], mBodyR[1], mBodyR[2]); glVertex3d(mBodyT[0], mBodyT[1], mBodyT[2]);
                    glColor4ub(185, 195, 192, 255);
                    glVertex3d(mTip[0], mTip[1], mTip[2]); glVertex3d(mBodyL[0], mBodyL[1], mBodyL[2]); glVertex3d(mBodyB[0], mBodyB[1], mBodyB[2]);
                    glVertex3d(mTip[0], mTip[1], mTip[2]); glVertex3d(mBodyB[0], mBodyB[1], mBodyB[2]); glVertex3d(mBodyR[0], mBodyR[1], mBodyR[2]);

                    // Узкий цилиндрический корпус ракеты
                    glColor4ub(cr, cg, cb, 255);
                    glVertex3d(mBodyT[0], mBodyT[1], mBodyT[2]); glVertex3d(mTailT[0], mTailT[1], mTailT[2]); glVertex3d(mBodyL[0], mBodyL[1], mBodyL[2]);
                    glVertex3d(mBodyL[0], mBodyL[1], mBodyL[2]); glVertex3d(mTailT[0], mTailT[1], mTailT[2]); glVertex3d(mTailL[0], mTailL[1], mTailL[2]);
                    glVertex3d(mBodyR[0], mBodyR[1], mBodyR[2]); glVertex3d(mTailR[0], mTailR[1], mTailR[2]); glVertex3d(mBodyT[0], mBodyT[1], mBodyT[2]);
                    glVertex3d(mBodyT[0], mBodyT[1], mBodyT[2]); glVertex3d(mTailR[0], mTailR[1], mTailR[2]); glVertex3d(mTailT[0], mTailT[1], mTailT[2]);

                    glColor4ub((byte)(cr * 0.6), (byte)(cg * 0.6), (byte)(cb * 0.6), 255);
                    glVertex3d(mBodyL[0], mBodyL[1], mBodyL[2]); glVertex3d(mTailL[0], mTailL[1], mTailL[2]); glVertex3d(mBodyB[0], mBodyB[1], mBodyB[2]);
                    glVertex3d(mBodyB[0], mBodyB[1], mBodyB[2]); glVertex3d(mTailL[0], mTailL[1], mTailL[2]); glVertex3d(mTailB[0], mTailB[1], mTailB[2]);
                    glVertex3d(mBodyB[0], mBodyB[1], mBodyB[2]); glVertex3d(mTailB[0], mTailB[1], mTailB[2]); glVertex3d(mBodyR[0], mBodyR[1], mBodyR[2]);
                    glVertex3d(mBodyR[0], mBodyR[1], mBodyR[2]); glVertex3d(mTailB[0], mTailB[1], mTailB[2]); glVertex3d(mTailR[0], mTailR[1], mTailR[2]);

                    // Крестообразные хвостовые рули (X-оперение)
                    double[] finFront = toWorld(-0.65, 0.0, 0.0);
                    double[] finBack = toWorld(-1.18, 0.0, 0.0);
                    double[] f1 = toWorld(-1.12, -0.34, 0.34);
                    double[] f2 = toWorld(-1.12, 0.34, 0.34);
                    double[] f3 = toWorld(-1.12, -0.34, -0.34);
                    double[] f4 = toWorld(-1.12, 0.34, -0.34);

                    glColor4ub((byte)Math.Min(255, cr + 25), (byte)Math.Min(255, cg + 25), (byte)Math.Min(255, cb + 25), 255);
                    glVertex3d(finFront[0], finFront[1], finFront[2]); glVertex3d(f1[0], f1[1], f1[2]); glVertex3d(finBack[0], finBack[1], finBack[2]);
                    glVertex3d(finFront[0], finFront[1], finFront[2]); glVertex3d(finBack[0], finBack[1], finBack[2]); glVertex3d(f2[0], f2[1], f2[2]);
                    glVertex3d(finFront[0], finFront[1], finFront[2]); glVertex3d(f3[0], f3[1], f3[2]); glVertex3d(finBack[0], finBack[1], finBack[2]);
                    glVertex3d(finFront[0], finFront[1], finFront[2]); glVertex3d(finBack[0], finBack[1], finBack[2]); glVertex3d(f4[0], f4[1], f4[2]);
                }
                else
                {
                    // Узнаваемый силуэт истребителя: острый нос, объёмный фюзеляж,
                    // стреловидные крылья, стабилизаторы и два киля.
                    double[] nose = toWorld(2.05, 0.0, 0.02);
                    double[] bodyTop = toWorld(0.30, 0.0, 0.20);
                    double[] bot = toWorld(0.30, 0.0, -0.13);
                    double[] tail = toWorld(-1.05, 0.0, 0.06);

                    double[] wlF = toWorld(0.32, -0.12, 0.03);
                    double[] wlT = toWorld(-0.52, -1.30, -0.01);
                    double[] wlB = toWorld(-0.98, -0.30, 0.03);
                    double[] wrF = toWorld(0.32, 0.12, 0.03);
                    double[] wrT = toWorld(-0.52, 1.30, -0.01);
                    double[] wrB = toWorld(-0.98, 0.30, 0.03);

                    double[] stL0 = toWorld(-0.72, -0.10, 0.05);
                    double[] stL1 = toWorld(-1.02, -0.58, 0.05);
                    double[] stL2 = toWorld(-1.06, -0.16, 0.05);
                    double[] stR0 = toWorld(-0.72, 0.10, 0.05);
                    double[] stR1 = toWorld(-1.02, 0.58, 0.05);
                    double[] stR2 = toWorld(-1.06, 0.16, 0.05);

                    double[] finL0 = toWorld(-0.50, -0.26, 0.10);
                    double[] finL1 = toWorld(-0.92, -0.28, 0.58);
                    double[] finL2 = toWorld(-1.00, -0.24, 0.10);
                    double[] finR0 = toWorld(-0.50, 0.26, 0.10);
                    double[] finR1 = toWorld(-0.92, 0.28, 0.58);
                    double[] finR2 = toWorld(-1.00, 0.24, 0.10);

                    // Верх фюзеляжа и крыльев
                    glColor4ub(cr, cg, cb, 255);
                    glVertex3d(nose[0], nose[1], nose[2]); glVertex3d(bodyTop[0], bodyTop[1], bodyTop[2]); glVertex3d(wlF[0], wlF[1], wlF[2]);
                    glVertex3d(nose[0], nose[1], nose[2]); glVertex3d(wrF[0], wrF[1], wrF[2]); glVertex3d(bodyTop[0], bodyTop[1], bodyTop[2]);
                    glVertex3d(bodyTop[0], bodyTop[1], bodyTop[2]); glVertex3d(tail[0], tail[1], tail[2]); glVertex3d(wlB[0], wlB[1], wlB[2]);
                    glVertex3d(bodyTop[0], bodyTop[1], bodyTop[2]); glVertex3d(wrB[0], wrB[1], wrB[2]); glVertex3d(tail[0], tail[1], tail[2]);

                    glColor4ub((byte)(cr * 0.92), (byte)(cg * 0.92), (byte)(cb * 0.92), 255);
                    glVertex3d(wlF[0], wlF[1], wlF[2]); glVertex3d(wlT[0], wlT[1], wlT[2]); glVertex3d(wlB[0], wlB[1], wlB[2]);
                    glVertex3d(wrF[0], wrF[1], wrF[2]); glVertex3d(wrB[0], wrB[1], wrB[2]); glVertex3d(wrT[0], wrT[1], wrT[2]);
                    glVertex3d(stL0[0], stL0[1], stL0[2]); glVertex3d(stL1[0], stL1[1], stL1[2]); glVertex3d(stL2[0], stL2[1], stL2[2]);
                    glVertex3d(stR0[0], stR0[1], stR0[2]); glVertex3d(stR2[0], stR2[1], stR2[2]); glVertex3d(stR1[0], stR1[1], stR1[2]);

                    // Брюхо и нижние плоскости
                    glColor4ub((byte)(cr * 0.52), (byte)(cg * 0.52), (byte)(cb * 0.52), 255);
                    glVertex3d(nose[0], nose[1], nose[2]); glVertex3d(wlF[0], wlF[1], wlF[2]); glVertex3d(bot[0], bot[1], bot[2]);
                    glVertex3d(nose[0], nose[1], nose[2]); glVertex3d(bot[0], bot[1], bot[2]); glVertex3d(wrF[0], wrF[1], wrF[2]);
                    glVertex3d(bot[0], bot[1], bot[2]); glVertex3d(wlB[0], wlB[1], wlB[2]); glVertex3d(tail[0], tail[1], tail[2]);
                    glVertex3d(bot[0], bot[1], bot[2]); glVertex3d(tail[0], tail[1], tail[2]); glVertex3d(wrB[0], wrB[1], wrB[2]);
                    glVertex3d(wlF[0], wlF[1], wlF[2]); glVertex3d(wlB[0], wlB[1], wlB[2]); glVertex3d(wlT[0], wlT[1], wlT[2]);
                    glVertex3d(wrF[0], wrF[1], wrF[2]); glVertex3d(wrT[0], wrT[1], wrT[2]); glVertex3d(wrB[0], wrB[1], wrB[2]);
                    glVertex3d(stL0[0], stL0[1], stL0[2]); glVertex3d(stL2[0], stL2[1], stL2[2]); glVertex3d(stL1[0], stL1[1], stL1[2]);
                    glVertex3d(stR0[0], stR0[1], stR0[2]); glVertex3d(stR1[0], stR1[1], stR1[2]); glVertex3d(stR2[0], stR2[1], stR2[2]);

                    // Кили
                    glColor4ub((byte)Math.Min(255, cr + 28), (byte)Math.Min(255, cg + 28), (byte)Math.Min(255, cb + 28), 255);
                    glVertex3d(finL0[0], finL0[1], finL0[2]); glVertex3d(finL1[0], finL1[1], finL1[2]); glVertex3d(finL2[0], finL2[1], finL2[2]);
                    glVertex3d(finR0[0], finR0[1], finR0[2]); glVertex3d(finR1[0], finR1[1], finR1[2]); glVertex3d(finR2[0], finR2[1], finR2[2]);
                    // Фонарь кабины
                    double[] canopyF = toWorld(0.95, 0.0, 0.10);
                    double[] canopyL = toWorld(0.45, -0.10, 0.22);
                    double[] canopyR = toWorld(0.45, 0.10, 0.22);
                    glColor4ub((byte)Math.Min(255, cr + 45), (byte)Math.Min(255, cg + 45), (byte)Math.Min(255, cb + 45), 255);
                    glVertex3d(canopyF[0], canopyF[1], canopyF[2]); glVertex3d(canopyL[0], canopyL[1], canopyL[2]); glVertex3d(canopyR[0], canopyR[1], canopyR[2]);
                }
                glEnd();
            }

            // 3D-волны взрывов при сбитии
            foreach (TacticalEvent ev in s.Events)
            {
                double age = curT - ev.Time;
                if (curT <= 1.5 || age < 0.0 || age > 1.8) continue;
                if (ev.Kind != "KILL" && ev.Kind != "GROUND_KILL" && ev.Kind != "KIA") continue;
                double evZ = s.GetGroundAltitudeAt(ev.WorldX, ev.WorldY) + 40.0;
                if (ev.Kind == "KILL" || ev.Kind == "KIA")
                {
                    foreach (DebriefEntity cand in s.Entities)
                    {
                        if (cand.Type == "Air" && Math.Abs(cand.LastTime - ev.Time) < 1.5 && cand.Track.Count > 0)
                        {
                            DebriefSample ls = cand.Track[cand.Track.Count - 1];
                            if (Math.Abs(ls.X - ev.WorldX) < 600 && Math.Abs(ls.Y - ev.WorldY) < 600)
                            { evZ = ls.Alt; break; }
                        }
                    }
                }
                double blastR = 0.5 + age * 2.4;
                byte ba = (byte)Math.Max(0, Math.Min(255, (int)(230 * (1.0 - age / 1.8))));
                glLineWidth(2.2f);
                if (ev.Kind == "KIA") glColor4ub(255, 65, 65, ba);
                else glColor4ub(65, 255, 95, ba);
                glBegin(GL_LINE_LOOP);
                for (int k = 0; k < 28; k++)
                {
                    double a = k * (Math.PI * 2.0 / 28.0);
                    glVertex3d(ev.WorldX * 0.001 + Math.Cos(a) * blastR, evZ * 0.001, -ev.WorldY * 0.001 + Math.Sin(a) * blastR);
                }
                glEnd();
            }
            // Атомарный рендер всех 2D-плашек, HUD и баннеров в текстуру OpenGL ДО SwapBuffers (0% мерцания!)
            RenderZeroFlickerHudTexture(w, h, s, cx, cy, groundCamAlt, distKm, yaw, pitch, arlLabels, jammedTargets);
        }
        // --- Непрерывный звук NOISE-помех (только 3D) ---
        // Позиция камеры в мировых координатах (метры): обход цели (cx,cy) на distKm с углом pitch
        {
            double pitchR = pitch * Math.PI / 180.0;
            double yawR   = yaw   * Math.PI / 180.0;
            double distM  = distKm * 1000.0;
            double camXM  = cx * 1000.0 - distM * Math.Cos(pitchR) * Math.Sin(yawR);
            double camYM  = cy * 1000.0 - distM * Math.Cos(pitchR) * Math.Cos(yawR);
            double camAltM = (groundCamAlt + 1.2) * 1000.0 + distM * Math.Sin(pitchR);
            UpdateJamContinuous(s, curT, isPlaying, showBeams, camXM, camYM, camAltM, liveById);
        }
        SwapBuffers(hdc);
    }
    // Аэродром на рельефе: ВПП, осевая, пороги и перрон. Данных о полосах в реплее нет,
    // поэтому геометрия типовая, а положение и курс берутся из восстановленных точек взлёта/посадки.
    private void DrawAirfields3D(DebriefSession s, double distKm)
    {
        if (s.Airfields == null || s.Airfields.Count == 0) return;
        foreach (MapPoiMarker af in s.Airfields)
        {
            double hdg = af.Heading * Math.PI / 180.0;
            double fwdX = Math.Sin(hdg), fwdZ = -Math.Cos(hdg);
            double rgtX = Math.Cos(hdg), rgtZ = Math.Sin(hdg);
            double L = 2400.0;
            // Ширина чуть растёт с удалением камеры, иначе полоса не читается на общем плане
            double W = Math.Max(70.0, distKm * 5.5);

            Func<double, double, double[]> P = (along, side) =>
            {
                double wx = af.X + fwdX * along + rgtX * side;
                double wy = af.Y + fwdZ * along + rgtZ * side;
                double g = s.GetGroundAltitudeAt(wx, wy) + 3.0;
                return new double[] { wx * 0.001, g * 0.001, -wy * 0.001 };
            };

            double[] p0 = P(-L * 0.5, -W * 0.5);
            double[] p1 = P(L * 0.5, -W * 0.5);
            double[] p2 = P(L * 0.5, W * 0.5);
            double[] p3 = P(-L * 0.5, W * 0.5);

            glBegin(GL_QUADS);
            glColor4ub(52, 56, 58, 235);
            glVertex3d(p0[0], p0[1], p0[2]);
            glVertex3d(p1[0], p1[1], p1[2]);
            glVertex3d(p2[0], p2[1], p2[2]);
            glVertex3d(p3[0], p3[1], p3[2]);
            glEnd();

            glLineWidth(1.4f);
            glColor4ub(200, 210, 205, 175);
            glBegin(GL_LINE_LOOP);
            glVertex3d(p0[0], p0[1], p0[2]);
            glVertex3d(p1[0], p1[1], p1[2]);
            glVertex3d(p2[0], p2[1], p2[2]);
            glVertex3d(p3[0], p3[1], p3[2]);
            glEnd();

            // Осевая линия пунктиром
            glLineWidth(1.2f);
            glColor4ub(235, 240, 235, 160);
            glBegin(GL_LINES);
            for (int seg = 0; seg < 12; seg++)
            {
                double t0 = -0.46 + seg * (0.92 / 12.0);
                double t1 = t0 + (0.92 / 12.0) * 0.55;
                double[] a = P(t0 * L, 0.0);
                double[] b = P(t1 * L, 0.0);
                glVertex3d(a[0], a[1], a[2]);
                glVertex3d(b[0], b[1], b[2]);
            }
            glEnd();

            // Пороги (по три полосы с каждого торца)
            glLineWidth(1.6f);
            glColor4ub(240, 245, 240, 190);
            glBegin(GL_LINES);
            for (int k = -1; k <= 1; k++)
            {
                double side = k * W * 0.22;
                double[] t0a = P(-L * 0.47, side);
                double[] t0b = P(-L * 0.40, side);
                double[] t1a = P(L * 0.40, side);
                double[] t1b = P(L * 0.47, side);
                glVertex3d(t0a[0], t0a[1], t0a[2]); glVertex3d(t0b[0], t0b[1], t0b[2]);
                glVertex3d(t1a[0], t1a[1], t1a[2]); glVertex3d(t1b[0], t1b[1], t1b[2]);
            }
            glEnd();

            // Перрон сбоку от середины полосы
            double pW = W * 1.9, pL = L * 0.22;
            double[] q0 = P(-pL * 0.5, W * 0.5 + W * 0.25);
            double[] q1 = P(pL * 0.5, W * 0.5 + W * 0.25);
            double[] q2 = P(pL * 0.5, W * 0.5 + W * 0.25 + pW);
            double[] q3 = P(-pL * 0.5, W * 0.5 + W * 0.25 + pW);
            glBegin(GL_QUADS);
            glColor4ub(64, 68, 70, 215);
            glVertex3d(q0[0], q0[1], q0[2]);
            glVertex3d(q1[0], q1[1], q1[2]);
            glVertex3d(q2[0], q2[1], q2[2]);
            glVertex3d(q3[0], q3[1], q3[2]);
            glEnd();
            glColor4ub(170, 180, 176, 130);
            glBegin(GL_LINE_LOOP);
            glVertex3d(q0[0], q0[1], q0[2]);
            glVertex3d(q1[0], q1[1], q1[2]);
            glVertex3d(q2[0], q2[1], q2[2]);
            glVertex3d(q3[0], q3[1], q3[2]);
            glEnd();
        }
    }

    // Параллелепипед в мировых километрах. yawRad: 0 — «нос» на север (-Z).
    // Грани затеняются вручную — фиксированный конвейер OpenGL без освещения.
    private static void DrawBox3D(double cx, double cy, double cz,
        double sx, double sy, double sz, double yawRad, byte r, byte g, byte b, byte a)
    {
        double hx = sx * 0.5, hy = sy * 0.5, hz = sz * 0.5;
        double ch = Math.Cos(yawRad), sh = Math.Sin(yawRad);
        Func<double, double, double, double[]> w = (lx, ly, lz) => new double[]
        {
            cx + lx * ch + lz * sh,
            cy + ly,
            cz + lx * sh - lz * ch
        };
        double[] b0 = w(-hx, -hy, -hz), b1 = w(hx, -hy, -hz), b2 = w(hx, -hy, hz), b3 = w(-hx, -hy, hz);
        double[] t0 = w(-hx, hy, -hz), t1 = w(hx, hy, -hz), t2 = w(hx, hy, hz), t3 = w(-hx, hy, hz);

        glBegin(GL_QUADS);
        glColor4ub((byte)Math.Min(255, r + 22), (byte)Math.Min(255, g + 22), (byte)Math.Min(255, b + 22), a);
        glVertex3d(t0[0], t0[1], t0[2]); glVertex3d(t1[0], t1[1], t1[2]); glVertex3d(t2[0], t2[1], t2[2]); glVertex3d(t3[0], t3[1], t3[2]);

        glColor4ub((byte)(r * 0.45), (byte)(g * 0.45), (byte)(b * 0.45), a);
        glVertex3d(b3[0], b3[1], b3[2]); glVertex3d(b2[0], b2[1], b2[2]); glVertex3d(b1[0], b1[1], b1[2]); glVertex3d(b0[0], b0[1], b0[2]);

        glColor4ub((byte)(r * 0.90), (byte)(g * 0.90), (byte)(b * 0.90), a);
        glVertex3d(b2[0], b2[1], b2[2]); glVertex3d(t2[0], t2[1], t2[2]); glVertex3d(t3[0], t3[1], t3[2]); glVertex3d(b3[0], b3[1], b3[2]);

        glColor4ub((byte)(r * 0.65), (byte)(g * 0.65), (byte)(b * 0.65), a);
        glVertex3d(b0[0], b0[1], b0[2]); glVertex3d(t0[0], t0[1], t0[2]); glVertex3d(t1[0], t1[1], t1[2]); glVertex3d(b1[0], b1[1], b1[2]);

        glColor4ub((byte)(r * 0.78), (byte)(g * 0.78), (byte)(b * 0.78), a);
        glVertex3d(b3[0], b3[1], b3[2]); glVertex3d(t3[0], t3[1], t3[2]); glVertex3d(t0[0], t0[1], t0[2]); glVertex3d(b0[0], b0[1], b0[2]);

        glColor4ub((byte)(r * 0.84), (byte)(g * 0.84), (byte)(b * 0.84), a);
        glVertex3d(b1[0], b1[1], b1[2]); glVertex3d(t1[0], t1[1], t1[2]); glVertex3d(t2[0], t2[1], t2[2]); glVertex3d(b2[0], b2[1], b2[2]);
        glEnd();
    }

    // Наземка/корабль: вместо безликой пирамиды — узнаваемый силуэт по типу юнита.
    private void DrawGroundUnit3D(DebriefSession s, DebriefEntity ent, DebriefSample cur,
        byte cr, byte cg, byte cb, double distKm, double curT)
    {
        double gz = Math.Max(cur.Alt, s.GetGroundAltitudeAt(cur.X, cur.Y));
        double bx = cur.X * 0.001, by = gz * 0.001, bz = -cur.Y * 0.001;
        double sc = Math.Max(0.05, Math.Min(0.35, distKm * 0.010));
        double yaw = cur.Heading * Math.PI / 180.0;
        double fwdX = Math.Sin(yaw), fwdZ = -Math.Cos(yaw);
        double rgtX = Math.Cos(yaw), rgtZ = Math.Sin(yaw);
        string nm = ent.Name ?? "";
        Func<string, bool> has = (kw) => nm.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0;

        bool isSea = ent.Class == "Sea" || has("Ship") || has("Cruiser") || has("Carrier") ||
                     has("Boat") || has("Destroyer") || has("Frigate");
        bool isRadar = !isSea && (has("Radar") || has("Watchman") || has("EWR"));
        bool isSam = !isSea && !isRadar && (ent.RoleTag == "SAM" || has("SAM") || has("SAAW") ||
                     has("SLAM") || has("SRAD") || has("CIWS") || has("Phalanx") || has("C-RAM") ||
                     has("VLS") || has("Launcher") || has("Turret") || has("Anti-Air") || has("MANPADS"));
        bool isArmor = !isSea && !isRadar && !isSam && (has("Tank") || has("IFV") || has("APC"));
        bool isArty = !isSea && !isRadar && !isSam && !isArmor && (has("Artillery") || has("Howitzer") || has("Mortar"));
        bool isTruck = !isSea && !isRadar && !isSam && !isArmor && !isArty &&
                       (has("Truck") || has("Supply") || has("Logistic") || has("Transport"));

        if (isSea)
        {
            double L = 1.70 * sc, W = 0.42 * sc, H = 0.20 * sc;
            DrawBox3D(bx, by + H * 0.5, bz, W, H, L, yaw, cr, cg, cb, 245);
            // Надстройка ближе к носу
            DrawBox3D(bx + fwdX * L * 0.12, by + H + 0.13 * sc, bz + fwdZ * L * 0.12,
                W * 0.55, 0.26 * sc, L * 0.28, yaw, cr, cg, cb, 245);
            // Мачта
            DrawBox3D(bx + fwdX * L * 0.10, by + H + 0.38 * sc, bz + fwdZ * L * 0.10,
                0.045 * sc, 0.42 * sc, 0.045 * sc, yaw, cr, cg, cb, 235);
            // Палуба/полоса
            DrawBox3D(bx - fwdX * L * 0.22, by + H + 0.03 * sc, bz - fwdZ * L * 0.22,
                W * 0.72, 0.035 * sc, L * 0.34, yaw, (byte)Math.Min(255, cr + 20), (byte)Math.Min(255, cg + 20), (byte)Math.Min(255, cb + 20), 240);
            return;
        }

        if (isRadar)
        {
            DrawBox3D(bx, by + 0.10 * sc, bz, 0.55 * sc, 0.20 * sc, 0.55 * sc, yaw, cr, cg, cb, 245);
            DrawBox3D(bx, by + 0.30 * sc, bz, 0.30 * sc, 0.22 * sc, 0.30 * sc, yaw, cr, cg, cb, 245);
            DrawBox3D(bx, by + 0.56 * sc, bz, 0.05 * sc, 0.38 * sc, 0.05 * sc, yaw, cr, cg, cb, 235);
            // Вращающаяся антенна-тарелка
            double dishYaw = curT * 1.35 + (ent.Id != null ? ent.Id.Length : 0);
            DrawBox3D(bx, by + 0.78 * sc, bz, 0.50 * sc, 0.07 * sc, 0.15 * sc, dishYaw, cr, cg, cb, 240);
            return;
        }

        if (isSam)
        {
            DrawBox3D(bx, by + 0.09 * sc, bz, 0.55 * sc, 0.18 * sc, 0.95 * sc, yaw, cr, cg, cb, 245);
            DrawBox3D(bx - fwdX * 0.30 * sc, by + 0.23 * sc, bz - fwdZ * 0.30 * sc,
                0.32 * sc, 0.20 * sc, 0.28 * sc, yaw, cr, cg, cb, 245);
            // Три транспортно-пусковых контейнера
            for (int i = -1; i <= 1; i++)
            {
                DrawBox3D(bx + fwdX * 0.16 * sc + rgtX * i * 0.14 * sc,
                          by + 0.40 * sc,
                          bz + fwdZ * 0.16 * sc + rgtZ * i * 0.14 * sc,
                          0.10 * sc, 0.38 * sc, 0.55 * sc, yaw, cr, cg, cb, 250);
            }
            return;
        }

        if (isArmor)
        {
            DrawBox3D(bx, by + 0.10 * sc, bz, 0.34 * sc, 0.16 * sc, 0.72 * sc, yaw, cr, cg, cb, 245);
            DrawBox3D(bx + rgtX * 0.19 * sc, by + 0.07 * sc, bz + rgtZ * 0.19 * sc,
                0.10 * sc, 0.12 * sc, 0.76 * sc, yaw, (byte)(cr * 0.5), (byte)(cg * 0.5), (byte)(cb * 0.5), 245);
            DrawBox3D(bx - rgtX * 0.19 * sc, by + 0.07 * sc, bz - rgtZ * 0.19 * sc,
                0.10 * sc, 0.12 * sc, 0.76 * sc, yaw, (byte)(cr * 0.5), (byte)(cg * 0.5), (byte)(cb * 0.5), 245);
            DrawBox3D(bx, by + 0.23 * sc, bz, 0.24 * sc, 0.14 * sc, 0.28 * sc, yaw, cr, cg, cb, 250);
            DrawBox3D(bx + fwdX * 0.36 * sc, by + 0.25 * sc, bz + fwdZ * 0.36 * sc,
                0.04 * sc, 0.04 * sc, 0.52 * sc, yaw, cr, cg, cb, 240);
            return;
        }

        if (isArty)
        {
            DrawBox3D(bx, by + 0.09 * sc, bz, 0.46 * sc, 0.16 * sc, 0.58 * sc, yaw, cr, cg, cb, 245);
            DrawBox3D(bx - fwdX * 0.18 * sc, by + 0.22 * sc, bz - fwdZ * 0.18 * sc,
                0.26 * sc, 0.16 * sc, 0.24 * sc, yaw, cr, cg, cb, 245);
            // Длинный ствол
            DrawBox3D(bx + fwdX * 0.48 * sc, by + 0.30 * sc, bz + fwdZ * 0.48 * sc,
                0.045 * sc, 0.045 * sc, 0.85 * sc, yaw, cr, cg, cb, 240);
            return;
        }

        if (isTruck)
        {
            DrawBox3D(bx, by + 0.10 * sc, bz, 0.34 * sc, 0.16 * sc, 0.85 * sc, yaw, cr, cg, cb, 245);
            DrawBox3D(bx + fwdX * 0.30 * sc, by + 0.24 * sc, bz + fwdZ * 0.30 * sc,
                0.30 * sc, 0.20 * sc, 0.22 * sc, yaw, cr, cg, cb, 245);
            DrawBox3D(bx - fwdX * 0.18 * sc, by + 0.26 * sc, bz - fwdZ * 0.18 * sc,
                0.32 * sc, 0.24 * sc, 0.42 * sc, yaw, cr, cg, cb, 245);
            return;
        }

        // Здание/укрепление/прочее
        DrawBox3D(bx, by + 0.22 * sc, bz, 0.55 * sc, 0.44 * sc, 0.55 * sc, yaw, cr, cg, cb, 245);
        DrawBox3D(bx, by + 0.47 * sc, bz, 0.62 * sc, 0.06 * sc, 0.62 * sc, yaw,
            (byte)(cr * 0.8), (byte)(cg * 0.8), (byte)(cb * 0.8), 240);
    }

    // Объёмный конус помех NOISE. Ось и углы — из реальной модели RadarJammer:
    // относительный сигнал = cos(theta)^8, поэтому границы 10% и 50% считаются
    // через JammerModel.AngleForLevel. Поверхность прижимается к рельефу.
    private void DrawNoiseCone(DebriefSession s, double ox, double oy, double oz,
        double dirX, double dirY, double dirZ, double lenMeters, double curT)
    {
        double alen = Math.Sqrt(dirX * dirX + dirY * dirY + dirZ * dirZ);
        if (alen < 1e-6) return;
        double ax = dirX / alen, ay = dirY / alen, az = dirZ / alen;

        double hx = Math.Abs(ax) < 0.9 ? 1.0 : 0.0;
        double hz = Math.Abs(ax) < 0.9 ? 0.0 : 1.0;
        double uX = ay * hz, uY = az * hx - ax * hz, uZ = -ay * hx;
        double ul = Math.Sqrt(uX * uX + uY * uY + uZ * uZ);
        if (ul < 1e-6) return;
        uX /= ul; uY /= ul; uZ /= ul;
        double vX = ay * uZ - az * uY;
        double vY = az * uX - ax * uZ;
        double vZ = ax * uY - ay * uX;

        double lenM = Math.Min(lenMeters, JammerModel.NoiseConeRangeM);
        if (lenM < 600.0) return;
        double tanOuter = Math.Tan(JammerModel.NoiseConeHalfAngleDeg * Math.PI / 180.0);
        double pulse = 0.5 + 0.5 * Math.Sin(curT * 1.6);

        Func<double, double, double, double[]> pt = (frac, ang, tanA) =>
        {
            double d = frac * lenM;
            double r = d * tanA;
            double ca = Math.Cos(ang), sa = Math.Sin(ang);
            double wx = ox + ax * d + (uX * ca + vX * sa) * r;
            double wy = oy + ay * d + (uY * ca + vY * sa) * r;
            double wz = oz + az * d + (uZ * ca + vZ * sa) * r;
            return new double[] { wx, wy, wz };
        };

        const int rings = 8;
        const int segs = 20;

        // Только конус: полупрозрачная поверхность сильного подавления
        glDepthMask(false);
        DrawConeSurface(pt, rings, segs, tanOuter, 150, 90, 255, (int)(10 + 8 * pulse));

        // Шум внутри конуса. Яркость штриха пропорциональна реальному сигналу
        // подавления cos(theta)^8, поэтому помехи гуще у оси и у источника,
        // а к кромке конуса и вдаль затухают.
        glLineWidth(1.0f);
        glBegin(GL_LINES);
        for (int i = 0; i < 320; i++)
        {
            double h1 = Hash01(i * 3 + 1);
            double h2 = Hash01(i * 3 + 2);
            double h3 = Hash01(i * 3 + 3);

            double frac = 0.05 + 0.95 * h1;      // положение вдоль оси
            double radial = Math.Sqrt(h2);        // 0 — ось, 1 — кромка
            double ang = h3 * 2.0 * Math.PI;

            double d = frac * lenM;
            double r = radial * d * tanOuter;
            double cosT = 1.0 / Math.Sqrt(1.0 + radial * radial * tanOuter * tanOuter);
            double sig = Math.Pow(cosT, 8.0);
            double flick = 0.30 + 0.70 * (0.5 + 0.5 * Math.Sin(curT * 17.0 + h2 * 40.0));
            int alpha = (int)(16.0 + 165.0 * sig * flick * (1.0 - 0.35 * frac));
            if (alpha < 7) continue;

            double ca = Math.Cos(ang), sa = Math.Sin(ang);
            double bx = ox + ax * d + (uX * ca + vX * sa) * r;
            double by = oy + ay * d + (uY * ca + vY * sa) * r;
            double bz = oz + az * d + (uZ * ca + vZ * sa) * r;

            double segLen = Math.Max(300.0, d * 0.020);
            double tAng = h1 * 6.283185307;
            double tcx = Math.Cos(tAng), tsx = Math.Sin(tAng);
            double ex = bx + (uX * tcx + vX * tsx) * segLen;
            double ey = by + (uY * tcx + vY * tsx) * segLen;
            double ez = bz + (uZ * tcx + vZ * tsx) * segLen;

            glColor4ub(205, 135, 255, (byte)Math.Min(235, alpha));
            glVertex3d(bx * 0.001, bz * 0.001, -by * 0.001);
            glVertex3d(ex * 0.001, ez * 0.001, -ey * 0.001);
        }
        glEnd();
        glDepthMask(true);

        // Кромка конуса и образующие
        glLineWidth(1.2f);
        glColor4ub(205, 130, 255, (byte)(60 + 25 * pulse));
        glBegin(GL_LINE_LOOP);
        for (int j = 0; j < segs; j++)
        {
            double[] p = pt(1.0, j * 2.0 * Math.PI / segs, tanOuter);
            glVertex3d(p[0] * 0.001, p[2] * 0.001, -p[1] * 0.001);
        }
        glEnd();

        glLineWidth(1.0f);
        glColor4ub(195, 120, 255, (byte)(34 + 18 * pulse));
        glBegin(GL_LINES);
        for (int j = 0; j < 6; j++)
        {
            double a = j * Math.PI / 3.0;
            double[] p0 = pt(0.0, a, tanOuter);
            double[] p1 = pt(1.0, a, tanOuter);
            glVertex3d(p0[0] * 0.001, p0[2] * 0.001, -p0[1] * 0.001);
            glVertex3d(p1[0] * 0.001, p1[2] * 0.001, -p1[1] * 0.001);
        }
        glEnd();

        // Ось антенны
        glLineWidth(1.4f);
        glColor4ub(225, 150, 255, (byte)(70 + 25 * pulse));
        glBegin(GL_LINES);
        double[] o0 = pt(0.0, 0.0, 0.0);
        double[] o1 = pt(1.0, 0.0, 0.0);
        glVertex3d(o0[0] * 0.001, o0[2] * 0.001, -o0[1] * 0.001);
        glVertex3d(o1[0] * 0.001, o1[2] * 0.001, -o1[1] * 0.001);
        glEnd();
    }

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

    private static void DrawConeSurface(Func<double, double, double, double[]> pt,
        int rings, int segs, double tanA, byte r, byte g, byte b, int alpha)
    {
        glBegin(GL_QUADS);
        for (int i = 0; i < rings; i++)
        {
            double f0 = (double)i / rings;
            double f1 = (double)(i + 1) / rings;
            int a = Math.Max(0, Math.Min(255, alpha - (int)(f0 * alpha * 0.45)));
            glColor4ub(r, g, b, (byte)a);
            for (int j = 0; j < segs; j++)
            {
                double a0 = j * 2.0 * Math.PI / segs;
                double a1 = (j + 1) * 2.0 * Math.PI / segs;
                double[] p00 = pt(f0, a0, tanA);
                double[] p01 = pt(f0, a1, tanA);
                double[] p11 = pt(f1, a1, tanA);
                double[] p10 = pt(f1, a0, tanA);
                glVertex3d(p00[0] * 0.001, p00[2] * 0.001, -p00[1] * 0.001);
                glVertex3d(p01[0] * 0.001, p01[2] * 0.001, -p01[1] * 0.001);
                glVertex3d(p11[0] * 0.001, p11[2] * 0.001, -p11[1] * 0.001);
                glVertex3d(p10[0] * 0.001, p10[2] * 0.001, -p10[1] * 0.001);
            }
        }
        glEnd();
    }

    private void RenderZeroFlickerHudTexture(
    int w, int h, DebriefSession s, double cx, double cy, double groundCamAlt, double distKm, double yaw, double pitch,
    List<Tuple<string, string, PointF, Color>> arlLabels, Dictionary<string, string> jammedTargets)
    {
        if (hudBmp == null || hudBmp.Width != w || hudBmp.Height != h)
        {
            if (hudBmp != null) hudBmp.Dispose();
            hudBmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        }
        using (Graphics g = Graphics.FromImage(hudBmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            double curT = GetCurrentTime != null ? GetCurrentTime() : 0.0;
            bool showAllies = GetShowAllies == null || GetShowAllies();
            string followId = GetFollowId != null ? GetFollowId() : "";
            List<RectangleF> occupied = new List<RectangleF>();
            Action<string, string, Font, Color, Color, float, float, bool> drawBadge = (key, text, fnt, fgCol, bdCol, ax, ay, isMsl) =>
            {
                if (ax < -50 || ax > w + 50 || ay < -50 || ay > h + 50) return;
                SizeF sz = g.MeasureString(text, fnt);
                float bw = sz.Width + 8f;
                float bh = sz.Height + 3f;

                float[] dxSlots = new float[] { 14f, -bw * 0.5f, -bw * 0.5f, -bw - 14f, 14f, 14f, -bw - 14f, -bw - 14f };
                float[] dySlots = new float[] { -bh * 0.5f, -bh - 14f, 14f, -bh * 0.5f, bh + 6f, -bh - 8f, bh + 6f, -bh - 8f };
                float targetDx = 14f, targetDy = -bh * 0.5f;

                PointF prev;
                bool hasPrev = smoothOffsets.TryGetValue(key, out prev);
                bool foundSlot = false;
                if (hasPrev)
                {
                    RectangleF pr = new RectangleF(ax + prev.X, ay + prev.Y, bw, bh);
                    if (pr.Left >= 4 && pr.Right <= w - 4 && pr.Top >= 4 && pr.Bottom <= h - 24)
                    {
                        bool hit = false;
                        for (int k = 0; k < occupied.Count; k++)
                            if (pr.IntersectsWith(occupied[k])) { hit = true; break; }
                        if (!hit) { targetDx = prev.X; targetDy = prev.Y; foundSlot = true; }
                    }
                }
                if (!foundSlot)
                {
                    for (int sIdx = 0; sIdx < dxSlots.Length; sIdx++)
                    {
                        RectangleF cand = new RectangleF(ax + dxSlots[sIdx], ay + dySlots[sIdx], bw, bh);
                        if (cand.Left < 4 || cand.Right > w - 4 || cand.Top < 4 || cand.Bottom > h - 24) continue;
                        bool hit = false;
                        for (int k = 0; k < occupied.Count; k++)
                            if (cand.IntersectsWith(occupied[k])) { hit = true; break; }
                        if (!hit) { targetDx = dxSlots[sIdx]; targetDy = dySlots[sIdx]; break; }
                    }
                }
                if (hasPrev)
                {
                    targetDx = prev.X + (targetDx - prev.X) * 0.25f;
                    targetDy = prev.Y + (targetDy - prev.Y) * 0.25f;
                }
                smoothOffsets[key] = new PointF(targetDx, targetDy);

                RectangleF rect = new RectangleF(ax + targetDx, ay + targetDy, bw, bh);
                if (rect.Right > w - 4 || rect.Bottom > h - 24 || rect.Left < 4 || rect.Top < 4) return;
                occupied.Add(rect);

                if (isMsl)
                {
                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(155, 8, 12, 11)))
                        g.FillRectangle(bg, rect);
                    using (SolidBrush fg = new SolidBrush(fgCol))
                        g.DrawString(text, fnt, fg, rect.X + 4f, rect.Y + 1.5f);
                }
                else
                {
                    float leadX = rect.Left > ax ? rect.Left : rect.Right;
                    using (Pen lp = new Pen(Color.FromArgb(145, bdCol), 1.0f))
                        g.DrawLine(lp, ax, ay, leadX, rect.Y + bh * 0.5f);
                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(200, 11, 16, 15)))
                        g.FillRectangle(bg, rect);
                    using (Pen bp = new Pen(Color.FromArgb(135, bdCol), 1.0f))
                        g.DrawRectangle(bp, rect.X, rect.Y, rect.Width, rect.Height);
                    using (SolidBrush bar = new SolidBrush(bdCol))
                        g.FillRectangle(bar, rect.X, rect.Y, 2.5f, rect.Height);
                    using (SolidBrush fg = new SolidBrush(fgCol))
                        g.DrawString(text, fnt, fg, rect.X + 4f, rect.Y + 1.5f);
                }
            };
            // Взрывы из eventTrack в 3D-проекции: короткая вспышка и затухающее кольцо
            if (s != null && s.Explosions != null && s.Explosions.Count > 0)
            {
                foreach (DebriefExplosion ex in s.Explosions)
                {
                    double age = curT - ex.Time;
                    if (age < 0.0 || age > 2.5) continue;
                    PointF ep = Project3DToScreen(ex.X, ex.Y, ex.Alt, cx, cy, groundCamAlt, distKm, yaw, pitch, w, h);
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
                }
            }
            // ЛТЦ/ДО из eventTrack (в текущих реплеях данных нет — блок молчит)
            if (s != null && s.Flares != null && s.Flares.Count > 0)
            {
                using (Font flFont = new Font("Consolas", 6.0f, FontStyle.Bold))
                {
                    foreach (DebriefFlare fl in s.Flares)
                    {
                        double age = curT - fl.Time;
                        if (age < 0.0 || age > 4.0) continue;
                        PointF fp = Project3DToScreen(fl.X, fl.Y, fl.Alt, cx, cy, groundCamAlt, distKm, yaw, pitch, w, h);
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
            using (Font acFont = new Font("Consolas", 7.8f, FontStyle.Bold))
            using (Font wpnFont = new Font("Consolas", 6.8f, FontStyle.Italic))
            using (Font evFont = new Font("Consolas", 8.0f, FontStyle.Bold))
            using (Font jamTagFont = new Font("Consolas", 6.8f, FontStyle.Bold))
            {
                Action<PointF, string> drawJamBadge = (pt, mode) =>
                {
                    if (pt.X < -50 || pt.X > w + 50 || pt.Y < -50 || pt.Y > h + 50) return;
                    float pulse = (float)(0.5 + 0.5 * Math.Sin(curT * 10.0));
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

                int myMsl = 0, allyMsl = 0, enemyMsl = 0;
                DebriefEntity activeTracked = null;
                DebriefSample activeSample = null;

                // 0. Метки ориентиров (Bullseye и ЗРК при приближении)
                int pIdx = 0;
                foreach (MapPoiMarker poi in s.MapMarkers)
                {
                    pIdx++;
                    if (poi.RadiusMeters < 0 && curT > -poi.RadiusMeters) continue;
                    if (!showAllies && poi.Coalition == "Allies" && poi.Kind != "Base") continue;
                    if (poi.Kind == "Bullseye" || (poi.Kind == "SAM" && distKm < 75.0))
                    {
                        double pz = s.GetGroundAltitudeAt(poi.X, poi.Y);
                        PointF pSp = Project3DToScreen(poi.X, poi.Y, pz, cx, cy, groundCamAlt, distKm, yaw, pitch, w, h);
                        Color pc = poi.Kind == "Bullseye" ? Color.Gold : (poi.Coalition == "Enemies" ? Color.FromArgb(255, 155, 35) : Color.DeepSkyBlue);
                        drawBadge("poi_" + pIdx, poi.Label, wpnFont, pc, pc, pSp.X, pSp.Y, false);
                    }
                }

                // 0.1 Подписи восстановленных аэродромов
                if (s.Airfields != null)
                {
                    for (int ai = 0; ai < s.Airfields.Count; ai++)
                    {
                        MapPoiMarker af = s.Airfields[ai];
                        double az = s.GetGroundAltitudeAt(af.X, af.Y);
                        PointF ap = Project3DToScreen(af.X, af.Y, az, cx, cy, groundCamAlt, distKm, yaw, pitch, w, h);
                        drawBadge("af_" + ai, "🛫 " + af.Label, wpnFont,
                            Color.FromArgb(225, 220, 230), Color.FromArgb(170, 200, 215), ap.X, ap.Y, false);
                    }
                }

                // 1. Плашки бортов, наземных юнитов и ракет
                foreach (DebriefEntity ent in s.Entities)
                {
                    if (!showAllies && !ent.IsPlayer && ent.Coalition == "Allies") continue;
                    DebriefSample cur = ent.GetStateAt(curT);
                    if (cur == null) continue;

                    if (ent.Type == "Ground")
                    {
                        double gz = Math.Max(cur.Alt, s.GetGroundAltitudeAt(cur.X, cur.Y));
                        PointF gSp = Project3DToScreen(cur.X, cur.Y, gz, cx, cy, groundCamAlt, distKm, yaw, pitch, w, h);
                        Color gCol = ent.Coalition == "Enemies" ? Color.FromArgb(255, 155, 35) : Color.DeepSkyBlue;

                        string jTag;
                        if (jammedTargets.TryGetValue(ent.Id, out jTag))
                            drawJamBadge(gSp, jTag);

                        if (distKm < 95.0)
                            drawBadge("g_" + ent.Id, ent.Name, wpnFont, Color.FromArgb(220, gCol), gCol, gSp.X, gSp.Y, false);
                        continue;
                    }

                    if (!string.IsNullOrEmpty(followId) && ent.Id == followId) { activeTracked = ent; activeSample = cur; }
                    else if (string.IsNullOrEmpty(followId) && ent.IsPlayer) { activeTracked = ent; activeSample = cur; }
                    bool isMine = ent.IsPlayer || (ent.Type == "Missile" && ent.Pilot != null && ent.Pilot.StartsWith("MY_MISSILE:"));
                    if (ent.Type == "Missile")
                    {
                        if (isMine) myMsl++;
                        else if (ent.Coalition == "Allies") allyMsl++;
                        else enemyMsl++;
                    }
                    PointF sp = Project3DToScreen(cur.X, cur.Y, cur.Alt, cx, cy, groundCamAlt, distKm, yaw, pitch, w, h);
                    Color col = isMine
                        ? Color.FromArgb(55, 255, 85)
                        : (ent.Type == "Missile"
                            ? (ent.Coalition == "Enemies" ? Color.FromArgb(255, 175, 50) : Color.FromArgb(0, 215, 255))
                            : (ent.Coalition == "Enemies" ? Color.FromArgb(255, 155, 35) : Color.DeepSkyBlue));
                    if (ent.Type == "Missile")
                    {
                        drawBadge("m_" + ent.Id, ent.Name, wpnFont, col, col, sp.X, sp.Y, true);
                    }
                    else
                    {
                        string jTag;
                        if (jammedTargets.TryGetValue(ent.Id, out jTag))
                            drawJamBadge(sp, jTag);

                        string vsTag = cur.Pitch > 4.0 ? " ▲" : (cur.Pitch < -4.0 ? " ▼" : "");
                        string title = !string.IsNullOrEmpty(ent.Pilot) && ent.Pilot != "AI Bandit" && ent.Pilot != ent.Name ? ent.Pilot : ent.Name;
                        string lbl = string.Format("{0}\n{1:F0}m{2} · {3:F0}km/h", title, cur.Alt, vsTag, cur.Speed * 3.6);
                        drawBadge("a_" + ent.Id, lbl, acFont, col, col, sp.X, sp.Y, false);
                    }
                }

                // 2. Дистанции и TTI на лучах АРЛ-ракет
                foreach (var al in arlLabels)
                    drawBadge(al.Item1, al.Item2, wpnFont, al.Item4, al.Item4, al.Item3.X, al.Item3.Y, true);
                // 3. Полная хроника пусков по игроку, угроз, повреждений и сбитий в 3D
                string latestThreatBanner = "";
                int evIdx = 0;
                foreach (TacticalEvent ev in s.Events)
                {
                    evIdx++;
                    double age = curT - ev.Time;
                    double maxAge = (ev.Kind == "THREAT" || ev.Kind == "KIA" || ev.Kind == "MISTAKE" || ev.Kind == "RWR") ? 6.5 : 5.0;
                    if (age < 0.0 || age > maxAge) continue;
                    if (ev.Kind == "RADAR" || ev.Kind == "JAMMER" || ev.Kind == "INFO") continue;

                    bool isThreatOnMe = (ev.Kind == "THREAT" || ev.Kind == "RWR" || ev.Kind == "MISTAKE") &&
                                        (ev.Title.IndexOf("по вам", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         ev.Title.IndexOf("ваш", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         ev.Severity == "CRITICAL");
                    if (isThreatOnMe) latestThreatBanner = ev.Title;

                    double wx = ev.WorldX, wy = ev.WorldY;
                    double evZ = s.GetGroundAltitudeAt(wx, wy) + 30.0;

                    if (isThreatOnMe && activeSample != null)
                    {
                        // Угрозу по вам привязываем прямо к вашему летящему самолёту, чтобы она всегда была в кадре
                        wx = activeSample.X;
                        wy = activeSample.Y;
                        evZ = activeSample.Alt;
                    }
                    else
                    {
                        if (Math.Abs(wx) < 1.0 && Math.Abs(wy) < 1.0 && activeSample != null)
                        {
                            wx = activeSample.X;
                            wy = activeSample.Y;
                        }
                        if (Math.Abs(wx) < 1.0 && Math.Abs(wy) < 1.0) continue;

                        foreach (DebriefEntity cand in s.Entities)
                        {
                            DebriefSample stAtEv = cand.GetStateAt(ev.Time);
                            if (stAtEv != null && Math.Abs(stAtEv.X - wx) < 650 && Math.Abs(stAtEv.Y - wy) < 650)
                            {
                                evZ = stAtEv.Alt;
                                break;
                            }
                        }
                    }

                    PointF ep = Project3DToScreen(wx, wy, evZ, cx, cy, groundCamAlt, distKm, yaw, pitch, w, h);
                    if (ev.Kind == "KILL" || ev.Kind == "GROUND_KILL")
                    {
                        Color ec = Color.FromArgb(55, 255, 85);
                        using (Pen kp = new Pen(ec, 2.0f))
                        {
                            g.DrawLine(kp, ep.X - 7, ep.Y - 7, ep.X + 7, ep.Y + 7);
                            g.DrawLine(kp, ep.X + 7, ep.Y - 7, ep.X - 7, ep.Y + 7);
                        }
                        drawBadge("ev_" + evIdx, (ev.Kind == "GROUND_KILL" ? "💥 " : "★ ") + ev.Title, evFont, ec, ec, ep.X, ep.Y, false);
                    }
                    else if (ev.Kind == "LAUNCH")
                    {
                        Color ec = ev.Severity == "GOOD" ? Color.FromArgb(65, 255, 95) : Color.DeepSkyBlue;
                        drawBadge("ev_" + evIdx, "🚀 " + ev.Title, evFont, ec, ec, ep.X, ep.Y, false);
                    }
                    else if (ev.Kind == "DAMAGE")
                    {
                        using (Pen dp = new Pen(Color.Orange, 1.8f))
                            g.DrawRectangle(dp, ep.X - 7, ep.Y - 7, 14, 14);
                        drawBadge("ev_" + evIdx, "💥 " + ev.Title, evFont, Color.Orange, Color.Orange, ep.X, ep.Y, false);
                    }
                    else
                    {
                        Color ec = ev.Severity == "CRITICAL" ? Color.FromArgb(255, 65, 65) : Color.Gold;
                        float waveR = 11f + (float)((age % 1.0) * 16.0);
                        int waveA = Math.Max(0, (int)(220 * (1.0 - (age % 1.0))));
                        using (Pen rp = new Pen(Color.FromArgb(waveA, ec), 2.0f))
                            g.DrawEllipse(rp, ep.X - waveR, ep.Y - waveR, waveR * 2f, waveR * 2f);
                        drawBadge("ev_" + evIdx, "⚠ " + ev.Title, evFont, ec, ec, ep.X, ep.Y, false);
                    }
                }

                // 4. Эффект включения РЭБ + баннер «Внимание: электронная атака»
                double surgeElapsed = curT - lastJamSurgeT;
                if (surgeElapsed >= 0.0 && surgeElapsed <= 0.75)
                {
                    float wave = (float)Math.Sin((surgeElapsed / 0.75) * Math.PI);
                    using (SolidBrush tintBr = new SolidBrush(Color.FromArgb((int)(16 * wave), 185, 110, 255)))
                        g.FillRectangle(tintBr, 0, 0, w, h);
                    using (Pen outerPen = new Pen(Color.FromArgb((int)(110 * wave), 210, 120, 255), 3f))
                        g.DrawRectangle(outerPen, 1.5f, 1.5f, w - 4f, h - 4f);
                }
                if (surgeElapsed >= 0.0 && surgeElapsed <= 3.5 && !string.IsNullOrEmpty(lastJamBanner))
                {
                    float bAlpha = surgeElapsed > 2.7 ? (float)((3.5 - surgeElapsed) / 0.8) : 1.0f;
                    int aTxt = Math.Max(0, Math.Min(255, (int)(245 * bAlpha)));
                    if (aTxt > 10)
                    {
                        string bTxt = "⚡ " + lastJamBanner.ToUpperInvariant();
                        SizeF bSz = g.MeasureString(bTxt, evFont);
                        float bx = Math.Max(340f, (w - bSz.Width) * 0.5f);
                        RectangleF bRect = new RectangleF(bx - 10f, 14f, bSz.Width + 20f, bSz.Height + 8f);
                        using (SolidBrush bg = new SolidBrush(Color.FromArgb((int)(195 * bAlpha), 22, 12, 34)))
                            g.FillRectangle(bg, bRect);
                        using (Pen bd = new Pen(Color.FromArgb((int)(170 * bAlpha), 210, 120, 255), 1.3f))
                            g.DrawRectangle(bd, bRect.X, bRect.Y, bRect.Width, bRect.Height);
                        using (SolidBrush fg = new SolidBrush(Color.FromArgb(aTxt, 235, 185, 255)))
                            g.DrawString(bTxt, evFont, fg, bx, 18f);
                    }
                }
                // 5. Тактический HUD в левом верхнем углу и строка состояния внизу
                int activeRwr = 0;
                if (activeTracked != null)
                {
                    foreach (TacticalBeam bm in s.Beams)
                        if (curT >= bm.StartTime && curT <= bm.EndTime && bm.Kind == "RWR_SPIKE" && bm.ToName.Equals(activeTracked.Name, StringComparison.OrdinalIgnoreCase))
                            activeRwr++;
                }
                bool hasAlert = activeRwr > 0 || !string.IsNullOrEmpty(latestThreatBanner);
                float hudH = activeSample != null ? (hasAlert ? 104f : 86f) : (hasAlert ? 66f : 48f);
                using (SolidBrush hudBg = new SolidBrush(Color.FromArgb(195, 10, 16, 15)))
                    g.FillRectangle(hudBg, 12, 12, 345, hudH);
                Color hudBdCol = hasAlert ? Color.FromArgb(220, 255, 55, 55) : Color.FromArgb(110, 0, 255, 130);
                using (Pen hudBd = new Pen(hudBdCol, 1.2f))
                    g.DrawRectangle(hudBd, 12, 12, 345, hudH);
                using (SolidBrush bar = new SolidBrush(hudBdCol))
                    g.FillRectangle(bar, 12, 12, 3, hudH);
                float cyHud = 19f;
                if (hasAlert)
                {
                    string alertTxt = !string.IsNullOrEmpty(latestThreatBanner)
                        ? ("🚨 " + latestThreatBanner.ToUpperInvariant())
                        : string.Format("⚠ ЗАХВАТ БОРТА ПО RWR: {0} ИСТОЧН.", activeRwr);
                    using (SolidBrush rwrBr = new SolidBrush(Color.FromArgb(255, 75, 75)))
                        g.DrawString(alertTxt, acFont, rwrBr, 22, cyHud);
                    cyHud += 18f;
                }
                using (SolidBrush hdrBr = new SolidBrush(Color.FromArgb(0, 255, 135)))
                    g.DrawString(string.Format("РАКЕТЫ В ВОЗДУХЕ: МОИ {0} · СОЮЗ {1} · ВРАГ {2}", myMsl, allyMsl, enemyMsl), acFont, hdrBr, 22, cyHud);
                cyHud += 19f;
                if (activeSample != null)
                {
                    double mach = activeSample.Speed / 315.0;
                    string role = activeTracked.IsPlayer ? "[ВЫ]" : (activeTracked.Coalition == "Allies" ? "[СОЮЗ]" : (activeTracked.Coalition == "Enemies" ? "[ВРАГ]" : "[?]"));
                    using (SolidBrush b1 = new SolidBrush(Color.FromArgb(225, 240, 230)))
                        g.DrawString(string.Format("БОРТ {0}: {1}", role, activeTracked.Name), wpnFont, b1, 22, cyHud);
                    using (SolidBrush b2 = new SolidBrush(Color.FromArgb(170, 235, 195)))
                        g.DrawString(string.Format("HDG {0:000}° · ALT {1:F0}m · {2:F0}km/h (M{3:F2})", activeSample.Heading, activeSample.Alt, activeSample.Speed * 3.6, mach), wpnFont, b2, 22, cyHud + 17f);
                    using (SolidBrush b3 = new SolidBrush(Color.FromArgb(120, 180, 150)))
                        g.DrawString("3D GPU OpenGL · ЛКМ — вращение · ПКМ — сдвиг", wpnFont, b3, 22, cyHud + 34f);
                }
                using (SolidBrush hintBr = new SolidBrush(Color.FromArgb(150, 210, 185)))
                    g.DrawString(string.Format("3D GPU OpenGL (Yaw {0:F0}° / Pitch {1:F0}° / Dist {2:F1} км) | {3}", yaw, pitch, distKm, s.MapTitle ?? ""),
                    wpnFont, hintBr, 10, h - 20);
            }
        }
        // Загружаем HUD-слой в текстуру GPU и накладываем поверх 3D-сцены ДО SwapBuffers (абсолютно без мерцания!)
        glDisable(GL_DEPTH_TEST);
        glDisable(GL_FOG);
        glEnable(GL_TEXTURE_2D);
        glBindTexture(GL_TEXTURE_2D, hudTexId);
        BitmapData bmpData = hudBmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        if (hudTexW != w || hudTexH != h)
        {
            glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, w, h, 0, GL_BGRA_EXT, GL_UNSIGNED_BYTE, bmpData.Scan0);
            hudTexW = w;
            hudTexH = h;
        }
        else
        {
            glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, w, h, GL_BGRA_EXT, GL_UNSIGNED_BYTE, bmpData.Scan0);
        }
        hudBmp.UnlockBits(bmpData);
        glMatrixMode(GL_PROJECTION);
        glLoadIdentity();
        glOrtho(0, w, h, 0, -1, 1);
        glMatrixMode(GL_MODELVIEW);
        glLoadIdentity();
        glColor4ub(255, 255, 255, 255);
        glBegin(GL_QUADS);
        glTexCoord2f(0f, 0f); glVertex2f(0f, 0f);
        glTexCoord2f(1f, 0f); glVertex2f(w, 0f);
        glTexCoord2f(1f, 1f); glVertex2f(w, h);
        glTexCoord2f(0f, 1f); glVertex2f(0f, h);
        glEnd();
        glDisable(GL_TEXTURE_2D);
    }
}

