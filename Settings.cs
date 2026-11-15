using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace YoutubeDownloader;

public sealed class Settings
{
    // Appearance
    public string Mode { get; set; } = "Dark";
    public string Accent { get; set; } = "Red";
    public string CustomAccent { get; set; } = "";
    public string CustomBackground { get; set; } = "";
    public string Font { get; set; } = Themes.DefaultFont;

    // Downloads
    public string OutputFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Youtube Downloader");
    public string Format { get; set; } = "MP4";
    public string Quality { get; set; } = "1080p";
    public string AudioBitrate { get; set; } = "320";
    public bool Playlist { get; set; }
    public bool EmbedThumbnail { get; set; } = true;
    public bool Subtitles { get; set; }

    public double Width { get; set; } = 980;
    public double Height { get; set; } = 660;

    private static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YoutubeDownloader");
    private static string FilePath => Path.Combine(Dir, "settings.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string DataDir => Dir;

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) ?? new Settings();
        }
        catch { }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch { }
    }
}

public static class Themes
{
    public static readonly (string Name, string Hex)[] Accents =
    {
        ("Red",      "#FF3B30"),
        ("Orange",   "#FF9F0A"),
        ("Yellow",   "#FFD60A"),
        ("Green",    "#30D158"),
        ("Teal",     "#40C8E0"),
        ("Blue",     "#0A84FF"),
        ("Indigo",   "#5E5CE6"),
        ("Purple",   "#BF5AF2"),
        ("Pink",     "#FF375F"),
        ("Graphite", "#8E8E93"),
    };

    /// <summary>Background presets per mode; "Default" uses the system-like grey.</summary>
    public static readonly (string Name, string Dark, string Light)[] Backgrounds =
    {
        ("Default",  "#1C1C1E", "#F2F2F7"),
        ("Midnight", "#0B0F1A", "#E8ECF5"),
        ("Black",    "#000000", "#FFFFFF"),
        ("Warm",     "#1F1A17", "#F7F1EA"),
        ("Forest",   "#121A15", "#ECF3EE"),
    };

    public const string DefaultFont = "Segoe UI Variable";
    public static readonly string[] Fonts = { "Segoe UI Variable", "Inter", "Segoe UI", "Bahnschrift", "Cascadia Code", "Georgia" };

    public static FontFamily FontFor(string name) => name == "Inter"
        ? new FontFamily("avares://Avalonia.Fonts.Inter/Assets#Inter")
        : new FontFamily(name);

    public static bool TryParse(string? hex, out Color c)
    {
        c = default;
        return !string.IsNullOrWhiteSpace(hex) && Color.TryParse(hex.Trim(), out c);
    }

    public static Color AccentColor(Settings s)
    {
        var hex = s.Accent == "Custom" ? s.CustomAccent : Accents.FirstOrDefault(a => a.Name == s.Accent).Hex;
        return TryParse(hex, out var c) ? c : Color.Parse(Accents[0].Hex);
    }

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private static double Luma(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;

    public static void Apply(Settings s)
    {
        var app = Application.Current!;
        bool dark = s.Mode != "Light";
        app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;

        var accent = AccentColor(s);
        Color bg;
        if (TryParse(s.CustomBackground, out var cb)) bg = cb;
        else bg = Color.Parse(dark ? Backgrounds[0].Dark : Backgrounds[0].Light);
        var preset = Backgrounds.FirstOrDefault(b => b.Name == s.CustomBackground);
        if (preset.Name != null) bg = Color.Parse(dark ? preset.Dark : preset.Light);

        var text = Luma(bg) > 0.5 ? Color.Parse("#1C1C1E") : Color.Parse("#F5F5F7");
        var onAccent = Luma(accent) > 0.72 ? Colors.Black : Colors.White;
        bool bgDark = Luma(bg) <= 0.5;

        void Set(string key, Color c) => app.Resources[key] = new SolidColorBrush(c);
        Set("BgBrush", bg);
        Set("SidebarBrush", bgDark ? Mix(bg, Colors.White, 0.05) : Mix(bg, Colors.Black, 0.035));
        Set("CardBrush", bgDark ? Mix(bg, Colors.White, 0.08) : Colors.White);
        Set("FieldBrush", bgDark ? Mix(bg, Colors.White, 0.13) : Mix(bg, Colors.Black, 0.06));
        Set("HoverBrush", Color.FromArgb(34, text.R, text.G, text.B));
        Set("FrameBrush", Color.FromArgb(bgDark ? (byte)50 : (byte)35, text.R, text.G, text.B));
        Set("TextBrush", text);
        Set("SubtleBrush", Color.FromArgb(150, text.R, text.G, text.B));
        Set("FaintBrush", Color.FromArgb(80, text.R, text.G, text.B));
        Set("AccentBrush", accent);
        Set("AccentHoverBrush", Mix(accent, Luma(accent) > 0.6 ? Colors.Black : Colors.White, 0.14));
        Set("AccentSoftBrush", Color.FromArgb(40, accent.R, accent.G, accent.B));
        Set("OnAccentBrush", onAccent);
        Set("ErrorBrush", Color.Parse("#FF6961"));
        Set("SuccessBrush", Color.Parse("#30D158"));

        foreach (var k in new[] { "SystemAccentColor", "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorDark1", "SystemAccentColorDark2" })
            app.Resources[k] = accent;
        app.Resources["AppFont"] = FontFor(s.Font);
    }
}
