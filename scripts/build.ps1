# Builds publish\YoutubeDownloader.exe (portable app) and publish\YoutubeDownloaderSetup.exe (installer).
# Both are the same self-contained single file: the PC needs no .NET installed.
# Run the setup exe to install; it also accepts --install for a silent install.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

if (-not (Test-Path 'Assets\YoutubeDownloader.ico')) { & (Join-Path $PSScriptRoot 'make-icon.ps1') }

$out = Join-Path $root 'publish'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish YoutubeDownloader.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=none -o $out
if ($LASTEXITCODE) { throw 'Publish failed' }

Copy-Item (Join-Path $out 'YoutubeDownloader.exe') (Join-Path $out 'YoutubeDownloaderSetup.exe')
Get-ChildItem $out -Filter *.exe | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}}
