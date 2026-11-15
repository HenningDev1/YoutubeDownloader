using Avalonia;
using Avalonia.Controls;

namespace YoutubeDownloader;

internal static class Program
{
    public static bool SetupMode { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--uninstall")) { Installer.Uninstall(); return; }
        if (args.Contains("--install")) { Installer.Install(); return; } // silent install

        SetupMode = Installer.IsSetupMode(args);
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnMainWindowClose);
        }
        catch (Exception ex)
        {
            // Leave a trace for troubleshooting; the app has no console.
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "YoutubeDownloader-crash.txt"), ex.ToString()); } catch { }
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
