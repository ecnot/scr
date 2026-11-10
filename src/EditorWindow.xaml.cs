using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Scr;

public partial class EditorWindow : GlassWindow
{
    /// <summary>One capture in the editor, with its own frame.</summary>
    sealed class CaptureTab
    {
        public required FrameSource Src;
        public required FrameStyle Style;
        public FrameStyle? Framed; // the frame to go back to after "Plain"
        public DateTime Taken = DateTime.Now;
        public string? Name; // for opened or pasted pictures
        public RadioButton Button = null!;
    }

    readonly List<CaptureTab> tabs = new();
    CaptureTab cur = null!;
    FrameSource src => cur.Src;
    FrameStyle style { get => cur.Style; set => cur.Style = value; }
    FrameStyle? framedStyle { get => cur.Framed; set => cur.Framed = value; }
    bool loading;
    bool renderPending;
    RadioButton autoSwatch = null!;
    Point? dragStart;

    public int TabCount => tabs.Count;

    public EditorWindow(BitmapSource capture, double scale, string? name = null)
    {
        InitializeComponent();

        var wa = SystemParameters.WorkArea;
        double pw = capture.PixelWidth / scale * 1.2 + 90, ph = capture.PixelHeight / scale * 1.2 + 110;
        Width = Math.Clamp(pw + 316, 900, Math.Max(900, wa.Width * 0.9));
        Height = Math.Clamp(ph + 44, 600, Math.Max(600, wa.Height * 0.9));

        AlignTitleBar(TitleBar, 14);
        BuildSwatches();
        Wire();
        AddCapture(capture, name);

        PreviewKeyDown += OnKeys;
        AllowDrop = true;
        DragOver += (_, e) => { e.Effects = Import.HasPicture(e.Data) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        Drop += (_, e) => AddPictures(Import.Read(e.Data), "Those files aren't pictures Scr can open.");
        Closed += (_, _) =>
        {
            App.Instance.Settings.Style = style.Clone();
            App.Instance.Settings.Save();
        };
    }

    // ---------- Tabs ----------

    /// <summary>Adds a capture as a new tab and shows it. New tabs start with the frame you used last.</summary>
    public void AddCapture(BitmapSource capture, string? name = null)
    {
        var tab = new CaptureTab
        {
            Src = new FrameSource(capture),
            Style = (tabs.Count > 0 ? cur.Style : App.Instance.Settings.Style).Clone(),
            Name = name,
        };
        tab.Button = MakeTabButton(tab, capture);
        tabs.Add(tab);
        TabStrip.Children.Add(tab.Button);
        Select(tab);
    }

    RadioButton MakeTabButton(CaptureTab tab, BitmapSource capture)
    {
        // A small picture of the capture, the time it was taken, and a close button.
        double k = Math.Min(30.0 / capture.PixelWidth, 18.0 / capture.PixelHeight);
        var thumb = new Image
        {
            Source = capture,
            Width = Math.Max(8, Math.Round(capture.PixelWidth * k)),
            Height = Math.Max(8, Math.Round(capture.PixelHeight * k)),
            Stretch = Stretch.Fill,
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        thumb.Clip = new RectangleGeometry(new Rect(0, 0, thumb.Width, thumb.Height), 3, 3);
        RenderOptions.SetBitmapScalingMode(thumb, BitmapScalingMode.HighQuality);

        var icon = new System.Windows.Shapes.Path { Data = (Geometry)FindResource("I.X"), Style = (Style)FindResource("Icon"), StrokeThickness = 2.2 };
        var close = new Button
        {
            Style = (Style)FindResource("TabClose"),
            ToolTip = "Close this capture (Ctrl+W)",
            Margin = new Thickness(6, 0, 0, 0),
            Content = new Viewbox { Width = 10, Height = 10, Child = icon },
        };
        close.Click += (_, e) => { CloseTab(tab); e.Handled = true; };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(thumb);
        string label = tab.Name is { Length: > 0 } n ? (n.Length > 16 ? n[..15] + "…" : n) : tab.Taken.ToString("HH:mm");
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(close);

        var rb = new RadioButton
        {
            Style = (Style)FindResource("Tab"),
            Content = row,
            ToolTip = (tab.Name is { Length: > 0 } ? tab.Name : "Taken at " + tab.Taken.ToString("HH:mm:ss")) + " · " + capture.PixelWidth + " × " + capture.PixelHeight,
        };
        WindowChrome.SetIsHitTestVisibleInChrome(rb, true);
        rb.Checked += (_, _) => { if (cur != tab) Select(tab); };
        rb.PreviewMouseDown += (_, e) => { if (e.ChangedButton == MouseButton.Middle) { CloseTab(tab); e.Handled = true; } };
        return rb;
    }

    void Select(CaptureTab tab)
    {
        cur = tab;
        tab.Button.IsChecked = true;
        tab.Button.BringIntoView();
        autoSwatch.Background = AutoSwatchBrush(src.AutoColors);
        UpdateEditUi();
        LoadStyle();
        RenderNow();
    }

    /// <summary>Closes one capture. The window closes with the last one.</summary>
    void CloseTab(CaptureTab tab)
    {
        int i = tabs.IndexOf(tab);
        if (i < 0) return;
        if (tabs.Count == 1)
        {
            Close();
            return;
        }
        App.Instance.Settings.Style = tab.Style.Clone();
        tabs.RemoveAt(i);
        TabStrip.Children.Remove(tab.Button);
        if (cur == tab) Select(tabs[Math.Min(i, tabs.Count - 1)]);
        App.Instance.ScheduleTrim();
    }

    void StepTab(int delta)
    {
        if (tabs.Count < 2) return;
        int i = (tabs.IndexOf(cur) + delta + tabs.Count) % tabs.Count;
        Select(tabs[i]);
    }

    // ---------- UI setup ----------

    void BuildSwatches()
    {
        var swatchStyle = (Style)FindResource("Swatch");
        autoSwatch = new RadioButton { Style = swatchStyle, ToolTip = "From the screenshot: colours picked from your capture" };
        autoSwatch.Checked += (_, _) => { if (loading) return; style.GradientIndex = -1; RequestRender(); };
        GradientGrid.Children.Add(autoSwatch);
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
        foreach (var s in new[] { PadSlider, ShadowSlider, ShadowSizeSlider, OuterSlider, InsetSlider, InnerSlider })
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

        NewCaptureBtn.Click += (_, _) => App.Instance.StartCapture();
        ToolRedact.Click += (_, _) => PickTool(Tool.Redact);
        ToolCrop.Click += (_, _) => PickTool(Tool.Crop);
        ToolHighlight.Click += (_, _) => PickTool(Tool.Highlight);
        ToolSpotlight.Click += (_, _) => PickTool(Tool.Spotlight);
        RedactPixelate.Checked += (_, _) => { if (!loading) style.RedactKind = RedactKind.Pixelate; };
        RedactBlur.Checked += (_, _) => { if (!loading) style.RedactKind = RedactKind.Blur; };
        HighlightBox.Checked += (_, _) => { if (!loading) style.HighlightKind = MarkKind.Box; };
        HighlightMarker.Checked += (_, _) => { if (!loading) style.HighlightKind = MarkKind.Marker; };
        CropResetBtn.Click += (_, _) => { src.SetCrop(null); UpdateEditUi(); RequestRender(); };
        UndoBtn.Click += (_, _) => UndoEdit();
        BuildColorDots();
        Preview.MouseLeftButtonDown += OnToolDown;
        Preview.MouseMove += OnToolMove;
        Preview.MouseLeftButtonUp += OnToolUp;
        Preview.SizeChanged += (_, _) => UpdateCropShade();
        PickTool(Tool.None);
        PlainBtn.Click += (_, _) => TogglePlain();
        SaveBtn.Click += (_, _) => Save();
        CopyBtn.Click += (_, _) => CopyAndMaybeClose();
    }

    void LoadStyle()
    {
        loading = true;
        PadSlider.Value = style.Padding;
        ShadowSlider.Value = style.Shadow;
        ShadowSizeSlider.Value = style.ShadowSize;
        OuterSlider.Value = style.OuterRadius;
        InsetSlider.Value = style.Inset;
        InnerSlider.Value = style.InnerRadius;

        (style.Kind switch { BackdropKind.Solid => KindSolid, BackdropKind.Picture => KindPicture, _ => KindGradient }).IsChecked = true;
        (style.Shape switch { GradientShape.Linear => ShapeLinear, GradientShape.Radial => ShapeRadial, _ => ShapeMesh }).IsChecked = true;
        (style.Ratio switch { AspectRatio.Wide => RatioWide, AspectRatio.Classic => RatioClassic, AspectRatio.Square => RatioSquare, _ => RatioAuto }).IsChecked = true;

        (style.RedactKind == RedactKind.Blur ? RedactBlur : RedactPixelate).IsChecked = true;
        (style.HighlightKind == MarkKind.Marker ? HighlightMarker : HighlightBox).IsChecked = true;
        foreach (RadioButton dot in ColorDots.Children)
            dot.IsChecked = string.Equals((string)dot.Tag, style.MarkColor, StringComparison.OrdinalIgnoreCase);
        int gi = style.GradientIndex < 0 ? 0 : Math.Min(style.GradientIndex + 1, GradientGrid.Children.Count - 1);
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
        bool shadow = style.Shadow > 0;
        ShadowSizeSlider.IsEnabled = shadow;
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
        style.ShadowSize = Math.Round(ShadowSizeSlider.Value);
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
        var (drawing, w, h) = FrameRenderer.Build(src, style, wholeCapture: tool == Tool.Crop);
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
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateCropShade));
    }

    // ---------- Copy and save ----------

    void OnKeys(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool inText = e.OriginalSource is TextBox;
        if (e.Key == Key.Escape && tool != Tool.None) { PickTool(Tool.None); e.Handled = true; }
        else if (e.Key == Key.Escape || (ctrl && e.Key == Key.W)) { CloseTab(cur); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.None && !inText && e.Key is Key.R or Key.C or Key.H or Key.S)
        {
            PickTool(e.Key switch { Key.R => Tool.Redact, Key.C => Tool.Crop, Key.H => Tool.Highlight, _ => Tool.Spotlight });
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.Tab) { StepTab(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1); e.Handled = true; }
        else if (ctrl && e.Key >= Key.D1 && e.Key <= Key.D9 && e.Key - Key.D1 < tabs.Count) { Select(tabs[e.Key - Key.D1]); e.Handled = true; }
        else if (ctrl && e.Key == Key.C && !inText) { CopyAndMaybeClose(); e.Handled = true; }
        else if (ctrl && e.Key == Key.S) { Save(); e.Handled = true; }
        else if (ctrl && e.Key == Key.Z && !inText) { UndoEdit(); e.Handled = true; }
        else if (ctrl && e.Key == Key.V && !inText) { AddPictures(Import.FromClipboard(), "There's no picture on the clipboard."); e.Handled = true; }
        else if (ctrl && e.Key == Key.O) { OpenPictures(); e.Handled = true; }
        else if (e.Key == Key.Enter && !inText && e.OriginalSource is not ButtonBase) { CopyAndMaybeClose(); e.Handled = true; }
    }

    void CopyAndMaybeClose()
    {
        if (!CopyToClipboard()) return;
        if (App.Instance.Settings.CloseAfterCopy) CloseTab(cur);
        else ShowToast("Copied");
    }

    public void ShowNote(string text) => ShowToast(text);

    // ---------- Opening and pasting pictures ----------

    /// <summary>Adds opened, dropped or pasted pictures as tabs.</summary>
    public void AddPictures(List<(BitmapSource Picture, string? Name)> pictures, string ifNone)
    {
        if (pictures.Count == 0) { ShowToast(ifNone); return; }
        foreach (var (pic, name) in pictures) AddCapture(pic, name ?? "Pasted");
    }

    void OpenPictures()
    {
        var files = App.AskForPictures(this);
        if (files.Count > 0) AddPictures(files, "Scr can't open that picture.");
    }

    // ---------- Redact ----------

    /// <summary>The swatch for "from the screenshot": the picked colours with a small sparkle.</summary>
    Brush AutoSwatchBrush(Color[] colors)
    {
        var sparkle = new System.Windows.Shapes.Path
        {
            Data = (Geometry)FindResource("I.Sparkle"),
            Fill = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
            Stretch = Stretch.Uniform,
            Width = 13,
            Height = 13,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Opacity = 0.35 },
        };
        var g = new Grid { Width = 44, Height = 28, Background = Presets.PreviewBrush(new GradientPreset("Auto", colors)) };
        g.Children.Add(sparkle);
        return new VisualBrush(g) { Stretch = Stretch.Fill };
    }

#if DEBUG
    /// <summary>For the preview pictures only.</summary>
    internal void EditForPreview(Action<FrameSource> edit, string? toolName = null)
    {
        edit(src);
        if (toolName != null) PickTool(Enum.Parse<Tool>(toolName));
        UpdateEditUi();
        RenderNow();
    }
#endif

    // ---------- Tools: Redact, Crop, Highlight, Spotlight ----------

    enum Tool { None, Redact, Crop, Highlight, Spotlight }
    Tool tool = Tool.None;

    static readonly (string Hex, string Name)[] MarkColors =
    {
        ("#E5484D", "Red"), ("#F5A524", "Yellow"), ("#30A46C", "Green"), ("#3E63DD", "Blue"),
    };

    void BuildColorDots()
    {
        var dotStyle = (Style)FindResource("ColorDot");
        foreach (var (hex, name) in MarkColors)
        {
            var c = FrameRenderer.ParseColor(hex)!.Value;
            var dot = new RadioButton { Style = dotStyle, Background = new SolidColorBrush(c), Tag = hex, ToolTip = name };
            dot.Checked += (_, _) => { if (!loading) style.MarkColor = hex; };
            ColorDots.Children.Add(dot);
        }
    }

    /// <summary>Turns a tool on, or off again if it's already on. Only the active tool's options show.</summary>
    void PickTool(Tool t)
    {
        bool wasCrop = tool == Tool.Crop;
        tool = tool == t ? Tool.None : t;
        ToolRedact.IsChecked = tool == Tool.Redact;
        ToolCrop.IsChecked = tool == Tool.Crop;
        ToolHighlight.IsChecked = tool == Tool.Highlight;
        ToolSpotlight.IsChecked = tool == Tool.Spotlight;
        ToolOptions.Visibility = tool == Tool.None ? Visibility.Collapsed : Visibility.Visible;
        RedactOptions.Visibility = tool == Tool.Redact ? Visibility.Visible : Visibility.Collapsed;
        CropResetBtn.Visibility = tool == Tool.Crop ? Visibility.Visible : Visibility.Collapsed;
        HighlightOptions.Visibility = tool == Tool.Highlight ? Visibility.Visible : Visibility.Collapsed;
        ToolHint.Text = tool switch
        {
            Tool.Redact => "Drag over what to hide",
            Tool.Crop => "Drag the part to keep",
            Tool.Highlight => "Drag around it",
            Tool.Spotlight => "Drag the part to light up",
            _ => "",
        };
        Preview.Cursor = tool == Tool.None ? null : Cursors.Cross;
        UpdateEditUi();
        if (cur != null && (wasCrop || tool == Tool.Crop)) RequestRender(); // Crop shows the whole capture
    }

    void UpdateEditUi()
    {
        if (cur == null) return;
        UndoBtn.IsEnabled = src.CanUndo;
        CropResetBtn.IsEnabled = src.Crop != null;
    }

    void UndoEdit()
    {
        if (!src.Undo()) return;
        UpdateEditUi();
        RequestRender();
    }

    /// <summary>While cropping, the whole capture shows with everything outside the crop dimmed.</summary>
    void UpdateCropShade()
    {
        if (cur == null || tool != Tool.Crop || src.Crop is not Int32Rect c || Preview.ActualWidth <= 0 || src.LastCanvas.Width <= 0)
        {
            CropShade.Visibility = Visibility.Collapsed;
            return;
        }
        double k = Preview.ActualWidth / src.LastCanvas.Width;
        var img = src.LastImageRect;
        var whole = new Rect(img.X * k, img.Y * k, img.Width * k, img.Height * k);
        var keep = new Rect((img.X + c.X) * k, (img.Y + c.Y) * k, c.Width * k, c.Height * k);
        CropShade.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(whole), new RectangleGeometry(keep));
        CropShade.Visibility = Visibility.Visible;
    }

    /// <summary>A point on the preview, in the original capture's own pixels.</summary>
    Point ToCapture(Point p)
    {
        if (Preview.ActualWidth <= 0) return p;
        double k = src.LastCanvas.Width / Preview.ActualWidth;
        var r = src.LastImageRect;
        var o = src.LastOffset;
        return new Point(Math.Clamp(p.X * k - r.X + o.X, 0, src.Capture.PixelWidth),
                         Math.Clamp(p.Y * k - r.Y + o.Y, 0, src.Capture.PixelHeight));
    }

    void OnToolDown(object sender, MouseButtonEventArgs e)
    {
        if (tool == Tool.None) return;
        dragStart = e.GetPosition(Preview);
        Preview.CaptureMouse();
        e.Handled = true;
    }

    void OnToolMove(object sender, MouseEventArgs e)
    {
        if (dragStart is not Point s) return;
        var p = e.GetPosition(Preview);
        var r = new Rect(s, p);
        r.Intersect(new Rect(0, 0, Preview.ActualWidth, Preview.ActualHeight));
        if (r.IsEmpty) return;
        Canvas.SetLeft(RedactBox, r.X);
        Canvas.SetTop(RedactBox, r.Y);
        RedactBox.Width = r.Width;
        RedactBox.Height = r.Height;
        RedactBox.Visibility = Visibility.Visible;
    }

    void OnToolUp(object sender, MouseButtonEventArgs e)
    {
        if (dragStart is not Point s) return;
        dragStart = null;
        Preview.ReleaseMouseCapture();
        RedactBox.Visibility = Visibility.Collapsed;
        var a = ToCapture(s);
        var b = ToCapture(e.GetPosition(Preview));
        int x0 = (int)Math.Floor(Math.Min(a.X, b.X)), y0 = (int)Math.Floor(Math.Min(a.Y, b.Y));
        int x1 = (int)Math.Ceiling(Math.Max(a.X, b.X)), y1 = (int)Math.Ceiling(Math.Max(a.Y, b.Y));
        if (x1 - x0 < 3 || y1 - y0 < 3) return; // a click, not a drag
        var area = new Int32Rect(x0, y0, x1 - x0, y1 - y0);
        var colour = FrameRenderer.ParseColor(style.MarkColor) ?? Colors.Red;
        switch (tool)
        {
            case Tool.Redact: src.AddRedaction(new Redaction(area, style.RedactKind)); break;
            case Tool.Highlight: src.AddMark(new Mark(style.HighlightKind, area, colour)); break;
            case Tool.Spotlight: src.AddMark(new Mark(MarkKind.Spotlight, area, colour)); break;
            case Tool.Crop:
                src.SetCrop(area);
                PickTool(Tool.None); // show the result; click Crop again to change it
                break;
        }
        UpdateEditUi();
        RequestRender();
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
            FileName = cur.Name is { Length: > 0 } nm ? $"{nm} framed.png" : $"Scr {DateTime.Now:yyyy-MM-dd HH.mm.ss}.png",
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
