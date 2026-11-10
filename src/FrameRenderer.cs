using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Scr;

/// <summary>A capture plus the things worked out from it once (edge colour, blurred backdrop, chosen pictures).</summary>
public sealed class FrameSource
{
    public BitmapSource Capture { get; }
    public Color EdgeColor { get; }
    BitmapSource? blurredCapture;
    readonly Dictionary<string, BitmapSource?> pictures = new();
    string? shadowKey;
    BitmapSource? shadowMask;
    double shadowScale;

    /// <summary>The last shadow is kept, so changing colours or gradients doesn't redo it.</summary>
    public BitmapSource Shadow(int w, int h, Rect card, double radius, double t, double u, out double scale)
    {
        string key = $"{w}|{h}|{card}|{radius}|{t}|{u}";
        if (key != shadowKey || shadowMask == null)
        {
            shadowMask = Pixels.ShadowMask(w, h, card, radius,
                offsetY: (2 + 16 * t) * u, sigma: (5 + 34 * t) * u, opacity: 0.16 + 0.34 * t,
                offsetY2: (1 + 2 * t) * u, sigma2: (1.5 + 3 * t) * u, opacity2: 0.10 + 0.12 * t,
                out shadowScale);
            shadowKey = key;
        }
        scale = shadowScale;
        return shadowMask;
    }

    public FrameSource(BitmapSource capture)
    {
        Capture = capture;
        EdgeColor = Pixels.EdgeColor(capture);
    }

    public BitmapSource? Picture(FrameStyle s)
    {
        if (s.PictureFromCapture || string.IsNullOrEmpty(s.PicturePath))
            return blurredCapture ??= Pixels.Blur(Capture, 260, 9);

        string key = s.PicturePath + (s.PictureBlur ? "|blur" : "");
        if (!pictures.TryGetValue(key, out var bmp))
        {
            bmp = Pixels.LoadPicture(s.PicturePath!, s.PictureBlur);
            pictures[key] = bmp;
        }
        return bmp;
    }
}

/// <summary>Builds the framed picture as a WPF drawing: the same drawing is shown live in the editor and rendered to pixels for Copy and Save.</summary>
public static class FrameRenderer
{
    public static (DrawingGroup Drawing, int Width, int Height) Build(FrameSource src, FrameStyle s)
    {
        var cap = src.Capture;
        int iw = cap.PixelWidth, ih = cap.PixelHeight;
        double u = Math.Clamp(Math.Max(iw, ih) / 1200.0, 0.5, 4.0);

        double pad = Math.Round(s.Padding * u);
        double inset = Math.Round(s.Inset * u);
        double outerR = s.OuterRadius * u;
        double innerR = s.InnerRadius * u;
        double cardW = iw + 2 * inset, cardH = ih + 2 * inset;
        double W = cardW + 2 * pad, H = cardH + 2 * pad;

        double ratio = s.Ratio switch { AspectRatio.Wide => 16.0 / 9, AspectRatio.Classic => 4.0 / 3, AspectRatio.Square => 1, _ => 0 };
        if (ratio > 0)
        {
            if (W / H < ratio) W = H * ratio; else H = W / ratio;
        }
        int w = (int)Math.Ceiling(W), h = (int)Math.Ceiling(H);
        double cardX = Math.Round((w - cardW) / 2), cardY = Math.Round((h - cardH) / 2);
        var canvas = new Rect(0, 0, w, h);
        var cardRect = new Rect(cardX, cardY, cardW, cardH);
        bool hasBackdrop = w > cardW || h > cardH;

        var g = new DrawingGroup();
        using (var dc = g.Open())
        {
            dc.PushClip(new RectangleGeometry(canvas));
            dc.DrawRectangle(Brushes.Transparent, null, canvas); // fixes the drawing's bounds to the canvas

            if (hasBackdrop) DrawBackdrop(dc, src, s, canvas);

            if (hasBackdrop && s.Shadow > 0)
            {
                double t = s.Shadow / 100.0;
                var mask = src.Shadow(w, h, cardRect, outerR, t, u, out double scale);
                dc.DrawImage(mask, new Rect(0, 0, mask.PixelWidth / scale, mask.PixelHeight / scale));
                // A faint hairline keeps the edge crisp on light backdrops.
                var edge = new SolidColorBrush(Color.FromArgb((byte)(10 + 14 * t), 0, 0, 0));
                var ring = cardRect; ring.Inflate(0.5 * u, 0.5 * u);
                dc.DrawRoundedRectangle(null, new Pen(edge, 1 * u), ring, outerR + 0.5 * u, outerR + 0.5 * u);
            }

            dc.PushClip(new RectangleGeometry(cardRect, outerR, outerR));
            var imgRect = new Rect(cardX + inset, cardY + inset, iw, ih);
            if (inset > 0)
            {
                dc.DrawRectangle(new SolidColorBrush(src.EdgeColor), null, cardRect);
                dc.PushClip(new RectangleGeometry(imgRect, innerR, innerR));
                dc.DrawImage(cap, imgRect);
                dc.Pop();
            }
            else
            {
                dc.DrawImage(cap, imgRect);
            }
            dc.Pop();
            dc.Pop();
        }
        RenderOptions.SetBitmapScalingMode(g, BitmapScalingMode.HighQuality);
        g.Freeze();
        return (g, w, h);
    }

    static void DrawBackdrop(DrawingContext dc, FrameSource src, FrameStyle s, Rect canvas)
    {
        switch (s.Kind)
        {
            case BackdropKind.Solid:
                var c = ParseColor(s.SolidColor) ?? Colors.White;
                if (c.A > 0) dc.DrawRectangle(new SolidColorBrush(c), null, canvas);
                break;
            case BackdropKind.Picture:
                var pic = src.Picture(s);
                if (pic != null) DrawCover(dc, pic, canvas);
                else DrawGradient(dc, s, canvas);
                break;
            default:
                DrawGradient(dc, s, canvas);
                break;
        }
    }

    public static Color[] GradientColors(FrameStyle s)
    {
        var preset = Presets.Gradients[Math.Clamp(s.GradientIndex, 0, Presets.Gradients.Length - 1)];
        var colors = (Color[])preset.Colors.Clone();
        if (s.Reversed) Array.Reverse(colors);
        return colors;
    }

    static void DrawGradient(DrawingContext dc, FrameStyle s, Rect canvas)
    {
        var colors = GradientColors(s);
        int seed = Math.Abs(s.Seed);
        switch (s.Shape)
        {
            case GradientShape.Linear:
            {
                double[] angles = { 135, 160, 110, 90, 200, 45, 180, 65 };
                double a = angles[seed % angles.Length] * Math.PI / 180;
                double cx = Math.Cos(a) / 2, cy = Math.Sin(a) / 2;
                var b = new LinearGradientBrush { StartPoint = new Point(0.5 - cx, 0.5 - cy), EndPoint = new Point(0.5 + cx, 0.5 + cy) };
                AddStops(b, colors);
                dc.DrawRectangle(b, null, canvas);
                break;
            }
            case GradientShape.Radial:
            {
                Point[] centres = { new(0.2, 0.15), new(0.8, 0.2), new(0.5, 0), new(0.15, 0.85), new(0.5, 0.5), new(0.85, 0.85) };
                var c = centres[seed % centres.Length];
                var b = new RadialGradientBrush { Center = c, GradientOrigin = c, RadiusX = 1.15, RadiusY = 1.15 };
                AddStops(b, colors);
                dc.DrawRectangle(b, null, canvas);
                break;
            }
            default:
            {
                // Mesh: a base colour with big soft blobs of the others.
                var rnd = new Random(seed * 7919 + 17);
                dc.DrawRectangle(new SolidColorBrush(colors[0]), null, canvas);
                double m = Math.Max(canvas.Width, canvas.Height);
                for (int i = 0; i < 6; i++)
                {
                    var col = colors[(i + 1) % colors.Length];
                    var centre = new Point(rnd.NextDouble() * canvas.Width, rnd.NextDouble() * canvas.Height);
                    double r = m * (0.38 + rnd.NextDouble() * 0.38);
                    var b = new RadialGradientBrush();
                    b.GradientStops.Add(new GradientStop(Color.FromArgb(235, col.R, col.G, col.B), 0));
                    b.GradientStops.Add(new GradientStop(Color.FromArgb(120, col.R, col.G, col.B), 0.45));
                    b.GradientStops.Add(new GradientStop(Color.FromArgb(0, col.R, col.G, col.B), 1));
                    b.Freeze();
                    dc.DrawEllipse(b, null, centre, r, r);
                }
                break;
            }
        }
    }

    static void AddStops(GradientBrush b, Color[] colors)
    {
        for (int i = 0; i < colors.Length; i++)
            b.GradientStops.Add(new GradientStop(colors[i], colors.Length == 1 ? 0 : (double)i / (colors.Length - 1)));
        b.Freeze();
    }

    static void DrawCover(DrawingContext dc, BitmapSource pic, Rect canvas)
    {
        double k = Math.Max(canvas.Width / pic.PixelWidth, canvas.Height / pic.PixelHeight);
        double w = pic.PixelWidth * k, h = pic.PixelHeight * k;
        dc.DrawImage(pic, new Rect((canvas.Width - w) / 2, (canvas.Height - h) / 2, w, h));
    }

    public static Color? ParseColor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        if (!text.StartsWith('#')) text = "#" + text;
        if (text.Length is not (4 or 7 or 9)) return null;
        try { return (Color)ColorConverter.ConvertFromString(text); } catch { return null; }
    }

    /// <summary>Renders the framed picture to real pixels.</summary>
    public static BitmapSource RenderBitmap(FrameSource src, FrameStyle s)
    {
        var (d, w, h) = Build(src, s);
        var dv = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
        using (var dc = dv.RenderOpen()) dc.DrawDrawing(d);
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    public static void SavePng(BitmapSource bmp, string path)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
