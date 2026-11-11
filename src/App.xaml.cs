using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
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

    // A test copy (SCR_DATA set) gets its own names, so it never talks to the Scr you're using.
    static readonly string Suffix = Environment.GetEnvironmentVariable("SCR_DATA") is { Length: > 0 } d
        ? "-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(d.ToLowerInvariant())))[..12] : "";
    static readonly string MutexName = @"Local\Scr-ecnot-instance" + Suffix;
    static readonly string SignalName = @"Local\Scr-ecnot-capture" + Suffix;
    static readonly string QuitName = @"Local\Scr-ecnot-quit" + Suffix;
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
        // The new version finishing an update from its temporary folder.
        if (e.Args.Length >= 3 && e.Args[0] == "--finish-update" && int.TryParse(e.Args[2], out int oldPid))
        {
            Updater.FinishUpdate(e.Args[1], oldPid);
            Shutdown();
            return;
        }
        bool trayOnly = e.Args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
        bool justUpdated = e.Args.Any(a => a.Equals("--updated", StringComparison.OrdinalIgnoreCase));
        bool updateNow = e.Args.Any(a => a.Equals("--update-now", StringComparison.OrdinalIgnoreCase));
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
        RefreshTheme();

        Tray = new TrayIcon();
        Tray.Capture += StartCapture;
        Tray.FullScreen += () => { RefreshTheme(); CaptureSession.FullScreenNow(); };
        Tray.OpenEditor += ShowEditor;
        Tray.Reopen += ReopenClosed;
        ClosedCaptures.Changed += () => Dispatcher.BeginInvoke(new Action(() => Tray?.SetReopenVisible(ClosedCaptures.Count > 0)));
        Tray.BalloonClicked += () => { if (balloonIsUpdate) InstallUpdate(); else ShowEditor(); };
        Tray.Update += InstallUpdate;
        Tray.OpenPicture += () => OpenPictures(AskForPictures(null), "Scr can't open that picture.");
        Tray.PastePicture += () => OpenPictures(Import.FromClipboard(), "There's no picture on the clipboard.");
        Tray.Settings += () => ShowSettings(false);
        Tray.OpenFolder += OpenSaveFolder;
        Tray.Quit += () => Shutdown();

        keyHook = new KeyboardHook(Dispatcher, StartCapture);
        if (!TestMode) ApplyShortcuts();

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
        if (justUpdated) ShowBalloon("Scr is updated", $"You now have Scr {Updater.CurrentText}. Your settings are just as they were.");
        if (updateNow) _ = CheckForUpdates(userAsked: true, installIfFound: true);
        else StartUpdateChecks();
        ScheduleTrim(4000);
    }

    // ---------- Updates ----------

    public UpdateInfo? AvailableUpdate { get; private set; }
    public string UpdateStatus { get; private set; } = "";
    public bool UpdateBusy { get; private set; }
    public event Action? UpdateStateChanged;
    DispatcherTimer? updateTimer;
    bool balloonIsUpdate;

    /// <summary>A test copy (SCR_TEST=1) leaves your shortcuts alone and shows no notices.</summary>
    static bool TestMode => Environment.GetEnvironmentVariable("SCR_TEST") == "1";

    public void ShowBalloon(string title, string text, bool isUpdate = false)
    {
        balloonIsUpdate = isUpdate;
        if (TestMode) return;
        Tray?.ShowBalloon(title, text);
    }

    void SetUpdateState(string status)
    {
        UpdateStatus = status;
        Tray?.SetUpdateItem(AvailableUpdate == null ? null : $"Update to Scr {AvailableUpdate.Version.ToString(3)}");
        UpdateStateChanged?.Invoke();
    }

    /// <summary>A minute after starting, then every hour: check if it's been a day since the last check.</summary>
    void StartUpdateChecks()
    {
        updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        updateTimer.Tick += (_, _) =>
        {
            updateTimer.Interval = TimeSpan.FromHours(1);
            if (!Settings.CheckForUpdates) return;
            if (Settings.LastUpdateCheck is DateTime last && DateTime.Now - last < TimeSpan.FromHours(20)) return;
            _ = CheckForUpdates(userAsked: false);
        };
        updateTimer.Start();
    }

    public async System.Threading.Tasks.Task CheckForUpdates(bool userAsked, bool installIfFound = false)
    {
        if (UpdateBusy) return;
        UpdateBusy = true;
        SetUpdateState("Checking…");
        try
        {
            var found = await Updater.CheckAsync();
            Settings.LastUpdateCheck = DateTime.Now;
            Settings.Save();
            AvailableUpdate = found;
            UpdateBusy = false;
            if (found == null)
            {
                SetUpdateState($"You have the latest version ({Updater.CurrentText}).");
                return;
            }
            SetUpdateState($"Scr {found.Version.ToString(3)} is ready.");
            if (installIfFound) InstallUpdate();
            else if (!userAsked) ShowBalloon($"Scr {found.Version.ToString(3)} is ready", "Click here to update. It only takes a moment, and your settings stay.", isUpdate: true);
        }
        catch (Exception ex)
        {
            UpdateBusy = false;
            SetUpdateState(userAsked ? "Couldn't reach GitHub. Check your connection and try again." : "");
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    /// <summary>Downloads the update, starts the new version to swap the files, and quits.</summary>
    public async void InstallUpdate()
    {
        if (AvailableUpdate is not UpdateInfo u || UpdateBusy) return;
        if (editor != null && editor.TabCount > 0 && Environment.GetEnvironmentVariable("SCR_DATA") is not { Length: > 0 })
        {
            var answer = MessageBox.Show($"Scr will close the editor and restart as {u.Version.ToString(3)}. Your open captures will be closed.\n\nUpdate now?",
                "Update Scr", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (answer != MessageBoxResult.OK) return;
        }
        UpdateBusy = true;
        SetUpdateState($"Downloading Scr {u.Version.ToString(3)}…");
        try
        {
            await Updater.DownloadAndStartAsync(u);
            Shutdown();
        }
        catch (Exception ex)
        {
            UpdateBusy = false;
            SetUpdateState("The update didn't work: " + ex.Message);
            ShowBalloon("Scr couldn't update", ex.Message + " You can download it from GitHub instead.");
        }
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

    /// <summary>Applies the chosen theme and light/dark to every open window.</summary>
    public void RefreshTheme()
    {
        bool dark = Settings.ColorMode switch
        {
            ColorMode.Light => false,
            ColorMode.Dark => true,
            _ => ThemeColors.SystemIsDark(),
        };
        bool changed = dark != ThemeColors.IsDark;
        ThemeColors.Apply(Resources, Settings.Theme, dark, GlassWindow.HasAcrylic, Settings.GlassLevel);
        if (changed)
            foreach (Window w in Windows)
                if (w is GlassWindow g) g.ApplyBackdrop();
    }

    EditorWindow? editor;
    readonly List<(BitmapSource Capture, double Scale)> waiting = new(); // quiet captures before the editor is open

    public void StartCapture()
    {
        if (CaptureSession.IsActive) return;
        RefreshTheme();
        // A full-screen game minimises if anything takes focus, so capture its screen quietly instead.
        if (Settings.QuietFullScreen && ScreenCapture.FullScreenAppMonitor() is ScreenCapture.Monitor m)
        {
            try { OpenEditor(ScreenCapture.Capture(m.Bounds), m.Scale, quiet: true); }
            catch (Exception ex) { ShowBalloon("Scr couldn't capture the screen", ex.Message); }
            return;
        }
        CaptureSession.Start();
    }

    /// <summary>
    /// Puts the plain screenshot on the clipboard and adds it to the editor as a new tab.
    /// Quiet captures (from a full-screen app) never take focus; they wait until you open the editor.
    /// </summary>
    public void OpenEditor(BitmapSource capture, double scale, bool quiet = false)
    {
        if (quiet)
        {
            bool copied = Settings.CopyOnCapture && TryCopy(capture);
            if (editor != null) editor.AddCapture(capture);
            else waiting.Add((capture, scale));
            ShowBalloon("Screenshot taken",
                (copied ? "It's on your clipboard. " : "") + "Your full-screen app kept running. Click here to frame it.");
            return;
        }

        if (editor != null)
        {
            editor.AddCapture(capture);
            if (editor.WindowState == WindowState.Minimized) editor.WindowState = WindowState.Normal;
            editor.Activate();
        }
        else
        {
            CreateEditor(capture, scale);
        }
        // The plain screenshot goes on the clipboard at once; Copy in the editor swaps in the framed one.
        if (Settings.CopyOnCapture)
        {
            var ed = editor!;
            ed.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (TryCopy(capture)) ed.ShowNote("Screenshot copied. Copy swaps in the framed one.");
            }));
        }
    }

    static bool TryCopy(BitmapSource bmp)
    {
        try { ClipboardImage.Set(bmp); return true; }
        catch { return false; }
    }

    /// <summary>Asks for one or more picture files and opens them. An empty list if cancelled.</summary>
    public static List<(BitmapSource Picture, string? Name)> AskForPictures(Window? owner)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Open pictures to frame", Filter = Import.Filter, Multiselect = true };
        var list = new List<(BitmapSource, string?)>();
        if (dlg.ShowDialog(owner) != true) return list;
        foreach (var f in dlg.FileNames)
        {
            try { list.Add((Import.LoadFile(f), System.IO.Path.GetFileNameWithoutExtension(f))); }
            catch { }
        }
        if (list.Count == 0) Instance.ShowBalloon("Scr can't open that picture", "It may be damaged or in a format Windows can't read.");
        return list;
    }

    /// <summary>Opens pictures (from a file or the clipboard) in the editor, without touching the clipboard.</summary>
    public void OpenPictures(List<(BitmapSource Picture, string? Name)> pictures, string ifNone)
    {
        if (pictures.Count == 0)
        {
            if (ifNone.StartsWith("There's")) ShowBalloon("Nothing to frame", ifNone);
            return;
        }
        if (editor == null)
        {
            var (first, name) = pictures[0];
            CreateEditor(first, 1, name ?? "Pasted");
            pictures = pictures.GetRange(1, pictures.Count - 1);
        }
        editor!.AddPictures(pictures, ifNone);
        if (editor.WindowState == WindowState.Minimized) editor.WindowState = WindowState.Normal;
        editor.Activate();
    }

    /// <summary>Brings back the most recently closed capture, with its frame and edits, in the editor.</summary>
    public void ReopenClosed() => ReopenClosed(null);

    /// <summary>Brings back a particular closed capture (from the history list), or the most recent one.</summary>
    public void ReopenClosed(ClosedCapture? which)
    {
        var c = which != null ? (ClosedCaptures.Take(which) ? which : null) : ClosedCaptures.Pop();
        if (c == null) return;
        var picture = ClosedCaptures.PictureOf(c);
        if (editor == null) CreateEditor(picture, 1, c.Name, c);
        else
        {
            editor.AddCapture(picture, c.Name, c);
            if (editor.WindowState == WindowState.Minimized) editor.WindowState = WindowState.Normal;
            editor.Activate();
        }
    }

    void CreateEditor(BitmapSource capture, double scale, string? name = null, ClosedCapture? restore = null)
    {
        RefreshTheme();
        var ed = new EditorWindow(capture, scale, name, restore);
        foreach (var w in waiting) ed.AddCapture(w.Capture);
        waiting.Clear();
        ed.Closed += (_, _) => { if (editor == ed) editor = null; ScheduleTrim(); };
        editor = ed;
        ed.Show();
        ed.Activate();
    }

    /// <summary>Shows the editor with any captures waiting (tray menu, or clicking the notice).</summary>
    public void ShowEditor()
    {
        if (editor != null)
        {
            if (editor.WindowState == WindowState.Minimized) editor.WindowState = WindowState.Normal;
            editor.Activate();
        }
        else if (waiting.Count > 0)
        {
            var first = waiting[0];
            waiting.RemoveAt(0);
            CreateEditor(first.Capture, first.Scale);
        }
        else
        {
            // Nothing to show: open it empty, with ways to start.
            RefreshTheme();
            var ed = new EditorWindow();
            ed.Closed += (_, _) => { if (editor == ed) editor = null; ScheduleTrim(); };
            editor = ed;
            ed.Show();
            ed.Activate();
        }
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
