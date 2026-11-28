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
    private Button btnLiveryInt;
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

        // Кнопка проверки целостности ливрей — правее чекбокса Workshop.
        btnLiveryInt = new Button()
        {
            Text = "Проверить целостность ливрей",
            Top = 79,
            Width = 196,
            Height = 25,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(60, 50, 40),
            ForeColor = Color.White
        };
        btnLiveryInt.Click += BtnLiveryIntegrity_Click;
        this.Controls.Add(btnLiveryInt);
        // Ставим точно справа от чекбокса (его ширина зависит от шрифта/AutoSize).
        btnLiveryInt.Left = chkWorkshop.Right + 12;

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
        btnLiveryInt.Visible = !catalog;
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
                            {
                                Image old = picPreview.Image;
                                picPreview.Image = new Bitmap(tmpImg);
                                if (old != null) old.Dispose();
                            }
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
                {
                    Image old = picPreview.Image;
                    picPreview.Image = new Bitmap(tmpImg);
                    if (old != null) old.Dispose();
                }
            }
            catch { SetPreviewLoaderText("Ошибка чтения превью"); }
        }
        else
        {
            SetPreviewLoaderText("Превью отсутствует");
        }
    }


    // =====================================================================
    // === ПРОВЕРКА ЦЕЛОСТНОСТИ ЛИВРЕЙ =====================================
    // Только ЧИТАЕТ и рапортует. Ничего не удаляет и не переименовывает,
    // чтобы не сломать ни игру, ни этот менеджер.
    // Проверяем:
    //   1) пустые папки ливрей;
    //   2) папки без текстуры (texture.*) и без описания (.vtlb / livery.cfg);
    //   3) дубликаты одного workshop-id в CustomLiveries и CustomLivery\<борт>;
    //   4) подписки Steam Workshop, у которых нет папки с содержимым
    //      (из-за них игра падает с DirectoryNotFoundException и меню ливрей висит).
    // =====================================================================
    private void BtnLiveryIntegrity_Click(object sender, EventArgs e)
    {
        Cursor = Cursors.WaitCursor;
        try
        {
            StringBuilder report = new StringBuilder();
            int problems = 0;

            List<string> liveryDirs = new List<string>();

            string customLiveries = GetLiveriesRootDir();
            if (Directory.Exists(customLiveries))
                foreach (string d in Directory.GetDirectories(customLiveries)) liveryDirs.Add(d);

            if (!string.IsNullOrEmpty(gameRoot) && Directory.Exists(gameRoot))
            {
                string perAircraft = Path.Combine(gameRoot, "CustomLivery");
                if (Directory.Exists(perAircraft))
                {
                    foreach (string acDir in Directory.GetDirectories(perAircraft))
                        foreach (string d in Directory.GetDirectories(acDir)) liveryDirs.Add(d);
                }
            }

            // 1-2: проверка содержимого каждой папки
            List<string> emptyDirs = new List<string>();
            List<string> noTexture = new List<string>();
            List<string> noDescriptor = new List<string>();
            foreach (string d in liveryDirs)
            {
                string[] files;
                try { files = Directory.GetFiles(d, "*", SearchOption.AllDirectories); }
                catch { continue; }
                if (files.Length == 0) { emptyDirs.Add(d); continue; }

                bool hasTexture = false, hasDescriptor = false;
                foreach (string f in files)
                {
                    string fn = Path.GetFileName(f).ToLowerInvariant();
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (fn.StartsWith("texture") || ext == ".jpgb" || ext == ".png" || ext == ".jpg" || ext == ".dds") hasTexture = true;
                    if (ext == ".vtlb" || fn == "livery.cfg") hasDescriptor = true;
                }
                if (!hasTexture) noTexture.Add(d);
                if (!hasDescriptor) noDescriptor.Add(d);
            }

            // 3: дубликаты по имени папки (workshop id) в двух местах
            Dictionary<string, List<string>> byName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string d in liveryDirs)
            {
                string nm = Path.GetFileName(d);
                if (!byName.ContainsKey(nm)) byName[nm] = new List<string>();
                byName[nm].Add(d);
            }
            List<string> dupes = new List<string>();
            foreach (KeyValuePair<string, List<string>> kv in byName)
                if (kv.Value.Count > 1) dupes.Add(kv.Key + "  →  " + kv.Value.Count + " копии");

            // 4: подписки воркшопа без содержимого
            List<string> missingSubs = new List<string>();
            if (!string.IsNullOrEmpty(gameRoot))
            {
                try
                {
                    string steamLibRoot = Path.GetFullPath(Path.Combine(gameRoot, @"..\..\.."));
                    string contentDir = Path.Combine(steamLibRoot, @"steamapps\workshop\content\667970");
                    string acf = Path.Combine(steamLibRoot, @"steamapps\workshop\appworkshop_667970.acf");
                    if (File.Exists(acf))
                    {
                        string text = File.ReadAllText(acf);
                        MatchCollection ms = Regex.Matches(text, "\"(\\d{9,11})\"\\s*[\\r\\n]+\\s*\\{");
                        HashSet<string> seen = new HashSet<string>();
                        foreach (Match m in ms)
                        {
                            string id = m.Groups[1].Value;
                            if (!seen.Add(id)) continue;
                            if (!Directory.Exists(Path.Combine(contentDir, id))) missingSubs.Add(id);
                        }
                    }
                }
                catch { }
            }

            if (emptyDirs.Count > 0)
            {
                problems += emptyDirs.Count;
                report.AppendLine("ПУСТЫЕ ПАПКИ ЛИВРЕЙ (" + emptyDirs.Count + "):");
                foreach (string d in emptyDirs) report.AppendLine("   " + RelPath(d));
                report.AppendLine();
            }
            if (noTexture.Count > 0)
            {
                problems += noTexture.Count;
                report.AppendLine("БЕЗ ТЕКСТУРЫ (" + noTexture.Count + "):");
                foreach (string d in noTexture) report.AppendLine("   " + RelPath(d));
                report.AppendLine();
            }
            if (noDescriptor.Count > 0)
            {
                problems += noDescriptor.Count;
                report.AppendLine("БЕЗ ОПИСАНИЯ .vtlb/livery.cfg (" + noDescriptor.Count + "):");
                foreach (string d in noDescriptor) report.AppendLine("   " + RelPath(d));
                report.AppendLine();
            }
            if (dupes.Count > 0)
            {
                problems += dupes.Count;
                report.AppendLine("ДУБЛИКАТЫ (" + dupes.Count + "):");
                foreach (string s in dupes) report.AppendLine("   " + s);
                report.AppendLine();
            }
            if (missingSubs.Count > 0)
            {
                problems += missingSubs.Count;
                report.AppendLine("ПОДПИСКИ WORKSHOP БЕЗ ФАЙЛОВ (" + missingSubs.Count + ") — из-за них меню ливрей в игре может висеть:");
                foreach (string id in missingSubs)
                    report.AppendLine("   " + id + "   steam://url/CommunityFilePage/" + id);
                report.AppendLine();
            }

            report.AppendLine("Проверено папок ливрей: " + liveryDirs.Count);
            report.AppendLine("Подписок Workshop в манифесте проверено: см. выше");
            if (problems == 0) report.AppendLine("ПРОБЛЕМ НЕ НАЙДЕНО.");

            string outPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "livery_integrity.txt");
            try { File.WriteAllText(outPath, report.ToString(), Encoding.UTF8); } catch { outPath = null; }

            string head = problems == 0
                ? "Проверка целостности ливрей: проблем не найдено.\n\nПапок ливрей проверено: " + liveryDirs.Count
                : "Проверка целостности ливрей: найдено проблем — " + problems + ".\n\n" + FirstLines(report.ToString(), 18);

            if (outPath != null) head += "\n\nПолный отчёт: " + outPath;

            MessageBox.Show(this, head, "Целостность ливрей", MessageBoxButtons.OK,
                problems == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

            lblStatus.Text = problems == 0 ? "Ливреи: проблем не найдено" : ("Ливреи: найдено проблем — " + problems);

            // === Автопочинка (только с согласия пользователя) ===
            if (problems > 0) OfferAutoFix(emptyDirs, byName, noTexture, noDescriptor, missingSubs);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось выполнить проверку: " + ex.Message, "Целостность ливрей", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { Cursor = Cursors.Default; }
    }

    // Автопочинка ливрей по согласию пользователя.
    // Ничего не делается без нажатия «Исправить» и без включённой галочки.
    private void OfferAutoFix(List<string> emptyDirs, Dictionary<string, List<string>> byName,
                              List<string> noTexture, List<string> noDescriptor, List<string> missingSubs)
    {
        List<string> dupeKeys = new List<string>();
        foreach (KeyValuePair<string, List<string>> kv in byName)
            if (kv.Value.Count > 1) dupeKeys.Add(kv.Key);

        List<string> brokenDirs = new List<string>();
        HashSet<string> seenBroken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string d in noTexture) if (seenBroken.Add(d)) brokenDirs.Add(d);
        foreach (string d in noDescriptor) if (seenBroken.Add(d)) brokenDirs.Add(d);

        List<LiveryFixOption> options = new List<LiveryFixOption>();

        if (missingSubs.Count > 0)
            options.Add(new LiveryFixOption
            {
                Key = "subs",
                Title = "Отписаться в Steam Workshop от подписок без файлов (лечит зависание меню ливрей в игре)",
                Count = missingSubs.Count,
                Checked = true
            });

        if (emptyDirs.Count > 0)
            options.Add(new LiveryFixOption
            {
                Key = "empty",
                Title = "Удалить пустые папки ливрей",
                Count = emptyDirs.Count,
                Checked = true
            });

        if (brokenDirs.Count > 0)
            options.Add(new LiveryFixOption
            {
                Key = "broken",
                Title = "Удалить ливреи без текстуры или без описания (.vtlb / livery.cfg)",
                Count = brokenDirs.Count,
                Checked = false
            });

        if (dupeKeys.Count > 0)
            options.Add(new LiveryFixOption
            {
                Key = "dupe",
                Title = "Удалить дубликаты (оставить копию в CustomLiveries)",
                Count = dupeKeys.Count,
                Checked = false
            });

        if (options.Count == 0) return;

        List<string> accepted;
        using (LiveryFixForm dlg = new LiveryFixForm(options))
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            accepted = new List<string>(dlg.AcceptedKeys);
        }
        if (accepted.Count == 0) return;

        Cursor = Cursors.WaitCursor;
        StringBuilder log = new StringBuilder();
        int done = 0, failed = 0;

        try
        {
            if (accepted.Contains("subs"))
            {
                foreach (string id in missingSubs)
                {
                    bool ok = false;
                    try { ok = WorkshopBrowser.UnsubscribeById(id, gameRoot); }
                    catch (Exception ex) { AppLogger.Error("LIVERY", "Отписка " + id, ex); }
                    if (ok) { done++; log.AppendLine("Отписка от Workshop: " + id + " — ок"); }
                    else { failed++; log.AppendLine("Отписка от Workshop: " + id + " — НЕ удалось (закрой VTOL VR / Steam должен быть запущен)"); }
                }
            }

            if (accepted.Contains("empty"))
            {
                foreach (string d in emptyDirs)
                {
                    try { Directory.Delete(d, true); done++; log.AppendLine("Удалена пустая папка: " + RelPath(d)); }
                    catch (Exception ex) { failed++; log.AppendLine("Не удалось удалить " + RelPath(d) + ": " + ex.Message); }
                }
            }

            if (accepted.Contains("broken"))
            {
                foreach (string d in brokenDirs)
                {
                    try { Directory.Delete(d, true); done++; log.AppendLine("Удалена неполная ливрея: " + RelPath(d)); }
                    catch (Exception ex) { failed++; log.AppendLine("Не удалось удалить " + RelPath(d) + ": " + ex.Message); }
                }
            }

            if (accepted.Contains("dupe"))
            {
                foreach (string key in dupeKeys)
                {
                    List<string> copies = byName[key];
                    // Оставляем копию в CustomLiveries (стандартное место), удаляем остальные.
                    string keep = copies[0];
                    foreach (string c in copies)
                        if (c.IndexOf(@"\CustomLiveries\", StringComparison.OrdinalIgnoreCase) >= 0) { keep = c; break; }

                    foreach (string c in copies)
                    {
                        if (string.Equals(c, keep, StringComparison.OrdinalIgnoreCase)) continue;
                        try { Directory.Delete(c, true); done++; log.AppendLine("Удалён дубликат: " + RelPath(c) + " (оставлен " + RelPath(keep) + ")"); }
                        catch (Exception ex) { failed++; log.AppendLine("Не удалось удалить дубликат " + RelPath(c) + ": " + ex.Message); }
                    }
                }
            }
        }
        finally { Cursor = Cursors.Default; }

        // Файлы в "Program Files" обычный процесс удалить не может — нужны права администратора.
        // Если что-то осталось на диске, предлагаем выполнить удаление с повышением (один запрос UAC).
        List<string> remaining = new List<string>();
        if (accepted.Contains("empty")) foreach (string d in emptyDirs) if (Directory.Exists(d)) remaining.Add(d);
        if (accepted.Contains("broken")) foreach (string d in brokenDirs) if (Directory.Exists(d)) remaining.Add(d);
        if (accepted.Contains("dupe"))
        {
            foreach (string key in dupeKeys)
            {
                List<string> copies = byName[key];
                string keep = copies[0];
                foreach (string c in copies)
                    if (c.IndexOf(@"\CustomLiveries\", StringComparison.OrdinalIgnoreCase) >= 0) { keep = c; break; }
                foreach (string c in copies)
                    if (!string.Equals(c, keep, StringComparison.OrdinalIgnoreCase) && Directory.Exists(c)) remaining.Add(c);
            }
        }

        if (remaining.Count > 0)
        {
            DialogResult dr = MessageBox.Show(this,
                "Часть папок находится в защищённой папке игры (Program Files) — обычный запуск не имеет прав их удалить.\n\n"
                + "Осталось удалить: " + remaining.Count + "\n\n"
                + "Выполнить удаление с правами администратора? Появится запрос Windows (UAC).",
                "Нужны права администратора", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (dr == DialogResult.Yes)
            {
                int okElev = DeleteElevated(remaining);
                if (okElev > 0)
                {
                    int left = 0;
                    foreach (string d in remaining) if (Directory.Exists(d)) left++;
                    done += (remaining.Count - left);
                    failed += left;
                    log.AppendLine("Удалено с правами администратора: " + (remaining.Count - left) + ", осталось: " + left);
                }
                else
                {
                    log.AppendLine("Повышение прав отклонено или не удалось; удаление не выполнено.");
                }
            }
            else
            {
                log.AppendLine("Пользователь отказался от повышения прав; удаление не выполнено (" + remaining.Count + ").");
            }
        }

        string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "livery_fix.log");
        try { File.AppendAllText(logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine + log.ToString() + Environment.NewLine, Encoding.UTF8); } catch { logPath = null; }

        MessageBox.Show(this,
            "Автопочинка завершена.\n\nУспешно: " + done + "   Не удалось: " + failed
            + (logPath != null ? "\n\nПодробности: " + logPath : ""),
            "Автопочинка ливрей", MessageBoxButtons.OK,
            failed == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

        lblStatus.Text = "Автопочинка: успешно " + done + ", ошибок " + failed;
        LoadLiveries();
    }

    // Удаление списка папок одним приподнятым процессом (один запрос UAC).
    // Возвращает количество папок, которых после этого не стало.
    private int DeleteElevated(List<string> dirs)
    {
        if (dirs == null || dirs.Count == 0) return 0;
        string bat = Path.Combine(Path.GetTempPath(), "vtolqol_livery_fix_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".bat");
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("chcp 65001 >nul");
        foreach (string d in dirs)
        {
            // /f — снять read-only у файлов, /q — без вопросов
            sb.AppendLine("rd /s /q \"" + d + "\"");
        }
        sb.AppendLine("exit /b 0");

        try
        {
            File.WriteAllText(bat, sb.ToString(), Encoding.Default);
            ProcessStartInfo psi = new ProcessStartInfo(bat);
            psi.Verb = "runas";
            psi.UseShellExecute = true;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            using (Process p = Process.Start(psi))
            {
                if (p != null) p.WaitForExit(60000);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("LIVERY", "DeleteElevated", ex);
            return 0;
        }
        finally
        {
            try { File.Delete(bat); } catch { }
        }

        int gone = 0;
        foreach (string d in dirs) if (!Directory.Exists(d)) gone++;
        return gone;
    }

    // =====================================================================
    // === АВТОПОЧИНКА БЕЗ GUI (для самопроверки через CLI) ==================
    // Выполняет безопасный набор: пустые папки, дубликаты, ливреи без
    // текстуры/описания, отписка от подписок Workshop без файлов.
    // Возвращает текстовый отчёт.
    // =====================================================================
    public static string RunAutoFix(string gameRoot, out string logPath)
    {
        StringBuilder log = new StringBuilder();
        int done = 0, failed = 0;
        logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "livery_fix.log");
        log.AppendLine("=== АВТОПОЧИНКА " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
        log.AppendLine("gameRoot = " + gameRoot);

        try
        {
            string customLiveries = Path.Combine(gameRoot, "CustomLiveries");
            List<string> liveryDirs = new List<string>();
            if (Directory.Exists(customLiveries))
                foreach (string d in Directory.GetDirectories(customLiveries)) liveryDirs.Add(d);
            string perAircraft = Path.Combine(gameRoot, "CustomLivery");
            if (Directory.Exists(perAircraft))
                foreach (string acDir in Directory.GetDirectories(perAircraft))
                    foreach (string d in Directory.GetDirectories(acDir)) liveryDirs.Add(d);
            log.AppendLine("папок ливрей найдено: " + liveryDirs.Count);

            // 1. Пустые папки
            foreach (string d in liveryDirs)
            {
                try
                {
                    string[] files = Directory.GetFiles(d, "*", SearchOption.AllDirectories);
                    if (files.Length == 0)
                    {
                        Directory.Delete(d, true);
                        done++;
                        log.AppendLine("УДАЛЕНА пустая папка: " + d);
                    }
                }
                catch (Exception ex) { failed++; log.AppendLine("ошибка пустой " + d + ": " + ex.GetType().Name + " " + ex.Message); }
            }

            // 2. Дубликаты: оставляем копию в CustomLiveries
            Dictionary<string, List<string>> byName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string d in liveryDirs)
            {
                if (!Directory.Exists(d)) continue;
                string nm = Path.GetFileName(d);
                if (!byName.ContainsKey(nm)) byName[nm] = new List<string>();
                byName[nm].Add(d);
            }
            foreach (KeyValuePair<string, List<string>> kv in byName)
            {
                if (kv.Value.Count < 2) continue;
                string keep = kv.Value[0];
                foreach (string c in kv.Value)
                    if (c.IndexOf(@"\CustomLiveries\", StringComparison.OrdinalIgnoreCase) >= 0) { keep = c; break; }
                foreach (string c in kv.Value)
                {
                    if (string.Equals(c, keep, StringComparison.OrdinalIgnoreCase)) continue;
                    try { Directory.Delete(c, true); done++; log.AppendLine("УДАЛЁН дубликат: " + c + " (оставлен " + keep + ")"); }
                    catch (Exception ex) { failed++; log.AppendLine("ошибка дубликата " + c + ": " + ex.GetType().Name + " " + ex.Message); }
                }
            }

            // 3. Подписки без файлов
            string steamLibRoot = Path.GetFullPath(Path.Combine(gameRoot, @"..\..\.."));
            string contentDir = Path.Combine(steamLibRoot, @"steamapps\workshop\content\667970");
            string acf = Path.Combine(steamLibRoot, @"steamapps\workshop\appworkshop_667970.acf");
            log.AppendLine("Steam запущен: " + (Process.GetProcessesByName("steam").Length > 0));
            if (File.Exists(acf))
            {
                string text = File.ReadAllText(acf);
                MatchCollection ms = Regex.Matches(text, "\"(\\d{9,11})\"\\s*[\\r\\n]+\\s*\\{");
                HashSet<string> seen = new HashSet<string>();
                foreach (Match m in ms)
                {
                    string id = m.Groups[1].Value;
                    if (!seen.Add(id)) continue;
                    if (Directory.Exists(Path.Combine(contentDir, id))) continue;
                    bool ok = false;
                    try { ok = WorkshopBrowser.UnsubscribeById(id, gameRoot); }
                    catch (Exception ex) { log.AppendLine("исключение отписки " + id + ": " + ex.Message); }
                    if (ok)
                    {
                        done++;
                        log.AppendLine("ОТПИСАЛИСЬ от Workshop: " + id);
                    }
                    else
                    {
                        // Steam недоступен — локальный обход: создаём папку, чтобы игра не падала
                        // с DirectoryNotFoundException. Потом Steam сам её наполнит при загрузке.
                        try
                        {
                            Directory.CreateDirectory(Path.Combine(contentDir, id));
                            done++;
                            log.AppendLine("СОЗДАНА папка для подписки (Steam недоступен): " + id);
                        }
                        catch (Exception ex2)
                        {
                            failed++;
                            log.AppendLine("не удалось ни отписаться, ни создать папку " + id + ": " + ex2.Message);
                        }
                    }
                }
            }
        }
        catch (Exception ex) { log.AppendLine("ОБЩАЯ ОШИБКА: " + ex.GetType().Name + " " + ex.Message); }

        log.AppendLine("ИТОГО: успешно " + done + ", неудач " + failed);
        try { File.AppendAllText(logPath, log.ToString() + Environment.NewLine, Encoding.UTF8); } catch { }
        return log.ToString();
    }

    private string RelPath(string full)
    {
        try
        {
            if (!string.IsNullOrEmpty(gameRoot) && full.StartsWith(gameRoot, StringComparison.OrdinalIgnoreCase))
                return full.Substring(gameRoot.Length).TrimStart('\\');
        }
        catch { }
        return full;
    }

    private static string FirstLines(string text, int count)
    {
        string[] parts = text.Replace("\r\n", "\n").Split('\n');
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < parts.Length && i < count; i++) sb.AppendLine(parts[i]);
        if (parts.Length > count) sb.AppendLine("...");
        return sb.ToString();
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

