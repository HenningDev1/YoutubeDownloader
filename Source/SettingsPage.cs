using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace YoutubeDownloader;

public sealed class SettingsPage : ScrollViewer
{
    private readonly Settings _s;
    private readonly Action _apply;
    private readonly WrapPanel _accents = new();
    private readonly WrapPanel _backgrounds = new();
    private readonly StackPanel _fonts = new() { Spacing = 4 };

    public SettingsPage(Settings s, Action apply)
    {
        _s = s; _apply = apply;
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;

        // ---- appearance ----
        var mode = new Segmented(new[] { "Dark", "Light" }, s.Mode);
        mode.Changed += m => { s.Mode = m; Changed(); };

        var customAccent = new TextBox { Classes = { "field" }, Watermark = "#FF3B30", Width = 120, Text = s.CustomAccent };
        customAccent.TextChanged += (_, _) =>
        {
            if (!Themes.TryParse(customAccent.Text, out _)) return;
            s.CustomAccent = customAccent.Text!.Trim(); s.Accent = "Custom"; Changed();
        };
        var customBg = new TextBox { Classes = { "field" }, Watermark = "#1C1C1E", Width = 120, Text = Themes.TryParse(s.CustomBackground, out _) ? s.CustomBackground : "" };
        customBg.TextChanged += (_, _) =>
        {
            if (!Themes.TryParse(customBg.Text, out _)) return;
            s.CustomBackground = customBg.Text!.Trim(); Changed();
        };

        var appearance = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"), RowSpacing = 14 };
        void AddRow(Grid g, int row, string label, Control c)
        {
            var l = Ui.Label(label, 13, "SubtleBrush");
            l.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(l, row); g.Children.Add(l);
            Grid.SetRow(c, row); Grid.SetColumn(c, 1); g.Children.Add(c);
        }
        AddRow(appearance, 0, "Appearance", mode);
        AddRow(appearance, 1, "Accent colour", _accents);
        AddRow(appearance, 2, "Custom accent", Ui.Row(8, customAccent, Hint("Hex colour, e.g. #FF3B30")));
        AddRow(appearance, 3, "Background", _backgrounds);
        AddRow(appearance, 4, "Custom background", Ui.Row(8, customBg, Hint("Hex colour; text colour adapts")));
        BuildSwatches();

        // ---- font ----
        BuildFonts();

        // ---- downloads ----
        var folder = new TextBox { Classes = { "field" }, Text = s.OutputFolder };
        folder.LostFocus += (_, _) => { if (!string.IsNullOrWhiteSpace(folder.Text)) { s.OutputFolder = folder.Text.Trim(); s.Save(); } };
        var browse = Ui.Pill("Browse…");
        browse.Click += async (_, _) =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top == null) return;
            var res = await top.StorageProvider.OpenFolderPickerAsync(new() { Title = "Default download folder" });
            if (res.Count > 0 && res[0].TryGetLocalPath() is { } p) { folder.Text = p; s.OutputFolder = p; s.Save(); }
        };
        var folderRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        folderRow.Children.Add(folder);
        Grid.SetColumn(browse, 1); folderRow.Children.Add(browse);

        // ---- engine ----
        var engineStatus = Ui.Label(EngineText(), 12.5, "SubtleBrush");
        var update = Ui.Pill("Update yt-dlp");
        update.Click += async (_, _) =>
        {
            update.IsEnabled = false;
            engineStatus.Text = "Checking for updates…";
            try { engineStatus.Text = await Tools.UpdateYtDlpAsync(); }
            catch (Exception ex) { engineStatus.Text = ex.Message; }
            update.IsEnabled = true;
        };
        var openTools = Ui.Pill("Open folder");
        openTools.Click += (_, _) => { Directory.CreateDirectory(Tools.ToolsDir); DownloadPage.OpenPath(Tools.ToolsDir); };
        var engine = new StackPanel { Spacing = 10, Children = { engineStatus, Ui.Row(8, update, openTools),
            Hint("If downloads suddenly fail, YouTube probably changed something — updating yt-dlp usually fixes it.") } };

        var title = new TextBlock { Text = "Settings", Classes = { "title" } };
        title.Margin = new Thickness(0, 0, 0, 6);
        var about = Hint($"Youtube Downloader {Installer.Version} · Only download videos you have the right to save.");
        about.Margin = new Thickness(0, 18, 0, 0);
        Content = new StackPanel
        {
            Margin = new Thickness(28, 0, 28, 24),
            Children =
            {
                title,
                Ui.Header("Theme"), Ui.Card(appearance),
                Ui.Header("Font"), Ui.Card(_fonts, 8),
                Ui.Header("Default download folder"), Ui.Card(folderRow),
                Ui.Header("Download engine"), Ui.Card(engine),
                about,
            },
        };
    }

    private static string EngineText() =>
        $"yt-dlp: {Tools.YtDlp ?? "not installed yet"}\nffmpeg: {Tools.Ffmpeg ?? "not installed yet"}";

    private static TextBlock Hint(string t)
    {
        var l = Ui.Label(t, 12, "SubtleBrush");
        l.VerticalAlignment = VerticalAlignment.Center;
        return l;
    }

    private void Changed()
    {
        _apply();
        BuildSwatches();
        BuildFonts();
    }

    private void BuildSwatches()
    {
        _accents.Children.Clear();
        foreach (var (name, hex) in Themes.Accents)
            _accents.Children.Add(Swatch(Color.Parse(hex), name, _s.Accent == name, () => { _s.Accent = name; Changed(); }));
        if (Themes.TryParse(_s.CustomAccent, out var ca))
            _accents.Children.Add(Swatch(ca, "Custom", _s.Accent == "Custom", () => { _s.Accent = "Custom"; Changed(); }));

        _backgrounds.Children.Clear();
        bool dark = _s.Mode != "Light";
        foreach (var (name, d, l) in Themes.Backgrounds)
        {
            bool on = _s.CustomBackground == name || (name == "Default" && string.IsNullOrEmpty(_s.CustomBackground));
            _backgrounds.Children.Add(Swatch(Color.Parse(dark ? d : l), name, on, () => { _s.CustomBackground = name == "Default" ? "" : name; Changed(); }, outline: true));
        }
    }

    private static Control Swatch(Color c, string tip, bool selected, Action click, bool outline = false)
    {
        var dot = new Border
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = new SolidColorBrush(c),
            BorderThickness = new Thickness(outline ? 1 : 0),
        }.Res(Border.BorderBrushProperty, "FrameBrush");
        var ring = new Border
        {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(17), BorderThickness = new Thickness(2),
            Child = dot, Margin = new Thickness(0, 0, 6, 4), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Background = Brushes.Transparent,
        };
        if (selected) ring.Res(Border.BorderBrushProperty, "AccentBrush");
        else ring.BorderBrush = Brushes.Transparent;
        ToolTip.SetTip(ring, tip);
        ring.PointerPressed += (_, _) => click();
        return ring;
    }

    private void BuildFonts()
    {
        _fonts.Children.Clear();
        foreach (var f in Themes.Fonts)
        {
            var sample = new TextBlock { Text = f, FontFamily = Themes.FontFor(f), FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
            var preview = new TextBlock { Text = "The quick brown fox · 1080p", FontFamily = Themes.FontFor(f), FontSize = 12, VerticalAlignment = VerticalAlignment.Center }
                .Res(TextBlock.ForegroundProperty, "SubtleBrush");
            var check = new TextBlock { Text = _s.Font == f ? "" : "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), VerticalAlignment = VerticalAlignment.Center }
                .Res(TextBlock.ForegroundProperty, "AccentBrush");
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*,Auto") };
            g.Children.Add(sample);
            Grid.SetColumn(preview, 1); g.Children.Add(preview);
            Grid.SetColumn(check, 2); g.Children.Add(check);
            var b = new Button { Classes = { "nav" }, Content = g, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            if (_s.Font == f) b.Classes.Add("selected");
            b.Click += (_, _) => { _s.Font = f; Changed(); };
            _fonts.Children.Add(b);
        }
    }
}
