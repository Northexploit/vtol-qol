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

    private void RebuildTerrainOnGpu(DebriefSession s)
    {
        if (terrainListId != 0) { glDeleteLists(terrainListId, 1); terrainListId = 0; }
        cachedSession = s;
        smoothOffsets.Clear();
        triggeredJams.Clear();
        lastJamSurgeT = -99.0;
        lastJamBanner = "";
        if (s == null || s.TerrainHeights == null || s.MapSizeMeters <= 100.0) return;

        int srcN = s.TerrainHeights.GetLength(0);
        int meshN = Math.Min(768, srcN - 1);
        double stepM = s.MapSizeMeters / meshN;
        double minX = s.MapMinX;
        double minY = s.MapMinY;

        double[,] hCache = new double[meshN + 1, meshN + 1];
        for (int iy = 0; iy <= meshN; iy++)
            for (int ix = 0; ix <= meshN; ix++)
                hCache[ix, iy] = s.GetGroundAltitudeAt(minX + ix * stepM, minY + iy * stepM);

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
                double h10 = ix < meshN ? hCache[ix + 1, iy] : h00;

                if (h00 <= 1.5 && h01 <= 1.5)
                {
                    glColor3ub(10, 26, 42);
                }
                else
                {
                    double slopeX = (h10 - h00) / stepM;
                    double slopeY = (h01 - h00) / stepM;
                    double shade = Math.Max(-0.45, Math.Min(0.60, (-slopeX * 0.72 + slopeY * 0.72) * 2.2));
                    double norm = Math.Min(1.0, h00 / 2400.0);
                    int r = (int)((22 + norm * 96) * (1.0 + shade));
                    int gr = (int)((54 + norm * 96) * (1.0 + shade));
                    int b = (int)((34 + norm * 70) * (1.0 + shade));
                    if (h00 < 35.0) { r += 8; gr += 24; b += 22; }
                    if (h00 > 1600.0) { r += 32; gr += 32; b += 36; }
                    int band0 = (int)(h00 / 250.0);
                    int band1 = (int)(h10 / 250.0);
                    int band2 = (int)(h01 / 250.0);
                    if (band0 != band1 || band0 != band2) { r += 18; gr += 28; b += 18; }
                    glColor3ub((byte)Math.Max(8, Math.Min(245, r)), (byte)Math.Max(14, Math.Min(250, gr)), (byte)Math.Max(14, Math.Min(240, b)));
                }
                glVertex3d(x0, h00 * 0.001, z0);
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
        if (s != null)
        {
            if (s != cachedSession) RebuildTerrainOnGpu(s);
            bool showTerr = GetShowTerrain == null || GetShowTerrain();
            if (showTerr && terrainListId != 0)
                glCallList(terrainListId);
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
            double curT = GetCurrentTime != null ? GetCurrentTime() : 0.0;
            bool showAllies = GetShowAllies == null || GetShowAllies();
            bool showBeams = GetShowBeams == null || GetShowBeams();
            string followId = GetFollowId != null ? GetFollowId() : "";
            bool isPlaying = GetIsPlaying != null && GetIsPlaying();
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
            Dictionary<string, DebriefSample> liveById = new Dictionary<string, DebriefSample>();
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
                            }
                        }
                        double horizLen = Math.Sqrt(bm.DirX * bm.DirX + bm.DirY * bm.DirY);
                        double ux2d = horizLen > 1e-4 ? (bm.DirX / horizLen) : 0.0;
                        double uy2d = horizLen > 1e-4 ? (bm.DirY / horizLen) : 0.0;
                        double maxRay = 95000.0;
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
                        if (isDirMode)
                        {
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

                            string hitId = "";
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
                                double hitRadius = Math.Max(1800.0, proj * 0.06);
                                if (perp <= hitRadius)
                                {
                                    maxRay = proj;
                                    hitId = cand.Id;
                                }
                            }
                            if (!string.IsNullOrEmpty(hitId))
                                jammedTargets[hitId] = jamModeName.ToUpperInvariant();
                            else
                                continue;   // цель не найдена — луч DRFM/SAS «в никуда» не рисуем
                        }

                        tx = fx + ux2d * maxRay;
                        ty = fy + uy2d * maxRay;
                        double ray3d = horizLen > 0.05 ? (maxRay / horizLen) : maxRay;
                        tz = Math.Max(s.GetGroundAltitudeAt(tx, ty), fz + bm.DirZ * ray3d);

                        if (!isDirMode)
                        {
                            // Приглушённая медленная пульсация конуса NOISE (1.6 Гц)
                            double jamPulse = 0.5 + 0.5 * Math.Sin(curT * 1.6);
                            byte edgeAlpha = (byte)(22 + 16 * jamPulse);
                            byte coreAlpha = (byte)(40 + 26 * jamPulse);

                            // Границы лепестка 10% и 50% от осевого сигнала (углы из directionalPower=8)
                            double px2d = -uy2d, py2d = ux2d;
                            double halfW10 = maxRay * Math.Tan(JammerModel.AngleForLevel(0.10) * Math.PI / 180.0);
                            double halfW = maxRay * Math.Tan(JammerModel.AngleForLevel(0.50) * Math.PI / 180.0);
                            glLineWidth(1.1f);
                            glColor4ub(200, 110, 255, coreAlpha);
                            glBegin(GL_LINES);
                            glVertex3d(fx * 0.001, fz * 0.001, -fy * 0.001);
                            glVertex3d((tx + px2d * halfW) * 0.001, tz * 0.001, -(ty + py2d * halfW) * 0.001);
                            glVertex3d(fx * 0.001, fz * 0.001, -fy * 0.001);
                            glVertex3d((tx - px2d * halfW) * 0.001, tz * 0.001, -(ty - py2d * halfW) * 0.001);
                            glEnd();
                            // Внешняя граница лепестка — уровень 10%
                            glLineWidth(1.0f);
                            glColor4ub(200, 110, 255, edgeAlpha);
                            glBegin(GL_LINES);
                            glVertex3d(fx * 0.001, fz * 0.001, -fy * 0.001);
                            glVertex3d((tx + px2d * halfW10) * 0.001, tz * 0.001, -(ty + py2d * halfW10) * 0.001);
                            glVertex3d(fx * 0.001, fz * 0.001, -fy * 0.001);
                            glVertex3d((tx - px2d * halfW10) * 0.001, tz * 0.001, -(ty - py2d * halfW10) * 0.001);
                            glEnd();
                            // Медленное обновление засветков (2 Гц) и сниженная яркость
                            int tick = (int)(curT * 2.0);
                            glBegin(GL_QUADS);
                            for (int i = 0; i < 20; i++)
                            {
                                int hash = ((i * 73856093) ^ (tick * 19349663)) & 0x7FFFFFFF;
                                double frac = 0.05 + ((hash % 1000) / 1000.0) * 0.90;
                                double lat = (((hash / 1000) % 200) - 100) / 100.0;
                                double bx = fx + ux2d * (maxRay * frac) + px2d * (halfW * frac * lat * 0.85);
                                double by = fy + uy2d * (maxRay * frac) + py2d * (halfW * frac * lat * 0.85);
                                double bz = fz + (tz - fz) * frac;
                                byte al = (byte)(38 + ((hash / 200000) % 46));
                                double bw = Math.Max(0.04, halfW * frac * 0.06 * 0.001);
                                glColor4ub(210, 115, 255, al);
                                glVertex3d(bx * 0.001 - px2d * bw, bz * 0.001, -(by * 0.001 - py2d * bw));
                                glVertex3d(bx * 0.001 + px2d * bw, bz * 0.001, -(by * 0.001 + py2d * bw));
                                glVertex3d((bx + ux2d * 250) * 0.001 + px2d * bw, bz * 0.001, -((by + uy2d * 250) * 0.001 + py2d * bw));
                                glVertex3d((bx + ux2d * 250) * 0.001 - px2d * bw, bz * 0.001, -((by + uy2d * 250) * 0.001 - py2d * bw));
                            }
                            glEnd();
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
            }
            // 3D-траектории, стойки высоты и силуэты бортов/ракет
            foreach (DebriefEntity ent in s.Entities)
            {
                if (!showAllies && !ent.IsPlayer && ent.Coalition == "Allies") continue;
                DebriefSample cur = ent.GetStateAt(curT);
                if (cur == null) continue;
                bool isMine = ent.IsPlayer || (ent.Type == "Missile" && ent.Pilot != null && ent.Pilot.StartsWith("MY_MISSILE:"));
                byte cr = isMine ? (byte)65 : (ent.Coalition == "Enemies" ? (byte)255 : (byte)40);
                byte cg = isMine ? (byte)255 : (ent.Coalition == "Enemies" ? (byte)155 : (byte)200);
                byte cb = isMine ? (byte)95 : (ent.Coalition == "Enemies" ? (byte)40 : (byte)255);
                if (ent.Type == "Ground")
                {
                    double gz = Math.Max(cur.Alt, s.GetGroundAltitudeAt(cur.X, cur.Y));
                    double gx = cur.X * 0.001, gy = gz * 0.001, gzz = -cur.Y * 0.001;
                    double r = 0.28, topY = gy + 0.36;

                    // Объёмная 4-гранная 3D-пирамида наземного юнита со светотенью граней
                    glBegin(GL_TRIANGLES);
                    glColor4ub(cr, cg, cb, 245);
                    glVertex3d(gx, topY, gzz); glVertex3d(gx - r, gy, gzz - r); glVertex3d(gx + r, gy, gzz - r);
                    glColor4ub((byte)(cr * 0.8), (byte)(cg * 0.8), (byte)(cb * 0.8), 245);
                    glVertex3d(gx, topY, gzz); glVertex3d(gx + r, gy, gzz - r); glVertex3d(gx + r, gy, gzz + r);
                    glColor4ub((byte)(cr * 0.65), (byte)(cg * 0.65), (byte)(cb * 0.65), 245);
                    glVertex3d(gx, topY, gzz); glVertex3d(gx + r, gy, gzz + r); glVertex3d(gx - r, gy, gzz + r);
                    glColor4ub((byte)(cr * 0.85), (byte)(cg * 0.85), (byte)(cb * 0.85), 245);
                    glVertex3d(gx, topY, gzz); glVertex3d(gx - r, gy, gzz + r); glVertex3d(gx - r, gy, gzz - r);
                    glEnd();

                    glLineWidth(1.1f);
                    glColor4ub(255, 255, 255, 90);
                    glBegin(GL_LINE_LOOP);
                    glVertex3d(gx - r, gy + 0.01, gzz - r);
                    glVertex3d(gx + r, gy + 0.01, gzz - r);
                    glVertex3d(gx + r, gy + 0.01, gzz + r);
                    glVertex3d(gx - r, gy + 0.01, gzz + r);
                    glEnd();
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

                double zoomScale = Math.Max(0.32, Math.Min(1.0, distKm / 18.0));
                double sc = (ent.Type == "Missile" ? 0.14 : 0.24) * zoomScale;
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
                    // Объёмный 3D-истребитель с крыльями, брюхом и двумя килями
                    double[] vNose = toWorld(1.75, 0.0, 0.0);
                    double[] vTop = toWorld(-0.25, 0.0, 0.24);
                    double[] vBot = toWorld(-0.25, 0.0, -0.14);
                    double[] vTail = toWorld(-1.15, 0.0, 0.04);
                    double[] vWingL = toWorld(-0.85, -1.15, -0.04);
                    double[] vWingR = toWorld(-0.85, 1.15, -0.04);

                    glColor4ub(cr, cg, cb, 255);
                    glVertex3d(vNose[0], vNose[1], vNose[2]); glVertex3d(vTop[0], vTop[1], vTop[2]); glVertex3d(vWingL[0], vWingL[1], vWingL[2]);
                    glVertex3d(vNose[0], vNose[1], vNose[2]); glVertex3d(vWingR[0], vWingR[1], vWingR[2]); glVertex3d(vTop[0], vTop[1], vTop[2]);
                    glVertex3d(vTop[0], vTop[1], vTop[2]); glVertex3d(vTail[0], vTail[1], vTail[2]); glVertex3d(vWingL[0], vWingL[1], vWingL[2]);
                    glVertex3d(vTop[0], vTop[1], vTop[2]); glVertex3d(vWingR[0], vWingR[1], vWingR[2]); glVertex3d(vTail[0], vTail[1], vTail[2]);

                    glColor4ub((byte)(cr * 0.55), (byte)(cg * 0.55), (byte)(cb * 0.55), 255);
                    glVertex3d(vNose[0], vNose[1], vNose[2]); glVertex3d(vWingL[0], vWingL[1], vWingL[2]); glVertex3d(vBot[0], vBot[1], vBot[2]);
                    glVertex3d(vNose[0], vNose[1], vNose[2]); glVertex3d(vBot[0], vBot[1], vBot[2]); glVertex3d(vWingR[0], vWingR[1], vWingR[2]);
                    glVertex3d(vBot[0], vBot[1], vBot[2]); glVertex3d(vWingL[0], vWingL[1], vWingL[2]); glVertex3d(vTail[0], vTail[1], vTail[2]);
                    glVertex3d(vBot[0], vBot[1], vBot[2]); glVertex3d(vTail[0], vTail[1], vTail[2]); glVertex3d(vWingR[0], vWingR[1], vWingR[2]);

                    double[] vFinLBase = toWorld(-0.55, -0.22, 0.06);
                    double[] vFinLTop = toWorld(-1.05, -0.34, 0.48);
                    double[] vFinLBack = toWorld(-1.12, -0.22, 0.06);

                    double[] vFinRBase = toWorld(-0.55, 0.22, 0.06);
                    double[] vFinRTop = toWorld(-1.05, 0.34, 0.48);
                    double[] vFinRBack = toWorld(-1.12, 0.22, 0.06);

                    glColor4ub((byte)Math.Min(255, cr + 30), (byte)Math.Min(255, cg + 30), (byte)Math.Min(255, cb + 30), 255);
                    glVertex3d(vFinLBase[0], vFinLBase[1], vFinLBase[2]); glVertex3d(vFinLTop[0], vFinLTop[1], vFinLTop[2]); glVertex3d(vFinLBack[0], vFinLBack[1], vFinLBack[2]);
                    glVertex3d(vFinRBase[0], vFinRBase[1], vFinRBase[2]); glVertex3d(vFinRTop[0], vFinRTop[1], vFinRTop[2]); glVertex3d(vFinRBack[0], vFinRBack[1], vFinRBack[2]);
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
        SwapBuffers(hdc);
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
                    string role = activeTracked.IsPlayer ? "[ВЫ]" : (activeTracked.Coalition == "Allies" ? "[СОЮЗ]" : "[ВРАГ]");
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

