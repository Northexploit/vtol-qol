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
// === СИСТЕМА ОЗВУЧКИ СЕРИЙ УБИЙСТВ (WT + UNREAL TOURNAMENT 3) =============
// =========================================================================
public static class KillSoundPlayer
{
    private static readonly object stateLock = new object();
    private static int comboStreak = 0;
    private static DateTime lastKillUtc = DateTime.MinValue;
    private const int ComboTimeoutSeconds = 25;

    private static int requestedGeneration = 0;
    private static int requestedTier = 0;
    private static Thread playerThread = null;
    private static AutoResetEvent playSignal = new AutoResetEvent(false);

    private static readonly string[] tierFileBases = new string[]
    {
        "1_kill",          // 1: War Thunder
        "2_doublekill",    // 2: Double Kill
        "3_triplekill",    // 3: Triple Kill
        "4_megakill",      // 4: Mega Kill
        "5_ultrakill",     // 5: Ultra Kill
        "6_monsterkill",   // 6: Monster Kill
        "7_ludicrouskill", // 7: Ludicrous Kill
        "8_holyshit"       // 8+: Holy Shit
    };

    public static string GetSoundsDir()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL", "KillSounds");
        try { Directory.CreateDirectory(dir); } catch { }
        return dir;
    }

    public static void ResetCombo()
    {
        lock (stateLock)
        {
            comboStreak = 0;
            lastKillUtc = DateTime.MinValue;
        }
    }

    public static void RegisterKills(int count)
    {
        if (count <= 0) return;
        lock (stateLock)
        {
            DateTime now = DateTime.UtcNow;
            if ((now - lastKillUtc).TotalSeconds > ComboTimeoutSeconds)
                comboStreak = 0;

            comboStreak += count;
            requestedTier = Math.Min(comboStreak, 8);
            requestedGeneration++;
            lastKillUtc = now;

            if (playerThread == null || !playerThread.IsAlive)
            {
                playerThread = new Thread(AudioLoop);
                playerThread.IsBackground = true;
                playerThread.SetApartmentState(ApartmentState.STA);
                playerThread.Start();
            }
            playSignal.Set();
        }
    }

    private static string ResolveSoundFile(int tier)
    {
        string dir = GetSoundsDir();
        string baseName = tierFileBases[Math.Max(0, Math.Min(tier - 1, tierFileBases.Length - 1))];
        string[] exts = new string[] { ".mp3", ".wav" };

        // 1. Проверяем извлечённый или пользовательский файл
        foreach (string ext in exts)
        {
            string p = Path.Combine(dir, baseName + ext);
            if (File.Exists(p)) return p;
        }

        // 2. Извлекаем из вшитых ресурсов .exe
        foreach (string ext in exts)
        {
            string resName = baseName + ext;
            string targetPath = Path.Combine(dir, resName);
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resName))
                {
                    if (s != null)
                    {
                        using (FileStream fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write))
                            s.CopyTo(fs);
                        return targetPath;
                    }
                }
            }
            catch { }
        }

        if (tier > 1) return ResolveSoundFile(1);
        return null;
    }

    private static void AudioLoop()
    {
        System.Windows.Media.MediaPlayer player = new System.Windows.Media.MediaPlayer();
        player.Volume = 1.0;
        int currentGen = 0;

        while (true)
        {
            int genToPlay = 0;
            int tierToPlay = 0;

            lock (stateLock)
            {
                if (requestedGeneration != currentGen)
                {
                    genToPlay = requestedGeneration;
                    tierToPlay = requestedTier;
                }
            }

            if (genToPlay != 0)
            {
                currentGen = genToPlay;
                try
                {
                    // Мгновенно обрываем предыдущий звук, если он ещё играет
                    player.Stop();
                    player.Close();

                    string filePath = ResolveSoundFile(tierToPlay);
                    AppLogger.Log("SOUND", string.Format("Тир {0} (прерывание предыдущего): {1}", tierToPlay, filePath ?? "не найден"));

                    if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                    {
                        player.Open(new Uri(filePath, UriKind.Absolute));
                        player.Play();
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error("SOUND", "AudioLoop", ex);
                }
            }

            // Прокачиваем сообщения WPF Dispatcher в STA-потоке и ждём следующего сигнала
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(delegate { })
            );
            playSignal.WaitOne(25);
        }
    }
}

