using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Scr;

/// <summary>Colours in the Kalendar family: Lavender theme, one accent, frosted surfaces. Light and dark follow Windows.</summary>
public static class ThemeColors
{
    public static bool IsDark { get; private set; }

    public static bool SystemIsDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    static SolidColorBrush B(uint argb)
    {
        var b = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        b.Freeze();
        return b;
    }

    static Brush Tint(byte alpha, params uint[] rgb)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0.33, 0), EndPoint = new Point(0.67, 1) };
        for (int i = 0; i < rgb.Length; i++)
        {
            var c = rgb[i];
            b.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, (byte)(c >> 16), (byte)(c >> 8), (byte)c), (double)i / (rgb.Length - 1)));
        }
        b.Freeze();
        return b;
    }

    public static void Apply(ResourceDictionary r, bool dark, bool glass)
    {
        IsDark = dark;
        if (!dark)
        {
            r["B.WindowTint"] = Tint(glass ? (byte)0x9E : (byte)0xFF, 0xEFEEFC, 0xEBEBFA, 0xE9E9FA);
            r["B.Ink"] = B(0xFF1D1D1F);
            r["B.InkInverse"] = B(0xFFFFFFFF);
            r["B.Muted"] = B(0xFF6B6A66);
            r["B.Accent"] = B(0xFF5558C0);
            r["B.AccentInk"] = B(0xFFFFFFFF);
            r["B.AccentSoft"] = B(0x245558C0);
            r["B.Hairline"] = B(0x121E1E3C);
            r["B.HairlineStrong"] = B(0x2A1E1E3C);
            r["B.Sheet"] = B(0x9EFFFFFF);
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
            r["B.WindowTint"] = Tint(glass ? (byte)0xDB : (byte)0xFF, 0x17161F, 0x15151D, 0x14141B);
            r["B.Ink"] = B(0xFFECECEA);
            r["B.InkInverse"] = B(0xFF15151E);
            r["B.Muted"] = B(0xFF9A9993);
            r["B.Accent"] = B(0xFF9A9CF0);
            r["B.AccentInk"] = B(0xFF15151E);
            r["B.AccentSoft"] = B(0x2E9A9CF0);
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
            r["B.Menu"] = B(0xF5272731);
            r["B.MenuEdge"] = B(0x22FFFFFF);
            r["B.SwitchOff"] = B(0x45FFFFFF);
            r["B.GhostHover"] = B(0x16FFFFFF);
        }
    }
}
