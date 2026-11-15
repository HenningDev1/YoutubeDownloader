using System.Globalization;

namespace YoutubeDownloader;

/// <summary>Local file conversion with ffmpeg.</summary>
public static class Converter
{
    public static readonly string[] VideoTargets = { "MP4", "MKV", "WEBM", "MOV", "AVI", "GIF" };
    public static readonly string[] AudioTargets = { "MP3", "M4A", "WAV", "FLAC", "OPUS", "OGG", "AAC" };
    public static readonly string[] Sizes = { "Original", "1080p", "720p", "480p" };

    public static readonly string[] InputExtensions =
    {
        "mp4", "mkv", "webm", "mov", "avi", "wmv", "flv", "m4v", "3gp", "mpg", "mpeg", "ts", "gif",
        "mp3", "m4a", "wav", "flac", "opus", "ogg", "aac", "wma", "aiff", "alac",
    };

    public static async Task<TimeSpan?> ProbeDurationAsync(string file)
    {
        if (Tools.Ffprobe is not { } probe) return null;
        var r = await Tools.RunAsync(probe, new[] { "-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1", file }, null, CancellationToken.None);
        return double.TryParse(r.Output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? TimeSpan.FromSeconds(s) : null;
    }

    public static string OutputPath(string input, string target, string? folder)
    {
        var dir = string.IsNullOrWhiteSpace(folder) ? Path.GetDirectoryName(input)! : folder;
        var name = Path.GetFileNameWithoutExtension(input);
        var ext = "." + target.ToLowerInvariant();
        var path = Path.Combine(dir, name + ext);
        for (int i = 2; File.Exists(path) || string.Equals(path, input, StringComparison.OrdinalIgnoreCase); i++)
            path = Path.Combine(dir, $"{name} ({i}){ext}");
        return path;
    }

    public static List<string> BuildArgs(string input, string output, string target, string size, string bitrate)
    {
        var a = new List<string> { "-hide_banner", "-y", "-nostats", "-progress", "pipe:1", "-i", input };
        int h = size switch { "1080p" => 1080, "720p" => 720, "480p" => 480, _ => 0 };
        // Keep aspect ratio; never upscale; even dimensions for the encoders.
        string? scale = h > 0 ? $"scale=-2:'min({h},ih)'" : null;
        string br = bitrate + "k";

        switch (target)
        {
            case "MP4":
            case "MOV":
                a.AddRange(new[] { "-map", "0:v:0?", "-map", "0:a:0?", "-c:v", "libx264", "-preset", "medium", "-crf", "20", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", br });
                if (target == "MP4") a.AddRange(new[] { "-movflags", "+faststart" });
                break;
            case "MKV":
                a.AddRange(new[] { "-map", "0", "-c:v", "libx264", "-preset", "medium", "-crf", "20", "-c:a", "aac", "-b:a", br, "-c:s", "copy" });
                break;
            case "WEBM":
                a.AddRange(new[] { "-map", "0:v:0?", "-map", "0:a:0?", "-c:v", "libvpx-vp9", "-crf", "32", "-b:v", "0", "-row-mt", "1", "-c:a", "libopus", "-b:a", br });
                break;
            case "AVI":
                a.AddRange(new[] { "-c:v", "mpeg4", "-q:v", "3", "-c:a", "libmp3lame", "-b:a", br });
                break;
            case "GIF":
            {
                int gh = h > 0 ? h : 480;
                a.AddRange(new[] { "-an", "-vf", $"fps=15,scale=-2:'min({gh},ih)':flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse" });
                scale = null;
                break;
            }
            case "MP3": a.AddRange(new[] { "-vn", "-c:a", "libmp3lame", "-b:a", br }); break;
            case "M4A":
            case "AAC": a.AddRange(new[] { "-vn", "-c:a", "aac", "-b:a", br }); break;
            case "WAV": a.AddRange(new[] { "-vn", "-c:a", "pcm_s16le" }); break;
            case "FLAC": a.AddRange(new[] { "-vn", "-c:a", "flac" }); break;
            case "OPUS": a.AddRange(new[] { "-vn", "-c:a", "libopus", "-b:a", br }); break;
            case "OGG": a.AddRange(new[] { "-vn", "-c:a", "libvorbis", "-q:a", "6" }); break;
        }
        if (scale != null && VideoTargets.Contains(target)) a.AddRange(new[] { "-vf", scale });
        a.Add(output);
        return a;
    }

    /// <summary>Runs ffmpeg; progress is 0..1 (or -1 when the length is unknown).</summary>
    public static async Task ConvertAsync(string input, string output, string target, string size, string bitrate,
        Action<double> progress, CancellationToken ct)
    {
        var duration = await ProbeDurationAsync(input);
        var args = BuildArgs(input, output, target, size, bitrate);
        var r = await Tools.RunAsync(Tools.Ffmpeg!, args, line =>
        {
            if (line.StartsWith("out_time_us=") && duration is { TotalSeconds: > 0 } d &&
                long.TryParse(line[12..], out var us))
                progress(Math.Clamp(us / 1_000_000.0 / d.TotalSeconds, 0, 1));
        }, ct);
        if (r.ExitCode != 0)
        {
            try { if (File.Exists(output)) File.Delete(output); } catch { }
            var last = r.Error.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "ffmpeg failed";
            throw new Exception(last);
        }
    }
}
