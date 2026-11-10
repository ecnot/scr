using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Scr;

/// <summary>A theme from Kalendar: a pale three-stop gradient for the window and an accent colour that goes with it.</summary>
public sealed record AppTheme(string Name, Color[] Stops, Color Accent);

/// <summary>Colours in the Kalendar family: the same eight themes, frosted surfaces, light and dark.</summary>
public static class ThemeColors
{
    public static bool IsDark { get; private set; }

    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    static AppTheme T(string name, string a, string b, string c, string accent) => new(name, new[] { C(a), C(b), C(c) }, C(accent));

    /// <summary>The same themes, in the same order, as Kalendar. Lavender is the default.</summary>
    public static readonly AppTheme[] Themes =
    {
        T("Blossom", "#fef1f6", "#fdecf3", "#faedf7", "#bd3177"),
        T("Peach", "#fff7e8", "#feeee5", "#fce7e8", "#a84f18"),
        T("Honey", "#fff9dc", "#fff5d9", "#fdf1dc", "#8a6100"),
        T("Meadow", "#f1f8e7", "#e8f6f0", "#eaf3f8", "#377a30"),
        T("Glacier", "#e5edfb", "#f1f4fd", "#fbfcfe", "#1f4fb4"),
        T("Lavender", "#efeefc", "#ebebfa", "#e9e9fa", "#5558c0"),
        T("Dusk", "#edebfb", "#f6eaf7", "#fbecf0", "#824bc5"),
        T("Slate", "#f4f5f7", "#eff1f4", "#e9ebf0", "#475569"),
    };

    public static AppTheme Find(string? name) =>
        Themes.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Themes[5];

    public static bool SystemIsDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    static SolidColorBrush B(uint argb) => Solid(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));

    static SolidColorBrush Solid(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));

    /// <summary>The window's background: the theme gradient at 160°, see-through over acrylic.</summary>
    public static Brush Gradient(Color[] stops, byte alpha)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0.33, 0), EndPoint = new Point(0.67, 1) };
        for (int i = 0; i < stops.Length; i++)
            b.GradientStops.Add(new GradientStop(WithAlpha(stops[i], alpha), (double)i / (stops.Length - 1)));
        b.Freeze();
        return b;
    }

    /// <summary>The accent for dark mode: same hue, lighter (as Kalendar does it).</summary>
    static Color DarkAccent(Color c)
    {
        var (h, s, _) = ToHsl(c);
        return FromHsl(h, Math.Clamp(s, 0.55, 0.8), 0.70);
    }

    /// <summary>Dark mode's near-black, with a hint of the theme's hue.</summary>
    static Color DarkBase(Color accent, double lightness)
    {
        var (h, _, _) = ToHsl(accent);
        return FromHsl(h, 0.18, lightness);
    }

    // Transparency, five steps like Kalendar's (3 is the original look): how much of the window and the main surface is see-through.
    static readonly byte[] LightTint = { 0x40, 0x70, 0x9E, 0xCC, 0xFF };
    static readonly byte[] LightSheet = { 0x60, 0x80, 0x9E, 0xCC, 0xFF };
    static readonly byte[] DarkTint = { 0x99, 0xBB, 0xDB, 0xF0, 0xFF };

    public static void Apply(ResourceDictionary r, string? themeName, bool dark, bool glass, int level = 3)
    {
        IsDark = dark;
        var theme = Find(themeName);
        int i = Math.Clamp(level, 1, 5) - 1;
        if (!glass) i = 4; // without acrylic (Windows 10) everything is solid
        if (!dark)
        {
            r["B.WindowTint"] = Gradient(theme.Stops, LightTint[i]);
            r["B.Ink"] = B(0xFF1D1D1F);
            r["B.InkInverse"] = B(0xFFFFFFFF);
            r["B.Muted"] = B(0xFF6B6A66);
            r["B.Accent"] = Solid(theme.Accent);
            r["B.AccentInk"] = B(0xFFFFFFFF);
            r["B.AccentSoft"] = Solid(WithAlpha(theme.Accent, 0x24));
            r["B.Hairline"] = B(0x121E1E3C);
            r["B.HairlineStrong"] = B(0x2A1E1E3C);
            r["B.Sheet"] = Solid(Color.FromArgb(LightSheet[i], 0xFF, 0xFF, 0xFF));
            r["B.SheetEdge"] = B(0xC0FFFFFF);
            r["B.Control"] = B(0xB8FFFFFF);
            r["B.ControlHover"] = B(0xF0FFFFFF);
            r["B.ControlEdge"] = B(0x1C1E1E3C);
            r["B.Track"] = B(0x221E1E3C);
            r["B.SegTrack"] = B(0x141E1E3C);
            r["B.SegOn"] = B(0xFFFFFFFF);
            r["B.Menu"] = B(0xF7FBFBFE);
            r["B.MenuEdge"] = B(0x221E1E3C);
            r["B.SwitchOff"] = B(0x401E1E3C);
            r["B.GhostHover"] = B(0x121E1E3C);
        }
        else
        {
            // The theme's gradient seen through a dark veil, with a hint of its hue.
            var veil = DarkBase(theme.Accent, 0.10);
            var stops = theme.Stops.Select(s => Mix(s, veil, 0.88)).ToArray();
            var accent = DarkAccent(theme.Accent);
            r["B.WindowTint"] = Gradient(stops, DarkTint[i]);
            r["B.Ink"] = B(0xFFECECEA);
            r["B.InkInverse"] = B(0xFF15151E);
            r["B.Muted"] = B(0xFF9A9993);
            r["B.Accent"] = Solid(accent);
            r["B.AccentInk"] = Solid(DarkBase(theme.Accent, 0.09));
            r["B.AccentSoft"] = Solid(WithAlpha(accent, 0x2E));
            r["B.Hairline"] = B(0x16FFFFFF);
            r["B.HairlineStrong"] = B(0x2EFFFFFF);
            r["B.Sheet"] = B(0x12FFFFFF);
            r["B.SheetEdge"] = B(0x1CFFFFFF);
            r["B.Control"] = B(0x16FFFFFF);
            r["B.ControlHover"] = B(0x26FFFFFF);
            r["B.ControlEdge"] = B(0x14FFFFFF);
            r["B.Track"] = B(0x2AFFFFFF);
            r["B.SegTrack"] = B(0x1AFFFFFF);
            r["B.SegOn"] = B(0x3CFFFFFF);
            r["B.Menu"] = Solid(WithAlpha(DarkBase(theme.Accent, 0.16), 0xF5));
            r["B.MenuEdge"] = B(0x22FFFFFF);
            r["B.SwitchOff"] = B(0x45FFFFFF);
            r["B.GhostHover"] = B(0x16FFFFFF);
        }
    }

    // ---------- HSL ----------

    public static (double H, double S, double L) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2, h = 0, s = 0;
        if (max != min)
        {
            double d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h /= 6;
        }
        return (h, s, l);
    }

    public static Color FromHsl(double h, double s, double l)
    {
        double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }
        double q2 = l < 0.5 ? l * (1 + s) : l + s - l * s, p2 = 2 * l - q2;
        return Color.FromRgb((byte)Math.Round(Hue(p2, q2, h + 1.0 / 3) * 255),
                             (byte)Math.Round(Hue(p2, q2, h) * 255),
                             (byte)Math.Round(Hue(p2, q2, h - 1.0 / 3) * 255));
    }
}
