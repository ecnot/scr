using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace Scr;

/// <summary>A frameless window with Windows 11 acrylic behind it, Windows' own shadow and title bar buttons.</summary>
public class GlassWindow : Window
{
    /// <summary>The acrylic backdrop needs Windows 11 22H2 or newer; older systems get solid colours.</summary>
    public static bool HasAcrylic => Environment.OSVersion.Version.Build >= 22621;

    /// <summary>Where Windows draws its minimise/maximise/close buttons, in this window's own units (top right).</summary>
    public Rect CaptionButtons { get; private set; } = new(0, 0, 138, 32);
    bool measured;

    /// <summary>Raised when the caption buttons move or change size, so the title bar can line up with them.</summary>
    public event Action? CaptionButtonsChanged;

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
        Loaded += (_, _) => MeasureCaptionButtons();
        SizeChanged += (_, _) => MeasureCaptionButtons();
        DpiChanged += (_, _) => MeasureCaptionButtons();
    }

    /// <summary>Applies dark or light frame, rounded corners, acrylic and the shadow. Called again when the theme changes.</summary>
    public void ApplyBackdrop()
    {
        var h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero) return;
        int dark = ThemeColors.IsDark ? 1 : 0;
        Native.DwmSetWindowAttribute(h, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
        int round = 2;
        Native.DwmSetWindowAttribute(h, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4);

        // Make sure Windows draws its normal window shadow around the frameless window, as it does for Kalendar and Notat.
        int ncEnabled = 2; // DWMNCRP_ENABLED
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

    void MeasureCaptionButtons()
    {
        var h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero) return;
        if (Native.DwmGetWindowAttribute(h, Native.DWMWA_CAPTION_BUTTON_BOUNDS, out Native.RECT r, System.Runtime.InteropServices.Marshal.SizeOf<Native.RECT>()) != 0)
            return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var rect = new Rect(r.Left / dpi.DpiScaleX, r.Top / dpi.DpiScaleY,
                            (r.Right - r.Left) / dpi.DpiScaleX, (r.Bottom - r.Top) / dpi.DpiScaleY);
        if (rect.Width <= 0 || rect.Height <= 0 || (measured && rect == CaptionButtons)) return;
        CaptionButtons = rect;
        measured = true;
        CaptionButtonsChanged?.Invoke();
    }

    /// <summary>Lines a title bar element up with Windows' buttons: same height and top, ending just left of them.</summary>
    protected void AlignTitleBar(FrameworkElement bar, double leftMargin)
    {
        void Apply()
        {
            var b = CaptionButtons;
            double inset = WindowState == WindowState.Maximized ? 7 : 0;
            // Until Windows reports where its buttons are, assume the usual three 46-wide buttons.
            double right = measured ? Math.Max(0, ActualWidth - b.Left - inset) + 4 : 142;
            bar.VerticalAlignment = VerticalAlignment.Top;
            bar.Height = b.Height;
            bar.Margin = new Thickness(leftMargin, Math.Max(0, b.Top - inset), right, 0);
        }
        CaptionButtonsChanged += Apply;
        SizeChanged += (_, _) => Apply();
        Apply();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        // A maximised frameless window hangs a few pixels off each edge; pull the content back in.
        if (Content is FrameworkElement fe)
            fe.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
    }
}
