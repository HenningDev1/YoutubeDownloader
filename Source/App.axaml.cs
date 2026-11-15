using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace YoutubeDownloader;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = Settings.Load();
            Themes.Apply(settings);
            desktop.MainWindow = Program.SetupMode ? new SetupWindow() : new MainWindow(settings);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
