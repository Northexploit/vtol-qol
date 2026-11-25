using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

class Program
{
    static void Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.Title = "Установщик умного радио для VTOL VR";
        Console.WriteLine("========================================================");
        Console.WriteLine("   УСТАНОВКА УМНОГО РАДИО ДЛЯ VTOL VR");
        Console.WriteLine("========================================================\n");

        Console.WriteLine("[1/5] Поиск папки с игрой VTOL VR...");
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

        string gameDir = "";
        foreach (string dir in searchDirs)
        {
            if (Directory.Exists(dir)) { gameDir = dir; break; }
        }

        if (string.IsNullOrEmpty(gameDir))
        {
            Console.WriteLine("[!] Не удалось найти папку автоматически.");
            Console.Write("Вставьте полный путь к папке RadioMusic вручную: ");
            gameDir = Console.ReadLine().Trim('"');
        }
        Console.WriteLine("[OK] Папка найдена: " + gameDir);

        Console.WriteLine("[2/5] Распаковка конвертера ffmpeg.exe...");
        try
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ffmpeg.exe"))
            {
                if (stream != null)
                {
                    using (FileStream fs = new FileStream(Path.Combine(gameDir, "ffmpeg.exe"), FileMode.Create, FileAccess.Write))
                    {
                        stream.CopyTo(fs);
                    }
                    Console.WriteLine("[OK] ffmpeg.exe установлен.");
                }
                else
                {
                    Console.WriteLine("[!] Вшитый ресурс ffmpeg.exe не найден.");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[!] Ошибка распаковки ffmpeg: " + ex.Message);
        }

        Console.WriteLine("[3/5] Создание скриптов радио...");
        string batPath = Path.Combine(gameDir, "Shuffle.bat");
        string ps1Path = Path.Combine(gameDir, "SmartShuffle.ps1");

        File.WriteAllText(batPath, "@echo off\r\ncd /d \"%~dp0\"\r\nstart \"\" /b powershell -WindowStyle Hidden -ExecutionPolicy Bypass -File \"%~dp0SmartShuffle.ps1\"\r\n", Encoding.ASCII);
        string ps1Code = @"Get-CimInstance Win32_Process -Filter ""Name = 'powershell.exe' AND CommandLine LIKE '%SmartShuffle.ps1%'"" | Where-Object { $_.ProcessId -ne $PID } | Invoke-CimMethod -MethodName Terminate | Out-Null

$musicDir = $PSScriptRoot
$vtolAppData = ""$env:APPDATA\Boundless Dynamics, LLC\VTOLVR\SaveData""
$logPath = ""$env:USERPROFILE\AppData\LocalLow\Boundless Dynamics, LLC\VTOLVR\Player.log""
$lastShuffle = [DateTime]::MinValue

$ffmpeg = Join-Path $musicDir 'ffmpeg.exe'
if (Test-Path $ffmpeg) {
    $otherAudio = @(Get-ChildItem -Path $musicDir -File | Where-Object { $_.Extension -match '^\.(flac|wav|ogg|m4a|aac|wma|opus)$' })
    foreach ($track in $otherAudio) {
        $outMp3 = Join-Path $musicDir ""$($track.BaseName).mp3""
        Start-Process -FilePath $ffmpeg -ArgumentList ""-y -i `""$($track.FullName)`"" -codec:a libmp3lame -qscale:a 2 `""$outMp3`"""" -WindowStyle Hidden -Wait
        if (Test-Path $outMp3) { Remove-Item -LiteralPath $track.FullName -Force -ErrorAction SilentlyContinue }
    }
}

$myNick = ''
$pilotsFile = Join-Path $vtolAppData 'pilots.cfg'
if (Test-Path $pilotsFile) {
    $content = Get-Content $pilotsFile -Raw -Encoding UTF8
    if ($content -match '(?m)^(?:lastPilot|pilotName)\s*=\s*(.+)$') {
        $myNick = [regex]::Escape($matches.Item(1).Trim())
    }
}

function Shuffle-Music {
    $now = Get-Date
    if (($now - $script:lastShuffle).TotalSeconds -ge 5) {
        Start-Sleep -Seconds 2
        $files = @(Get-ChildItem -Path $musicDir -Filter '*.mp3')
        foreach ($file in $files) {
            $cleanName = $file.Name -replace '^\d{4}_', ''
            $newName = '{0:D4}_{1}' -f (Get-Random -Max 9999), $cleanName
            try { Rename-Item -LiteralPath $file.FullName -NewName $newName -ErrorAction Stop } catch {}
        }
        $script:lastShuffle = Get-Date
    }
}

$initFiles = @(Get-ChildItem -Path $musicDir -Filter '*.mp3')
foreach ($file in $initFiles) {
    $cleanName = $file.Name -replace '^\d{4}_', ''
    $newName = '{0:D4}_{1}' -f (Get-Random -Max 9999), $cleanName
    try { Rename-Item -LiteralPath $file.FullName -NewName $newName -ErrorAction Stop } catch {}
}

Start-Sleep -Seconds 15

if (Test-Path $logPath) {
    Get-Content -Path $logPath -Tail 0 -Wait | ForEach-Object {
        if (-not (Get-Process -Name 'VTOLVR' -ErrorAction SilentlyContinue)) { break }
        if ($myNick -ne '') {
            if ($_ -match ""\($myNick\) ejected|\($myNick\) was killed"") { Shuffle-Music }
        } else {
            if ($_ -match 'ejected|was killed') { Shuffle-Music }
        }
    }
}";
        File.WriteAllText(ps1Path, ps1Code, Encoding.UTF8);
        Console.WriteLine("[OK] Скрипты созданы.");

        Console.WriteLine("[4/5] Настройка параметров запуска в Steam...");
        bool steamWasRunning = false;
        Process[] steamProcs = Process.GetProcessesByName("steam");
        if (steamProcs.Length > 0)
        {
            Console.WriteLine("[!] Закрываю Steam на пару секунд для записи настроек...");
            foreach (var p in steamProcs) { try { p.Kill(); } catch { } }
            Thread.Sleep(3000);
            steamWasRunning = true;
        }

        if (!string.IsNullOrEmpty(steamPath) && Directory.Exists(Path.Combine(steamPath, "userdata")))
        {
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
                        {
                            appBlock = Regex.Replace(appBlock, "(\"LaunchOptions\"\\s*\")[^\"]*(\")", "${1}" + escapedLaunch + "${2}");
                        }
                        else
                        {
                            appBlock = Regex.Replace(appBlock, "(\"667970\"\\s*\\{)", "\"667970\"\n\t\t\t\t\t{\n\t\t\t\t\t\t\"LaunchOptions\"\t\t\"" + escapedLaunch + "\"");
                        }
                        text = text.Substring(0, startIdx) + appBlock + text.Substring(endIdx + 1);
                        File.WriteAllText(vdf, text, new UTF8Encoding(false));
                    }
                }
            }
            Console.WriteLine("[OK] Параметры прописаны в Steam!");
        }

        if (steamWasRunning && File.Exists(Path.Combine(steamPath, "steam.exe")))
        {
            Process.Start(Path.Combine(steamPath, "steam.exe"));
        }

        Console.WriteLine("[5/5] Создание ярлыка на Рабочем столе...");
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        string lnkPath = Path.Combine(desktop, "VTOL VR (Smart Radio).lnk");
        string exeIcon = Path.GetFullPath(Path.Combine(gameDir, @"..\VTOLVR.exe"));
        string psShortcut = string.Format(
            "$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut('{0}'); $s.TargetPath = 'cmd.exe'; $s.Arguments = '/c \"\"{1}\" & start \"\" \"steam://rungameid/667970\"\"'; if (Test-Path '{2}') {{ $s.IconLocation = '{2}' }}; $s.WindowStyle = 7; $s.Save()",
            lnkPath.Replace("'", "''"), batPath.Replace("'", "''"), exeIcon.Replace("'", "''")
        );
        string encodedPs = Convert.ToBase64String(Encoding.Unicode.GetBytes(psShortcut));
        Process pShortcut = Process.Start(new ProcessStartInfo("powershell", "-NoProfile -EncodedCommand " + encodedPs) { WindowStyle = ProcessWindowStyle.Hidden });
        if (pShortcut != null) pShortcut.WaitForExit();

        Console.WriteLine("[OK] Ярлык готов.");

        Console.WriteLine("\n========================================================");
        Console.WriteLine("   УСТАНОВКА ПОЛНОСТЬЮ ЗАВЕРШЕНА! Нажмите Enter...");
        Console.WriteLine("========================================================");
        Console.ReadLine();
    }
}
