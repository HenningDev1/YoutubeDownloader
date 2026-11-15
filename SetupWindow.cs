using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace YoutubeDownloader;

/// <summary>Small installer window shown when the exe runs as YoutubeDownloaderSetup.exe.</summary>
public sealed class SetupWindow : Window
{
    public SetupWindow()
    {
        Title = "Install Youtube Downloader";
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://YoutubeDownloader/YoutubeDownloader.ico")));
        Width = 460; Height = 350;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        this[!FontFamilyProperty] = this.GetResourceObservable("AppFont").ToBinding();
        this[!BackgroundProperty] = this.GetResourceObservable("BgBrush").ToBinding();

        IBrush Res(string k) => (IBrush)Application.Current!.Resources[k]!;

        var icon = new Image { Width = 64, Height = 64, HorizontalAlignment = HorizontalAlignment.Left };
        try { icon.Source = new Bitmap(AssetLoader.Open(new Uri("avares://YoutubeDownloader/Assets/icon-256.png"))); } catch { }

        var title = new TextBlock { Text = "Youtube Downloader", FontSize = 26, FontWeight = FontWeight.Bold, Foreground = Res("TextBrush"), Margin = new Thickness(0, 12, 0, 2) };
        var sub = new TextBlock
        {
            Text = Installer.IsInstalled ? "Youtube Downloader is already installed. Install again to update it."
                                         : "Download YouTube videos as MP4, MP3 and more — up to 4K — plus a built-in file converter.",
            FontSize = 13, Foreground = Res("SubtleBrush"), TextWrapping = TextWrapping.Wrap,
        };
        var desktop = new CheckBox
        {
            Content = "Create a desktop shortcut", Margin = new Thickness(0, 18, 0, 0),
            IsChecked = Installer.HasDesktopShortcut || !Installer.IsInstalled, Foreground = Res("TextBrush"),
        };
        var where = new TextBlock
        {
            Text = "Installs to " + Installer.InstallDir, FontSize = 11, Margin = new Thickness(0, 6, 0, 0),
            Foreground = Res("SubtleBrush"), TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var status = new TextBlock { FontSize = 12.5, Foreground = Res("SubtleBrush"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };

        var install = new Button
        {
            Content = "Install", Background = Res("AccentBrush"), Foreground = Res("OnAccentBrush"),
            CornerRadius = new CornerRadius(18), Padding = new Thickness(26, 9), FontWeight = FontWeight.SemiBold,
        };
        var cancel = new Button
        {
            Content = "Cancel", Background = Res("FieldBrush"), Foreground = Res("TextBrush"),
            CornerRadius = new CornerRadius(18), Padding = new Thickness(20, 9), Margin = new Thickness(0, 0, 8, 0),
        };
        cancel.Click += (_, _) => Close();

        bool done = false;
        install.Click += async (_, _) =>
        {
            if (done) { Installer.LaunchInstalled(); Close(); return; }
            install.IsEnabled = cancel.IsEnabled = desktop.IsEnabled = false;
            status.Text = "Installing…";
            try
            {
                bool link = desktop.IsChecked == true;
                await Task.Run(() => Installer.Install(link));
                done = true;
                status.Text = "✓ Installed. Find it in the Start menu.";
                install.Content = "Open Youtube Downloader";
                cancel.Content = "Close";
                install.IsEnabled = cancel.IsEnabled = true;
            }
            catch (Exception ex)
            {
                status.Text = "Install failed: " + ex.Message;
                install.IsEnabled = cancel.IsEnabled = desktop.IsEnabled = true;
            }
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel);
        buttons.Children.Add(install);

        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        footer.Children.Add(status);
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);

        var top = new StackPanel();
        top.Children.Add(icon);
        top.Children.Add(title);
        top.Children.Add(sub);
        top.Children.Add(desktop);
        top.Children.Add(where);

        var root = new Grid { Margin = new Thickness(28, 24), RowDefinitions = new RowDefinitions("*,Auto") };
        root.Children.Add(top);
        Grid.SetRow(footer, 1);
        root.Children.Add(footer);
        Content = root;
    }
}
