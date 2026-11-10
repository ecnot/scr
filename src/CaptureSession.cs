using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Scr;

/// <summary>
/// One capture: freezes the screen, shows a dimmed overlay on every monitor and lets you drag a region,
/// click a window, or press Enter for the whole screen. All positions are physical screen pixels.
/// </summary>
public sealed class CaptureSession
{
    static CaptureSession? current;
    public static bool IsActive => current != null;

    BitmapSource? full;
    Int32Rect virt;
    List<Int32Rect> windows = new();
    List<ScreenCapture.Monitor> monitors = new();
    readonly List<OverlayWindow> overlays = new();
    Point? down;
    bool done;

    public Int32Rect Hole { get; private set; }
    public Point Cursor { get; private set; }
    public bool Dragging { get; private set; }
    public bool ShowHint { get; private set; } = true;

    public static void Start()
    {
        if (current != null) return;
        current = new CaptureSession();
        try { current.Begin(); }
        catch (Exception ex)
        {
            current.CloseAll();
            current = null;
            MessageBox.Show("Scr couldn't capture the screen.\n\n" + ex.Message, "Scr");
        }
    }

    /// <summary>Captures the monitor under the mouse straight away, without the overlay.</summary>
    public static void FullScreenNow()
    {
        if (current != null) return;
        var cursor = ScreenCapture.CursorPosition();
        var mon = ScreenCapture.Monitors().FirstOrDefault(m => ScreenCapture.Contains(m.Bounds, cursor));
        if (mon.Bounds.Width == 0) mon = ScreenCapture.Monitors()[0];
        var bmp = ScreenCapture.Capture(mon.Bounds);
        App.Instance.OpenEditor(bmp, mon.Scale);
    }

    void Begin()
    {
        windows = ScreenCapture.Windows();
        virt = ScreenCapture.VirtualScreen;
        full = ScreenCapture.Capture(virt);
        monitors = ScreenCapture.Monitors();
        Cursor = ScreenCapture.CursorPosition();
        UpdateHover();

        foreach (var m in monitors)
        {
            var part = ScreenCapture.Intersect(m.Bounds, virt);
            if (part.Width == 0) continue;
            var crop = new CroppedBitmap(full, new Int32Rect(part.X - virt.X, part.Y - virt.Y, part.Width, part.Height));
            crop.Freeze();
            overlays.Add(new OverlayWindow(this, m, crop));
        }
        foreach (var o in overlays) o.Show();

        var active = overlays.FirstOrDefault(o => ScreenCapture.Contains(o.Monitor.Bounds, Cursor)) ?? overlays[0];
        active.Activate();
        Native.SetForegroundWindow(new WindowInteropHelper(active).Handle);
        active.Focus();
        RefreshAll();
    }

    public void Down(Point p)
    {
        down = p;
        Cursor = p;
        ShowHint = false;
        RefreshAll();
    }

    public void Move(Point p)
    {
        Cursor = p;
        if (down is Point d && !Dragging && (Math.Abs(p.X - d.X) > 4 || Math.Abs(p.Y - d.Y) > 4))
            Dragging = true;
        if (Dragging && down is Point s)
        {
            int x0 = (int)Math.Floor(Math.Min(s.X, p.X)), y0 = (int)Math.Floor(Math.Min(s.Y, p.Y));
            int x1 = (int)Math.Floor(Math.Max(s.X, p.X)) + 1, y1 = (int)Math.Floor(Math.Max(s.Y, p.Y)) + 1;
            Hole = ScreenCapture.Intersect(new Int32Rect(x0, y0, x1 - x0, y1 - y0), virt);
        }
        else if (down == null)
        {
            UpdateHover();
        }
        RefreshAll();
    }

    public void Up(Point p)
    {
        Move(p);
        if (down == null) return;
        Finish(Hole);
    }

    public void WholeScreen()
    {
        var mon = monitors.FirstOrDefault(m => ScreenCapture.Contains(m.Bounds, Cursor));
        Finish(mon.Bounds.Width > 0 ? mon.Bounds : virt);
    }

    public void Cancel()
    {
        if (done) return;
        done = true;
        CloseAll();
        App.Instance.ScheduleTrim();
    }

    /// <summary>What a click would take: the window under the mouse, or else the whole monitor.</summary>
    void UpdateHover()
    {
        foreach (var w in windows)
        {
            if (ScreenCapture.Contains(w, Cursor))
            {
                var r = ScreenCapture.Intersect(w, virt);
                if (r.Width > 0) { Hole = r; return; }
            }
        }
        var mon = monitors.FirstOrDefault(m => ScreenCapture.Contains(m.Bounds, Cursor));
        Hole = mon.Bounds.Width > 0 ? mon.Bounds : virt;
    }

    void Finish(Int32Rect r)
    {
        if (done || full == null) return;
        done = true;
        r = ScreenCapture.Intersect(r, virt);
        BitmapSource? bmp = null;
        double scale = 1;
        if (r.Width >= 2 && r.Height >= 2)
        {
            bmp = ScreenCapture.Copy(full, new Int32Rect(r.X - virt.X, r.Y - virt.Y, r.Width, r.Height));
            var centre = new Point(r.X + r.Width / 2.0, r.Y + r.Height / 2.0);
            scale = monitors.FirstOrDefault(m => ScreenCapture.Contains(m.Bounds, centre)).Scale;
            if (scale <= 0) scale = 1;
        }
        CloseAll();
        if (bmp != null) App.Instance.OpenEditor(bmp, scale);
        else App.Instance.ScheduleTrim();
    }

    void RefreshAll()
    {
        foreach (var o in overlays) o.Refresh();
    }

    void CloseAll()
    {
        foreach (var o in overlays) o.Hide();
        foreach (var o in overlays) o.Close();
        overlays.Clear();
        full = null;
        current = null;
    }
}
