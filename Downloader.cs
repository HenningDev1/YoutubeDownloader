using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace YoutubeDownloader;

public static class Formats
{
    public static readonly string[] Video = { "MP4", "MKV", "WEBM", "MOV" };
    public static readonly string[] Audio = { "MP3", "M4A", "WAV", "FLAC", "OPUS" };
    public static readonly string[] Qualities = { "4K", "1440p", "1080p", "720p" };
    public static readonly string[] Bitrates = { "320", "256", "192", "128" };

    public static bool IsAudio(string f) => Audio.Contains(f);

    public static int Height(string quality) => quality switch
    {
        "4K" => 2160,
        "1440p" => 1440,
        "720p" => 720,
        _ => 1080,
    };
}

public sealed record VideoInfo(string Title, string? Channel, string? Thumbnail, TimeSpan? Duration, int MaxHeight, int Count, bool IsPlaylist);

public static partial class Downloader
{
    /// <summary>Accepts youtube.com/watch, youtu.be, shorts, live, embed, music and playlist links.</summary>
    public static bool LooksLikeYouTube(string text)
    {
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var u)) return false;
        if (u.Scheme != "http" && u.Scheme != "https") return false;
        var h = u.Host.ToLowerInvariant();
        return h == "youtu.be" || h == "youtube.com" || h.EndsWith(".youtube.com") || h == "youtube-nocookie.com" || h.EndsWith(".youtube-nocookie.com");
    }

    public static async Task<VideoInfo> FetchInfoAsync(string url, bool playlist, CancellationToken ct)
    {
        var args = new List<string> { "-J", "--flat-playlist", "--no-warnings", playlist ? "--yes-playlist" : "--no-playlist", url };
        var r = await Tools.RunAsync(Tools.YtDlp!, args, null, ct);
        if (r.ExitCode != 0) throw new Exception(CleanError(r.Error));

        using var doc = JsonDocument.Parse(r.Output);
        var root = doc.RootElement;
        string Str(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";

        if (Str(root, "_type") == "playlist")
        {
            int count = root.TryGetProperty("entries", out var entries) ? entries.GetArrayLength() : 0;
            string? thumb = null;
            if (root.TryGetProperty("thumbnails", out var ts) && ts.GetArrayLength() > 0) thumb = Str(ts[ts.GetArrayLength() - 1], "url");
            if (string.IsNullOrEmpty(thumb) && count > 0 && entries[0].TryGetProperty("thumbnails", out var et) && et.GetArrayLength() > 0)
                thumb = Str(et[et.GetArrayLength() - 1], "url");
            return new VideoInfo(Str(root, "title"), Str(root, "uploader") is { Length: > 0 } up ? up : Str(root, "channel"),
                thumb, null, 0, count, true);
        }

        int maxH = 0;
        if (root.TryGetProperty("formats", out var fmts))
            foreach (var f in fmts.EnumerateArray())
                if (f.TryGetProperty("height", out var hh) && hh.ValueKind == JsonValueKind.Number) maxH = Math.Max(maxH, hh.GetInt32());
        TimeSpan? dur = root.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? TimeSpan.FromSeconds(d.GetDouble()) : null;
        return new VideoInfo(Str(root, "title"), Str(root, "channel") is { Length: > 0 } ch ? ch : Str(root, "uploader"),
            Str(root, "thumbnail"), dur, maxH, 1, false);
    }

    public static List<string> BuildArgs(string url, string format, string quality, string bitrate, string folder,
        bool playlist, bool embedThumb, bool subs)
    {
        var a = new List<string>
        {
            "--newline", "--no-warnings", "--no-mtime", "--progress",
            "--progress-template", "download:[dl] %(progress._percent_str)s|%(progress._speed_str)s|%(progress._eta_str)s",
            "--print", "after_move:[file] %(filepath)s",
            "--no-simulate",
            "--ffmpeg-location", Path.GetDirectoryName(Tools.Ffmpeg!)!,
            "-P", folder,
            "-o", playlist ? "%(playlist_title|Playlist)s/%(playlist_index|0)03d - %(title)s.%(ext)s" : "%(title)s.%(ext)s",
            playlist ? "--yes-playlist" : "--no-playlist",
            "--embed-metadata",
        };

        var f = format.ToLowerInvariant();
        if (Formats.IsAudio(format))
        {
            a.AddRange(new[] { "-f", "ba/b", "-x", "--audio-format", f });
            if (f is "mp3" or "m4a" or "opus") a.AddRange(new[] { "--audio-quality", bitrate + "K" });
            if (embedThumb && f is "mp3" or "m4a" or "flac" or "opus") a.Add("--embed-thumbnail");
        }
        else
        {
            int h = Formats.Height(quality);
            // Resolution first (best at or below the chosen one), then codecs that suit the container.
            string sort = f switch
            {
                "webm" => $"res:{h},fps,vcodec:vp9,acodec:opus",
                "mkv" => $"res:{h},fps",
                _ => $"res:{h},fps,vcodec:h264,acodec:aac",
            };
            a.AddRange(new[] { "-f", "bv*+ba/b", "-S", sort, "--merge-output-format", f });
            if (f == "mov") a.AddRange(new[] { "--remux-video", "mov" });
            if (embedThumb && f is "mp4" or "mkv" or "mov") a.Add("--embed-thumbnail");
            if (subs) a.AddRange(new[] { "--write-subs", "--sub-langs", "en.*,de.*", "--embed-subs" });
        }
        a.Add("--");
        a.Add(url);
        return a;
    }

    [GeneratedRegex(@"^\[dl\]\s*([\d.]+)%\|([^|]*)\|(.*)$")]
    private static partial Regex ProgressRx();

    /// <summary>Parses one yt-dlp output line. Returns (percent or null, speed, eta, status, finished file).</summary>
    public static (double? Percent, string? Speed, string? Eta, string? Status, string? File) ParseLine(string line)
    {
        var m = ProgressRx().Match(line.Trim());
        if (m.Success)
        {
            double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var p);
            return (p, m.Groups[2].Value.Trim(), m.Groups[3].Value.Trim(), null, null);
        }
        if (line.StartsWith("[file] ")) return (null, null, null, null, line[7..].Trim());
        if (line.StartsWith("[Merger]")) return (null, null, null, "Merging video and audio…", null);
        if (line.StartsWith("[ExtractAudio]")) return (null, null, null, "Converting audio…", null);
        if (line.StartsWith("[VideoRemuxer]") || line.StartsWith("[VideoConvertor]")) return (null, null, null, "Converting video…", null);
        if (line.StartsWith("[EmbedThumbnail]") || line.StartsWith("[Metadata]") || line.StartsWith("[EmbedSubtitle]"))
            return (null, null, null, "Finishing…", null);
        if (line.StartsWith("[download] Downloading item"))
            return (null, null, null, line["[download] ".Length..].Trim(), null);
        return (null, null, null, null, null);
    }

    public static string CleanError(string err)
    {
        var lines = err.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.StartsWith("ERROR:")).ToList();
        var msg = lines.LastOrDefault() ?? err.Trim().Split('\n').LastOrDefault() ?? "Unknown error";
        msg = msg.Replace("ERROR:", "").Trim();
        // Strip the "[youtube] id: " prefix for readability.
        msg = Regex.Replace(msg, @"^\[[^\]]+\]\s*[\w-]+:\s*", "");
        return msg.Length > 220 ? msg[..220] + "…" : msg;
    }
}
