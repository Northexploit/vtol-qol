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
// === МЕНЕДЖЕР ЛИВРЕЙ (НОВЫЙ, с 3D-кнопкой, ZIP, Workshop) ================
// =========================================================================
public class LiveryManagerForm : Form
{
    private string gameRoot;
    private bool isCatalogMode = false;
    private int catalogPage = 1;

    private Button btnTabMy;
    private Button btnTabCatalog;
    private ComboBox cmbAircraft;
    private TextBox txtSearch;
    private Button btnSearchOrRefresh;
    private CheckBox chkWorkshop;
    private Button btnPrevPage;
    private Button btnNextPage;
    private Label lblPage;

    private ListBox lstLiveries;
    private PictureBox picPreview;
    private Label lblPreviewInfo;
    private Label lblStatus;

    private Button btn3D;
    private Button btnDownloadWs;
    private Button btnDeleteWs;
    private Button btnInstallFolder;
    private Button btnInstallZip;
    private Button btnDelete;
    private Button btnOpenDir;
    private Button btnOpenItemDir;

    private List<LiveryEntry> currentEntries = new List<LiveryEntry>();
    private List<LiveryEntry> filteredEntries = new List<LiveryEntry>();
    private List<WorkshopCatalogItem> catalogItems = new List<WorkshopCatalogItem>();
    private readonly string[] aircrafts = new string[] { "Все самолеты", "Не определен", "FA-26B", "F-45A", "AV-42C", "AH-94", "T-55", "EF-24G" };

    public LiveryManagerForm(string root)
    {
        this.gameRoot = root;
        this.Text = "VTOL QoL — Менеджер ливрей, 3D-просмотр и Steam Workshop";
        DarkWindowHelper.Apply(this);
        this.Size = new Size(920, 585);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.BackColor = Color.FromArgb(18, 22, 20);
        this.ForeColor = Color.FromArgb(0, 255, 102);
        this.Font = new Font("Consolas", 9.5f, FontStyle.Regular);

        // Вкладки сверху
        btnTabMy = new Button() { Text = "📂 МОИ ЛИВРЕИ (LOCAL / STEAM)", Left = 20, Top = 12, Width = 230, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 80), ForeColor = Color.White, Font = new Font("Consolas", 9f, FontStyle.Bold) };
        btnTabMy.Click += (s, e) => SwitchMode(false);
        this.Controls.Add(btnTabMy);

        btnTabCatalog = new Button() { Text = "🌐 КАТАЛОГ STEAM WORKSHOP", Left = 255, Top = 12, Width = 230, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(32, 40, 36), ForeColor = Color.LightGray, Font = new Font("Consolas", 9f, FontStyle.Bold) };
        btnTabCatalog.Click += (s, e) => SwitchMode(true);
        this.Controls.Add(btnTabCatalog);

        cmbAircraft = new ComboBox();
        cmbAircraft.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbAircraft.Location = new Point(20, 50);
        cmbAircraft.Size = new Size(165, 26);
        cmbAircraft.BackColor = Color.FromArgb(30, 36, 33);
        cmbAircraft.ForeColor = Color.Cyan;
        foreach (string ac in aircrafts) cmbAircraft.Items.Add(ac);
        cmbAircraft.SelectedIndex = 0;
        cmbAircraft.SelectedIndexChanged += (s, e) => { if (isCatalogMode) { catalogPage = 1; SearchCatalogAsync(); } else LoadLiveries(); };
        this.Controls.Add(cmbAircraft);

        txtSearch = new TextBox() { Left = 192, Top = 50, Width = 185, Height = 25, BackColor = Color.FromArgb(30, 36, 33), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        txtSearch.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; if (isCatalogMode) { catalogPage = 1; SearchCatalogAsync(); } else LoadLiveries(); } };
        this.Controls.Add(txtSearch);

        btnSearchOrRefresh = new Button() { Text = "Поиск / Обн.", Left = 383, Top = 49, Width = 102, Height = 27, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(40, 55, 48), ForeColor = Color.White };
        btnSearchOrRefresh.Click += (s, e) => { if (isCatalogMode) { catalogPage = 1; SearchCatalogAsync(); } else LoadLiveries(); };
        this.Controls.Add(btnSearchOrRefresh);

        chkWorkshop = new CheckBox() { Text = "Показывать скачанный Workshop", Left = 20, Top = 82, AutoSize = true, ForeColor = Color.FromArgb(210, 240, 220), Checked = true };
        chkWorkshop.CheckedChanged += (s, e) => LoadLiveries();
        this.Controls.Add(chkWorkshop);

        btnPrevPage = new Button() { Text = "< Пред.", Left = 20, Top = 80, Width = 80, Height = 24, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(35, 45, 40), ForeColor = Color.White, Visible = false };
        btnPrevPage.Click += (s, e) => { if (catalogPage > 1) { catalogPage--; SearchCatalogAsync(); } };
        this.Controls.Add(btnPrevPage);

        lblPage = new Label() { Text = "Стр. 1", Left = 108, Top = 84, Width = 70, ForeColor = Color.Cyan, Visible = false };
        this.Controls.Add(lblPage);

        btnNextPage = new Button() { Text = "След. >", Left = 180, Top = 80, Width = 80, Height = 24, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(35, 45, 40), ForeColor = Color.White, Visible = false };
        btnNextPage.Click += (s, e) => { catalogPage++; SearchCatalogAsync(); };
        this.Controls.Add(btnNextPage);

        lstLiveries = new ListBox();
        lstLiveries.Location = new Point(20, 110);
        lstLiveries.Size = new Size(465, 375);
        lstLiveries.BackColor = Color.FromArgb(24, 28, 26);
        lstLiveries.ForeColor = Color.White;
        lstLiveries.BorderStyle = BorderStyle.FixedSingle;
        lstLiveries.SelectedIndexChanged += (s, e) => UpdatePreview();
        this.Controls.Add(lstLiveries);
        lstLiveries.DrawMode = DrawMode.OwnerDrawFixed;
        lstLiveries.ItemHeight = 18;
        lstLiveries.DrawItem += (s, e) => {
            if (e.Index < 0 || e.Index >= lstLiveries.Items.Count) return;
            string text = lstLiveries.Items[e.Index].ToString();
            bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            bool isInstalled = text.StartsWith("✔ ");

            Color bg = isSelected ? Color.FromArgb(0, 95, 135) : Color.FromArgb(24, 28, 26);
            Color fg = isInstalled ? Color.FromArgb(0, 255, 102) : Color.White;

            using (SolidBrush bgBrush = new SolidBrush(bg))
                e.Graphics.FillRectangle(bgBrush, e.Bounds);
            using (SolidBrush fgBrush = new SolidBrush(fg))
                e.Graphics.DrawString(text, e.Font, fgBrush, e.Bounds.Left + 4, e.Bounds.Top + 1);
        };

        ContextMenuStrip ctxLivery = new ContextMenuStrip();
        ToolStripMenuItem item3D = new ToolStripMenuItem("🎮 Открыть в 3D-просмотре");
        item3D.Click += Btn3D_Click;
        ctxLivery.Items.Add(item3D);

        ToolStripMenuItem itemAssignMenu = new ToolStripMenuItem("✈ Назначить борт вручную");
        string[] assignPlanes = new string[] { "FA-26B", "F-45A", "AV-42C", "AH-94", "T-55", "EF-24G" };
        foreach (string pl in assignPlanes)
        {
            string targetPlane = pl;
            ToolStripMenuItem sub = new ToolStripMenuItem(targetPlane);
            sub.Click += (s, e) => {
                if (isCatalogMode) return;
                LiveryEntry entry = GetSelectedEntry();
                if (entry == null) return;
                entry.Aircraft = targetPlane;
                LiveryParser.SaveUserAircraftMapping(entry.FolderPath, entry.Aircraft);
                int idx = lstLiveries.SelectedIndex;
                if (idx >= 0 && idx < filteredEntries.Count)
                {
                    lstLiveries.Items[idx] = string.Format("[{0}] {1}  ({2})", entry.Source, entry.Name, entry.Aircraft);
                    UpdatePreview();
                }
            };
            itemAssignMenu.DropDownItems.Add(sub);
        }
        ctxLivery.Items.Add(itemAssignMenu);

        ToolStripMenuItem itemFolder = new ToolStripMenuItem("📁 Открыть папку мода");
        itemFolder.Click += (s, e) => {
            LiveryEntry entry = GetSelectedEntry();
            if (entry != null && Directory.Exists(entry.FolderPath)) Process.Start("explorer.exe", entry.FolderPath);
        };
        ctxLivery.Items.Add(itemFolder);

        lstLiveries.MouseDown += (s, e) => {
            if (e.Button == MouseButtons.Right)
            {
                int idx = lstLiveries.IndexFromPoint(e.Location);
                if (idx >= 0) lstLiveries.SelectedIndex = idx;
            }
        };
        lstLiveries.ContextMenuStrip = ctxLivery;

        Label lblPrevTitle = new Label() { Text = "ПРЕВЬЮ ЛИВРЕИ:", Left = 500, Top = 15, AutoSize = true, Font = new Font("Consolas", 9.5f, FontStyle.Bold) };
        this.Controls.Add(lblPrevTitle);

        picPreview = new PictureBox();
        picPreview.Location = new Point(500, 38);
        picPreview.Size = new Size(390, 230);
        picPreview.BackColor = Color.FromArgb(24, 28, 26);
        picPreview.BorderStyle = BorderStyle.FixedSingle;
        picPreview.SizeMode = PictureBoxSizeMode.Zoom;
        this.Controls.Add(picPreview);

        lblPreviewInfo = new Label() { Left = 500, Top = 274, Width = 390, Height = 20, ForeColor = Color.Cyan, Text = "Выберите ливрею из списка" };
        this.Controls.Add(lblPreviewInfo);

        int top = 302, btnW = 190, btnH = 36, gap = 10;
        btn3D = CreateBtn("🎮 3D-просмотр", 500, ref top, btnW, btnH, gap, Color.FromArgb(70, 50, 110));
        btn3D.Click += Btn3D_Click;

        btnDownloadWs = new Button() { Text = "⬇ Скачать ливрею", Location = new Point(500, 302), Size = new Size(390, 42), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 130, 80), ForeColor = Color.White, Font = new Font("Consolas", 10f, FontStyle.Bold), Visible = false };
        btnDownloadWs.Click += BtnDownloadWs_Click;
        this.Controls.Add(btnDownloadWs);

        btnDeleteWs = new Button() { Text = "🗑 Удалить установленную ливрею", Location = new Point(500, 352), Size = new Size(390, 36), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(140, 35, 35), ForeColor = Color.White, Font = new Font("Consolas", 9.5f, FontStyle.Bold), Visible = false };
        btnDeleteWs.Click += BtnDeleteWs_Click;
        this.Controls.Add(btnDeleteWs);

        btnInstallFolder = CreateBtn("+ Добавить скин", 500, ref top, btnW, btnH, gap, Color.FromArgb(0, 110, 140));
        btnInstallFolder.Click += BtnInstallFolder_Click;

        btnInstallZip = CreateBtn("+ Установить .zip", 500, ref top, btnW, btnH, gap, Color.FromArgb(0, 130, 90));
        btnInstallZip.Click += BtnInstallZip_Click;

        int topRight = 302;
        btnDelete = CreateBtn("Удалить ливрею", 700, ref topRight, btnW, btnH, gap, Color.FromArgb(140, 35, 35));
        btnDelete.Click += BtnDelete_Click;

        btnOpenDir = CreateBtn("Папка CustomLiveries", 700, ref topRight, btnW, btnH, gap, Color.FromArgb(45, 55, 50));
        btnOpenDir.Click += (s, e) => {
            string dir = GetLiveriesRootDir();
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            Process.Start("explorer.exe", dir);
        };

        btnOpenItemDir = CreateBtn("Открыть папку мода", 700, ref topRight, btnW, btnH, gap, Color.FromArgb(45, 55, 50));
        btnOpenItemDir.Click += (s, e) => {
            LiveryEntry entry = GetSelectedEntry();
            if (entry != null && Directory.Exists(entry.FolderPath)) Process.Start("explorer.exe", entry.FolderPath);
        };

        lblStatus = new Label() { Left = 20, Top = 496, Width = 870, Height = 22, Text = "", ForeColor = Color.Silver };
        this.Controls.Add(lblStatus);

        LoadLiveries();
    }

    private Button CreateBtn(string text, int left, ref int top, int width, int height, int gap, Color backColor)
    {
        Button btn = new Button() { Text = text, Location = new Point(left, top), Size = new Size(width, height), FlatStyle = FlatStyle.Flat, BackColor = backColor, ForeColor = Color.White };
        this.Controls.Add(btn);
        top += height + gap;
        return btn;
    }

    private void SwitchMode(bool catalog)
    {
        catalogSearchGen++;
        ShowCatalogLoader(false);
        isCatalogMode = catalog;
        btnTabMy.BackColor = !catalog ? Color.FromArgb(0, 120, 80) : Color.FromArgb(32, 40, 36);
        btnTabMy.ForeColor = !catalog ? Color.White : Color.LightGray;
        btnTabCatalog.BackColor = catalog ? Color.FromArgb(0, 110, 150) : Color.FromArgb(32, 40, 36);
        btnTabCatalog.ForeColor = catalog ? Color.White : Color.LightGray;

        chkWorkshop.Visible = !catalog;
        btnPrevPage.Visible = catalog;
        lblPage.Visible = catalog;
        btnNextPage.Visible = catalog;

        btn3D.Visible = !catalog;
        btnInstallFolder.Visible = !catalog;
        btnInstallZip.Visible = !catalog;
        btnDelete.Visible = !catalog;
        btnOpenDir.Visible = !catalog;
        btnOpenItemDir.Visible = !catalog;
        btnDownloadWs.Visible = catalog;
        if (!catalog && btnDeleteWs != null) btnDeleteWs.Visible = false;

        if (catalog) { catalogPage = 1; SearchCatalogAsync(); }
        else LoadLiveries();
    }
    private Panel pnlCatalogLoader = null;
    private System.Windows.Forms.Timer catalogAnimTimer = null;
    private int catalogSearchGen = 0;

    private void ShowCatalogLoader(bool show)
    {
        if (catalogAnimTimer != null)
        {
            catalogAnimTimer.Stop();
            catalogAnimTimer.Dispose();
            catalogAnimTimer = null;
        }
        if (pnlCatalogLoader != null)
        {
            if (pnlCatalogLoader.Parent != null) this.Controls.Remove(pnlCatalogLoader);
            pnlCatalogLoader.Dispose();
            pnlCatalogLoader = null;
        }
        if (!show) return;

        pnlCatalogLoader = new Panel();
        pnlCatalogLoader.Size = new Size(340, 78);
        pnlCatalogLoader.Location = new Point(lstLiveries.Left + (lstLiveries.Width - 340) / 2, lstLiveries.Top + (lstLiveries.Height - 78) / 2);
        pnlCatalogLoader.BackColor = Color.FromArgb(28, 36, 33);
        pnlCatalogLoader.BorderStyle = BorderStyle.FixedSingle;

        Label lblLoader = new Label();
        lblLoader.Text = "⏳ Загрузка каталога Steam Workshop...";
        lblLoader.Location = new Point(12, 14);
        lblLoader.Width = 316;
        lblLoader.Height = 22;
        lblLoader.TextAlign = ContentAlignment.MiddleCenter;
        lblLoader.ForeColor = Color.Cyan;
        lblLoader.Font = new Font("Consolas", 9.5f, FontStyle.Bold);
        pnlCatalogLoader.Controls.Add(lblLoader);

        ProgressBar pb = new ProgressBar();
        pb.Style = ProgressBarStyle.Marquee;
        pb.MarqueeAnimationSpeed = 25;
        pb.Location = new Point(20, 44);
        pb.Size = new Size(300, 16);
        pnlCatalogLoader.Controls.Add(pb);

        this.Controls.Add(pnlCatalogLoader);
        pnlCatalogLoader.BringToFront();

        int elapsedSec = 0;
        catalogAnimTimer = new System.Windows.Forms.Timer();
        catalogAnimTimer.Interval = 1000;
        catalogAnimTimer.Tick += (s, e) => {
            elapsedSec++;
            lblLoader.Text = string.Format("⏳ Загрузка Steam Workshop... ({0} сек)", elapsedSec);
        };
        catalogAnimTimer.Start();
    }

    private void SearchCatalogAsync()
    {
        int currentGen = ++catalogSearchGen;
        lstLiveries.Items.Clear();
        catalogItems.Clear();
        if (picPreview.Image != null) { picPreview.Image.Dispose(); picPreview.Image = null; }
        lblPage.Text = "Стр. " + catalogPage;
        string ac = cmbAircraft.SelectedItem != null ? cmbAircraft.SelectedItem.ToString() : "Все самолеты";
        string q = txtSearch.Text.Trim();
        lblStatus.Text = "Загрузка каталога Steam Workshop...";
        ShowCatalogLoader(true);

        ThreadPool.QueueUserWorkItem(delegate {
            try
            {
                List<WorkshopCatalogItem> found = WorkshopBrowser.SearchLiveries(q, ac, catalogPage);
                this.Invoke(new Action(delegate {
                    if (currentGen != catalogSearchGen || !isCatalogMode) return;
                    ShowCatalogLoader(false);
                    catalogItems = found;
                    lstLiveries.Items.Clear();
                    string wsContentDir = !string.IsNullOrEmpty(gameRoot)
    ? Path.Combine(Path.GetFullPath(Path.Combine(gameRoot, @"..\..\..")), @"steamapps\workshop\content\667970")
    : "";

                    foreach (WorkshopCatalogItem it in catalogItems)
                    {
                        bool alreadyInstalled = !string.IsNullOrEmpty(wsContentDir) && Directory.Exists(Path.Combine(wsContentDir, it.PublishedFileId));
                        string prefix = alreadyInstalled ? "✔ " : "";
                        string suffix = alreadyInstalled ? "  [УСТАНОВЛЕНО]" : "";
                        lstLiveries.Items.Add(string.Format("{0}[WS #{1}] {2}{3}", prefix, it.PublishedFileId, it.Title, suffix));
                    }
                    lblStatus.Text = "Найдено в Steam Workshop на странице " + catalogPage + ": " + catalogItems.Count;
                }));
            }
            catch (Exception ex)
            {
                AppLogger.Error("WORKSHOP", "SearchCatalogAsync", ex);
                try
                {
                    this.Invoke(new Action(delegate {
                        if (currentGen != catalogSearchGen) return;
                        ShowCatalogLoader(false);
                        lblStatus.Text = "Ошибка загрузки каталога: " + ex.Message;
                    }));
                }
                catch { }
            }
        });
    }


    private bool isWaitingDownload = false;

    private void BtnDownloadWs_Click(object sender, EventArgs e)
    {
        if (isWaitingDownload)
        {
            WorkshopBrowser.CancelWait = true;
            return;
        }

        if (lstLiveries.SelectedIndex < 0 || lstLiveries.SelectedIndex >= catalogItems.Count)
        {
            MessageBox.Show("Выберите ливрею из каталога.", "Внимание", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        WorkshopCatalogItem item = catalogItems[lstLiveries.SelectedIndex];
        string existingFolder = GetWorkshopItemFolder(item.PublishedFileId);
        if (!string.IsNullOrEmpty(existingFolder) && Directory.Exists(existingFolder))
        {
            OpenCatalogItemIn3D(item, existingFolder);
            return;
        }
        isWaitingDownload = true;
        btnDownloadWs.BackColor = Color.FromArgb(140, 95, 20);
        btnDownloadWs.Text = "⏳ Подписка... (нажмите для отмены)";

        ProgressBar pbWs = new ProgressBar();
        pbWs.Style = ProgressBarStyle.Continuous;
        pbWs.Minimum = 0;
        pbWs.Maximum = 100;
        pbWs.Value = 5;
        pbWs.Location = new Point(500, 396);
        pbWs.Size = new Size(390, 16);
        this.Controls.Add(pbWs);

        ThreadPool.QueueUserWorkItem(delegate {
            bool downloaded = false;
            string errMsg = null;
            try
            {
                downloaded = WorkshopBrowser.SubscribeAndDownloadWorkshop(item, gameRoot, (pct, msg) => {
                    try
                    {
                        this.Invoke(new Action(delegate {
                            if (isWaitingDownload)
                            {
                                pbWs.Value = Math.Max(0, Math.Min(100, pct));
                                lblStatus.Text = msg;
                                btnDownloadWs.Text = "⏳ " + msg;
                            }
                        }));
                    }
                    catch { }
                });
            }
            catch (Exception ex)
            {
                errMsg = ex.Message;
                AppLogger.Error("WORKSHOP", "BtnDownloadWs_Click", ex);
            }

            try
            {
                this.Invoke(new Action(delegate {
                    isWaitingDownload = false;
                    if (pbWs.Parent != null) this.Controls.Remove(pbWs);
                    pbWs.Dispose();
                    btnDownloadWs.BackColor = Color.FromArgb(0, 130, 80);
                    btnDownloadWs.Text = "⬇ Скачать ливрею";

                    if (downloaded)
                    {
                        lblStatus.Text = "✔ Ливрея \"" + item.Title + "\" скачана и установлена!";
                        int selIdx = lstLiveries.SelectedIndex;
                        if (selIdx >= 0 && selIdx < lstLiveries.Items.Count)
                        {
                            string curText = lstLiveries.Items[selIdx].ToString();
                            if (!curText.StartsWith("✔ "))
                                lstLiveries.Items[selIdx] = "✔ " + curText + "  [УСТАНОВЛЕНО]";
                        }
                        SetCatalogPrimaryButtonMode(true);
                        if (btnDeleteWs != null) btnDeleteWs.Visible = true;
                    }
                    else if (!string.IsNullOrEmpty(errMsg))
                    {
                        lblStatus.Text = "Ошибка: " + errMsg;
                    }
                    else
                    {
                        lblStatus.Text = "Загрузка отменена или превышено время ожидания.";
                    }
                }));
            }
            catch { }
        });
    }

    private Label lblPreviewLoader = null;
    private int previewLoadGen = 0;

    private void SetPreviewLoaderText(string text)
    {
        if (lblPreviewLoader == null)
        {
            lblPreviewLoader = new Label();
            lblPreviewLoader.Size = new Size(picPreview.Width - 20, 32);
            lblPreviewLoader.Location = new Point(picPreview.Left + 10, picPreview.Top + (picPreview.Height - 32) / 2);
            lblPreviewLoader.BackColor = Color.FromArgb(24, 28, 26);
            lblPreviewLoader.ForeColor = Color.Cyan;
            lblPreviewLoader.TextAlign = ContentAlignment.MiddleCenter;
            lblPreviewLoader.Font = new Font("Consolas", 9.5f, FontStyle.Bold);
            this.Controls.Add(lblPreviewLoader);
        }

        if (string.IsNullOrEmpty(text))
        {
            lblPreviewLoader.Visible = false;
        }
        else
        {
            lblPreviewLoader.Text = text;
            lblPreviewLoader.Visible = true;
            lblPreviewLoader.BringToFront();
        }
    }

    private string GetWorkshopItemFolder(string publishedFileId)
    {
        if (string.IsNullOrEmpty(gameRoot) || string.IsNullOrEmpty(publishedFileId)) return "";
        string steamLibRoot = Path.GetFullPath(Path.Combine(gameRoot, @"..\..\.."));
        return Path.Combine(steamLibRoot, @"steamapps\workshop\content\667970", publishedFileId);
    }

    private void BtnDeleteWs_Click(object sender, EventArgs e)
    {
        if (!isCatalogMode || lstLiveries.SelectedIndex < 0 || lstLiveries.SelectedIndex >= catalogItems.Count) return;
        WorkshopCatalogItem item = catalogItems[lstLiveries.SelectedIndex];
        string folderPath = GetWorkshopItemFolder(item.PublishedFileId);

        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
        {
            if (btnDeleteWs != null) btnDeleteWs.Visible = false;
            return;
        }

        if (MessageBox.Show("Отписаться и удалить Workshop-ливрею \"" + item.Title + "\"?",
            "Подтверждение удаления", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            try
            {
                WorkshopBrowser.UnsubscribeAndDeleteWorkshop(folderPath, gameRoot);
                int selIdx = lstLiveries.SelectedIndex;
                if (selIdx >= 0 && selIdx < lstLiveries.Items.Count)
                {
                    lstLiveries.Items[selIdx] = string.Format("[WS #{0}] {1}", item.PublishedFileId, item.Title);
                }
                SetCatalogPrimaryButtonMode(false);
                if (btnDeleteWs != null) btnDeleteWs.Visible = false;
                lblStatus.Text = "Ливрея \"" + item.Title + "\" удалена из Steam Workshop.";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка удаления: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void SetCatalogPrimaryButtonMode(bool isInstalled)
    {
        if (btnDownloadWs == null) return;
        if (isInstalled)
        {
            btnDownloadWs.Text = "🎮 3D-просмотр";
            btnDownloadWs.BackColor = Color.FromArgb(70, 50, 110);
        }
        else
        {
            btnDownloadWs.Text = "⬇ Скачать ливрею";
            btnDownloadWs.BackColor = Color.FromArgb(0, 130, 80);
        }
    }

    private void OpenCatalogItemIn3D(WorkshopCatalogItem item, string folderPath)
    {
        try
        {
            LiveryEntry parsed = LiveryParser.ParseFolder(folderPath, "Workshop");
            if (string.IsNullOrEmpty(parsed.Name) || parsed.Name == item.PublishedFileId)
                parsed.Name = item.Title;
            if (parsed.Aircraft == "Не определен" && !string.IsNullOrEmpty(item.AircraftTag) && item.AircraftTag != "Не определен")
                parsed.Aircraft = item.AircraftTag;

            using (Livery3DViewerForm viewer = new Livery3DViewerForm(parsed))
                viewer.ShowDialog(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Ошибка открытия 3D-просмотра: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void UpdatePreview()
    {
        int currentPrevGen = ++previewLoadGen;
        if (isWaitingDownload) WorkshopBrowser.CancelWait = true;
        if (picPreview.Image != null) { picPreview.Image.Dispose(); picPreview.Image = null; }
        SetPreviewLoaderText(null);

        if (isCatalogMode)
        {
            if (lstLiveries.SelectedIndex < 0 || lstLiveries.SelectedIndex >= catalogItems.Count)
            {
                lblPreviewInfo.Text = "Выберите скин из каталога";
                SetCatalogPrimaryButtonMode(false);
                if (btnDeleteWs != null) btnDeleteWs.Visible = false;
                return;
            }
            WorkshopCatalogItem item = catalogItems[lstLiveries.SelectedIndex];
            lblPreviewInfo.Text = item.Title + " (ID: " + item.PublishedFileId + ")";

            string wsFolder = GetWorkshopItemFolder(item.PublishedFileId);
            bool isInstalled = (!string.IsNullOrEmpty(wsFolder) && Directory.Exists(wsFolder));
            SetCatalogPrimaryButtonMode(isInstalled);
            if (btnDeleteWs != null)
                btnDeleteWs.Visible = isInstalled;

            if (string.IsNullOrEmpty(item.PreviewUrl))
            {
                SetPreviewLoaderText("Превью отсутствует");
                return;
            }

            SetPreviewLoaderText("⏳ Загрузка превью...");
            string url = item.PreviewUrl;
            ThreadPool.QueueUserWorkItem(delegate {
                try
                {
                    WorkshopBrowser.EnableTls12();
                    using (System.Net.WebClient wc = new System.Net.WebClient())
                    {
                        byte[] data = wc.DownloadData(url);
                        this.Invoke(new Action(delegate {
                            if (currentPrevGen != previewLoadGen || !isCatalogMode) return;
                            SetPreviewLoaderText(null);
                            using (MemoryStream ms = new MemoryStream(data))
                            using (Image tmpImg = Image.FromStream(ms))
                                picPreview.Image = new Bitmap(tmpImg);
                        }));
                    }
                }
                catch
                {
                    try
                    {
                        this.Invoke(new Action(delegate {
                            if (currentPrevGen == previewLoadGen && isCatalogMode)
                                SetPreviewLoaderText("Не удалось загрузить превью");
                        }));
                    }
                    catch { }
                }
            });
            return;
        }

        LiveryEntry entry = GetSelectedEntry();
        if (entry == null) { lblPreviewInfo.Text = "Выберите ливрею (ПКМ — назначить борт)"; return; }

        lblPreviewInfo.Text = entry.Name + " (" + entry.Aircraft + ")";
        if (!string.IsNullOrEmpty(entry.PreviewPath) && File.Exists(entry.PreviewPath))
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(entry.PreviewPath);
                using (MemoryStream ms = new MemoryStream(bytes))
                using (Image tmpImg = Image.FromStream(ms))
                    picPreview.Image = new Bitmap(tmpImg);
            }
            catch { SetPreviewLoaderText("Ошибка чтения превью"); }
        }
        else
        {
            SetPreviewLoaderText("Превью отсутствует");
        }
    }


    private string GetLiveriesRootDir()
    {
        if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL", "CustomLiveries");
        return Path.Combine(gameRoot, "CustomLiveries");
    }

    private void LoadLiveries()
    {
        lstLiveries.Items.Clear();
        currentEntries.Clear();
        string dir = GetLiveriesRootDir();
        string filterAc = cmbAircraft.SelectedItem != null ? cmbAircraft.SelectedItem.ToString() : "Все самолеты";
        string searchQ = txtSearch != null ? txtSearch.Text.Trim() : "";

        List<LiveryEntry> local = LiveryParser.ScanLocal(dir);
        currentEntries.AddRange(local);

        if (chkWorkshop.Checked && !string.IsNullOrEmpty(gameRoot))
        {
            string steamLibRoot = Path.GetFullPath(Path.Combine(gameRoot, @"..\..\.."));
            List<LiveryEntry> ws = LiveryParser.ScanWorkshop(steamLibRoot);
            currentEntries.AddRange(ws);
        }

        filteredEntries.Clear();
        foreach (LiveryEntry e in currentEntries)
        {
            bool matchAc = (filterAc == "Все самолеты" || e.Aircraft.Equals(filterAc, StringComparison.OrdinalIgnoreCase));
            bool matchText = (string.IsNullOrEmpty(searchQ) || e.Name.IndexOf(searchQ, StringComparison.OrdinalIgnoreCase) >= 0);
            if (matchAc && matchText)
            {
                filteredEntries.Add(e);
                string mark = e.Valid ? "" : " [!невалидна]";
                lstLiveries.Items.Add(string.Format("[{0}] {1}  ({2}){3}", e.Source, e.Name, e.Aircraft, mark));
            }
        }
        lblStatus.Text = string.Format("Путь: {0} | Найдено: {1}", dir, lstLiveries.Items.Count);
        UpdatePreview();
    }

    private LiveryEntry GetSelectedEntry()
    {
        if (lstLiveries.SelectedIndex < 0 || lstLiveries.SelectedIndex >= filteredEntries.Count) return null;
        return filteredEntries[lstLiveries.SelectedIndex];
    }

    private void Btn3D_Click(object sender, EventArgs e)
    {
        LiveryEntry entry = GetSelectedEntry();
        if (entry == null) { MessageBox.Show("Выберите ливрею для просмотра.", "Внимание", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        try
        {
            using (Livery3DViewerForm viewer = new Livery3DViewerForm(entry)) { viewer.ShowDialog(this); }
            int idx = lstLiveries.SelectedIndex;
            if (idx >= 0 && idx < filteredEntries.Count)
            {
                string mark = entry.Valid ? "" : " [!невалидна]";
                lstLiveries.Items[idx] = string.Format("[{0}] {1}  ({2}){3}", entry.Source, entry.Name, entry.Aircraft, mark);
                UpdatePreview();
            }
        }
        catch (Exception ex) { MessageBox.Show("Ошибка открытия 3D-просмотра: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void BtnInstallFolder_Click(object sender, EventArgs e)
    {
        using (FolderBrowserDialog fbd = new FolderBrowserDialog())
        {
            fbd.Description = "Выберите папку со скином (livery.cfg/*.vtl и текстуры)";
            if (fbd.ShowDialog() == DialogResult.OK)
            {
                string src = fbd.SelectedPath;
                string folderName = Path.GetFileName(src);
                string dst = Path.Combine(GetLiveriesRootDir(), folderName);
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
                    MessageBox.Show("Ливрея \"" + folderName + "\" добавлена!", "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { MessageBox.Show("Ошибка установки скина: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }
    }
    private void BtnInstallZip_Click(object sender, EventArgs e)
    {
        using (OpenFileDialog ofd = new OpenFileDialog() { Filter = "ZIP-архив (.zip)|.zip" })
        {
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                if (LiveryParser.InstallZip(ofd.FileName, GetLiveriesRootDir()))
                {
                    LoadLiveries();
                    MessageBox.Show("Архив успешно распакован и установлен!", "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else MessageBox.Show("Не удалось распаковать архив.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
    private void BtnDelete_Click(object sender, EventArgs e)
    {
        LiveryEntry entry = GetSelectedEntry();
        if (entry == null) { MessageBox.Show("Выберите ливрею для удаления.", "Внимание", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        string msg = entry.Source == "Workshop"
            ? "Отписаться и удалить Workshop-ливрею \"" + entry.Name + "\"?"
            : "Удалить камуфляж \"" + entry.Name + "\"?";

        if (MessageBox.Show(msg, "Подтверждение удаления", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            try
            {
                if (entry.Source == "Workshop")
                    WorkshopBrowser.UnsubscribeAndDeleteWorkshop(entry.FolderPath, gameRoot);
                else if (Directory.Exists(entry.FolderPath))
                    Directory.Delete(entry.FolderPath, true);

                LoadLiveries();
                lblStatus.Text = "Ливрея \"" + entry.Name + "\" удалена.";
            }
            catch (Exception ex) { MessageBox.Show("Ошибка удаления: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }

}

