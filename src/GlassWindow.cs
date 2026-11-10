using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace Scr;

/// <summary>
/// A frameless window with Windows 11 acrylic behind it, Windows' own shadow, and a 44 px title bar with
/// Scr's own minimise, maximise and close buttons (like Kalendar), so everything in the title bar lines up.
/// </summary>
public class GlassWindow : Window
{
    /// <summary>The acrylic backdrop needs Windows 11 22H2 or newer; older systems get solid colours.</summary>
    public static bool HasAcrylic => Environment.OSVersion.Version.Build >= 22621;

    public const double TitleHeight = 44;
    const double ButtonWidth = 46;

    readonly StackPanel captionButtons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
    Button? maxButton;

    public GlassWindow()
    {
        Background = Brushes.Transparent;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13.5;
        SetResourceReference(ForegroundProperty, "B.Ink");
        UseLayoutRounding = true;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = TitleHeight,
            GlassFrameThickness = new Thickness(-1),
            ResizeBorderThickness = new Thickness(6),
            UseAeroCaptionButtons = false,
            CornerRadius = new CornerRadius(0),
        });
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(h)?.AddHook(Hook);
            HideWindowsButtons(h);
            ApplyBackdrop();
        };
    }

    /// <summary>Width taken by the caption buttons on the right, for laying out the title bar.</summary>
    public double CaptionWidth => (ResizeMode == ResizeMode.NoResize ? 1 : 3) * ButtonWidth;

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        // Put our own caption buttons on top of the window's content, top right.
        if (Content is UIElement content)
        {
            BuildCaptionButtons();
            var host = new Grid();
            Content = host;
            host.Children.Add(content);
            host.Children.Add(captionButtons);
        }
    }

    void BuildCaptionButtons()
    {
        Button Make(string glyph, string tip, bool close, Action click)
        {
            var b = new Button { Content = glyph, ToolTip = tip, Style = (Style)FindResource(close ? "CaptionClose" : "CaptionBtn") };
            WindowChrome.SetIsHitTestVisibleInChrome(b, true);
            b.Click += (_, _) => click();
            captionButtons.Children.Add(b);
            return b;
        }
        if (ResizeMode != ResizeMode.NoResize)
        {
            Make("", "Minimise", false, () => WindowState = WindowState.Minimized);
            maxButton = Make("", "Maximise", false, () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized);
        }
        Make("", "Close", true, Close);
    }

    /// <summary>Windows draws its own small caption buttons unless the window has no system menu, so that bit is removed (and kept off).</summary>
    static void HideWindowsButtons(IntPtr h)
    {
        long style = GetWindowLongPtr(h, GWL_STYLE).ToInt64();
        SetWindowLongPtr(h, GWL_STYLE, new IntPtr(style & ~WS_SYSMENU));
    }

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        const int WM_STYLECHANGING = 0x007C;
        if (msg == WM_STYLECHANGING && w.ToInt32() == GWL_STYLE)
        {
            var ss = Marshal.PtrToStructure<STYLESTRUCT>(l);
            ss.styleNew &= ~(uint)WS_SYSMENU;
            Marshal.StructureToPtr(ss, l, false);
        }
        return IntPtr.Zero;
    }

    /// <summary>Applies dark or light frame, rounded corners and acrylic. Called again when the theme changes.</summary>
    public void ApplyBackdrop()
    {
        var h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero) return;
        int dark = ThemeColors.IsDark ? 1 : 0;
        Native.DwmSetWindowAttribute(h, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
        int round = 2;
        Native.DwmSetWindowAttribute(h, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4);
        int ncEnabled = 2; // DWMNCRP_ENABLED: Windows draws the normal window shadow
        Native.DwmSetWindowAttribute(h, Native.DWMWA_NCRENDERING_POLICY, ref ncEnabled, 4);
        var margins = new Native.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        Native.DwmExtendFrameIntoClientArea(h, ref margins);

        if (HasAcrylic)
        {
            var src = HwndSource.FromHwnd(h);
            if (src?.CompositionTarget != null) src.CompositionTarget.BackgroundColor = Colors.Transparent;
            int acrylic = 3;
            Native.DwmSetWindowAttribute(h, Native.DWMWA_SYSTEMBACKDROP_TYPE, ref acrylic, 4);
        }
    }

    /// <summary>Gives a title bar the full 44 px height, ending just left of the caption buttons.</summary>
    protected void AlignTitleBar(FrameworkElement bar, double leftMargin)
    {
        bar.VerticalAlignment = VerticalAlignment.Stretch;
        bar.Height = double.NaN;
        bar.Margin = new Thickness(leftMargin, 0, CaptionWidth + 6, 0);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        // A maximised frameless window hangs a few pixels off each edge; pull the content back in.
        if (Content is FrameworkElement fe)
            fe.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        if (maxButton != null)
        {
            bool max = WindowState == WindowState.Maximized;
            maxButton.Content = max ? "" : "";
            maxButton.ToolTip = max ? "Restore" : "Maximise";
        }
    }

    const int GWL_STYLE = -16;
    const long WS_SYSMENU = 0x00080000;

    [StructLayout(LayoutKind.Sequential)]
    struct STYLESTRUCT { public uint styleOld, styleNew; }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
}
