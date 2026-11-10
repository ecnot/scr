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
    /// <summary>Shows an editor far off-screen for a moment and writes down what Windows says about its frame.</summary>
    public static void Diagnose(string outFile, BitmapSource sample)
    {
        var ed = new EditorWindow(sample, 1) { ShowInTaskbar = false, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000 };
        ed.Show();
        var h = new System.Windows.Interop.WindowInteropHelper(ed).Handle;
        var rgn = DiagNative.CreateRectRgn(0, 0, 0, 0);
        int rgnType = DiagNative.GetWindowRgn(h, rgn);
        long style = DiagNative.GetWindowLongPtr(h, -16).ToInt64();
        long ex = DiagNative.GetWindowLongPtr(h, -20).ToInt64();
        DiagNative.DwmGetWindowAttribute(h, 1, out int ncEnabled, 4);
        var lines = new[]
        {
            $"window region type: {rgnType} (0 = none, 1 = empty, 2/3 = a region is set, which removes the shadow)",
            $"style: 0x{style:X} thickframe={(style & 0x40000) != 0} caption={(style & 0xC00000) == 0xC00000} sysmenu={(style & 0x80000) != 0}",
            $"exstyle: 0x{ex:X} layered={(ex & 0x80000) != 0}",
            $"DWM non-client rendering enabled: {ncEnabled}",
        };
        File.WriteAllLines(outFile, lines);
        DiagNative.DeleteObject(rgn);
        ed.Close();
    }

    static void Wait(int ms)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (_, _) => { t.Stop(); frame.Continue = false; };
        t.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    /// <summary>
    /// Shows small test windows one at a time (top-left, no focus, not on the taskbar), photographs the screen just
    /// outside each window with and without it, and writes how much the edge changed: a shadow changes it, no shadow doesn't.
    /// </summary>
    static void ShadowTest(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var lines = new System.Collections.Generic.List<string>();
        var variants = new (string Name, Func<Window> Make, Action Setup)[]
        {
            ("plain WPF window (reference)", () => new Window { Background = Brushes.White }, () => { }),
            ("Scr as it is", () => new SettingsWindow(false), () => { }),
            ("Scr without acrylic", () => new SettingsWindow(false), () => GlassWindow.TestNoAcrylic = true),
            ("Scr keeping the system menu", () => new SettingsWindow(false), () => GlassWindow.TestKeepSysMenu = true),
            ("Scr with glass frame 1", () => new SettingsWindow(false), () => GlassWindow.TestGlassFrame = 1),
            ("Scr with glass frame 1, no own extend", () => new SettingsWindow(false), () => { GlassWindow.TestGlassFrame = 1; GlassWindow.TestNoExtend = true; }),
        };
        int n = 0;
        foreach (var (name, make, setup) in variants)
        {
            GlassWindow.TestGlassFrame = -1; GlassWindow.TestNoAcrylic = GlassWindow.TestKeepSysMenu = GlassWindow.TestNoExtend = false;
            setup();
            var w = make();
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = 120; w.Top = 120; w.Width = 420; w.Height = 300;
            w.ShowActivated = false; w.ShowInTaskbar = false; w.Topmost = true; w.Title = "Scr shadow test";
            w.Show();
            Wait(900);
            var h = new System.Windows.Interop.WindowInteropHelper(w).Handle;
            DiagNative.GetWindowRect(h, out var wr);
            DiagNative.DwmGetWindowAttribute(h, 9, out DiagNative.RECT fr, 16); // the visible frame
            var area = new Int32Rect(fr.Left - 60, fr.Top - 60, fr.Right - fr.Left + 120, fr.Bottom - fr.Top + 120);
            var with = ScreenCapture.Capture(area);
            w.Close();
            Wait(700);
            var without = ScreenCapture.Capture(area);
            n++;
            FrameRenderer.SavePng(with, Path.Combine(outDir, $"{n}-with.png"));
            FrameRenderer.SavePng(without, Path.Combine(outDir, $"{n}-without.png"));
            // How much the 4-40 px band below and right of the visible frame changed.
            double diff = BandDifference(with, without, 60, fr.Right - fr.Left, fr.Bottom - fr.Top);
            lines.Add($"{n}. {name}: edge change {diff:0.0} (window {wr.Right - wr.Left}x{wr.Bottom - wr.Top}, visible {fr.Right - fr.Left}x{fr.Bottom - fr.Top})");
        }
        GlassWindow.TestGlassFrame = -1; GlassWindow.TestNoAcrylic = GlassWindow.TestKeepSysMenu = GlassWindow.TestNoExtend = false;
        File.WriteAllLines(Path.Combine(outDir, "shadow.txt"), lines);
    }

    /// <summary>Captures a Scr window that sits far off-screen (so nothing shows), the way a clicked window is captured.</summary>
    static void WindowTest(string outDir)
    {
        Directory.CreateDirectory(outDir);
        ThemeColors.Apply(Application.Current.Resources, "Lavender", false, glass: true);
        var w = new SettingsWindow(false) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000, Width = 520, Height = 420, ShowActivated = false, ShowInTaskbar = false };
        w.Show();
        Wait(800);
        var h = new System.Windows.Interop.WindowInteropHelper(w).Handle;
        var pic = ScreenCapture.CaptureWindow(h);
        DiagNative.GetWindowRect(h, out var wr);
        DiagNative.DwmGetWindowAttribute(h, 9, out DiagNative.RECT fr, 16);
        File.WriteAllText(Path.Combine(outDir, "window.txt"),
            pic == null ? "no picture (fell back)" : $"picture {pic.PixelWidth}x{pic.PixelHeight}, window {wr.Right - wr.Left}x{wr.Bottom - wr.Top}, visible {fr.Right - fr.Left}x{fr.Bottom - fr.Top}");
        if (pic != null) FrameRenderer.SavePng(pic, Path.Combine(outDir, "window.png"));
        // What the first switch looks like inside the real window.
        var sw = (System.Windows.Controls.CheckBox)w.FindName("WinShiftS");
        var knob = sw.Template?.FindName("knob", sw) as FrameworkElement;
        var on = sw.Template?.FindName("on", sw) as UIElement;
        var track = sw.Template?.FindName("track", sw) as FrameworkElement;
        var tt = knob?.RenderTransform as System.Windows.Media.TranslateTransform;
        File.AppendAllText(Path.Combine(outDir, "window.txt"),
            "\n" + $"switch checked={sw.IsChecked} loaded={sw.IsLoaded} template={(sw.Template != null)} knob={(knob != null)} knobX={tt?.X} frozen={tt?.IsFrozen} on={(on != null)} onOpacity={on?.Opacity} track={track?.ActualWidth}x{track?.ActualHeight} trackBrush={(track as System.Windows.Controls.Border)?.Background}" + "\n");
        w.Close();
    }

    static double BandDifference(BitmapSource a, BitmapSource b, int m, int fw, int fh)
    {
        int w = a.PixelWidth, h = a.PixelHeight, stride = w * 4;
        var pa = new byte[stride * h]; var pb = new byte[stride * h];
        new FormatConvertedBitmap(a, PixelFormats.Bgr32, null, 0).CopyPixels(pa, stride, 0);
        new FormatConvertedBitmap(b, PixelFormats.Bgr32, null, 0).CopyPixels(pb, stride, 0);
        double sum = 0; int count = 0;
        for (int y = m + fh + 4; y < Math.Min(h, m + fh + 40); y++)
            for (int x = m; x < m + fw; x++)
            {
                int i = y * stride + x * 4;
                sum += Math.Abs(pa[i] - pb[i]) + Math.Abs(pa[i + 1] - pb[i + 1]) + Math.Abs(pa[i + 2] - pb[i + 2]);
                count++;
            }
        return count == 0 ? 0 : sum / count / 3;
    }

    static class DiagNative
    {
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] public static extern IntPtr CreateRectRgn(int a, int b, int c, int d);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);
        [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern int GetWindowRgn(IntPtr h, IntPtr rgn);
        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int s);
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT v, int s);
        [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        public struct RECT { public int Left, Top, Right, Bottom; }
    }

    public static void Run(string[] args)
    {
        if (args.Length >= 3 && args[0] == "--diag") { Diagnose(args[1], Load(args[2])); return; }
        if (args.Length >= 2 && args[0] == "--shadow-test") { ShadowTest(args[1]); return; }
        if (args.Length >= 2 && args[0] == "--window-test") { WindowTest(args[1]); return; }
        string outDir = args.Length > 0 ? args[0] : ".";
        Directory.CreateDirectory(outDir);
        var sample = Load(args.Length > 1 ? args[1] : null);
        double dpi = 1.5;

        // Two more "captures" for the tabs: parts of the sample.
        var part1 = ScreenCapture.Copy(sample, new Int32Rect(0, 0, sample.PixelWidth / 3, sample.PixelHeight * 2 / 3));
        var part2 = ScreenCapture.Copy(sample, new Int32Rect(sample.PixelWidth / 4, sample.PixelHeight / 4, sample.PixelWidth / 2, sample.PixelHeight / 3));

        // An inset in the editor, so the Inset choice (Auto / Glass / Edge colour) shows.
        App.Instance.Settings.Style = new FrameStyle { Inset = 26, InnerRadius = 10, OuterRadius = 22, InsetLook = InsetLook.Glass, GlassFrost = 45 };
        foreach (var (theme, dark, file, level) in new[] { ("Lavender", false, "light", 3), ("Lavender", true, "dark", 3), ("Meadow", false, "meadow", 3), ("Dusk", true, "dusk-dark", 3), ("Lavender", false, "clear", 1) })
        {
            App.Instance.Settings.Theme = theme;
            ThemeColors.Apply(Application.Current.Resources, theme, dark, glass: true, level);
            var ed = new EditorWindow(part1, dpi) { Width = 1180, Height = 760 };
            if (args.Length > 1) ed.AddCapture(Import.LoadFile(args[1]), System.IO.Path.GetFileNameWithoutExtension(args[1]));
            ed.AddCapture(part2);
            ed.AddCapture(sample);
            ed.EditForPreview(f =>
            {
                f.AddRedaction(new Redaction(new Int32Rect(13, 478, 328, 50), RedactKind.Pixelate));
                f.AddRedaction(new Redaction(new Int32Rect(13, 883, 328, 70), RedactKind.Blur));
                f.AddMark(new Mark(MarkKind.Box, new Int32Rect(708, 323, 178, 36), Color.FromRgb(0xE5, 0x48, 0x4D)));
            }, "Highlight");
            Snapshot((FrameworkElement)ed.Content, 1180, 760, dark, Path.Combine(outDir, $"editor-{file}.png"));

            var st = new SettingsWindow(welcome: false) { Height = 1060 };
            Snapshot((FrameworkElement)st.Content, st.Width, 1060, dark, Path.Combine(outDir, $"settings-{file}.png"));
        }
        App.Instance.Settings.Theme = "Lavender";
        ThemeColors.Apply(Application.Current.Resources, "Lavender", false, glass: true);

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
        // Glass inset on a colourful part of the calendar (its edge isn't one colour, so Auto picks glass).
        var mixed = new FrameSource(part2);
        foreach (var (name, index) in new[] { ("glass-dusk", 13), ("glass-ocean", 15), ("glass-auto", -1) })
        {
            var gs = new FrameStyle { GradientIndex = index, Inset = 40, InnerRadius = 12, OuterRadius = 30, Padding = 90 };
            FrameRenderer.SavePng(FrameRenderer.RenderBitmap(mixed, gs), Path.Combine(outDir, $"framed-{name}.png"));
        }
        foreach (var f in new[] { 0, 50, 100 })
        {
            var fs = new FrameStyle { GradientIndex = 13, Inset = 40, InnerRadius = 12, OuterRadius = 30, Padding = 90, InsetLook = InsetLook.Glass, GlassFrost = f };
            FrameRenderer.SavePng(FrameRenderer.RenderBitmap(mixed, fs), Path.Combine(outDir, $"framed-frost-{f}.png"));
            var ps = new FrameStyle { Kind = BackdropKind.Picture, Inset = 40, InnerRadius = 12, OuterRadius = 30, Padding = 90, InsetLook = InsetLook.Glass, GlassFrost = f };
            FrameRenderer.SavePng(FrameRenderer.RenderBitmap(mixed, ps), Path.Combine(outDir, $"framed-frost-picture-{f}.png"));
        }
        var edgeStyle = new FrameStyle { GradientIndex = 13, Inset = 40, InnerRadius = 12, OuterRadius = 30, Padding = 90, InsetLook = InsetLook.Edge };
        FrameRenderer.SavePng(FrameRenderer.RenderBitmap(mixed, edgeStyle), Path.Combine(outDir, "framed-edge-dusk.png"));
        {
            var sp = new FrameSource(sample);
            sp.AddMark(new Mark(MarkKind.Spotlight, new Int32Rect(350, 100, 1240, 380), Colors.Red));
            sp.AddMark(new Mark(MarkKind.Marker, new Int32Rect(1086, 186, 140, 30), Color.FromRgb(0xF5, 0xA5, 0x24)));
            sp.SetCrop(new Int32Rect(0, 0, 1600, 700));
            FrameRenderer.SavePng(FrameRenderer.RenderBitmap(sp, new FrameStyle()), Path.Combine(outDir, "framed-spotlight-crop.png"));
        }
        Example("auto", s => { s.GradientIndex = -1; s.Shape = GradientShape.Mesh; });
        Example("auto-linear", s => { s.GradientIndex = -1; s.Shape = GradientShape.Linear; s.Seed = 1; });
        Example("transparent", s => { s.Kind = BackdropKind.Solid; s.SolidColor = "#00FFFFFF"; s.Padding = 50; });
        Example("shadow-big", s => { s.Kind = BackdropKind.Solid; s.SolidColor = "#F3F4F6"; s.Padding = 110; s.Shadow = 75; s.ShadowSize = 95; });
        Example("shadow-tight", s => { s.Kind = BackdropKind.Solid; s.SolidColor = "#F3F4F6"; s.Padding = 110; s.Shadow = 70; s.ShadowSize = 8; });

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
        // Windows that are never shown don't get "loaded", so put the switches and highlights in place by hand.
        Anim.SettleAll(root);
        root.UpdateLayout();
        Anim.SettleAll(root);
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
