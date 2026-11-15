using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace YoutubeDownloader;

public sealed class ConvertPage : ScrollViewer
{
    private readonly Settings _s;
    private readonly ToolsBanner _banner;
    private readonly Segmented _type, _video, _audio, _size, _bitrate;
    private readonly StackPanel _queue = new() { Spacing = 8 };
    private readonly Border _drop;
    private readonly TextBlock _dropText = Ui.Label("Drop video or audio files here", 15, weight: FontWeight.SemiBold);
    private bool _sameFolder = true;
    private readonly SemaphoreSlim _slots = new(2);

    public ConvertPage(Settings s, ToolsBanner banner)
    {
        _s = s; _banner = banner;
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;

        // ---- drop zone ----
        var icon = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 30, HorizontalAlignment = HorizontalAlignment.Center }
            .Res(TextBlock.ForegroundProperty, "AccentBrush");
        _dropText.HorizontalAlignment = HorizontalAlignment.Center;
        var hint = Ui.Label("MP4, MKV, WEBM, MOV, AVI, MP3, M4A, WAV, FLAC and more", 12, "SubtleBrush");
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        var choose = Ui.Pill("Choose files…", primary: true);
        choose.HorizontalAlignment = HorizontalAlignment.Center;
        choose.Margin = new Thickness(0, 8, 0, 0);
        choose.Click += async (_, _) => await PickFiles();
        _drop = new Border
        {
            CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1.5), Padding = new Thickness(20, 26),
            Child = new StackPanel { Spacing = 6, Children = { icon, _dropText, hint, choose } },
        }.Res(Border.BackgroundProperty, "CardBrush").Res(Border.BorderBrushProperty, "FaintBrush");
        DragDrop.SetAllowDrop(_drop, true);
        _drop.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
            _drop.Res(Border.BorderBrushProperty, "AccentBrush");
        });
        _drop.AddHandler(DragDrop.DragLeaveEvent, (_, _) => _drop.Res(Border.BorderBrushProperty, "FaintBrush"));
        _drop.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            _drop.Res(Border.BorderBrushProperty, "FaintBrush");
            var files = e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).Where(p => p != null).Cast<string>().ToList();
            if (files is { Count: > 0 }) _ = Enqueue(files);
        });

        // ---- options ----
        _type = new Segmented(new[] { "Video", "Audio" }, "Video");
        _video = new Segmented(Converter.VideoTargets, "MP4");
        _audio = new Segmented(Converter.AudioTargets, "MP3");
        _size = new Segmented(Converter.Sizes, "Original");
        _bitrate = new Segmented(Formats.Bitrates, "320", b => b + " kbps");
        var where = new Segmented(new[] { "Next to the original", "Download folder" }, "Next to the original");
        where.Changed += v => _sameFolder = v == "Next to the original";

        var opts = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"), RowSpacing = 12 };
        void AddRow(int row, string label, Control c)
        {
            var l = Ui.Label(label, 13, "SubtleBrush");
            l.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(l, row); opts.Children.Add(l);
            Grid.SetRow(c, row); Grid.SetColumn(c, 1); opts.Children.Add(c);
        }
        AddRow(0, "Convert to", _type);
        AddRow(1, "Format", new Panel { Children = { _video, _audio } });
        AddRow(2, "Resolution", _size);
        AddRow(3, "Audio", _bitrate);
        AddRow(4, "Save", where);
        void Update()
        {
            bool audio = _type.Value == "Audio";
            _video.IsVisible = !audio; _audio.IsVisible = audio;
            _size.IsEnabled = !audio;
            _bitrate.IsEnabled = audio ? _audio.Value is not ("WAV" or "FLAC" or "OGG") : _video.Value != "GIF";
        }
        _type.Changed += _ => Update();
        _video.Changed += _ => Update();
        _audio.Changed += _ => Update();
        Update();

        var title = new TextBlock { Text = "Convert", Classes = { "title" } };
        var sub = Ui.Label("Turn any local video or audio file into another format.", 13, "SubtleBrush");
        sub.Margin = new Thickness(0, 2, 0, 16);
        var qh = Ui.Header("Conversions");
        qh.Margin = new Thickness(0, 22, 0, 8);
        Content = new StackPanel
        {
            Margin = new Thickness(28, 0, 28, 24),
            Children = { title, sub, Ui.Header("Output"), Ui.Card(opts), new Border { Height = 16 }, _drop, qh, _queue },
        };
    }

    private async Task PickFiles()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose files to convert",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Video and audio") { Patterns = Converter.InputExtensions.Select(e => "*." + e).ToArray() },
                FilePickerFileTypes.All,
            },
        });
        var paths = files.Select(f => f.TryGetLocalPath()).Where(p => p != null).Cast<string>().ToList();
        if (paths.Count > 0) await Enqueue(paths);
    }

    private async Task Enqueue(List<string> files)
    {
        if (!await _banner.EnsureToolsAsync()) return;
        string target = _type.Value == "Audio" ? _audio.Value : _video.Value;
        string size = _size.Value, bitrate = _bitrate.Value;
        string? folder = _sameFolder ? null : _s.OutputFolder;

        foreach (var file in files.Where(System.IO.File.Exists))
        {
            var detail = Path.GetExtension(file).TrimStart('.').ToUpperInvariant() + " → " + target;
            var item = new DownloadItem(Path.GetFileName(file), detail, null);
            _queue.Children.Insert(0, item);
            item.Run = async ct =>
            {
                await _slots.WaitAsync(ct);
                try
                {
                    if (folder != null) Directory.CreateDirectory(folder);
                    var output = Converter.OutputPath(file, target, folder);
                    Dispatcher.UIThread.Post(() => item.SetStatus("Converting…"));
                    await Converter.ConvertAsync(file, output, target, size, bitrate,
                        p => Dispatcher.UIThread.Post(() =>
                        {
                            if (p >= 0) item.SetProgress(p, $"Converting…  {p * 100:0}%");
                        }), ct);
                    return output;
                }
                finally { _slots.Release(); }
            };
            item.Retry = () => item.Begin();
            item.Begin();
        }
    }
}
