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

    /// <summary>A rectangle with its own radius at each corner.</summary>
    public static Geometry RoundedRect(Rect r, CornerRadius c)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            void Arc(Point to, double rad)
            {
                if (rad > 0) ctx.ArcTo(to, new Size(rad, rad), 0, false, SweepDirection.Clockwise, true, false);
                else ctx.LineTo(to, true, false);
            }
            ctx.BeginFigure(new Point(r.Left + c.TopLeft, r.Top), true, true);
            ctx.LineTo(new Point(r.Right - c.TopRight, r.Top), true, false);
            Arc(new Point(r.Right, r.Top + c.TopRight), c.TopRight);
            ctx.LineTo(new Point(r.Right, r.Bottom - c.BottomRight), true, false);
            Arc(new Point(r.Right - c.BottomRight, r.Bottom), c.BottomRight);
            ctx.LineTo(new Point(r.Left + c.BottomLeft, r.Bottom), true, false);
            Arc(new Point(r.Left, r.Bottom - c.BottomLeft), c.BottomLeft);
            ctx.LineTo(new Point(r.Left, r.Top + c.TopLeft), true, false);
            Arc(new Point(r.Left + c.TopLeft, r.Top), c.TopLeft);
        }
        g.Freeze();
        return g;
    }

    static void Attach(Border sheet)
    {
        if (sheet.Parent is not Grid parent || sheet.Tag is "shadowed") return;
        sheet.Tag = "shadowed";

        var color = Color.FromRgb(0x1E, 0x1E, 0x46);
        Border Layer(double blur, double depth, double opacity)
        {
            var layer = new Border
            {
                Background = Brushes.Black,
            Margin = sheet.Margin,
            IsHitTestVisible = false,
                Effect = new DropShadowEffect { Color = color, BlurRadius = blur, ShadowDepth = depth, Direction = 270, Opacity = opacity },
            };
            // Same corners as the surface, also when one of them changes (the first tab squares the top-left one).
            layer.SetBinding(Border.CornerRadiusProperty, new System.Windows.Data.Binding(nameof(Border.CornerRadius)) { Source = sheet });
            return layer;
        }
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
            host.Clip = new CombinedGeometry(GeometryCombineMode.Exclude,
                new RectangleGeometry(new Rect(-40, -40, w + 80, h + 80)),
                RoundedRect(inside, sheet.CornerRadius));
        }
        host.SizeChanged += (_, _) => Clip();
        System.ComponentModel.DependencyPropertyDescriptor.FromProperty(Border.CornerRadiusProperty, typeof(Border))
            .AddValueChanged(sheet, (_, _) => Clip());
    }
}
