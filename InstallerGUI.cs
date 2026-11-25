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
    private CheckBox chkShuffle;
    private CheckBox chkLoudNorm;
    private CheckBox chkYtDlp;
    private CheckBox chkDiscord;
    private Button btnInstall;
    private Button btnOpenFolder;
    private Label lblStatus;
    private ProgressBar progressBar;

    public MainForm()
    {
        this.Text = "VTOL VR — Smart Radio Suite";
        this.Size = new Size(520, 390);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.FixedSingle;
        this.MaximizeBox = false;
        this.BackColor = Color.FromArgb(22, 26, 24);
        this.ForeColor = Color.FromArgb(0, 255, 102);
        this.Font = new Font("Consolas", 9.5f, FontStyle.Regular);

        Label lblTitle = new Label();
        lblTitle.Text = "/// VTOL VR SMART RADIO SYSTEM ///";
        lblTitle.Font = new Font("Consolas", 13f, FontStyle.Bold);
        lblTitle.Location = new Point(20, 18);
        lblTitle.AutoSize = true;
        this.Controls.Add(lblTitle);

        Label lblDir = new Label();
        lblDir.Text = "Директория RadioMusic:";
        lblDir.Location = new Point(22, 55);
        lblDir.AutoSize = true;
        lblDir.ForeColor = Color.LightGray;
        this.Controls.Add(lblDir);

        txtPath = new TextBox();
        txtPath.Location = new Point(24, 78);
        txtPath.Size = new Size(375, 24);
        txtPath.BackColor = Color.FromArgb(35, 42, 38);
        txtPath.ForeColor = Color.White;
        txtPath.BorderStyle = BorderStyle.FixedSingle;
        txtPath.Text = FindVtolMusicDir();
        this.Controls.Add(txtPath);

        Button btnBrowse = new Button();
        btnBrowse.Text = "Обзор...";
        btnBrowse.Location = new Point(405, 77);
        btnBrowse.Size = new Size(80, 26);
        btnBrowse.FlatStyle = FlatStyle.Flat;
        btnBrowse.BackColor = Color.FromArgb(40, 50, 45);
        btnBrowse.Click += (s, e) => {
            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                if (fbd.ShowDialog() == DialogResult.OK) txtPath.Text = fbd.SelectedPath;
            }
        };
        this.Controls.Add(btnBrowse);

        chkShuffle = CreateCheckBox("Умный шаффл при запуске и каждом респавне", 120, true);
        chkLoudNorm = CreateCheckBox("Конвертер форматов (FLAC/M4A) + выравнивание громкости", 150, true);
        chkYtDlp = CreateCheckBox("Авто-скачивание треков из links.txt (YouTube / SoundCloud)", 180, true);
        chkDiscord = CreateCheckBox("Статус в Discord (показывает текущий самолет и трек)", 210, true);

        progressBar = new ProgressBar();
        progressBar.Location = new Point(24, 250);
        progressBar.Size = new Size(461, 14);
        progressBar.Style = ProgressBarStyle.Continuous;
        this.Controls.Add(progressBar);

        lblStatus = new Label();
        lblStatus.Text = "Готов к установке.";
        lblStatus.Location = new Point(22, 272);
        lblStatus.Size = new Size(465, 20);
        lblStatus.ForeColor = Color.Silver;
        this.Controls.Add(lblStatus);

        btnInstall = new Button();
        btnInstall.Text = "УСТАНОВИТЬ / ОБНОВИТЬ";
        btnInstall.Font = new Font("Consolas", 10f, FontStyle.Bold);
        btnInstall.Location = new Point(24, 302);
        btnInstall.Size = new Size(245, 36);
        btnInstall.FlatStyle = FlatStyle.Flat;
        btnInstall.BackColor = Color.FromArgb(0, 140, 60);
        btnInstall.ForeColor = Color.White;
        btnInstall.Click += BtnInstall_Click;
        this.Controls.Add(btnInstall);

        btnOpenFolder = new Button();
        btnOpenFolder.Text = "ОТКРЫТЬ ПАПКУ МУЗЫКИ";
        btnOpenFolder.Font = new Font("Consolas", 9.5f, FontStyle.Bold);
        btnOpenFolder.Location = new Point(280, 302);
        btnOpenFolder.Size = new Size(205, 36);
        btnOpenFolder.FlatStyle = FlatStyle.Flat;
        btnOpenFolder.BackColor = Color.FromArgb(40, 50, 45);
        btnOpenFolder.ForeColor = Color.FromArgb(0, 255, 102);
        btnOpenFolder.Click += (s, e) => {
            if (Directory.Exists(txtPath.Text)) Process.Start("explorer.exe", txtPath.Text);
        };
        this.Controls.Add(btnOpenFolder);
    }

    private CheckBox CreateCheckBox(string text, int top, bool isChecked)
    {
        CheckBox cb = new CheckBox();
        cb.Text = text;
        cb.Location = new Point(24, top);
        cb.Size = new Size(465, 24);
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

        foreach (string dir in searchDirs)
        {
            if (Directory.Exists(dir)) return dir;
        }
        return "";
    }

    private void ExtractResource(string resName, string targetPath)
    {
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
        lblStatus.Text = text;
        progressBar.Value = progress;
        Application.DoEvents();
    }

    private void BtnInstall_Click(object sender, EventArgs e)
    {
        string gameDir = txtPath.Text.Trim('"');
        if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
        {
            MessageBox.Show("Укажите существующую папку RadioMusic!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        btnInstall.Enabled = false;
        try
        {
            UpdateStatus("Распаковка ffmpeg и yt-dlp...", 20);
            if (chkLoudNorm.Checked || chkYtDlp.Checked)
                ExtractResource("ffmpeg.exe", Path.Combine(gameDir, "ffmpeg.exe"));
            if (chkYtDlp.Checked)
                ExtractResource("yt-dlp.exe", Path.Combine(gameDir, "yt-dlp.exe"));

            string linksFile = Path.Combine(gameDir, "links.txt");
            if (chkYtDlp.Checked && !File.Exists(linksFile))
            {
                File.WriteAllText(linksFile, "# Вставьте сюда ссылки на треки или плейлисты YouTube / SoundCloud (по одной на строку)\r\n# При запуске VTOL VR музыка скачается автоматически!\r\n", Encoding.UTF8);
            }

            UpdateStatus("Генерация умного движка радио...", 45);
            string batPath = Path.Combine(gameDir, "Shuffle.bat");
            string ps1Path = Path.Combine(gameDir, "SmartShuffle.ps1");

            File.WriteAllText(batPath, "@echo off\r\ncd /d \"%~dp0\"\r\nstart \"\" /b powershell -WindowStyle Hidden -ExecutionPolicy Bypass -File \"%~dp0SmartShuffle.ps1\"\r\n", Encoding.ASCII);

            string ps1Code = BuildPowerShellEngine(chkShuffle.Checked, chkLoudNorm.Checked, chkYtDlp.Checked, chkDiscord.Checked);
            File.WriteAllText(ps1Path, ps1Code, Encoding.UTF8);

            UpdateStatus("Настройка параметров запуска в Steam...", 70);
            ConfigureSteam(batPath);

            UpdateStatus("Создание ярлыка на Рабочем столе...", 90);
            CreateDesktopShortcut(gameDir, batPath);

            UpdateStatus("Установка успешно завершена!", 100);
            MessageBox.Show("Умное радио успешно установлено!\n\n- Кидайте любые треки в папку RadioMusic\n- Вставляйте ссылки на плейлисты в links.txt\n- Просто запускайте игру как обычно!", "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Ошибка при установке: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            UpdateStatus("Ошибка установки.", 0);
        }
        finally
        {
            btnInstall.Enabled = true;
        }
    }

    private void ConfigureSteam(string batPath)
    {
        string steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", "") as string ?? "";
        steamPath = steamPath.Replace('/', '\\');
        if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(Path.Combine(steamPath, "userdata"))) return;

        bool steamWasRunning = false;
        Process[] steamProcs = Process.GetProcessesByName("steam");
        if (steamProcs.Length > 0)
        {
            foreach (var p in steamProcs) { try { p.Kill(); } catch { } }
            Thread.Sleep(2500);
            steamWasRunning = true;
        }

        string escapedLaunch = ("cmd /c \"\"" + batPath + "\" & %command%\"").Replace("\\", "\\\\").Replace("\"", "\\\"");
        foreach (string vdf in Directory.GetFiles(Path.Combine(steamPath, "userdata"), "localconfig.vdf", SearchOption.AllDirectories))
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

    private void CreateDesktopShortcut(string gameDir, string batPath)
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        string lnkPath = Path.Combine(desktop, "VTOL VR (Smart Radio).lnk");
        string exeIcon = Path.GetFullPath(Path.Combine(gameDir, @"..\VTOLVR.exe"));
        string psShortcut = string.Format(
            "$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut('{0}'); $s.TargetPath = 'cmd.exe'; $s.Arguments = '/c \"\"{1}\" & start \"\" \"steam://rungameid/667970\"\"'; if (Test-Path '{2}') {{ $s.IconLocation = '{2}' }}; $s.WindowStyle = 7; $s.Save()",
            lnkPath.Replace("'", "''"), batPath.Replace("'", "''"), exeIcon.Replace("'", "''")
        );
        string encodedPs = Convert.ToBase64String(Encoding.Unicode.GetBytes(psShortcut));
        Process p = Process.Start(new ProcessStartInfo("powershell", "-NoProfile -EncodedCommand " + encodedPs) { WindowStyle = ProcessWindowStyle.Hidden });
        if (p != null) p.WaitForExit();
    }
    private string BuildPowerShellEngine(bool useShuffle, bool useLoudNorm, bool useYtDlp, bool useDiscord)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("Get-CimInstance Win32_Process -Filter \"Name = 'powershell.exe' AND CommandLine LIKE '%SmartShuffle.ps1%'\" | Where-Object { $_.ProcessId -ne $PID } | Invoke-CimMethod -MethodName Terminate | Out-Null");
        sb.AppendLine("$musicDir = $PSScriptRoot");
        sb.AppendLine("$vtolAppData = \"$env:APPDATA\\Boundless Dynamics, LLC\\VTOLVR\\SaveData\"");
        sb.AppendLine("$logPath = \"$env:USERPROFILE\\AppData\\LocalLow\\Boundless Dynamics, LLC\\VTOLVR\\Player.log\"");
        sb.AppendLine("$lastShuffle = [DateTime]::MinValue");
        sb.AppendLine("$currentAircraft = 'В ангаре / Меню'");

        if (useYtDlp)
        {
            sb.AppendLine("$ytdlp = Join-Path $musicDir 'yt-dlp.exe'");
            sb.AppendLine("$linksFile = Join-Path $musicDir 'links.txt'");
            sb.AppendLine("$archiveFile = Join-Path $musicDir 'downloaded.txt'");
            sb.AppendLine("if ((Test-Path $ytdlp) -and (Test-Path $linksFile)) {");
            sb.AppendLine("    $urls = Get-Content $linksFile -Encoding UTF8 | Where-Object { $_ -match '^https?://' }");
            sb.AppendLine("    if ($urls.Count -gt 0) {");
            sb.AppendLine("        Start-Process -FilePath $ytdlp -ArgumentList \"-x --audio-format mp3 --download-archive `\"$archiveFile`\" --no-playlist-reverse -o `\"$musicDir\\%(title)s.%(ext)s`\" -a `\"$linksFile`\"\" -WindowStyle Hidden -Wait");
            sb.AppendLine("    }");
            sb.AppendLine("}");
        }

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
            sb.AppendLine("    $doneList = if (Test-Path $normCache) { @(Get-Content $normCache) } else { @() }");
            sb.AppendLine("    $mp3Files = @(Get-ChildItem -Path $musicDir -Filter '*.mp3')");
            sb.AppendLine("    foreach ($mp3 in $mp3Files) {");
            sb.AppendLine("        $clean = $mp3.Name -replace '^\\d{4}_', ''");
            sb.AppendLine("        if ($doneList -notcontains $clean) {");
            sb.AppendLine("            $tmpMp3 = Join-Path $musicDir \"tmp_$($mp3.Name)\"");
            sb.AppendLine("            Start-Process -FilePath $ffmpeg -ArgumentList \"-y -i `\"$($mp3.FullName)`\" -af loudnorm=I=-14:TP=-1.5:LRA=11 -codec:a libmp3lame -qscale:a 2 `\"$tmpMp3`\"\" -WindowStyle Hidden -Wait");
            sb.AppendLine("            if (Test-Path $tmpMp3) { Move-Item -LiteralPath $tmpMp3 -Destination $mp3.FullName -Force; Add-Content -Path $normCache -Value $clean }");
            sb.AppendLine("        }");
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
            sb.AppendLine("$discordPipe = $null");
            sb.AppendLine("function Update-DiscordRPC($details, $state) {");
            sb.AppendLine("    try {");
            sb.AppendLine("        if ($null -eq $script:discordPipe -or -not $script:discordPipe.IsConnected) {");
            sb.AppendLine("            for ($i = 0; $i -lt 10; $i++) {");
            sb.AppendLine("                try {");
            sb.AppendLine("                    $p = New-Object System.IO.Pipes.NamedPipeClientStream('.', \"discord-ipc-$i\", [System.IO.Pipes.PipeDirection]::InOut)");
            sb.AppendLine("                    $p.Connect(100)");
            sb.AppendLine("                    $script:discordPipe = $p");
            sb.AppendLine("                    $hs = '{\"v\":1,\"client_id\":\"1185934821094514748\"}'");
            sb.AppendLine("                    $hb = [System.Text.Encoding]::UTF8.GetBytes($hs)");
            sb.AppendLine("                    $hdr = [BitConverter]::GetBytes([int]0) + [BitConverter]::GetBytes([int]$hb.Length)");
            sb.AppendLine("                    $script:discordPipe.Write($hdr, 0, 8); $script:discordPipe.Write($hb, 0, $hb.Length); $script:discordPipe.Flush()");
            sb.AppendLine("                    break");
            sb.AppendLine("                } catch {}");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("        if ($null -ne $script:discordPipe -and $script:discordPipe.IsConnected) {");
            sb.AppendLine("            $dEsc = $details -replace '\"', '\\\"'; $sEsc = $state -replace '\"', '\\\"'");
            sb.AppendLine("            $json = \"{\"\"cmd\"\":\"\"SET_ACTIVITY\"\",\"\"args\"\":{\"\"pid\"\":$PID,\"\"activity\"\":{\"\"details\"\":\"\"$dEsc\"\",\"\"state\"\":\"\"$sEsc\"\"}},\"\"nonce\"\":\"\"$([guid]::NewGuid())\"\"}\"");
            sb.AppendLine("            $jb = [System.Text.Encoding]::UTF8.GetBytes($json)");
            sb.AppendLine("            $hdr = [BitConverter]::GetBytes([int]1) + [BitConverter]::GetBytes([int]$jb.Length)");
            sb.AppendLine("            $script:discordPipe.Write($hdr, 0, 8); $script:discordPipe.Write($jb, 0, $jb.Length); $script:discordPipe.Flush()");
            sb.AppendLine("        }");
            sb.AppendLine("    } catch {}");
            sb.AppendLine("}");
        }

        sb.AppendLine("function Shuffle-Music($doRename) {");
        sb.AppendLine("    $files = @(Get-ChildItem -Path $musicDir -Filter '*.mp3')");
        sb.AppendLine("    if ($doRename) {");
        sb.AppendLine("        foreach ($file in $files) {");
        sb.AppendLine("            $cleanName = $file.Name -replace '^\\d{4}_', ''");
        sb.AppendLine("            $newName = '{0:D4}_{1}' -f (Get-Random -Max 9999), $cleanName");
        sb.AppendLine("            try { Rename-Item -LiteralPath $file.FullName -NewName $newName -ErrorAction Stop } catch {}");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        if (useDiscord)
        {
            sb.AppendLine("    $firstTrack = Get-ChildItem -Path $musicDir -Filter '*.mp3' | Sort-Object Name | Select-Object -First 1");
            sb.AppendLine("    $tName = if ($firstTrack) { ($firstTrack.BaseName -replace '^\\d{4}_', '') } else { 'Радио выключено' }");
            sb.AppendLine("    Update-DiscordRPC \"Борт: $script:currentAircraft\" \"В эфире: $tName\"");
        }
        sb.AppendLine("}");

        sb.AppendLine("Shuffle-Music " + (useShuffle ? "$true" : "$false"));
        sb.AppendLine("Start-Sleep -Seconds 15");

        sb.AppendLine("if (Test-Path $logPath) {");
        sb.AppendLine("    Get-Content -Path $logPath -Tail 0 -Wait | ForEach-Object {");
        sb.AppendLine("        if (-not (Get-Process -Name 'VTOLVR' -ErrorAction SilentlyContinue)) { break }");
        if (useDiscord)
        {
            sb.AppendLine("        if ($_ -match '(F/A-26B|EF-24G|AV-42C|F-45A|AH-94|T-55)') {");
            sb.AppendLine("            $script:currentAircraft = $matches.Item(1)");
            sb.AppendLine("            Shuffle-Music $false");
            sb.AppendLine("        }");
        }
        if (useShuffle)
        {
            sb.AppendLine("        $now = Get-Date");
            sb.AppendLine("        if (($now - $script:lastShuffle).TotalSeconds -ge 5) {");
            sb.AppendLine("            $hit = if ($myNick -ne '') { $_ -match \"\\($myNick\\) ejected|\\($myNick\\) was killed\" } else { $_ -match 'ejected|was killed' }");
            sb.AppendLine("            if ($hit) { Start-Sleep -Seconds 2; Shuffle-Music $true; $script:lastShuffle = Get-Date }");
            sb.AppendLine("        }");
        }
        sb.AppendLine("    }");
        sb.AppendLine("}");
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
