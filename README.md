# Youtube Downloader

An Apple-style YouTube downloader and file converter for Windows (C# + Avalonia).

- **Download**: paste any YouTube link (video, Short, live, music, playlist). A YouTube link copied to the clipboard is filled in automatically when the app gets focus. A preview shows the title, channel, length and best available resolution.
- **Video**: MP4, MKV, WEBM, MOV at 4K, 1440p, 1080p or 720p. If the video doesn't have that resolution, you get the best one below it.
- **Audio**: MP3, M4A, OPUS (320/256/192/128 kbps), WAV, FLAC (lossless).
- Options: embed the thumbnail and tags, embed subtitles, download a whole playlist. Up to 3 downloads run at once, and each can be cancelled, retried or shown in Explorer.
- **Convert**: drop local files (or choose them) and convert to MP4, MKV, WEBM, MOV, AVI, GIF, MP3, M4A, WAV, FLAC, OPUS, OGG or AAC, with optional downscaling to 1080p/720p/480p.
- **Look**: borderless rounded window with red/yellow/green close, minimize and full-screen buttons (F11 also toggles full screen).
- **Settings** (Ctrl + ,): dark/light mode, 10 accent colours or a custom hex, background presets or a custom hex, and 6 fonts (Segoe UI Variable, Inter, Segoe UI, Bahnschrift, Cascadia Code, Georgia).

The app uses [yt-dlp](https://github.com/yt-dlp/yt-dlp) and ffmpeg. If they're not on your PATH, it downloads them on first start to `%LOCALAPPDATA%\YoutubeDownloader\tools`. If downloads start failing, use **Settings › Update yt-dlp**.

Settings are saved in `%APPDATA%\YoutubeDownloader\settings.json`.

## Build

```bash
powershell -ExecutionPolicy Bypass -File build.ps1
```

This produces `publish\YoutubeDownloader.exe` (portable) and `publish\YoutubeDownloaderSetup.exe` (installer). Both are self-contained, so .NET doesn't need to be installed. The installer installs per user to `%LOCALAPPDATA%\Programs\Youtube Downloader` without admin rights, adds a Start Menu entry and an optional desktop shortcut, and registers an uninstaller under Settings › Apps.

The icon is drawn by `tools\make-icon.ps1`.
