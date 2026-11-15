using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace YoutubeDownloader;

/// <summary>
/// Finds (or fetches on first use) the two engines the app drives:
/// yt-dlp for talking to YouTube and ffmpeg for merging, extracting audio and converting.
/// </summary>
public static class Tools
{
    private const string YtDlpUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
    private const string FfmpegUrl = "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";

    public static string ToolsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YoutubeDownloader", "tools");

    private static readonly HttpClient Http = CreateHttp();
    private static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("YoutubeDownloader/1.0");
        return h;
    }

    public static string? YtDlp => Find("yt-dlp.exe");
    public static string? Ffmpeg => Find("ffmpeg.exe");
    public static string? Ffprobe => Find("ffprobe.exe");

    private static string? Find(string exe)
    {
        var local = Path.Combine(ToolsDir, exe);
        if (File.Exists(local)) return local;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try
            {
                var p = Path.Combine(dir.Trim('"'), exe);
                if (File.Exists(p)) return p;
            }
            catch { }
        }
        return null;
    }

    public static bool Ready => YtDlp != null && Ffmpeg != null;

    /// <summary>Downloads whatever is missing. Reports (status text, 0..1 progress or -1).</summary>
    public static async Task EnsureAsync(Action<string, double> report, CancellationToken ct = default)
    {
        Directory.CreateDirectory(ToolsDir);
        if (YtDlp == null)
        {
            var dest = Path.Combine(ToolsDir, "yt-dlp.exe");
            await DownloadFile(YtDlpUrl, dest + ".part", p => report("Downloading yt-dlp…", p), ct);
            File.Move(dest + ".part", dest, true);
        }
        if (Ffmpeg == null || Ffprobe == null)
        {
            var zip = Path.Combine(ToolsDir, "ffmpeg.zip");
            await DownloadFile(FfmpegUrl, zip, p => report("Downloading ffmpeg…", p), ct);
            report("Unpacking ffmpeg…", -1);
            await Task.Run(() =>
            {
                using var archive = ZipFile.OpenRead(zip);
                foreach (var entry in archive.Entries)
                {
                    var name = entry.Name.ToLowerInvariant();
                    if (name is "ffmpeg.exe" or "ffprobe.exe")
                        entry.ExtractToFile(Path.Combine(ToolsDir, name), true);
                }
            }, ct);
            File.Delete(zip);
        }
        report("Ready", 1);
    }

    /// <summary>Keeps yt-dlp current: YouTube changes often and old versions stop working.</summary>
    public static async Task<string> UpdateYtDlpAsync()
    {
        if (YtDlp is not { } exe) return "yt-dlp is not installed yet.";
        if (!exe.StartsWith(ToolsDir, StringComparison.OrdinalIgnoreCase))
            return "yt-dlp comes from your PATH; update it there.";
        var r = await RunAsync(exe, new[] { "-U" }, null, CancellationToken.None);
        return r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? "Done";
    }

    private static async Task DownloadFile(string url, string dest, Action<double> progress, CancellationToken ct)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? -1;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var fs = File.Create(dest);
        var buf = new byte[81920];
        long read = 0; int n; var last = DateTime.MinValue;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            await fs.WriteAsync(buf.AsMemory(0, n), ct);
            read += n;
            if ((DateTime.Now - last).TotalMilliseconds > 100)
            {
                last = DateTime.Now;
                progress(total > 0 ? (double)read / total : -1);
            }
        }
    }

    public sealed record Result(int ExitCode, string Output, string Error);

    /// <summary>Runs a tool, streaming each output line (stdout and stderr) to <paramref name="onLine"/>.</summary>
    public static async Task<Result> RunAsync(string exe, IEnumerable<string> args, Action<string>? onLine, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var outSb = new StringBuilder();
        var errSb = new StringBuilder();
        p.OutputDataReceived += (_, e) => { if (e.Data == null) return; lock (outSb) outSb.AppendLine(e.Data); onLine?.Invoke(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data == null) return; lock (errSb) errSb.AppendLine(e.Data); onLine?.Invoke(e.Data); };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        using var reg = ct.Register(() => { try { p.Kill(true); } catch { } });
        await p.WaitForExitAsync(CancellationToken.None);
        ct.ThrowIfCancellationRequested();
        return new Result(p.ExitCode, outSb.ToString(), errSb.ToString());
    }
}
