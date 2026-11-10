using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Scr;

public partial class App : Application
{
    public static App Instance => (App)Current;
    public AppSettings Settings { get; private set; } = new();
    public TrayIcon? Tray { get; private set; }

    const string MutexName = @"Local\Scr-ecnot-instance";
    const string SignalName = @"Local\Scr-ecnot-capture";
    const string QuitName = @"Local\Scr-ecnot-quit";
    Mutex? mutex;
    EventWaitHandle? signal, quitSignal;
    RegisteredWaitHandle? waiter, quitWaiter;
    SettingsWindow? settingsWindow;
    KeyboardHook? keyHook;
    public static readonly (int Mods, int Vk) WinShiftS = (Native.MOD_WIN | Native.MOD_SHIFT, 0x53);

    /// <summary>
    /// Registers the shortcut the normal way. If Windows or another app already has it, the keyboard hook catches it
    /// instead. The hook also takes Win + Shift + S from Snipping Tool when that's switched on.
    /// Returns true if the shortcut had to go through the hook.
    /// </summary>
    public bool ApplyShortcuts()
    {
        if (Tray == null || keyHook == null) return false;
        var hooked = new System.Collections.Generic.List<(int, int)>();
        bool viaHook = !Tray.SetHotkey(Settings.HotkeyMods, Settings.HotkeyVk);
        if (viaHook) hooked.Add((Settings.HotkeyMods, Settings.HotkeyVk));
        if (Settings.TakeOverWinShiftS) hooked.Add(WinShiftS);
        keyHook.SetTargets(hooked);
        return viaHook;
    }

    public void PauseShortcuts()
    {
        Tray?.PauseHotkey();
        keyHook?.Pause();
    }

    public void ResumeShortcuts()
    {
        Tray?.ResumeHotkey();
        keyHook?.Resume();
    }
    DispatcherTimer? trimTimer;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

#if DEBUG
        if (e.Args.Length >= 1 && e.Args[0] == "--preview")
        {
            try { DevPreview.Run(e.Args.Skip(1).ToArray()); }
            catch (Exception ex) { File.WriteAllText(Path.Combine(e.Args.Length > 1 ? e.Args[1] : ".", "error.txt"), ex.ToString()); }
            Shutdown();
            return;
        }
#endif
        bool trayOnly = e.Args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
        bool quit = e.Args.Any(a => a.Equals("--quit", StringComparison.OrdinalIgnoreCase));

        // One Scr at a time. Starting it again (Start menu, pinned icon) just takes a capture;
        // "Scr.exe --quit" closes the running one (handy before updating).
        mutex = new Mutex(true, MutexName, out bool first);
        if (!first || quit)
        {
            if (!first && !trayOnly)
            {
                try
                {
                    Native.AllowSetForegroundWindow(-1);
                    using var ev = EventWaitHandle.OpenExisting(quit ? QuitName : SignalName);
                    ev.Set();
                }
                catch { }
            }
            Shutdown();
            return;
        }
        signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
        waiter = ThreadPool.RegisterWaitForSingleObject(signal,
            (_, _) => Dispatcher.BeginInvoke(new Action(StartCapture)), null, -1, false);
        quitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, QuitName);
        quitWaiter = ThreadPool.RegisterWaitForSingleObject(quitSignal,
            (_, _) => Dispatcher.BeginInvoke(new Action(() => Shutdown())), null, -1, true);

        AppSettings.MigrateFromSnap();
        Settings = AppSettings.Load();
        ThemeColors.Apply(Resources, ThemeColors.SystemIsDark(), GlassWindow.HasAcrylic);

        Tray = new TrayIcon();
        Tray.Capture += StartCapture;
        Tray.FullScreen += () => { RefreshTheme(); CaptureSession.FullScreenNow(); };
        Tray.Settings += () => ShowSettings(false);
        Tray.OpenFolder += OpenSaveFolder;
        Tray.Quit += () => Shutdown();

        keyHook = new KeyboardHook(Dispatcher, StartCapture);
        ApplyShortcuts();

        if (!Settings.Welcomed)
        {
            Settings.Welcomed = true;
            Settings.Save();
            ShowSettings(true);
        }
        else if (!trayOnly)
        {
            StartCapture();
        }
        ScheduleTrim(4000);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        keyHook?.Dispose();
        Tray?.Dispose();
        waiter?.Unregister(null);
        quitWaiter?.Unregister(null);
        signal?.Dispose();
        quitSignal?.Dispose();
        mutex?.Dispose();
        base.OnExit(e);
    }

    void RefreshTheme() => ThemeColors.Apply(Resources, ThemeColors.SystemIsDark(), GlassWindow.HasAcrylic);

    public void StartCapture()
    {
        if (CaptureSession.IsActive) return;
        RefreshTheme();
        CaptureSession.Start();
    }

    public void OpenEditor(BitmapSource capture, double scale)
    {
        var editor = new EditorWindow(capture, scale);
        editor.Closed += (_, _) => ScheduleTrim();
        editor.Show();
        editor.Activate();
        // The plain screenshot goes on the clipboard at once; Copy in the editor swaps in the framed one.
        if (Settings.CopyOnCapture)
            editor.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(editor.CopyPlainQuietly));
    }

    public void ShowSettings(bool welcome)
    {
        if (settingsWindow != null)
        {
            settingsWindow.Activate();
            return;
        }
        RefreshTheme();
        settingsWindow = new SettingsWindow(welcome);
        settingsWindow.Closed += (_, _) => { settingsWindow = null; ScheduleTrim(); };
        settingsWindow.Show();
        settingsWindow.Activate();
    }

    public void OpenSaveFolder()
    {
        try
        {
            var folder = Settings.SaveFolderOrDefault;
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch { }
    }

    /// <summary>Hands unused memory back to Windows a little after the last window closes.</summary>
    public void ScheduleTrim(int ms = 1500)
    {
        trimTimer?.Stop();
        trimTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        trimTimer.Tick += (_, _) =>
        {
            trimTimer?.Stop();
            if (!CaptureSession.IsActive) Native.TrimMemory();
        };
        trimTimer.Start();
    }
}
