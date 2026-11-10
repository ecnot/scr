using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace Scr;

public partial class SettingsWindow : GlassWindow
{
    bool recording;
    bool loading = true;
    AppSettings S => App.Instance.Settings;

    public SettingsWindow(bool welcome)
    {
        InitializeComponent();

        if (welcome)
        {
            Welcome.Visibility = Visibility.Visible;
            string keys = Hotkey.Format(S.HotkeyMods, S.HotkeyVk) + (S.TakeOverWinShiftS ? " or Win + Shift + S" : "");
            WelcomeText.Text = $"Scr waits in the tray (the icon by the clock). Press {keys} to capture; the screenshot is " +
                               "on your clipboard at once. Frame it in the editor, then Copy or press Enter.";
        }

        ShowShortcut();
        WinShiftS.IsChecked = S.TakeOverWinShiftS;
        CopyNow.IsChecked = S.CopyOnCapture;
        CloseAfter.IsChecked = S.CloseAfterCopy;
        FolderText.Text = S.SaveFolderOrDefault;
        try { StartWithWindows.IsChecked = AppSettings.StartWithWindows; } catch { }
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Scr {v?.Major}.{v?.Minor}.{v?.Build}";
        loading = false;

        WinShiftS.Checked += (_, _) => Set(() => { S.TakeOverWinShiftS = true; App.Instance.ApplyShortcuts(); });
        WinShiftS.Unchecked += (_, _) => Set(() => { S.TakeOverWinShiftS = false; App.Instance.ApplyShortcuts(); });
        CopyNow.Checked += (_, _) => Set(() => S.CopyOnCapture = true);
        CopyNow.Unchecked += (_, _) => Set(() => S.CopyOnCapture = false);
        CloseAfter.Checked += (_, _) => Set(() => S.CloseAfterCopy = true);
        CloseAfter.Unchecked += (_, _) => Set(() => S.CloseAfterCopy = false);
        StartWithWindows.Checked += (_, _) => SetStartup(true);
        StartWithWindows.Unchecked += (_, _) => SetStartup(false);
        FolderBtn.Click += (_, _) => ChooseFolder();
        DoneBtn.Click += (_, _) => Close();
        ShortcutBtn.Click += (_, _) => StartRecording();
        KeyboardLink.Click += (_, _) => Open("ms-settings:easeofaccess-keyboard");

        PreviewKeyDown += OnPreviewKey;
        PreviewKeyUp += OnPreviewKeyUp;
        Deactivated += (_, _) => StopRecording();
        Closed += (_, _) => StopRecording();
    }

    void Set(Action change)
    {
        if (loading) return;
        change();
        S.Save();
    }

    void SetStartup(bool on)
    {
        if (loading) return;
        try { AppSettings.StartWithWindows = on; }
        catch (Exception ex) { MessageBox.Show(this, "Scr couldn't change this.\n\n" + ex.Message, "Scr"); }
    }

    void ChooseFolder()
    {
        var dlg = new OpenFolderDialog { Title = "Where should Save… start?", InitialDirectory = S.SaveFolderOrDefault };
        if (dlg.ShowDialog(this) != true) return;
        S.SaveFolder = dlg.FolderName;
        S.Save();
        FolderText.Text = S.SaveFolderOrDefault;
    }

    static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
    }

    // ---------- Shortcut recorder ----------

    void ShowShortcut(string? note = null)
    {
        ShortcutBtn.Content = Hotkey.Format(S.HotkeyMods, S.HotkeyVk);
        ShortcutNote.Text = note ?? "Click to change";
    }

    void StartRecording()
    {
        recording = true;
        App.Instance.PauseShortcuts();
        ShortcutBtn.Content = "Press keys…";
        ShortcutNote.Text = "Esc to cancel";
    }

    void StopRecording()
    {
        if (!recording) return;
        recording = false;
        App.Instance.ResumeShortcuts();
        ShowShortcut();
    }

    void OnPreviewKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !recording) { Close(); e.Handled = true; return; }
        if (!recording) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (Hotkey.IsModifier(key)) return;
        if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None) { StopRecording(); return; }
        if (key == Key.Snapshot) return; // Print Screen only arrives as a key-up
        Commit(key);
    }

    void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (!recording) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Snapshot) { e.Handled = true; Commit(key); }
    }

    void Commit(Key key)
    {
        int mods = Hotkey.ModsFrom(Keyboard.Modifiers);
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (mods == 0 && !Hotkey.WorksAlone(key))
        {
            ShortcutNote.Text = "Add Ctrl, Alt or Shift, so it doesn't type a letter";
            return;
        }
        recording = false;
        App.Instance.ResumeShortcuts();
        S.HotkeyMods = mods;
        S.HotkeyVk = vk;
        S.Save();
        bool taken = App.Instance.ApplyShortcuts();
        ShowShortcut(taken ? "Saved. Scr takes it over from Windows or the app that had it" : "Saved");
    }
}
