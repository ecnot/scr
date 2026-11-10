using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static Scr.Native;

namespace Scr;

/// <summary>Grabs screen pixels with GDI (a few milliseconds) and lists monitors and windows in real pixels.</summary>
public static class ScreenCapture
{
    public readonly record struct Monitor(Int32Rect Bounds, double Scale);
    public readonly record struct WindowInfo(IntPtr Handle, Int32Rect Bounds);

    public static Int32Rect VirtualScreen => new(
        GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN),
        GetSystemMetrics(SM_CXVIRTUALSCREEN), GetSystemMetrics(SM_CYVIRTUALSCREEN));

    public static List<Monitor> Monitors()
    {
        var list = new List<Monitor>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref RECT r, IntPtr data) =>
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(hMon, ref mi))
            {
                double scale = 1;
                if (GetDpiForMonitor(hMon, 0, out uint dx, out _) == 0 && dx > 0) scale = dx / 96.0;
                list.Add(new Monitor(ToRect(mi.rcMonitor), scale));
            }
            return true;
        }, IntPtr.Zero);
        if (list.Count == 0) list.Add(new Monitor(VirtualScreen, 1));
        return list;
    }

    /// <summary>Visible top-level windows, front to back, with their visible bounds (no invisible resize borders).</summary>
    public static List<WindowInfo> Windows()
    {
        var list = new List<WindowInfo>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || IsIconic(h)) return true;
            if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, 4) == 0 && cloaked != 0) return true;
            long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
            if ((ex & WS_EX_TRANSPARENT) != 0) return true;
            var cls = ClassName(h);
            if (cls is "Progman" or "WorkerW") return true;
            if (DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT r, Marshal.SizeOf<RECT>()) != 0)
                GetWindowRect(h, out r);
            if (r.Right - r.Left < 24 || r.Bottom - r.Top < 24) return true;
            list.Add(new WindowInfo(h, ToRect(r)));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>
    /// The monitor covered by the app in front, if that app is full screen (a game, a video, a presentation).
    /// Null for normal and maximised windows, the desktop and the taskbar.
    /// </summary>
    public static Monitor? FullScreenAppMonitor()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return null;
        GetWindowThreadProcessId(fg, out uint pid);
        if (pid == (uint)Environment.ProcessId) return null;
        if (ClassName(fg) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return null;
        if (!GetWindowRect(fg, out RECT r)) return null;
        foreach (var m in Monitors())
        {
            var b = m.Bounds;
            if (r.Left <= b.X && r.Top <= b.Y && r.Right >= b.X + b.Width && r.Bottom >= b.Y + b.Height)
                return m;
        }
        return null;
    }

    /// <summary>Copies a part of the screen (in physical pixels) to an opaque bitmap.</summary>
    public static BitmapSource Capture(Int32Rect r)
    {
        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr mem = CreateCompatibleDC(screen);
        IntPtr bmp = CreateCompatibleBitmap(screen, r.Width, r.Height);
        IntPtr old = SelectObject(mem, bmp);
        try
        {
            BitBlt(mem, 0, 0, r.Width, r.Height, screen, r.X, r.Y, SRCCOPY | CAPTUREBLT);
            SelectObject(mem, old);
            var src = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            return Copy(src, new Int32Rect(0, 0, r.Width, r.Height));
        }
        finally
        {
            DeleteObject(bmp);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>
    /// The window's own picture, the way Alt+Tab previews are made: just its content, without the border Windows
    /// draws around it or the desktop in its rounded corners, and whole even when other windows cover part of it.
    /// Null if Windows can't give one (some apps that draw with the graphics card come back black).
    /// </summary>
    public static BitmapSource? CaptureWindow(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out RECT wr)) return null;
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT fr, Marshal.SizeOf<RECT>()) != 0) fr = wr;
        int w = wr.Right - wr.Left, h = wr.Bottom - wr.Top;
        if (w <= 0 || h <= 0 || w > 16384 || h > 16384) return null;

        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr mem = CreateCompatibleDC(screen);
        IntPtr bmp = CreateCompatibleBitmap(screen, w, h);
        IntPtr old = SelectObject(mem, bmp);
        try
        {
            const uint PW_RENDERFULLCONTENT = 2;
            bool ok = PrintWindow(hwnd, mem, PW_RENDERFULLCONTENT);
            SelectObject(mem, old);
            if (!ok) return null;
            var src = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            // Only the visible window: the invisible resize borders around it are cut off.
            var part = Intersect(new Int32Rect(fr.Left - wr.Left, fr.Top - wr.Top, fr.Right - fr.Left, fr.Bottom - fr.Top), new Int32Rect(0, 0, w, h));
            if (part.Width < 2 || part.Height < 2) return null;
            var picture = Copy(src, part);
            return LooksEmpty(picture) ? null : picture;
        }
        catch
        {
            return null;
        }
        finally
        {
            DeleteObject(bmp);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>True if a picture is (almost) all black, which is how a window that can't be printed comes back.</summary>
    static bool LooksEmpty(BitmapSource bmp)
    {
        int w = bmp.PixelWidth, h = bmp.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        bmp.CopyPixels(px, stride, 0);
        int lit = 0, n = 0;
        for (int y = 0; y < h; y += Math.Max(1, h / 40))
            for (int x = 0; x < w; x += Math.Max(1, w / 40))
            {
                int i = y * stride + x * 4;
                if (px[i] + px[i + 1] + px[i + 2] > 30) lit++;
                n++;
            }
        return n == 0 || lit < n * 0.03;
    }

    /// <summary>An independent opaque copy of part of a bitmap (so the big full-screen grab can be let go).</summary>
    public static BitmapSource Copy(BitmapSource src, Int32Rect part)
    {
        var conv = new FormatConvertedBitmap(src, PixelFormats.Bgr32, null, 0);
        int stride = part.Width * 4;
        var px = new byte[stride * part.Height];
        conv.CopyPixels(part, px, stride, 0);
        var bmp = BitmapSource.Create(part.Width, part.Height, 96, 96, PixelFormats.Bgr32, null, px, stride);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>An independent copy of part of a picture that keeps see-through parts (for cropping).</summary>
    public static BitmapSource CopyKeepAlpha(BitmapSource src, Int32Rect part)
    {
        part = Intersect(part, new Int32Rect(0, 0, src.PixelWidth, src.PixelHeight));
        var conv = new FormatConvertedBitmap(src, PixelFormats.Pbgra32, null, 0);
        int stride = part.Width * 4;
        var px = new byte[stride * part.Height];
        conv.CopyPixels(part, px, stride, 0);
        var bmp = BitmapSource.Create(part.Width, part.Height, 96, 96, PixelFormats.Pbgra32, null, px, stride);
        bmp.Freeze();
        return bmp;
    }

    public static Int32Rect Intersect(Int32Rect a, Int32Rect b)
    {
        int x0 = Math.Max(a.X, b.X), y0 = Math.Max(a.Y, b.Y);
        int x1 = Math.Min(a.X + a.Width, b.X + b.Width), y1 = Math.Min(a.Y + a.Height, b.Y + b.Height);
        return x1 > x0 && y1 > y0 ? new Int32Rect(x0, y0, x1 - x0, y1 - y0) : Int32Rect.Empty;
    }

    public static bool Contains(Int32Rect r, Point p) =>
        p.X >= r.X && p.Y >= r.Y && p.X < r.X + r.Width && p.Y < r.Y + r.Height;

    public static Point CursorPosition()
    {
        GetCursorPos(out var p);
        return new Point(p.X, p.Y);
    }

    static Int32Rect ToRect(RECT r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
}
