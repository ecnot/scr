#if DEBUG
using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Scr;

/// <summary>
/// Debug builds only: renders the editor and settings windows to PNG files without ever showing them,
/// plus a few framed examples. Usage: Scr.exe --preview outDir sample.png
/// </summary>
static class DevPreview
{
    public static void Run(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : ".";
        Directory.CreateDirectory(outDir);
        var sample = Load(args.Length > 1 ? args[1] : null);
        double dpi = 1.5;

        foreach (bool dark in new[] { false, true })
        {
            ThemeColors.Apply(Application.Current.Resources, dark, glass: true);
            var ed = new EditorWindow(sample, dpi) { Width = 1180, Height = 760 };
            Snapshot((FrameworkElement)ed.Content, 1180, 760, dark, Path.Combine(outDir, dark ? "editor-dark.png" : "editor-light.png"));

            var st = new SettingsWindow(welcome: true);
            Snapshot((FrameworkElement)st.Content, st.Width, st.Height, dark, Path.Combine(outDir, dark ? "settings-dark.png" : "settings-light.png"));
        }

        var src = new FrameSource(sample);
        void Example(string name, Action<FrameStyle> change)
        {
            var s = new FrameStyle();
            change(s);
            FrameRenderer.SavePng(FrameRenderer.RenderBitmap(src, s), Path.Combine(outDir, $"framed-{name}.png"));
        }
        Example("default", _ => { });
        Example("ocean-linear", s => { s.GradientIndex = 15; s.Shape = GradientShape.Linear; s.Shadow = 80; });
        Example("sunset-wide", s => { s.GradientIndex = 10; s.Ratio = AspectRatio.Wide; s.OuterRadius = 20; });
        Example("inset-white", s => { s.Kind = BackdropKind.Solid; s.SolidColor = "#F3F4F6"; s.Inset = 30; s.InnerRadius = 10; s.OuterRadius = 22; });
        Example("blurred", s => { s.Kind = BackdropKind.Picture; s.Padding = 90; });
        Example("transparent", s => { s.Kind = BackdropKind.Solid; s.SolidColor = "#00FFFFFF"; s.Padding = 50; });

        // A rough timing of a full render, for the size of the sample.
        FrameRenderer.Build(src, new FrameStyle { Shadow = 10 });
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 10; i++) FrameRenderer.Build(src, new FrameStyle { Shadow = 20 + i * 5 });
        double build = sw.Elapsed.TotalMilliseconds / 10;
        sw.Restart();
        for (int i = 0; i < 10; i++)
            Pixels.ShadowMask(1786, 1186, new Rect(93, 93, 1600, 1000), 18, 20, 30 + i, 0.4, 3, 4, 0.2, out _);
        double mask = sw.Elapsed.TotalMilliseconds / 10;
        sw.Restart();
        for (int i = 0; i < 10; i++) FrameRenderer.Build(src, new FrameStyle { Shadow = 0, Seed = i });
        double noShadow = sw.Elapsed.TotalMilliseconds / 10;
        File.AppendAllText(Path.Combine(outDir, "timing.txt"), $"mask {mask:0.0} ms, build without shadow {noShadow:0.0} ms\n");
        sw.Restart();
        FrameRenderer.RenderBitmap(src, new FrameStyle());
        File.AppendAllText(Path.Combine(outDir, "timing.txt"),
            $"sample {sample.PixelWidth}x{sample.PixelHeight}: build {build:0.0} ms, render to pixels {sw.Elapsed.TotalMilliseconds:0} ms\n");
    }

    static BitmapSource Load(string? path)
    {
        if (path != null && File.Exists(path))
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.UriSource = new Uri(Path.GetFullPath(path));
            img.EndInit();
            return ScreenCapture.Copy(img, new Int32Rect(0, 0, img.PixelWidth, img.PixelHeight));
        }
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen()) dc.DrawRectangle(Brushes.SteelBlue, null, new Rect(0, 0, 1200, 760));
        var rtb = new RenderTargetBitmap(1200, 760, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        return ScreenCapture.Copy(rtb, new Int32Rect(0, 0, 1200, 760));
    }

    /// <summary>Lays out a window's content off-screen and draws it over a stand-in for the blurred desktop the acrylic shows.</summary>
    static void Snapshot(FrameworkElement root, double w, double h, bool dark, string file)
    {
        root.Measure(new Size(w, h));
        root.Arrange(new Rect(0, 0, w, h));
        root.UpdateLayout();
        double k = 1.5;
        var rtb = new RenderTargetBitmap((int)(w * k), (int)(h * k), 96 * k, 96 * k, PixelFormats.Pbgra32);

        var bg = new DrawingVisual();
        using (var dc = bg.RenderOpen())
        {
            var b = new LinearGradientBrush(
                dark ? Color.FromRgb(0x20, 0x26, 0x3A) : Color.FromRgb(0xC9, 0xD6, 0xEC),
                dark ? Color.FromRgb(0x2E, 0x22, 0x33) : Color.FromRgb(0xE8, 0xD9, 0xE6), 35);
            dc.DrawRoundedRectangle(b, null, new Rect(0, 0, w, h), 8, 8);
        }
        rtb.Render(bg);
        rtb.Render(root);
        FrameRenderer.SavePng(rtb, file);
    }
}
#endif
