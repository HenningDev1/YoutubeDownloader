using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace YoutubeDownloader;

/// <summary>Per-user install/uninstall: no admin rights needed.</summary>
public static class Installer
{
    public const string AppName = "Youtube Downloader";
    public const string Version = "1.0.0";
    private const string ExeName = "YoutubeDownloader.exe";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\YoutubeDownloader";

    public static string InstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);
    public static string InstalledExe => Path.Combine(InstallDir, ExeName);
    private static string StartMenuLink =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");
    private static string DesktopLink =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");

    /// <summary>True when this exe was launched as the setup program.</summary>
    public static bool IsSetupMode(string[] args)
    {
        if (args.Contains("--setup")) return true;
        var name = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "");
        return name.Contains("setup", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsInstalled => File.Exists(InstalledExe);
    public static bool HasDesktopShortcut => File.Exists(DesktopLink);

    public static void Install(bool desktopShortcut = false)
    {
        KillOtherInstances();
        Directory.CreateDirectory(InstallDir);
        var exe = Environment.ProcessPath!;
        var srcDir = Path.GetDirectoryName(exe)!;
        if (File.Exists(Path.Combine(srcDir, "YoutubeDownloader.dll")))
        {
            // Not a single-file build: the exe needs the files next to it, so copy them all.
            foreach (var f in Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories))
            {
                var dest = Path.Combine(InstallDir, Path.GetRelativePath(srcDir, f));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(f, dest, true);
            }
        }
        File.Copy(exe, InstalledExe, true);

        CreateShortcut(StartMenuLink, InstalledExe, AppName);
        if (desktopShortcut) CreateShortcut(DesktopLink, InstalledExe, AppName);
        else TryDelete(DesktopLink);

        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        key.SetValue("DisplayName", AppName);
        key.SetValue("DisplayVersion", Version);
        key.SetValue("Publisher", AppName);
        key.SetValue("DisplayIcon", InstalledExe);
        key.SetValue("InstallLocation", InstallDir);
        key.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
    }

    public static void LaunchInstalled() =>
        Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = true, WorkingDirectory = InstallDir });

    public static void Uninstall()
    {
        if (MessageBoxW(IntPtr.Zero, $"Remove {AppName} from this PC?\n\nYour settings and the download engine will be deleted too. Downloaded videos are kept.",
                $"Uninstall {AppName}", 0x24 /* YESNO | QUESTION */) != 6) return;

        KillOtherInstances();
        TryDelete(StartMenuLink);
        TryDelete(DesktopLink);
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
        try { if (Directory.Exists(Settings.DataDir)) Directory.Delete(Settings.DataDir, true); } catch { }
        try { var t = Path.GetDirectoryName(Tools.ToolsDir)!; if (Directory.Exists(t)) Directory.Delete(t, true); } catch { }

        // The running exe can't delete itself: let cmd do it after we exit.
        Process.Start(new ProcessStartInfo("cmd.exe",
            $"/c timeout /t 2 /nobreak >nul & rmdir /s /q \"{InstallDir}\"")
        { CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetTempPath() });

        MessageBoxW(IntPtr.Zero, $"{AppName} has been removed.", $"Uninstall {AppName}", 0x40);
    }

    private static void KillOtherInstances()
    {
        foreach (var p in Process.GetProcessesByName("YoutubeDownloader"))
        {
            if (p.Id == Environment.ProcessId) continue;
            try { p.Kill(); p.WaitForExit(3000); } catch { }
        }
    }

    private static void CreateShortcut(string linkPath, string target, string description)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
        var shellType = Type.GetTypeFromProgID("WScript.Shell")!;
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic link = shell.CreateShortcut(linkPath);
            link.TargetPath = target;
            link.WorkingDirectory = Path.GetDirectoryName(target);
            link.Description = description;
            link.IconLocation = target + ",0";
            link.Save();
            Marshal.FinalReleaseComObject(link);
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
