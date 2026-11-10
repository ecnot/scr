using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace Scr;

/// <summary>
/// Kalendar's switch motion: a toggle's knob slides across, and a segmented choice's raised highlight slides to the
/// option you pick, with a slight overshoot. Only changes animate (never the first layout), and nothing moves when
/// Windows' animation effects are off.
/// </summary>
public static class Anim
{
    static bool Motion => SystemParameters.ClientAreaAnimation;
    static readonly TimeSpan SlideTime = TimeSpan.FromMilliseconds(340);
    static readonly TimeSpan KnobTime = TimeSpan.FromMilliseconds(220);
    static IEasingFunction Overshoot => new BackEase { Amplitude = 0.28, EasingMode = EasingMode.EaseOut };

    // ---------- Toggle switch ----------

    /// <summary>Set on a toggle-switch style: the template needs a "knob" with a TranslateTransform and an "on" layer.</summary>
    public static readonly DependencyProperty SwitchProperty =
        DependencyProperty.RegisterAttached("Switch", typeof(bool), typeof(Anim), new PropertyMetadata(false, OnSwitch));
    public static void SetSwitch(DependencyObject d, bool v) => d.SetValue(SwitchProperty, v);
    public static bool GetSwitch(DependencyObject d) => (bool)d.GetValue(SwitchProperty);

    static void OnSwitch(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ToggleButton t || !(bool)e.NewValue) return;
        t.Loaded += (_, _) => PlaceKnob(t, animate: false);
        t.Initialized += (_, _) => PlaceKnob(t, animate: false);
        t.Checked += (_, _) => PlaceKnob(t, animate: true);
        t.Unchecked += (_, _) => PlaceKnob(t, animate: true);
    }

    static void PlaceKnob(ToggleButton t, bool animate)
    {
        t.ApplyTemplate();
        if (t.Template?.FindName("knob", t) is not FrameworkElement knob) return;
        // A transform from a style is shared and frozen; each switch needs its own to move.
        if (knob.RenderTransform is not TranslateTransform tt || tt.IsFrozen)
        {
            tt = new TranslateTransform();
            knob.RenderTransform = tt;
        }
        var on = t.Template.FindName("on", t) as UIElement;
        bool isOn = t.IsChecked == true;
        double x = isOn ? 16 : 0, opacity = isOn ? 1 : 0;
        if (!animate || !t.IsLoaded || !Motion)
        {
            tt.BeginAnimation(TranslateTransform.XProperty, null);
            tt.X = x;
            if (on != null) { on.BeginAnimation(UIElement.OpacityProperty, null); on.Opacity = opacity; }
            return;
        }
        tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, KnobTime) { EasingFunction = Overshoot });
        on?.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(opacity, TimeSpan.FromMilliseconds(160)));
    }

    // ---------- Segmented choice ----------

    /// <summary>Set on a segmented track (a Border holding a panel of RadioButtons): one raised highlight slides between them.</summary>
    public static readonly DependencyProperty SegmentsProperty =
        DependencyProperty.RegisterAttached("Segments", typeof(bool), typeof(Anim), new PropertyMetadata(false, OnSegments));
    public static void SetSegments(DependencyObject d, bool v) => d.SetValue(SegmentsProperty, v);
    public static bool GetSegments(DependencyObject d) => (bool)d.GetValue(SegmentsProperty);

    static readonly DependencyProperty ReadyProperty =
        DependencyProperty.RegisterAttached("Ready", typeof(bool), typeof(Anim), new PropertyMetadata(false));
    static readonly DependencyProperty MoveProperty =
        DependencyProperty.RegisterAttached("Move", typeof(Action<bool>), typeof(Anim), new PropertyMetadata(null));

    /// <summary>Puts every switch and highlight in place at once, for windows drawn without being shown (the preview pictures).</summary>
    public static void SettleAll(DependencyObject root)
    {
        if (root is ToggleButton t && GetSwitch(t)) PlaceKnob(t, animate: false);
        if (root is Border b && GetSegments(b))
        {
            SetUpSegments(b);
            (b.GetValue(MoveProperty) as Action<bool>)?.Invoke(false);
        }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) SettleAll(VisualTreeHelper.GetChild(root, i));
    }

    static void OnSegments(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Border b || !(bool)e.NewValue) return;
        b.Loaded += (_, _) => SetUpSegments(b);
    }

    static void SetUpSegments(Border b)
    {
        if ((bool)b.GetValue(ReadyProperty) || b.Child is not Panel panel) return;
        b.SetValue(ReadyProperty, true);

        var tt = new TranslateTransform();
        var highlight = new Border
        {
            CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            RenderTransform = tt,
            Visibility = Visibility.Hidden,
            Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Direction = 270, Opacity = 0.14 },
        };
        highlight.SetResourceReference(Border.BackgroundProperty, "B.SegOn");

        var host = new Grid();
        b.Child = null;
        host.Children.Add(highlight);
        host.Children.Add(panel);
        b.Child = host;

        void Move(bool animate)
        {
            var chosen = panel.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true);
            if (chosen == null || chosen.ActualWidth <= 0 || chosen.Visibility != Visibility.Visible)
            {
                highlight.Visibility = Visibility.Hidden;
                return;
            }
            var p = chosen.TranslatePoint(new Point(0, 0), host);
            bool wasHidden = highlight.Visibility != Visibility.Visible;
            highlight.Visibility = Visibility.Visible;
            highlight.Width = chosen.ActualWidth;
            highlight.Height = chosen.ActualHeight;
            highlight.Margin = new Thickness(0, p.Y, 0, 0);
            if (!animate || wasHidden || !Motion)
            {
                tt.BeginAnimation(TranslateTransform.XProperty, null);
                tt.X = p.X;
            }
            else
            {
                tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(p.X, SlideTime) { EasingFunction = Overshoot });
            }
        }

        b.SetValue(MoveProperty, (Action<bool>)Move);
        foreach (var rb in panel.Children.OfType<RadioButton>())
            rb.Checked += (_, _) => Move(animate: true);
        host.SizeChanged += (_, _) => Move(animate: false);
        host.IsVisibleChanged += (_, _) => b.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => Move(animate: false)));
        b.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => Move(animate: false)));
    }
}
