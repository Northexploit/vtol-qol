using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;

// =========================================================================
// === ОЗВУЧКА СОБЫТИЙ ДЕБРИФА (одноразовые эффекты в окне повтора) ========
// =========================================================================
// Звуки ищутся в %LOCALAPPDATA%\VTOL_QoL\DebriefSounds (можно подменить свои),
// при отсутствии извлекаются из вшитых в .exe ресурсов.
public static class DebriefSoundPlayer
{
    private static readonly object stateLock = new object();
    private static Thread playerThread = null;
    private static readonly AutoResetEvent playSignal = new AutoResetEvent(false);
    private static string pendingName = null;
    private static int requestedGeneration = 0;
    private static int currentGeneration = 0;

    // Защита от двойного срабатывания: один и тот же эффект не играем чаще, чем раз в N мс
    private const int DebounceMs = 0;
    // Громкость одноразовых эффектов (JAMMER_ON): половина от полной
    private const double OneShotVolume = 0.5;
    // Плавное включение и затухание одноразовых эффектов
    private const double FadeInMs = 150.0;          // нарастание от старта
    private const double FadeOutMs = 400.0;         // затухание к концу файла
    private const double FadeOutInterruptMs = 90.0; // быстрое затухание при прерывании новым событием
    private static readonly Dictionary<string, DateTime> lastPlayedUtc = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

    public static string GetSoundsDir()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VTOL_QoL", "DebriefSounds");
        try { Directory.CreateDirectory(dir); } catch { }
        return dir;
    }

    /// <summary>Однократное воспроизведение эффекта. Не блокирует вызывающий поток.</summary>
    public static void Play(string baseName)
    {
        if (string.IsNullOrEmpty(baseName)) return;
        try
        {
            lock (stateLock)
            {
                DateTime prev;
                if (lastPlayedUtc.TryGetValue(baseName, out prev) && (DateTime.UtcNow - prev).TotalMilliseconds < DebounceMs)
                    return;
                lastPlayedUtc[baseName] = DateTime.UtcNow;

                pendingName = baseName;
                requestedGeneration++;

                if (playerThread == null || !playerThread.IsAlive)
                {
                    playerThread = new Thread(AudioLoop);
                    playerThread.IsBackground = true;
                    playerThread.SetApartmentState(ApartmentState.STA);
                    playerThread.Start();
                }
            }
            playSignal.Set();
        }
        catch { }
    }

    private static string ResolveSoundFile(string baseName)
    {
        string dir = GetSoundsDir();
        foreach (string ext in new string[] { ".mp3", ".wav" })
        {
            string p = Path.Combine(dir, baseName + ext);
            if (File.Exists(p)) return p;
        }

        foreach (string ext in new string[] { ".mp3", ".wav" })
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
        return null;
    }

    private static void AudioLoop()
    {
        System.Windows.Media.MediaPlayer player = new System.Windows.Media.MediaPlayer();
        player.Volume = 0.0;

        bool active = false;
        double durationMs = 0.0;
        DateTime startedUtc = DateTime.MinValue;

        while (true)
        {
            string nameToPlay = null;

            lock (stateLock)
            {
                if (requestedGeneration != currentGeneration)
                {
                    currentGeneration = requestedGeneration;
                    nameToPlay = pendingName;
                }
            }

            if (!string.IsNullOrEmpty(nameToPlay))
            {
                // Если что-то играет — мягко гасим, чтобы не было щелчка на стыке
                if (active) FadeOutBlocking(player, FadeOutInterruptMs);

                active = false;
                durationMs = 0.0;
                try
                {
                    player.Stop();
                    player.Close();

                    string filePath = ResolveSoundFile(nameToPlay);
                    if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                    {
                        player.Volume = 0.0;
                        player.Open(new Uri(filePath, UriKind.Absolute));
                        player.Play();
                        active = true;
                        startedUtc = DateTime.UtcNow;
                    }
                }
                catch { active = false; }
            }

            if (active)
            {
                if (durationMs <= 0.0 && player.NaturalDuration.HasTimeSpan)
                    durationMs = player.NaturalDuration.TimeSpan.TotalMilliseconds;

                double elapsedMs = (DateTime.UtcNow - startedUtc).TotalMilliseconds;
                double posMs = 0.0;
                try { posMs = player.Position.TotalMilliseconds; } catch { }

                double vol;
                if (elapsedMs < FadeInMs)
                {
                    // Нарастание
                    vol = OneShotVolume * (elapsedMs / FadeInMs);
                }
                else if (durationMs > 0.0 && posMs > durationMs - FadeOutMs)
                {
                    // Затухание к концу файла
                    double k = (durationMs - posMs) / FadeOutMs;
                    vol = OneShotVolume * Math.Max(0.0, Math.Min(1.0, k));
                }
                else
                {
                    vol = OneShotVolume;
                }

                try { player.Volume = Math.Max(0.0, Math.Min(OneShotVolume, vol)); } catch { }

                // Файл доиграл — закрываем, чтобы не висел
                if (durationMs > 0.0 && posMs >= durationMs - 10.0)
                {
                    try { player.Stop(); player.Close(); } catch { }
                    player.Volume = 0.0;
                    active = false;
                }
            }

            // Прокачиваем сообщения WPF Dispatcher в STA-потоке и ждём следующего сигнала
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(delegate { })
            );
            playSignal.WaitOne(10);
        }
    }

    /// <summary>Плавно гасит звук за указанное время (блокирующе, в аудиопотоке).</summary>
    private static void FadeOutBlocking(System.Windows.Media.MediaPlayer player, double fadeMs)
    {
        try
        {
            double startVol = player.Volume;
            int steps = Math.Max(1, (int)(fadeMs / 10.0));
            for (int i = 1; i <= steps; i++)
            {
                try { player.Volume = startVol * (1.0 - (double)i / steps); } catch { break; }
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    new Action(delegate { })
                );
                Thread.Sleep(10);
            }
            try { player.Stop(); player.Close(); } catch { }
            player.Volume = 0.0;
        }
        catch { }
    }

    // =========================================================================
    // === НЕПРЕРЫВНЫЙ LOOP-КАНАЛ (JAMMER_CONTINUOUS) =========================
    // =========================================================================
    // MediaPlayer требует запущенного Dispatcher.Run() (WPF).
    // Создаём отдельный STA-поток, шлём туда все операции через Dispatcher.Invoke.
    // ---------------------------------------------------------------------
    private static Thread continuousThread = null;
    private static System.Windows.Threading.Dispatcher continuousDispatcher = null;
    private static readonly object continuousLock = new object();
    /// <summary>Начать зацикленное воспроизведение. Если уже играет — не дёргает.</summary>
    public static void PlayContinuousLoop(string baseName)
    {
        if (string.IsNullOrEmpty(baseName)) return;
        string filePath = ResolveSoundFile(baseName);
        if (filePath == null) return;
        EnsureContinuousThread();
        System.Windows.Threading.Dispatcher d;
        lock (continuousLock) d = continuousDispatcher;
        if (d == null) return;
        try { d.Invoke(new Action(delegate { OnPlayContinuous(filePath); })); } catch { }
    }

    /// <summary>Поставить на паузу (позиция сохраняется).</summary>
    public static void PauseContinuousLoop()
    {
        System.Windows.Threading.Dispatcher d;
        lock (continuousLock) d = continuousDispatcher;
        if (d == null) return;
        try { d.Invoke(new Action(OnPauseContinuous)); } catch { }
    }

    /// <summary>Снять с паузы (продолжает с той же позиции).</summary>
    public static void ResumeContinuousLoop()
    {
        System.Windows.Threading.Dispatcher d;
        lock (continuousLock) d = continuousDispatcher;
        if (d == null) return;
        try { d.Invoke(new Action(OnResumeContinuous)); } catch { }
    }

    /// <summary>Остановить полностью, сбросить в 0.</summary>
    public static void StopContinuousLoop()
    {
        System.Windows.Threading.Dispatcher d;
        lock (continuousLock) d = continuousDispatcher;
        if (d == null) return;
        try { d.Invoke(new Action(OnStopContinuous)); } catch { }
    }

    /// <summary>Установить громкость 0.0–1.0.</summary>
    public static void SetContinuousLoopVolume(float vol)
    {
        System.Windows.Threading.Dispatcher d;
        lock (continuousLock) d = continuousDispatcher;
        if (d == null) return;
        try { d.Invoke(new Action<float>(OnSetVolume), Math.Max(0f, Math.Min(1f, vol))); } catch { }
    }

    private static void EnsureContinuousThread()
    {
        lock (continuousLock)
        {
            if (continuousDispatcher != null) return;
            Thread t = new Thread(new ThreadStart(ContinuousThreadMain));
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            continuousThread = t;
            // Ждём, пока поток создаст Dispatcher
            for (int i = 0; i < 200 && continuousDispatcher == null; i++) Thread.Sleep(5);
        }
    }

    private static void ContinuousThreadMain()
    {
        continuousDispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        System.Windows.Threading.Dispatcher.Run();
    }

    private static System.Windows.Media.MediaPlayer contA = null;
    private static System.Windows.Media.MediaPlayer contB = null;
    private static System.Windows.Media.MediaPlayer contCurrent = null;   // какой из двух сейчас звучит
    private static System.Windows.Threading.DispatcherTimer contTimer = null;
    private static string contFilePath = null;
    private static float contVolume = 0.7f;
    private static bool contPaused = false;

    // За сколько до конца начинаем второй плеер (перекрытие скрывает стык)
    private const double ContOverlapMs = 120.0;

    private static void OnPlayContinuous(string filePath)
    {
        try
        {
            // Тот же файл уже открыт — не переоткрываем (сохраняем позицию для паузы/резюма)
            if (contFilePath != null && string.Equals(contFilePath, filePath, StringComparison.OrdinalIgnoreCase)
                && contA != null && contB != null)
                return;

            StopPlayersInternal();

            contFilePath = filePath;
            contPaused = false;

            contA = CreateContinuousPlayer(filePath);
            contB = CreateContinuousPlayer(filePath);
            contCurrent = contA;

            contA.Position = TimeSpan.Zero;
            contA.Play();

            EnsureContTimer();
            contTimer.Start();
        }
        catch { }
    }

    private static System.Windows.Media.MediaPlayer CreateContinuousPlayer(string filePath)
    {
        System.Windows.Media.MediaPlayer p = new System.Windows.Media.MediaPlayer();
        p.Volume = contVolume;
        p.Open(new Uri(filePath, UriKind.Absolute));
        return p;
    }

    private static void EnsureContTimer()
    {
        if (contTimer != null) return;
        contTimer = new System.Windows.Threading.DispatcherTimer();
        contTimer.Interval = TimeSpan.FromMilliseconds(20);
        contTimer.Tick += ContTimer_Tick;
    }

    private static void ContTimer_Tick(object sender, EventArgs e)
    {
        try
        {
            if (contPaused) return;
            if (contCurrent == null || contA == null || contB == null) return;

            // Длительность известна только после открытия файла
            if (!contCurrent.NaturalDuration.HasTimeSpan) return;
            double totalMs = contCurrent.NaturalDuration.TimeSpan.TotalMilliseconds;
            if (totalMs <= ContOverlapMs * 2) return;

            double posMs = contCurrent.Position.TotalMilliseconds;
            System.Windows.Media.MediaPlayer other = (contCurrent == contA) ? contB : contA;

            // Подошли к концу — запускаем второй с начала, старый доиграет (перекрытие)
            if (posMs >= totalMs - ContOverlapMs)
            {
                other.Position = TimeSpan.Zero;
                other.Play();
                contCurrent = other;
            }
        }
        catch { }
    }

    private static void OnPauseContinuous()
    {
        try
        {
            contPaused = true;
            if (contA != null) contA.Pause();
            if (contB != null) contB.Pause();
        }
        catch { }
    }

    private static void OnResumeContinuous()
    {
        try
        {
            contPaused = false;
            if (contCurrent != null) contCurrent.Play();
        }
        catch { }
    }

    private static void OnStopContinuous()
    {
        try
        {
            StopPlayersInternal();
            contFilePath = null;
            contPaused = false;
        }
        catch { }
    }

    private static void StopPlayersInternal()
    {
        if (contTimer != null) contTimer.Stop();
        if (contA != null) { try { contA.Stop(); contA.Close(); } catch { } contA = null; }
        if (contB != null) { try { contB.Stop(); contB.Close(); } catch { } contB = null; }
        contCurrent = null;
    }

    private static void OnSetVolume(float vol)
    {
        try
        {
            contVolume = vol;
            if (contA != null) contA.Volume = vol;
            if (contB != null) contB.Volume = vol;
        }
        catch { }
    }
}
