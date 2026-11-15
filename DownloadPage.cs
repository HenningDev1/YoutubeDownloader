using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace YoutubeDownloader;

public sealed class DownloadPage : ScrollViewer
{
    private readonly Settings _s;
    private readonly ToolsBanner _banner;
    private readonly TextBox _url = new() { Classes = { "field" }, Watermark = "Paste any YouTube link — video, Short, live or playlist", FontSize = 14, MinHeight = 40 };
    private readonly Button _go = Ui.Pill("Download", primary: true);
    private readonly Segmented _type, _videoFmt, _audioFmt, _quality, _bitrate;
    private readonly Control _qualityRow, _bitrateRow;
    private readonly CheckBox _thumb, _subs;
    private readonly TextBlock _folder = Ui.Label("", 12.5, "SubtleBrush");
    private readonly StackPanel _queue = new() { Spacing = 8 };
    private readonly TextBlock _emptyQueue = Ui.Label("Downloads you start appear here.", 12.5, "SubtleBrush");

    // preview
    private readonly Border _preview;
    private readonly Image _thumbImg = new() { Stretch = Stretch.UniformToFill };
    private readonly TextBlock _pTitle = Ui.Label("", 15, weight: FontWeight.SemiBold);
    private readonly TextBlock _pMeta = Ui.Label("", 12.5, "SubtleBrush");
    private readonly TextBlock _urlError = Ui.Label("", 12.5, "ErrorBrush");
    private CancellationTokenSource? _infoCts;
    private VideoInfo? _info;
    private string? _lastClipboard;
    private readonly SemaphoreSlim _slots = new(3);

    public DownloadPage(Settings s, ToolsBanner banner)
    {
        _s = s; _banner = banner;
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;

        // ---- link bar ----
        var paste = Ui.Pill("Paste");
        paste.Click += async (_, _) => { if (await ClipboardText() is { } t) _url.Text = t.Trim(); };
        _go.Padding = new Thickness(22, 9);
        _go.Click += (_, _) => Start();
        _url.KeyDown += (_, e) => { if (e.Key == Key.Enter) Start(); };
        _url.TextChanged += (_, _) => OnUrlChanged();
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
        bar.Children.Add(_url);
        Grid.SetColumn(paste, 1); bar.Children.Add(paste);
        Grid.SetColumn(_go, 2); bar.Children.Add(_go);

        var thumbBox = new Border { Width = 176, Height = 99, CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = _thumbImg }
            .Res(Border.BackgroundProperty, "FieldBrush");
        var pText = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Children = { _pTitle, _pMeta } };
        var pGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 14 };
        pGrid.Children.Add(thumbBox);
        Grid.SetColumn(pText, 1); pGrid.Children.Add(pText);
        _preview = new Border { Child = pGrid, Margin = new Thickness(0, 14, 0, 0), IsVisible = false };
        _urlError.IsVisible = false;
        _urlError.Margin = new Thickness(2, 8, 0, 0);

        var linkCard = Ui.Card(new StackPanel { Children = { bar, _urlError, _preview } });

        // ---- options ----
        bool audio = Formats.IsAudio(s.Format);
        _type = new Segmented(new[] { "Video", "Audio" }, audio ? "Audio" : "Video");
        _videoFmt = new Segmented(Formats.Video, audio ? "MP4" : s.Format);
        _audioFmt = new Segmented(Formats.Audio, audio ? s.Format : "MP3");
        _quality = new Segmented(Formats.Qualities, s.Quality);
        _bitrate = new Segmented(Formats.Bitrates, s.AudioBitrate, b => b + " kbps");
        _thumb = Ui.Check("Embed thumbnail & tags", s.EmbedThumbnail, v => { s.EmbedThumbnail = v; s.Save(); });
        _subs = Ui.Check("Embed subtitles", s.Subtitles, v => { s.Subtitles = v; s.Save(); });
        var playlist = Ui.Check("Download whole playlist", s.Playlist, v => { s.Playlist = v; s.Save(); OnUrlChanged(force: true); });

        var opts = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"), RowSpacing = 12 };
        void AddRow(int row, string label, Control c)
        {
            var l = Ui.Label(label, 13, "SubtleBrush");
            l.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(l, row); opts.Children.Add(l);
            Grid.SetRow(c, row); Grid.SetColumn(c, 1); opts.Children.Add(c);
        }
        var fmtHost = new Panel { Children = { _videoFmt, _audioFmt } };
        AddRow(0, "Type", _type);
        AddRow(1, "Format", fmtHost);
        _qualityRow = _quality;
        _bitrateRow = _bitrate;
        AddRow(2, "Quality", new Panel { Children = { _quality, _bitrate } });
        var checks = new WrapPanel { Children = { _thumb, _subs, playlist } };
        foreach (var c in checks.Children) c.Margin = new Thickness(0, 0, 18, 0);
        AddRow(3, "Options", checks);

        var change = Ui.Pill("Change…");
        change.Click += async (_, _) => await PickFolder();
        var open = Ui.Pill("Open");
        open.Click += (_, _) => { Directory.CreateDirectory(_s.OutputFolder); OpenPath(_s.OutputFolder); };
        _folder.VerticalAlignment = VerticalAlignment.Center;
        _folder.TextTrimming = TextTrimming.CharacterEllipsis;
        _folder.TextWrapping = TextWrapping.NoWrap;
        var folderRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 6 };
        folderRow.Children.Add(_folder);
        Grid.SetColumn(change, 1); folderRow.Children.Add(change);
        Grid.SetColumn(open, 2); folderRow.Children.Add(open);
        AddRow(4, "Save to", folderRow);
        UpdateFolder();

        _type.Changed += _ => { SaveFormat(); UpdateVisibility(); };
        _videoFmt.Changed += _ => SaveFormat();
        _audioFmt.Changed += _ => { SaveFormat(); UpdateVisibility(); };
        _quality.Changed += q => { s.Quality = q; s.Save(); };
        _bitrate.Changed += b => { s.AudioBitrate = b; s.Save(); };
        UpdateVisibility();

        // ---- queue ----
        var clear = Ui.Pill("Clear finished");
        clear.Padding = new Thickness(10, 4);
        clear.FontSize = 12;
        clear.Click += (_, _) =>
        {
            foreach (var item in _queue.Children.OfType<DownloadItem>().Where(i => i.Finished).ToList()) _queue.Children.Remove(item);
            _emptyQueue.IsVisible = !_queue.Children.OfType<DownloadItem>().Any();
        };
        var qHead = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 22, 0, 8) };
        var qh = Ui.Header("Downloads"); qh.Margin = new Thickness(0); qh.VerticalAlignment = VerticalAlignment.Center;
        qHead.Children.Add(qh);
        Grid.SetColumn(clear, 1); qHead.Children.Add(clear);
        _queue.Children.Add(_emptyQueue);

        var title = new TextBlock { Text = "Download", Classes = { "title" } };
        var sub = Ui.Label("Save YouTube videos as video or audio files, in up to 4K.", 13, "SubtleBrush");
        sub.Margin = new Thickness(0, 2, 0, 16);
        var optsHead = Ui.Header("Output");

        Content = new StackPanel
        {
            Margin = new Thickness(28, 0, 28, 24),
            Children = { title, sub, linkCard, optsHead, Ui.Card(opts), qHead, _queue },
        };
    }

    private string CurrentFormat => _type.Value == "Audio" ? _audioFmt.Value : _videoFmt.Value;

    private void SaveFormat() { _s.Format = CurrentFormat; _s.Save(); }

    private void UpdateVisibility()
    {
        bool audio = _type.Value == "Audio";
        _videoFmt.IsVisible = !audio;
        _audioFmt.IsVisible = audio;
        _qualityRow.IsVisible = !audio;
        _bitrateRow.IsVisible = audio;
        _bitrate.IsEnabled = !(audio && _audioFmt.Value is "WAV" or "FLAC");
        _subs.IsVisible = !audio;
    }

    private void UpdateFolder() => _folder.Text = _s.OutputFolder;

    private async Task PickFolder()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;
        IStorageFolder? start = null;
        try { if (Directory.Exists(_s.OutputFolder)) start = await top.StorageProvider.TryGetFolderFromPathAsync(_s.OutputFolder); } catch { }
        var res = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Save downloads to", SuggestedStartLocation = start });
        if (res.Count > 0 && res[0].TryGetLocalPath() is { } p) { _s.OutputFolder = p; _s.Save(); UpdateFolder(); }
    }

    private async Task<string?> ClipboardText()
    {
        try { return await (TopLevel.GetTopLevel(this)?.Clipboard?.GetTextAsync() ?? Task.FromResult<string?>(null)); }
        catch { return null; }
    }

    /// <summary>When the app gets focus with a fresh YouTube link on the clipboard, drop it into an empty field.</summary>
    public async Task TryPasteFromClipboardAsync()
    {
        if (!string.IsNullOrWhiteSpace(_url.Text)) return;
        var t = (await ClipboardText())?.Trim();
        if (t == null || t == _lastClipboard || !Downloader.LooksLikeYouTube(t)) return;
        _lastClipboard = t;
        _url.Text = t;
    }

    // ---------------- preview ----------------

    private void OnUrlChanged(bool force = false)
    {
        _infoCts?.Cancel();
        var text = _url.Text?.Trim() ?? "";
        _info = null;
        if (text.Length == 0) { _preview.IsVisible = false; _urlError.IsVisible = false; return; }
        if (!Downloader.LooksLikeYouTube(text))
        {
            _preview.IsVisible = false;
            _urlError.Text = "That doesn't look like a YouTube link.";
            _urlError.IsVisible = true;
            return;
        }
        _urlError.IsVisible = false;
        var cts = _infoCts = new CancellationTokenSource();
        _ = LoadInfo(text, cts.Token);
    }

    private async Task LoadInfo(string url, CancellationToken ct)
    {
        try
        {
            await Task.Delay(350, ct);
            if (!await _banner.EnsureToolsAsync()) return;
            _preview.IsVisible = true;
            _pTitle.Text = "Loading video info…";
            _pMeta.Text = "";
            _thumbImg.Source = null;
            var info = await Downloader.FetchInfoAsync(url, _s.Playlist, ct);
            if (ct.IsCancellationRequested) return;
            _info = info;
            _pTitle.Text = info.Title;
            var meta = new List<string>();
            if (!string.IsNullOrEmpty(info.Channel)) meta.Add(info.Channel);
            if (info.IsPlaylist) meta.Add($"Playlist · {info.Count} videos");
            if (info.Duration is { } d) meta.Add(Ui.FormatDuration(d));
            if (info.MaxHeight > 0) meta.Add("up to " + (info.MaxHeight >= 2160 ? "4K" : info.MaxHeight + "p"));
            _pMeta.Text = string.Join("  ·  ", meta);
            foreach (var q in Formats.Qualities) _quality.SetEnabled(q, info.MaxHeight == 0 || Formats.Height(q) <= Math.Max(info.MaxHeight, 720));
            var img = await Ui.LoadImageAsync(info.Thumbnail);
            if (!ct.IsCancellationRequested) _thumbImg.Source = img;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested) return;
            _preview.IsVisible = false;
            _urlError.Text = ex.Message;
            _urlError.IsVisible = true;
        }
    }

    // ---------------- downloading ----------------

    private async void Start()
    {
        var url = _url.Text?.Trim() ?? "";
        if (!Downloader.LooksLikeYouTube(url))
        {
            _urlError.Text = url.Length == 0 ? "Paste a YouTube link first." : "That doesn't look like a YouTube link.";
            _urlError.IsVisible = true;
            return;
        }
        if (!await _banner.EnsureToolsAsync()) return;

        string format = CurrentFormat, quality = _quality.Value, bitrate = _bitrate.Value, folder = _s.OutputFolder;
        bool playlist = _s.Playlist, thumb = _s.EmbedThumbnail, subs = _s.Subtitles && !Formats.IsAudio(format);
        var label = Formats.IsAudio(format) ? $"{format} · {(format is "WAV" or "FLAC" ? "lossless" : bitrate + " kbps")}" : $"{format} · {quality}";
        var item = new DownloadItem(_info?.Title ?? url, label, _info?.Thumbnail);
        _emptyQueue.IsVisible = false;
        _queue.Children.Insert(0, item);
        _url.Text = "";

        item.Run = async ct =>
        {
            await _slots.WaitAsync(ct);
            try
            {
                Directory.CreateDirectory(folder);
                var args = Downloader.BuildArgs(url, format, quality, bitrate, folder, playlist, thumb, subs);
                string? lastFile = null;
                var r = await Tools.RunAsync(Tools.YtDlp!, args, line =>
                {
                    var (pct, speed, eta, status, file) = Downloader.ParseLine(line);
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (pct is { } p) item.SetProgress(p / 100, $"{p:0.0}%  ·  {speed}  ·  {eta} left");
                        else if (status != null) item.SetStatus(status);
                        if (file != null) { lastFile = file; item.File = file; }
                    });
                }, ct);
                if (r.ExitCode != 0) throw new Exception(Downloader.CleanError(r.Error));
                return lastFile ?? folder;
            }
            finally { _slots.Release(); }
        };
        item.Retry = () => item.Begin();
        item.Begin();
    }

    public static void OpenPath(string path, bool select = false)
    {
        try
        {
            if (select && File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
            else Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch { }
    }
}

/// <summary>One row in the download / conversion queue.</summary>
public sealed class DownloadItem : Border
{
    private readonly TextBlock _status = Ui.Label("Waiting…", 12, "SubtleBrush");
    private readonly ProgressBar _bar = new() { Minimum = 0, Maximum = 1 };
    private readonly Button _cancel = Ui.Pill("Cancel");
    private readonly Button _show = Ui.Pill("Show");
    private readonly Button _retry = Ui.Pill("Retry");
    private CancellationTokenSource? _cts;

    public Func<CancellationToken, Task<string>>? Run { get; set; }
    public Action? Retry { get; set; }
    public string? File { get; set; }
    public bool Finished { get; private set; }

    public DownloadItem(string title, string detail, string? thumbnail)
    {
        CornerRadius = new CornerRadius(12);
        Padding = new Thickness(12);
        this.Res(BackgroundProperty, "CardBrush");

        var img = new Image { Stretch = Stretch.UniformToFill };
        var thumb = new Border { Width = 96, Height = 54, CornerRadius = new CornerRadius(7), ClipToBounds = true, Child = img }
            .Res(BackgroundProperty, "FieldBrush");
        if (thumbnail == null)
            thumb.Child = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                .Res(TextBlock.ForegroundProperty, "SubtleBrush");
        else _ = Load(img, thumbnail);

        var name = Ui.Label(title, 13.5, weight: FontWeight.SemiBold);
        name.TextWrapping = TextWrapping.NoWrap;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        var tag = new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 1), Child = Ui.Label(detail, 11, "AccentBrush", FontWeight.SemiBold), HorizontalAlignment = HorizontalAlignment.Left }
            .Res(BackgroundProperty, "AccentSoftBrush");
        _status.TextWrapping = TextWrapping.NoWrap;
        _status.TextTrimming = TextTrimming.CharacterEllipsis;
        var mid = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = { name, Ui.Row(8, tag, _status), _bar } };
        ((StackPanel)mid.Children[1]).Children[1].VerticalAlignment = VerticalAlignment.Center;

        foreach (var b in new[] { _cancel, _show, _retry }) { b.Padding = new Thickness(11, 5); b.FontSize = 12; }
        _show.IsVisible = _retry.IsVisible = false;
        _cancel.Click += (_, _) => _cts?.Cancel();
        _show.Click += (_, _) => DownloadPage.OpenPath(File ?? "", select: true);
        _retry.Click += (_, _) => Retry?.Invoke();
        var buttons = Ui.Row(6, _cancel, _retry, _show);
        buttons.VerticalAlignment = VerticalAlignment.Center;

        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
        g.Children.Add(thumb);
        Grid.SetColumn(mid, 1); g.Children.Add(mid);
        Grid.SetColumn(buttons, 2); g.Children.Add(buttons);
        Child = g;
    }

    private static async Task Load(Image img, string url) => img.Source = await Ui.LoadImageAsync(url);

    public void SetProgress(double p, string text)
    {
        _bar.IsIndeterminate = false;
        _bar.Value = p;
        _status.Text = text;
    }

    public void SetStatus(string text)
    {
        _status.Text = text;
        _bar.IsIndeterminate = true;
    }

    public async void Begin()
    {
        if (Run == null) return;
        Finished = false;
        _cts = new CancellationTokenSource();
        _cancel.IsVisible = true; _retry.IsVisible = _show.IsVisible = false;
        _bar.IsVisible = true; _bar.IsIndeterminate = true; _bar.Value = 0;
        _status.Res(TextBlock.ForegroundProperty, "SubtleBrush");
        _status.Text = "Starting…";
        try
        {
            File = await Run(_cts.Token);
            _bar.IsIndeterminate = false; _bar.Value = 1;
            _status.Text = "✓ Done — " + Path.GetFileName(File);
            _status.Res(TextBlock.ForegroundProperty, "SuccessBrush");
            _bar.IsVisible = false;
            _show.IsVisible = true;
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Cancelled";
            _bar.IsVisible = false;
            _retry.IsVisible = true;
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
            ToolTip.SetTip(_status, ex.Message);
            _status.Res(TextBlock.ForegroundProperty, "ErrorBrush");
            _bar.IsVisible = false;
            _retry.IsVisible = true;
        }
        finally
        {
            Finished = true;
            _cancel.IsVisible = false;
        }
    }
}
