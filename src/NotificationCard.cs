using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Scr;

/// <summary>
/// Scr's own "screenshot copied" card, bottom right of the screen you're on. Windows' notifications can be held back
/// (Do not disturb, full-screen apps), so this one always shows. It never takes focus, fades out after a few seconds
/// (not while the mouse is on it), and clicking it opens the editor.
/// </summary>
public sealed class NotificationCard : Window
{
    static NotificationCard? current;

    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(6) };
    readonly Action open;
    readonly TranslateTransform slide = new(0, 14);
    bool closing;

    const double CardWidth = 300;

    /// <summary>Shows the card for a new capture, replacing any card already showing.</summary>
    public static void Show(BitmapSource capture, string title, string note, Action open)
    {
        HideNow();
        current = new NotificationCard(capture, title, note, open);
        current.Show();
    }

    /// <summary>Takes the card away at once (before the next capture, so it isn't in the picture).</summary>
    public static void HideNow()
    {
        if (current == null) return;
        var c = current;
        current = null;
        c.closing = true;
        c.Close();
    }

    public NotificationCard(BitmapSource capture, string title, string note, Action open)
    {
        this.open = open;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Title = "Scr";
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        SetResourceReference(ForegroundProperty, "B.Ink");
        Content = BuildCard(capture, title, note);
        Opacity = 0;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000; Top = -32000; // placed exactly once Windows has given it a handle

        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            // A tool window that never takes focus: it doesn't pull you out of what you're doing, or show in Alt+Tab.
            long ex = GetWindowLongPtr(h, -20).ToInt64();
            SetWindowLongPtr(h, -20, new IntPtr(ex | 0x08000000 /* NOACTIVATE */ | 0x80 /* TOOLWINDOW */));
        };
        Loaded += (_, _) => { PlaceBottomRight(); Appear(); };
        MouseEnter += (_, _) => timer.Stop();
        MouseLeave += (_, _) => { if (!closing) timer.Start(); };
        timer.Tick += (_, _) => FadeAway();
    }

    FrameworkElement BuildCard(BitmapSource capture, string title, string note)
    {
        // The picture, as big as fits: 276 wide, at most 160 tall.
        double pw = CardWidth - 24, k = Math.Min(pw / capture.PixelWidth, 160.0 / capture.PixelHeight);
        double w = Math.Max(40, Math.Round(capture.PixelWidth * k)), h = Math.Max(30, Math.Round(capture.PixelHeight * k));
        var picture = new Image { Source = capture, Width = w, Height = h, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
        picture.Clip = new RectangleGeometry(new Rect(0, 0, w, h), 4, 4);
        var pictureBox = new Border
        {
            Child = picture,
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = (Brush)FindResource("Checker"),
            CornerRadius = new CornerRadius(4),
        };

        var titleText = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 13.5 };
        var noteText = new TextBlock { Text = note, FontSize = 12.5, Margin = new Thickness(0, 1, 0, 0) };
        noteText.SetResourceReference(TextBlock.ForegroundProperty, "B.Muted");
        var words = new StackPanel { Margin = new Thickness(2, 10, 0, 0) };
        words.Children.Add(titleText);
        words.Children.Add(noteText);

        var close = new Button
        {
            Style = (Style)FindResource("TabClose"),
            ToolTip = "Dismiss",
            Width = 24,
            Height = 24,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -4, -4, 0),
            Opacity = 0,
            Content = new Viewbox
            {
                Width = 10,
                Height = 10,
                Child = new System.Windows.Shapes.Path { Data = (Geometry)FindResource("I.X"), Style = (Style)FindResource("Icon"), StrokeThickness = 2.2 },
            },
        };
        close.Click += (_, e) => { e.Handled = true; FadeAway(); };

        var body = new Grid();
        var stack = new StackPanel();
        stack.Children.Add(pictureBox);
        stack.Children.Add(words);
        body.Children.Add(stack);
        body.Children.Add(close);

        var card = new Border
        {
            Width = CardWidth,
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Child = body,
            Cursor = Cursors.Hand,
            RenderTransform = slide,
            Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 4, Direction = 270, Opacity = 0.22, Color = Color.FromRgb(0x1E, 0x1E, 0x46) },
        };
        card.SetResourceReference(Border.BackgroundProperty, "B.Menu");
        card.SetResourceReference(Border.BorderBrushProperty, "B.MenuEdge");
        card.MouseEnter += (_, _) => close.Opacity = 1;
        card.MouseLeave += (_, _) => close.Opacity = 0;
        card.MouseLeftButtonUp += (_, _) => { HideNow(); open(); };

        // Room around the card for its shadow.
        return new Border { Padding = new Thickness(20, 16, 20, 24), Child = card };
    }

    /// <summary>Bottom right of the work area (above the taskbar) of the screen the mouse is on.</summary>
    void PlaceBottomRight()
    {
        var h = new WindowInteropHelper(this).Handle;
        Native.GetCursorPos(out var pt);
        IntPtr mon = MonitorFromPoint(pt, 2 /* nearest */);
        var mi = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(mon, ref mi)) return;
        double scale = Native.GetDpiForMonitor(mon, 0, out uint dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1;
        var content = (FrameworkElement)Content;
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        int w = (int)Math.Ceiling(content.DesiredSize.Width * scale), hh = (int)Math.Ceiling(content.DesiredSize.Height * scale);
        int x = mi.rcWork.Right - w, y = mi.rcWork.Bottom - hh;
        Native.SetWindowPos(h, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOACTIVATE | 0x0001 /* NOSIZE */);
    }

    void Appear()
    {
        bool motion = SystemParameters.ClientAreaAnimation;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(motion ? 180 : 0)));
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(motion ? 220 : 0)) { EasingFunction = ease });
        timer.Start();
    }

    void FadeAway()
    {
        if (closing) return;
        closing = true;
        timer.Stop();
        if (current == this) current = null;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? 220 : 0));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }

    protected override void OnClosed(EventArgs e)
    {
        timer.Stop();
        if (current == this) current = null;
        base.OnClosed(e);
    }

#if DEBUG
    /// <summary>For the preview pictures: the card's content without a window.</summary>
    internal static FrameworkElement ContentForPreview(BitmapSource capture) =>
        (FrameworkElement)new NotificationCard(capture, "Screenshot copied", "Click to frame it", () => { }).Content;
#endif

    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Native.POINT pt, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
}
