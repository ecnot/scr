using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Scr;

public partial class EditorWindow : GlassWindow
{
    readonly FrameSource src;
    FrameStyle style;
    FrameStyle? framedStyle; // the frame to go back to after "Plain"
    bool loading;
    bool renderPending;

    public EditorWindow(BitmapSource capture, double scale)
    {
        InitializeComponent();
        src = new FrameSource(capture);
        style = App.Instance.Settings.Style.Clone();

        var wa = SystemParameters.WorkArea;
        double pw = capture.PixelWidth / scale * 1.2 + 90, ph = capture.PixelHeight / scale * 1.2 + 110;
        Width = Math.Clamp(pw + 316, 900, Math.Max(900, wa.Width * 0.9));
        Height = Math.Clamp(ph + 44, 600, Math.Max(600, wa.Height * 0.9));

        BuildSwatches();
        Wire();
        LoadStyle();
        RenderNow();

        PreviewKeyDown += OnKeys;
        Closed += (_, _) =>
        {
            App.Instance.Settings.Style = style.Clone();
            App.Instance.Settings.Save();
        };
    }

    // ---------- UI setup ----------

    void BuildSwatches()
    {
        var swatchStyle = (Style)FindResource("Swatch");
        for (int i = 0; i < Presets.Gradients.Length; i++)
        {
            int index = i;
            var g = Presets.Gradients[i];
            var rb = new RadioButton { Style = swatchStyle, Background = Presets.PreviewBrush(g), ToolTip = g.Name };
            rb.Checked += (_, _) => { if (loading) return; style.GradientIndex = index; RequestRender(); };
            GradientGrid.Children.Add(rb);
        }
        foreach (var hex in Presets.Solids)
        {
            var c = FrameRenderer.ParseColor(hex)!.Value;
            Brush fill = c.A == 0 ? (Brush)FindResource("Checker") : new SolidColorBrush(c);
            var rb = new RadioButton { Style = swatchStyle, Background = fill, ToolTip = c.A == 0 ? "Transparent" : hex, Tag = hex };
            rb.Checked += (_, _) =>
            {
                if (loading) return;
                style.SolidColor = hex;
                HexBox.Text = c.A == 0 ? "" : hex;
                RequestRender();
            };
            SolidGrid.Children.Add(rb);
        }
    }

    void Wire()
    {
        foreach (var s in new[] { PadSlider, ShadowSlider, OuterSlider, InsetSlider, InnerSlider })
            s.ValueChanged += (_, _) => OnSlider();

        KindSolid.Checked += (_, _) => SetKind(BackdropKind.Solid);
        KindGradient.Checked += (_, _) => SetKind(BackdropKind.Gradient);
        KindPicture.Checked += (_, _) => SetKind(BackdropKind.Picture);

        ShapeMesh.Checked += (_, _) => SetShape(GradientShape.Mesh);
        ShapeLinear.Checked += (_, _) => SetShape(GradientShape.Linear);
        ShapeRadial.Checked += (_, _) => SetShape(GradientShape.Radial);
        SwapBtn.Click += (_, _) => { style.Reversed = !style.Reversed; RequestRender(); };
        ShuffleBtn.Click += (_, _) => { style.Seed = Random.Shared.Next(1, 100000); RequestRender(); };

        HexBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyHex(); e.Handled = true; } };
        HexBox.LostKeyboardFocus += (_, _) => ApplyHex();

        PicCapture.Checked += (_, _) => { if (loading) return; style.PictureFromCapture = true; UpdatePictureUi(); RequestRender(); };
        PicFile.Checked += (_, _) =>
        {
            if (loading) return;
            if (string.IsNullOrEmpty(style.PicturePath) && !ChoosePicture())
            {
                loading = true; PicCapture.IsChecked = true; loading = false;
                return;
            }
            style.PictureFromCapture = false;
            UpdatePictureUi();
            RequestRender();
        };
        ChooseBtn.Click += (_, _) =>
        {
            if (!ChoosePicture()) return;
            style.PictureFromCapture = false;
            loading = true; PicFile.IsChecked = true; loading = false;
            UpdatePictureUi();
            RequestRender();
        };
        PicBlur.Checked += (_, _) => { if (loading) return; style.PictureBlur = true; RequestRender(); };
        PicBlur.Unchecked += (_, _) => { if (loading) return; style.PictureBlur = false; RequestRender(); };

        RatioAuto.Checked += (_, _) => SetRatio(AspectRatio.Auto);
        RatioWide.Checked += (_, _) => SetRatio(AspectRatio.Wide);
        RatioClassic.Checked += (_, _) => SetRatio(AspectRatio.Classic);
        RatioSquare.Checked += (_, _) => SetRatio(AspectRatio.Square);

        PlainBtn.Click += (_, _) => TogglePlain();
        SaveBtn.Click += (_, _) => Save();
        CopyBtn.Click += (_, _) => CopyAndMaybeClose();
    }

    void LoadStyle()
    {
        loading = true;
        PadSlider.Value = style.Padding;
        ShadowSlider.Value = style.Shadow;
        OuterSlider.Value = style.OuterRadius;
        InsetSlider.Value = style.Inset;
        InnerSlider.Value = style.InnerRadius;

        (style.Kind switch { BackdropKind.Solid => KindSolid, BackdropKind.Picture => KindPicture, _ => KindGradient }).IsChecked = true;
        (style.Shape switch { GradientShape.Linear => ShapeLinear, GradientShape.Radial => ShapeRadial, _ => ShapeMesh }).IsChecked = true;
        (style.Ratio switch { AspectRatio.Wide => RatioWide, AspectRatio.Classic => RatioClassic, AspectRatio.Square => RatioSquare, _ => RatioAuto }).IsChecked = true;

        int gi = Math.Clamp(style.GradientIndex, 0, GradientGrid.Children.Count - 1);
        ((RadioButton)GradientGrid.Children[gi]).IsChecked = true;
        foreach (RadioButton rb in SolidGrid.Children)
            rb.IsChecked = string.Equals((string)rb.Tag, style.SolidColor, StringComparison.OrdinalIgnoreCase);
        var sc = FrameRenderer.ParseColor(style.SolidColor);
        HexBox.Text = sc is Color c && c.A > 0 ? style.SolidColor : "";

        PicCapture.IsChecked = style.PictureFromCapture || string.IsNullOrEmpty(style.PicturePath);
        PicFile.IsChecked = !PicCapture.IsChecked;
        PicBlur.IsChecked = style.PictureBlur;
        UpdatePictureUi();
        UpdatePanels();
        loading = false;
    }

    void UpdatePanels()
    {
        SolidPanel.Visibility = style.Kind == BackdropKind.Solid ? Visibility.Visible : Visibility.Collapsed;
        GradientPanel.Visibility = style.Kind == BackdropKind.Gradient ? Visibility.Visible : Visibility.Collapsed;
        PicturePanel.Visibility = style.Kind == BackdropKind.Picture ? Visibility.Visible : Visibility.Collapsed;
        bool inset = style.Inset > 0;
        InnerSlider.IsEnabled = inset;
        InnerBox.Opacity = inset ? 1 : 0.55;
        InnerNote.Visibility = inset ? Visibility.Hidden : Visibility.Visible;
    }

    void UpdatePictureUi()
    {
        bool file = !style.PictureFromCapture && !string.IsNullOrEmpty(style.PicturePath);
        PicBlur.IsEnabled = file;
        PicBlur.Opacity = file ? 1 : 0.5;
        PicName.Text = file ? Path.GetFileName(style.PicturePath) : "A soft, blurred copy of the screenshot itself.";
    }

    // ---------- Changes ----------

    void OnSlider()
    {
        if (loading) return;
        style.Padding = Math.Round(PadSlider.Value);
        style.Shadow = Math.Round(ShadowSlider.Value);
        style.OuterRadius = Math.Round(OuterSlider.Value);
        style.Inset = Math.Round(InsetSlider.Value);
        style.InnerRadius = Math.Round(InnerSlider.Value);
        UpdatePanels();
        RequestRender();
    }

    void SetKind(BackdropKind k)
    {
        if (loading) return;
        style.Kind = k;
        if (style.Padding <= 0) { style.Padding = 60; loading = true; PadSlider.Value = 60; loading = false; }
        UpdatePanels();
        RequestRender();
    }

    void SetShape(GradientShape s) { if (loading) return; style.Shape = s; RequestRender(); }
    void SetRatio(AspectRatio r) { if (loading) return; style.Ratio = r; RequestRender(); }

    void ApplyHex()
    {
        if (loading) return;
        var text = HexBox.Text.Trim();
        if (text.Length == 0) return;
        var c = FrameRenderer.ParseColor(text);
        if (c == null) { ShowToast("That isn't a colour code. Try something like #5558C0."); return; }
        var hex = $"#{c.Value.R:X2}{c.Value.G:X2}{c.Value.B:X2}";
        style.SolidColor = hex;
        loading = true;
        HexBox.Text = hex;
        foreach (RadioButton rb in SolidGrid.Children)
            rb.IsChecked = string.Equals((string)rb.Tag, hex, StringComparison.OrdinalIgnoreCase);
        loading = false;
        RequestRender();
    }

    bool ChoosePicture()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choose a backdrop picture",
            Filter = "Pictures|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.tif;*.tiff|All files|*.*",
        };
        if (dlg.ShowDialog(this) != true) return false;
        if (Pixels.LoadPicture(dlg.FileName, false) == null)
        {
            ShowToast("Scr can't open that picture.");
            return false;
        }
        style.PicturePath = dlg.FileName;
        return true;
    }

    void TogglePlain()
    {
        if (style.IsPlain && framedStyle != null)
        {
            style = framedStyle;
            framedStyle = null;
        }
        else
        {
            framedStyle = style.Clone();
            style.Padding = 0; style.Shadow = 0; style.Inset = 0; style.OuterRadius = 0; style.InnerRadius = 0;
            style.Ratio = AspectRatio.Auto;
        }
        LoadStyle();
        RequestRender();
    }

    // ---------- Rendering ----------

    void RequestRender()
    {
        if (renderPending) return;
        renderPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            renderPending = false;
            RenderNow();
        }));
    }

    void RenderNow()
    {
        var (drawing, w, h) = FrameRenderer.Build(src, style);
        var img = new DrawingImage(drawing);
        img.Freeze();
        Preview.Source = img;
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (dpi <= 0) dpi = 1;
        Preview.MaxWidth = w / dpi;
        Preview.MaxHeight = h / dpi;
        SizeText.Text = $"{w} × {h}";
        PlainBtn.Content = style.IsPlain && framedStyle != null ? "Framed" : "Plain";
        PlainBtn.ToolTip = style.IsPlain && framedStyle != null ? "Put the frame back" : "Just the screenshot, with no frame";
    }

    // ---------- Copy and save ----------

    void OnKeys(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool inText = e.OriginalSource is TextBox;
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        else if (ctrl && e.Key == Key.C && !inText) { CopyAndMaybeClose(); e.Handled = true; }
        else if (ctrl && e.Key == Key.S) { Save(); e.Handled = true; }
        else if (e.Key == Key.Enter && !inText && e.OriginalSource is not ButtonBase) { CopyAndMaybeClose(); e.Handled = true; }
    }

    void CopyAndMaybeClose()
    {
        if (!CopyToClipboard()) return;
        if (App.Instance.Settings.CloseAfterCopy) Close();
        else ShowToast("Copied");
    }

    /// <summary>Puts the plain screenshot (no frame) on the clipboard straight after a capture.</summary>
    public void CopyPlainQuietly()
    {
        try
        {
            ClipboardImage.Set(src.Capture);
            ShowToast("Screenshot copied. Copy swaps in the framed one.");
        }
        catch (Exception ex)
        {
            ShowToast("Couldn't copy: " + ex.Message);
        }
    }

    bool CopyToClipboard()
    {
        try
        {
            ClipboardImage.Set(FrameRenderer.RenderBitmap(src, style));
            return true;
        }
        catch (Exception ex)
        {
            ShowToast("Couldn't copy: " + ex.Message);
            return false;
        }
    }

    void Save()
    {
        var folder = App.Instance.Settings.SaveFolderOrDefault;
        try { Directory.CreateDirectory(folder); } catch { }
        var dlg = new SaveFileDialog
        {
            Title = "Save screenshot",
            Filter = "PNG picture|*.png",
            DefaultExt = ".png",
            FileName = $"Scr {DateTime.Now:yyyy-MM-dd HH.mm.ss}.png",
            InitialDirectory = folder,
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            FrameRenderer.SavePng(FrameRenderer.RenderBitmap(src, style), dlg.FileName);
            ShowToast("Saved " + Path.GetFileName(dlg.FileName));
        }
        catch (Exception ex)
        {
            ShowToast("Couldn't save: " + ex.Message);
        }
    }

    void ShowToast(string text)
    {
        ToastText.Text = text;
        var a = new DoubleAnimationUsingKeyFrames();
        a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1900))));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2200))));
        Toast.BeginAnimation(OpacityProperty, a);
    }

    void OnSettings(object sender, RoutedEventArgs e) => App.Instance.ShowSettings(false);
}
