using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Scr;

/// <summary>
/// Kalendar's soft shadow around the main surface (0 1px 2px and 0 3px 10px of a blue-grey).
/// The surface is see-through, so the shadow is drawn by a hidden shape behind it and cut away under the surface;
/// otherwise it would show through and grey the surface.
/// </summary>
public static class SheetShadow
{
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(SheetShadow), new PropertyMetadata(false, OnEnabled));
    public static void SetEnabled(DependencyObject d, bool v) => d.SetValue(EnabledProperty, v);
    public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);

    static void OnEnabled(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Border sheet || !(bool)e.NewValue) return;
        sheet.Initialized += (_, _) => Attach(sheet);
        if (sheet.IsInitialized) Attach(sheet);
    }

    static void Attach(Border sheet)
    {
        if (sheet.Parent is not Grid parent || sheet.Tag is "shadowed") return;
        sheet.Tag = "shadowed";

        var color = Color.FromRgb(0x1E, 0x1E, 0x46);
        Border Layer(double blur, double depth, double opacity) => new()
        {
            CornerRadius = sheet.CornerRadius,
            Background = Brushes.Black,
            Margin = sheet.Margin,
            IsHitTestVisible = false,
            Effect = new DropShadowEffect { Color = color, BlurRadius = blur, ShadowDepth = depth, Direction = 270, Opacity = opacity },
        };
        var host = new Grid { IsHitTestVisible = false };
        host.Children.Add(Layer(4, 1, 0.09));   // 0 1px 2px
        host.Children.Add(Layer(20, 3, 0.13));  // 0 3px 10px
        Grid.SetRow(host, Grid.GetRow(sheet));
        Grid.SetColumn(host, Grid.GetColumn(sheet));
        Grid.SetRowSpan(host, Grid.GetRowSpan(sheet));
        Grid.SetColumnSpan(host, Grid.GetColumnSpan(sheet));
        parent.Children.Insert(parent.Children.IndexOf(sheet), host);

        // Cut the shadow away under the surface itself, so only the part outside shows.
        void Clip()
        {
            double w = host.ActualWidth, h = host.ActualHeight;
            if (w <= 0 || h <= 0) return;
            var m = sheet.Margin;
            var inside = new Rect(m.Left, m.Top, w - m.Left - m.Right, h - m.Top - m.Bottom);
            if (inside.Width <= 0 || inside.Height <= 0) return;
            double r = sheet.CornerRadius.TopLeft;
            host.Clip = new CombinedGeometry(GeometryCombineMode.Exclude,
                new RectangleGeometry(new Rect(-40, -40, w + 80, h + 80)),
                new RectangleGeometry(inside, r, r));
        }
        host.SizeChanged += (_, _) => Clip();
    }
}
