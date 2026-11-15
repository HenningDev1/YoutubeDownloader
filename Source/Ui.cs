using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace YoutubeDownloader;

/// <summary>Small builders that keep the pages short and consistent.</summary>
public static class Ui
{
    public static T Res<T>(this T c, AvaloniaProperty prop, string key) where T : Control
    {
        c[!prop] = c.GetResourceObservable(key).ToBinding();
        return c;
    }

    public static Border Card(Control child, double pad = 16) =>
        new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(pad), Child = child }
            .Res(Border.BackgroundProperty, "CardBrush");

    public static TextBlock Header(string text) => new() { Text = text.ToUpperInvariant(), Classes = { "h" } };

    public static TextBlock Label(string text, double size = 13, string brush = "TextBrush", FontWeight? weight = null) =>
        new TextBlock { Text = text, FontSize = size, FontWeight = weight ?? FontWeight.Normal, TextWrapping = TextWrapping.Wrap }
            .Res(TextBlock.ForegroundProperty, brush);

    public static Button Pill(string text, bool primary = false)
    {
        var b = new Button { Content = text, Classes = { "pill" } };
        if (primary) b.Classes.Add("primary");
        return b;
    }

    public static StackPanel Row(double spacing, params Control[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
        p.Children.AddRange(children);
        return p;
    }

    public static CheckBox Check(string text, bool value, Action<bool> changed)
    {
        var c = new CheckBox { Content = text, IsChecked = value, FontSize = 13 }.Res(CheckBox.ForegroundProperty, "TextBrush");
        c.IsCheckedChanged += (_, _) => changed(c.IsChecked == true);
        return c;
    }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static async Task<Bitmap?> LoadImageAsync(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        try
        {
            var bytes = await Http.GetByteArrayAsync(url);
            using var ms = new MemoryStream(bytes);
            return Bitmap.DecodeToWidth(ms, 480);
        }
        catch { return null; }
    }

    public static string FormatDuration(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
}

/// <summary>macOS-style segmented control.</summary>
public sealed class Segmented : Border
{
    private readonly StackPanel _panel = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    private readonly Dictionary<string, Button> _buttons = new();
    public string Value { get; private set; }
    public event Action<string>? Changed;

    public Segmented(IEnumerable<string> items, string value, Func<string, string>? label = null)
    {
        CornerRadius = new CornerRadius(10);
        Padding = new Thickness(3);
        HorizontalAlignment = HorizontalAlignment.Left;
        this.Res(BackgroundProperty, "FieldBrush");
        Child = _panel;
        Value = value;
        foreach (var item in items)
        {
            var b = new Button { Content = label?.Invoke(item) ?? item, Classes = { "seg" }, MinWidth = 54 };
            b.Click += (_, _) => Select(item);
            _buttons[item] = b;
            _panel.Children.Add(b);
        }
        Mark();
    }

    public void Select(string item, bool notify = true)
    {
        if (!_buttons.ContainsKey(item)) return;
        Value = item;
        Mark();
        if (notify) Changed?.Invoke(item);
    }

    public void SetEnabled(string item, bool on) { if (_buttons.TryGetValue(item, out var b)) b.IsEnabled = on; }

    private void Mark()
    {
        foreach (var (k, b) in _buttons) b.Classes.Set("on", k == Value);
    }
}
