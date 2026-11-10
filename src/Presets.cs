using System.Windows;
using System.Windows.Media;

namespace Scr;

public sealed record GradientPreset(string Name, Color[] Colors);

public static class Presets
{
    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    static GradientPreset G(string name, params string[] hex) => new(name, System.Array.ConvertAll(hex, C));

    /// <summary>Soft pastels first, then deep blues, sunsets and greys.</summary>
    public static readonly GradientPreset[] Gradients =
    {
        G("Lavender mist", "#E4E1FB", "#F5E6F6", "#D8E7FB"),
        G("Sea glass", "#D3EEE6", "#E6F2F8", "#C9DDF2"),
        G("Peach", "#FFE0CF", "#FFCFDC", "#FFF0D6"),
        G("Honey", "#FFF0BD", "#FFD6A3", "#FFE8D4"),
        G("Meadow", "#DCF2D1", "#ECF7E9", "#C8E8D6"),
        G("Fog", "#E4E8EF", "#F0F2F6", "#D3D9E3"),

        G("Sky", "#BFDFFF", "#E2EDFF", "#CCC8FF"),
        G("Rose", "#F6C7D7", "#E9A3C2", "#C9A2DE"),
        G("Mint", "#C4F5DE", "#A6E3CB", "#DCEDC3"),
        G("Coral", "#FFA48F", "#FF7896", "#FFB0BF"),
        G("Sunset", "#FF8A65", "#FDB57E", "#FFD8A8"),
        G("Ember", "#E8590C", "#F59F00", "#FFD43B"),

        G("Violet", "#5157C9", "#8C91F7", "#C7C9FF"),
        G("Dusk", "#2E1A47", "#7B3F8C", "#F08CA8"),
        G("Berry", "#5E1742", "#B0386C", "#EE7EA4"),
        G("Ocean", "#1C3B70", "#2C5BA0", "#6DCDE8"),
        G("Aurora", "#0B3B4A", "#1B7A8A", "#7EDCC0"),
        G("Lagoon", "#16124F", "#2C5E8E", "#45C3AC"),

        G("Deep sea", "#0F2027", "#203A43", "#2C5364"),
        G("Midnight", "#121A2C", "#243A57", "#4A69B0"),
        G("Night sky", "#0B0B2A", "#2A2C6C", "#8C7BC2"),
        G("Pebble", "#8F9EAB", "#E9EEF0", "#C3CCD2"),
        G("Graphite", "#353A43", "#4D5563", "#737F8E"),
    };

    public static readonly string[] Solids =
    {
        "#00FFFFFF", "#FFFFFF", "#F3F4F6", "#1D1D1F", "#000000", "#EFEEFC",
        "#FDECF3", "#E8F6F0", "#E5EDFB", "#FFF5D9", "#FCE7E8", "#E9EBF0",
        "#5558C0", "#BD3177", "#377A30", "#1F4FB4", "#A84F18", "#475569",
    };

    public static Brush PreviewBrush(GradientPreset g)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        for (int i = 0; i < g.Colors.Length; i++)
            b.GradientStops.Add(new GradientStop(g.Colors[i], g.Colors.Length == 1 ? 0 : (double)i / (g.Colors.Length - 1)));
        b.Freeze();
        return b;
    }
}
