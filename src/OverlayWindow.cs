using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Scr;

/// <summary>The frozen screen of one monitor with the dimmed selection on top. One per monitor, so each gets its own DPI.</summary>
public sealed class OverlayWindow : Window
{
    readonly CaptureSession session;
    public ScreenCapture.Monitor Monitor { get; }

    readonly Canvas canvas = new() { IsHitTestVisible = false };
    readonly Path dim = new() { Fill = new SolidColorBrush(Color.FromArgb(0x78, 0x0A, 0x0A, 0x14)) };
    readonly Rectangle edge = new() { Stroke = Brushes.White, StrokeThickness = 1.5, SnapsToDevicePixels = true };
    readonly Rectangle edgeShade = new() { Stroke = new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0)), StrokeThickness = 1, SnapsToDevicePixels = true };
    readonly Line hLine = CrossLine(), vLine = CrossLine();
    readonly Border label;
    readonly TextBlock labelText = new() { Foreground = Brushes.White, FontSize = 12, FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI") };
    readonly Border hint;

    public OverlayWindow(CaptureSession session, ScreenCapture.Monitor monitor, BitmapSource shot)
    {
        this.session = session;
        Monitor = monitor;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = monitor.Bounds.X / monitor.Scale;
        Top = monitor.Bounds.Y / monitor.Scale;
        Width = monitor.Bounds.Width / monitor.Scale;
        Height = monitor.Bounds.Height / monitor.Scale;
        Background = Brushes.Black;
        Cursor = Cursors.Cross;
        Title = "Scr capture";

        var img = new Image { Source = shot, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);

        label = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD9, 0x1D, 0x1D, 0x24)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(7, 3, 7, 4),
            Child = labelText,
        };
        hint = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x1D, 0x1D, 0x24)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 8, 14, 9),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 28, 0, 0),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 13,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                Text = "Drag to pick an area   ·   Click a window   ·   Enter for the whole screen   ·   Esc to cancel",
            },
        };

        canvas.Children.Add(dim);
        canvas.Children.Add(edgeShade);
        canvas.Children.Add(edge);
        canvas.Children.Add(hLine);
        canvas.Children.Add(vLine);
        canvas.Children.Add(label);

        var root = new Grid();
        root.Children.Add(img);
        root.Children.Add(canvas);
        root.Children.Add(hint);
        Content = root;

        SourceInitialized += (_, _) =>
        {
            Place();
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(Hook);
        };
        Loaded += (_, _) => { Place(); Refresh(); };
        SizeChanged += (_, _) => Refresh();
    }

    static Line CrossLine() => new()
    {
        Stroke = new SolidColorBrush(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF)),
        StrokeThickness = 1,
        SnapsToDevicePixels = true,
    };

    /// <summary>Puts the window exactly over its monitor in physical pixels.</summary>
    void Place()
    {
        var b = Monitor.Bounds;
        Native.SetWindowPos(new WindowInteropHelper(this).Handle, Native.HWND_TOPMOST, b.X, b.Y, b.Width, b.Height, Native.SWP_NOACTIVATE);
    }

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        const int WM_DPICHANGED = 0x02E0;
        if (msg == WM_DPICHANGED)
            Dispatcher.BeginInvoke(new Action(Place));
        return IntPtr.Zero;
    }

    Point ScreenPoint(MouseEventArgs e) => PointToScreen(e.GetPosition(this));

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        CaptureMouse();
        session.Down(ScreenPoint(e));
    }

    protected override void OnMouseMove(MouseEventArgs e) => session.Move(ScreenPoint(e));

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        var p = ScreenPoint(e);
        ReleaseMouseCapture();
        session.Up(p);
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e) => session.Cancel();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: session.Cancel(); e.Handled = true; break;
            case Key.Enter:
            case Key.Space:
            case Key.F: session.WholeScreen(); e.Handled = true; break;
        }
    }

    public void Refresh()
    {
        if (PresentationSource.FromVisual(this) == null) return;
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        var hole = session.Hole;
        var p1 = PointFromScreen(new Point(hole.X, hole.Y));
        var p2 = PointFromScreen(new Point(hole.X + hole.Width, hole.Y + hole.Height));
        var local = new Rect(p1, p2);

        dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(0, 0, w, h)), new RectangleGeometry(local));

        SetRect(edge, local, 0.75);
        SetRect(edgeShade, local, 2);

        bool here = ScreenCapture.Contains(Monitor.Bounds, session.Cursor);
        var c = PointFromScreen(session.Cursor);
        hLine.Visibility = vLine.Visibility = here ? Visibility.Visible : Visibility.Collapsed;
        hLine.X1 = 0; hLine.X2 = w; hLine.Y1 = hLine.Y2 = Math.Floor(c.Y) + 0.5;
        vLine.Y1 = 0; vLine.Y2 = h; vLine.X1 = vLine.X2 = Math.Floor(c.X) + 0.5;

        label.Visibility = here ? Visibility.Visible : Visibility.Collapsed;
        labelText.Text = $"{hole.Width} × {hole.Height}";
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var ls = label.DesiredSize;
        double lx = Math.Clamp(local.X, 4, Math.Max(4, w - ls.Width - 4));
        double ly = local.Bottom + 8;
        if (ly + ls.Height > h - 4) ly = local.Y + 8;
        if (ly < 4) ly = 4;
        Canvas.SetLeft(label, lx);
        Canvas.SetTop(label, ly);

        hint.Visibility = session.ShowHint && here ? Visibility.Visible : Visibility.Collapsed;
    }

    static void SetRect(Rectangle r, Rect local, double grow)
    {
        Canvas.SetLeft(r, local.X - grow);
        Canvas.SetTop(r, local.Y - grow);
        r.Width = Math.Max(0, local.Width + 2 * grow);
        r.Height = Math.Max(0, local.Height + 2 * grow);
    }
}
