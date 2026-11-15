using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace YoutubeDownloader;

public sealed class MainWindow : Window
{
    private readonly Settings _settings;
    private readonly Border _frame;
    private readonly ContentControl _content = new();
    private readonly Dictionary<string, (Button Nav, Control Page)> _pages = new();
    private readonly DownloadPage _download;
    private readonly ToolsBanner _banner;

    public MainWindow(Settings settings)
    {
        _settings = settings;
        Title = "Youtube Downloader";
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://YoutubeDownloader/YoutubeDownloader.ico")));
        Width = settings.Width; Height = settings.Height;
        MinWidth = 760; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        this.Res(FontFamilyProperty, "AppFont");
        this.Res(ForegroundProperty, "TextBrush");

        _banner = new ToolsBanner();
        _download = new DownloadPage(settings, _banner);
        var convert = new ConvertPage(settings, _banner);
        var prefs = new SettingsPage(settings, () => { Themes.Apply(settings); settings.Save(); });

        // ---- sidebar ----
        var logo = new Image { Width = 28, Height = 28 };
        try { logo.Source = new Bitmap(AssetLoader.Open(new Uri("avares://YoutubeDownloader/Assets/icon-256.png"))); } catch { }
        var brand = Ui.Row(9, logo, Ui.Label("Youtube Downloader", 14, weight: FontWeight.SemiBold));
        brand.Margin = new Thickness(6, 22, 0, 18);
        ((TextBlock)brand.Children[1]).VerticalAlignment = VerticalAlignment.Center;

        var nav = new StackPanel { Spacing = 3 };
        void AddPage(string key, string glyph, string label, Control page)
        {
            var icon = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Width = 22 };
            var b = new Button { Classes = { "nav" }, Content = Ui.Row(6, icon, new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }) };
            b.Click += (_, _) => Show(key);
            nav.Children.Add(b);
            _pages[key] = (b, page);
        }
        AddPage("download", "", "Download", _download);
        AddPage("convert", "", "Convert", convert);
        AddPage("settings", "", "Settings", prefs);

        var lights = new TrafficLights(this) { Margin = new Thickness(6, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        var sideStack = new StackPanel();
        sideStack.Children.AddRange(new Control[] { lights, brand, nav });
        var sideDrag = new Border { Background = Brushes.Transparent, Child = sideStack, Padding = new Thickness(12, 14, 12, 12) };
        sideDrag.PointerPressed += DragWindow;
        var sidebar = new Border { Width = 210, Child = sideDrag, BorderThickness = new Thickness(0, 0, 1, 0) }
            .Res(Border.BackgroundProperty, "SidebarBrush").Res(Border.BorderBrushProperty, "FrameBrush");

        // ---- content ----
        var topDrag = new Border { Height = 30, Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Top };
        topDrag.PointerPressed += DragWindow;
        var right = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        right.Children.Add(topDrag);
        Grid.SetRow(_banner, 1);
        right.Children.Add(_banner);
        Grid.SetRow(_content, 2);
        right.Children.Add(_content);

        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        root.Children.Add(sidebar);
        Grid.SetColumn(right, 1);
        root.Children.Add(right);

        _frame = new Border { CornerRadius = new CornerRadius(14), ClipToBounds = true, BorderThickness = new Thickness(1), Child = root }
            .Res(Border.BackgroundProperty, "BgBrush").Res(Border.BorderBrushProperty, "FrameBrush");
        Content = _frame;

        AddHandler(PointerPressedEvent, OnResizePress, RoutingStrategies.Tunnel);
        PointerMoved += OnResizeHover;
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
                _frame.CornerRadius = new CornerRadius(WindowState == WindowState.Normal ? 14 : 0);
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.F11) ToggleFullScreen();
            else if (e.Key == Key.Escape && WindowState == WindowState.FullScreen) WindowState = WindowState.Normal;
            else if (e.Key == Key.OemComma && e.KeyModifiers == KeyModifiers.Control) Show("settings");
        };
        Closing += (_, _) =>
        {
            if (WindowState == WindowState.Normal) { _settings.Width = Width; _settings.Height = Height; }
            _settings.Save();
        };
        Opened += async (_, _) =>
        {
            Show("download");
            await _banner.EnsureToolsAsync();
            await _download.TryPasteFromClipboardAsync();
        };
        Activated += async (_, _) => await _download.TryPasteFromClipboardAsync();
    }

    private void Show(string key)
    {
        foreach (var (k, (b, _)) in _pages) b.Classes.Set("on", k == key);
        _content.Content = _pages[key].Page;
    }

    private void DragWindow(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || e.Source is Button) return;
        if (e.ClickCount == 2) ToggleMaximize(); else BeginMoveDrag(e);
    }

    public void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    public void ToggleFullScreen() =>
        WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;

    // ---------------- resizing a borderless window ----------------

    private WindowEdge? EdgeAt(Point p)
    {
        if (WindowState != WindowState.Normal) return null;
        const double g = 6, c = 16;
        double w = Bounds.Width, h = Bounds.Height;
        bool l = p.X < g, r = p.X > w - g, t = p.Y < g, b = p.Y > h - g;
        bool lc = p.X < c, rc = p.X > w - c, tc = p.Y < c, bc = p.Y > h - c;
        if ((t && lc) || (l && tc)) return WindowEdge.NorthWest;
        if ((t && rc) || (r && tc)) return WindowEdge.NorthEast;
        if ((b && lc) || (l && bc)) return WindowEdge.SouthWest;
        if ((b && rc) || (r && bc)) return WindowEdge.SouthEast;
        if (l) return WindowEdge.West;
        if (r) return WindowEdge.East;
        if (t) return WindowEdge.North;
        if (b) return WindowEdge.South;
        return null;
    }

    private void OnResizeHover(object? sender, PointerEventArgs e)
    {
        Cursor = EdgeAt(e.GetPosition(this)) switch
        {
            WindowEdge.West or WindowEdge.East => new Cursor(StandardCursorType.SizeWestEast),
            WindowEdge.North or WindowEdge.South => new Cursor(StandardCursorType.SizeNorthSouth),
            WindowEdge.NorthWest => new Cursor(StandardCursorType.TopLeftCorner),
            WindowEdge.SouthEast => new Cursor(StandardCursorType.BottomRightCorner),
            WindowEdge.NorthEast => new Cursor(StandardCursorType.TopRightCorner),
            WindowEdge.SouthWest => new Cursor(StandardCursorType.BottomLeftCorner),
            _ => Cursor.Default,
        };
    }

    private void OnResizePress(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (EdgeAt(e.GetPosition(this)) is { } edge) { e.Handled = true; BeginResizeDrag(edge, e); }
    }
}

/// <summary>Shows first-run setup of yt-dlp / ffmpeg, and errors if that fails.</summary>
public sealed class ToolsBanner : Border
{
    private readonly TextBlock _text = Ui.Label("", 12.5);
    private readonly ProgressBar _bar = new() { Minimum = 0, Maximum = 1, Width = 180, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _retry = Ui.Pill("Retry");
    private Task? _running;

    public ToolsBanner()
    {
        IsVisible = false;
        Margin = new Thickness(28, 0, 28, 8);
        CornerRadius = new CornerRadius(12);
        Padding = new Thickness(14, 10);
        this.Res(BackgroundProperty, "AccentSoftBrush");
        _text.VerticalAlignment = VerticalAlignment.Center;
        _retry.Click += async (_, _) => await EnsureToolsAsync();
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        g.Children.Add(_text);
        Grid.SetColumn(_bar, 1); g.Children.Add(_bar);
        Grid.SetColumn(_retry, 2); g.Children.Add(_retry);
        Child = g;
    }

    /// <summary>Makes sure yt-dlp and ffmpeg exist; returns false if they could not be set up.</summary>
    public async Task<bool> EnsureToolsAsync()
    {
        if (Tools.Ready) { IsVisible = false; return true; }
        if (_running != null) { await _running; return Tools.Ready; }
        IsVisible = true; _retry.IsVisible = false; _bar.IsVisible = true;
        _text.Text = "First start: setting up the download engine (yt-dlp + ffmpeg, about 140 MB)…";
        _running = Run();
        await _running;
        _running = null;
        return Tools.Ready;
    }

    private async Task Run()
    {
        try
        {
            await Tools.EnsureAsync((status, p) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _text.Text = "First start: " + status;
                _bar.IsIndeterminate = p < 0;
                if (p >= 0) _bar.Value = p;
            }));
            IsVisible = false;
        }
        catch (Exception ex)
        {
            _text.Text = "Could not set up the download engine: " + ex.Message;
            _bar.IsVisible = false; _retry.IsVisible = true;
        }
    }
}
