using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace Scr;

public enum BackdropKind { Solid, Gradient, Picture }
public enum GradientShape { Linear, Mesh, Radial }
public enum AspectRatio { Auto, Wide, Classic, Square }
public enum ColorMode { System, Light, Dark }
public enum InsetLook { Auto, Glass, Edge }
public enum AfterCapture { Editor, Notification }

/// <summary>How a capture is framed. Sizes are in "units" that grow with the capture, so a style looks the same on small and big shots.</summary>
public sealed class FrameStyle
{
    public double Padding { get; set; } = 70;
    public double Shadow { get; set; } = 55;      // strength
    public double ShadowSize { get; set; } = 40;
    public double Inset { get; set; } = 0;
    public InsetLook InsetLook { get; set; } = InsetLook.Auto;
    public double GlassFrost { get; set; } = 30; // 0 clear glass, 100 milky frosted glass
    public double OuterRadius { get; set; } = 14;
    public double InnerRadius { get; set; } = 8;
    public BackdropKind Kind { get; set; } = BackdropKind.Gradient;
    public string SolidColor { get; set; } = "#FFFFFF";
    public int GradientIndex { get; set; } = -1; // -1: colours from the screenshot
    public GradientShape Shape { get; set; } = GradientShape.Mesh;
    public bool Reversed { get; set; }
    public int Seed { get; set; } = 3;
    public bool PictureFromCapture { get; set; } = true;
    public string? PicturePath { get; set; }
    public bool PictureBlur { get; set; } = true;
    public AspectRatio Ratio { get; set; } = AspectRatio.Auto;
    public RedactKind RedactKind { get; set; } = RedactKind.Pixelate;
    public MarkKind HighlightKind { get; set; } = MarkKind.Box;
    public string MarkColor { get; set; } = "#E5484D";

    public FrameStyle Clone() => (FrameStyle)MemberwiseClone();

    [JsonIgnore]
    public bool IsPlain => Padding <= 0 && Shadow <= 0 && Inset <= 0 && OuterRadius <= 0 && Ratio == AspectRatio.Auto;
}

public sealed class AppSettings
{
    public int HotkeyMods { get; set; } = 0;
    public int HotkeyVk { get; set; } = 0x2C; // Print Screen
    public bool TakeOverWinShiftS { get; set; } = true;
    public bool QuietFullScreen { get; set; } = true;
    public bool CheckForUpdates { get; set; } = true;
    public bool StartMenuShortcut { get; set; } = true;
    public DateTime? LastUpdateCheck { get; set; }
    public string Theme { get; set; } = "Lavender";
    public ColorMode ColorMode { get; set; } = ColorMode.System;
    public int GlassLevel { get; set; } = 3; // transparency, 1 (clearest) to 5 (solid); 3 is the original look
    public bool CopyOnCapture { get; set; } = true;
    public AfterCapture AfterCapture { get; set; } = AfterCapture.Editor;
    public bool CloseAfterCopy { get; set; } = true;
    public string? SaveFolder { get; set; }
    public bool Welcomed { get; set; }
    public FrameStyle Style { get; set; } = new();

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    // SCR_DATA lets a test run use its own folder instead of %APPDATA%\Scr.
    public static string Folder => Environment.GetEnvironmentVariable("SCR_DATA") is { Length: > 0 } dev
        ? dev
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Scr");
    static string FilePath => Path.Combine(Folder, "settings.json");

    public string SaveFolderOrDefault => string.IsNullOrWhiteSpace(SaveFolder)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots")
        : SaveFolder!;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new AppSettings();
        }
        catch { /* a broken file falls back to the defaults */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, FilePath, true);
        }
        catch { /* not worth bothering the user about */ }
    }

    // Start with Windows lives in the registry, not in the settings file, so it's always the truth.
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>The app used to be called Snap: bring its settings and its Start with Windows entry over once.</summary>
    public static void MigrateFromSnap()
    {
        try
        {
            if (Environment.GetEnvironmentVariable("SCR_DATA") is { Length: > 0 }) return;
            var oldFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Snap", "settings.json");
            if (File.Exists(oldFile) && !File.Exists(FilePath))
            {
                var old = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(oldFile), Json) ?? new AppSettings();
                old.Welcomed = true; // they've used it already
                old.Save();
            }
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (k?.GetValue("Snap") is string)
            {
                k.DeleteValue("Snap", false);
                k.SetValue("Scr", $"\"{Environment.ProcessPath}\" --tray");
            }
        }
        catch { /* starting fresh is fine too */ }
    }

    public static bool StartWithWindows
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue("Scr") is string;
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) k.SetValue("Scr", $"\"{Environment.ProcessPath}\" --tray");
            else k.DeleteValue("Scr", false);
        }
    }
}
