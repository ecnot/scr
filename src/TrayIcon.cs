using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using static Scr.Native;

namespace Scr;

/// <summary>The tray icon and the global shortcut, both on one hidden native window (no WinForms needed).</summary>
public sealed class TrayIcon : IDisposable
{
    const int WM_TRAY = 0x8001, WM_HOTKEY = 0x0312, WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205;
    const int HotkeyId = 1, NIN_BALLOONUSERCLICK = 0x0405;

    readonly HwndSource source;
    readonly int taskbarCreated;
    IntPtr icon;
    Window? menuHost;
    readonly ContextMenu menu;
    readonly MenuItem captureItem;
    bool hotkeyOn;
    int mods, vk;

    public event Action? Capture, FullScreen, OpenEditor, OpenPicture, PastePicture, Settings, OpenFolder, Quit;

    public TrayIcon()
    {
        var p = new HwndSourceParameters("Scr tray") { Width = 0, Height = 0, WindowStyle = 0, ExtendedWindowStyle = 0x80 /* tool window */ };
        source = new HwndSource(p);
        source.AddHook(Hook);
        taskbarCreated = RegisterWindowMessage("TaskbarCreated");

        string icoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "scr.ico");
        int size = GetSystemMetrics(SM_CXSMICON);
        icon = LoadImage(IntPtr.Zero, icoPath, IMAGE_ICON, size, size, LR_LOADFROMFILE);

        menu = new ContextMenu();
        captureItem = Item("Capture", () => Capture?.Invoke());
        menu.Items.Add(captureItem);
        menu.Items.Add(Item("Capture the whole screen", () => FullScreen?.Invoke()));
        menu.Items.Add(Item("Open the editor", () => OpenEditor?.Invoke()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Open a picture…", () => OpenPicture?.Invoke()));
        menu.Items.Add(Item("Frame the clipboard picture", () => PastePicture?.Invoke()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Open screenshots folder", () => OpenFolder?.Invoke()));
        menu.Items.Add(Item("Settings", () => Settings?.Invoke()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Quit Scr", () => Quit?.Invoke()));
        menu.Closed += (_, _) => menuHost?.Hide();

        Add(NIM_ADD);
    }

    static MenuItem Item(string text, Action click)
    {
        var mi = new MenuItem { Header = text };
        mi.Click += (_, _) => click();
        return mi;
    }

    NOTIFYICONDATA Data(int flags) => new()
    {
        cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = source.Handle,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = WM_TRAY,
        hIcon = icon,
        szTip = "Scr",
        szInfo = "",
        szInfoTitle = "",
    };

    void Add(int message)
    {
        var d = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        d.szTip = vk != 0 ? $"Scr · {Hotkey.Format(mods, vk)} to capture" : "Scr";
        Shell_NotifyIcon(message, ref d);
    }

    public void ShowBalloon(string title, string text)
    {
        var d = Data(NIF_INFO);
        d.szInfoTitle = title;
        d.szInfo = text;
        d.dwInfoFlags = 0;
        Shell_NotifyIcon(NIM_MODIFY, ref d);
    }

    /// <summary>Registers the global shortcut. False if another app already has it.</summary>
    public bool SetHotkey(int newMods, int newVk)
    {
        UnregisterHotKey(source.Handle, HotkeyId);
        hotkeyOn = RegisterHotKey(source.Handle, HotkeyId, newMods | MOD_NOREPEAT, newVk);
        mods = newMods; vk = newVk; // if it failed, the keyboard hook catches it instead
        captureItem.InputGestureText = Hotkey.Format(mods, vk);
        Add(NIM_MODIFY);
        return hotkeyOn;
    }

    /// <summary>Lets go of the shortcut while a new one is being recorded.</summary>
    public void PauseHotkey() => UnregisterHotKey(source.Handle, HotkeyId);

    public void ResumeHotkey()
    {
        if (hotkeyOn) RegisterHotKey(source.Handle, HotkeyId, mods | MOD_NOREPEAT, vk);
    }

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            handled = true;
            Capture?.Invoke();
        }
        else if (msg == WM_TRAY)
        {
            int m = (int)(l.ToInt64() & 0xFFFF);
            if (m == WM_LBUTTONUP) Capture?.Invoke();
            else if (m == NIN_BALLOONUSERCLICK) OpenEditor?.Invoke();
            else if (m == WM_RBUTTONUP) ShowMenu();
            handled = true;
        }
        else if (msg == taskbarCreated)
        {
            Add(NIM_ADD); // Explorer restarted
        }
        return IntPtr.Zero;
    }

    void ShowMenu()
    {
        // A tiny off-screen window takes focus so the menu closes when you click elsewhere.
        menuHost ??= new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Width = 1,
            Height = 1,
            Left = -20000,
            Top = -20000,
            Title = "Scr menu",
        };
        menuHost.Show();
        menuHost.Activate();
        SetForegroundWindow(new WindowInteropHelper(menuHost).Handle);
        menu.PlacementTarget = menuHost;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    public void Dispose()
    {
        var d = Data(0);
        Shell_NotifyIcon(NIM_DELETE, ref d);
        UnregisterHotKey(source.Handle, HotkeyId);
        if (icon != IntPtr.Zero) DestroyIcon(icon);
        menuHost?.Close();
        source.Dispose();
    }
}
