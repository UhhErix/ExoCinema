# Building Exo Cinema

Exo Cinema is source-available: you may build it to check it against the official release (see [LICENSE](../LICENSE)).

## What it's made of

- `src/ExoCinema`: the app. WinUI 3 (Windows App SDK 2.6, unpackaged and self-contained) on .NET 10, as Exo Launcher is.
  - `MainWindow*.cs`: the window, Exo's caption, the player host and the glass, updates.
  - `Mpv/`: libmpv through its C API. The picture is drawn by libmpv into the window, under the page.
  - `Shaders/exo-glass.glsl`: the glass over the video, blurred by the player on the graphics card.
  - `Jellyfin/`: the Jellyfin client, the sealed sign-in and the picture cache.
  - `wwwroot/`: the interface, plain HTML, CSS and JavaScript in one WebView2 page with a transparent background, over the picture.
- `lib/mpv`: where `libmpv-2.dll` goes (`SOURCE.txt` says which build and its hash). It isn't in the repository.
- `setup/` and `tools/`: Exo Setup, the installer (a small C++ window over an NSIS engine), and the release script.

## Requirements

- Windows 11 x64.
- The .NET 10 SDK (`global.json` names the version).
- For the installer: Visual Studio 2026 Build Tools with the C++ x64 tools, and NSIS 3.13.
- libmpv: zhongfly's LGPL build named in `lib/mpv/SOURCE.txt`, from <https://github.com/zhongfly/mpv-winbuild/releases>. Put its `libmpv-2.dll` in `lib/mpv` and check its SHA-256 against `SOURCE.txt`.

## Build and run

```powershell
dotnet build src\ExoCinema\ExoCinema.csproj -c Debug -p:Platform=x64
.\src\ExoCinema\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\ExoCinema.exe
```

`EXO_CINEMA_DATA` points a copy at its own data folder, so it doesn't touch your installed Exo Cinema's sign-in and settings.

## The installer

```powershell
.\tools\Build-Release.ps1
```

It publishes the app (Release, trimmed, ReadyToRun), checks every file into a payload manifest, builds Exo Setup and writes `artifacts\ExoCinema-Setup.exe`, its `.sha256` and the release's `whats-new.json`.
