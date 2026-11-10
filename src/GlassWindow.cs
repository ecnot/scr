using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace Scr;

/// <summary>A frameless window with Windows 11 acrylic behind it and Windows' own title bar buttons.</summary>
public class GlassWindow : Window
{
    /// <summary>The acrylic backdrop needs Windows 11 22H2 or newer; older systems get solid colours.</summary>
    public static bool HasAcrylic => Environment.OSVersion.Version.Build >= 22621;

    public GlassWindow()
    {
        Background = Brushes.Transparent;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13.5;
        SetResourceReference(ForegroundProperty, "B.Ink");
        UseLayoutRounding = true;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 44,
            GlassFrameThickness = new Thickness(-1),
            ResizeBorderThickness = new Thickness(6),
            UseAeroCaptionButtons = true,
            CornerRadius = new CornerRadius(0),
        });
        SourceInitialized += (_, _) => ApplyBackdrop();
    }

    void ApplyBackdrop()
    {
        var h = new WindowInteropHelper(this).Handle;
        int dark = ThemeColors.IsDark ? 1 : 0;
        Native.DwmSetWindowAttribute(h, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
        int round = 2;
        Native.DwmSetWindowAttribute(h, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4);
        if (HasAcrylic)
        {
            var src = HwndSource.FromHwnd(h);
            if (src?.CompositionTarget != null) src.CompositionTarget.BackgroundColor = Colors.Transparent;
            int acrylic = 3;
            Native.DwmSetWindowAttribute(h, Native.DWMWA_SYSTEMBACKDROP_TYPE, ref acrylic, 4);
        }
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        // A maximised frameless window hangs a few pixels off each edge; pull the content back in.
        if (Content is FrameworkElement fe)
            fe.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
    }
}
