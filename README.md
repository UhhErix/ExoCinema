<p align="center"><img src="docs/media/logo.png" alt="Exo Cinema" width="88"></p>
<h1 align="center">Exo Cinema</h1>
<p align="center"><strong>Your Jellyfin library in Exo's look for Windows 11, with a native player that keeps up with anything.</strong></p>

<p align="center">
  <a href="https://github.com/UhhErix/ExoCinema/releases/latest/download/ExoCinema-Setup.exe"><strong>Download for Windows 11</strong></a>
  · <a href="https://github.com/UhhErix/ExoCinema/releases/tag/v1.0.8">What's new in 1.0.8</a>
  · <a href="CHANGELOG.md">Changelog</a>
  · <a href="PRIVACY.md">Privacy</a>
  · <a href="SECURITY.md">Security</a>
  · <a href="LICENSE">License</a>
</p>

![The player paused on a 4K Dolby Vision video, the audio, subtitles and speed menu open at the top right, the controls in Liquid Glass over the picture](docs/media/hero.jpg)

## Download

| File | Version | SHA-256 |
| --- | --- | --- |
| [ExoCinema-Setup.exe](https://github.com/UhhErix/ExoCinema/releases/download/v1.0.8/ExoCinema-Setup.exe) | 1.0.8 | `d718c8cd382c610ff7132b3dbb9163461546e794ab9d94b1a6da77f2c3214467` |

**Requirements:** Windows 11 x64 with WebView2 (built into Windows 11; Setup installs it if it's missing), and a Jellyfin server to sign in to. Installs for your Windows user only, in `%LOCALAPPDATA%\ExoCinema\app`, with no administrator rights.

**Check your download.** The installer isn't code-signed yet, so Windows SmartScreen may warn the first time (choose **More info**, then **Run anyway**). Before you run it, check that its hash matches the one above and on the [release page](https://github.com/UhhErix/ExoCinema/releases/tag/v1.0.8):

```powershell
Get-FileHash .\ExoCinema-Setup.exe -Algorithm SHA256
```

## Install

1. Download **ExoCinema-Setup.exe** (above) and check its hash.
2. Run it, choose **Install**, then **Open Exo Cinema**. The first run signs you in to your Jellyfin server and sets how you like to watch. Every choice can be changed later in Settings.
3. New versions show in **Settings > About**. **Install update** downloads it, checks it and installs it over the one you have; your sign-in and settings stay.

![First run: welcome to Exo Cinema](docs/media/first-run.jpg)

## What's built in

- **Your Jellyfin library:** Home with Continue Watching, Next Up and what's new in each library, your libraries in the Bar, every show by season, and Ctrl+K to search the whole server.
- **Progress that follows you:** where you stop and what you've watched go back to Jellyfin as you watch, so your phone, TV and every other Jellyfin app pick up where you left off.
- **A native player:** mpv draws the picture with hardware decoding, HDR and Dolby Vision tone mapping and high-quality scaling, at the video's own frame rate.
- **Liquid Glass over the picture:** the controls blur the video under them as it plays, drawn by the player on the graphics card at no cost to playback.
- **The things you reach for:** audio and subtitle tracks remembered for each show, playback speed, Skip intro and Skip credits, and Up Next that plays the next episode on its own.
- **Steady over a slow link:** the player reads far ahead, waits for a few seconds of picture rather than stuttering, and reconnects when a server drops the stream.
- **Exo Launcher's look:** Void black, Liquid Glass or solid, Inter and Exo's icons, and an opening that has Home ready when it lifts.

## The player

- **Built on mpv.** libmpv decodes and draws into the window itself, under the interface, so the picture never passes through a web page: hardware decoding through Direct3D 11, mpv's high-quality scalers picked for your PC as it plays (lighter on integrated graphics, on battery, or when frames drop), and HDR and Dolby Vision tone mapped for your screen.
- **AI upscaling on RTX cards.** A video smaller than your screen is enlarged by NVIDIA's RTX Video Super Resolution when it's switched on (Exo Launcher's PC > Display), and steps aside while a game runs.
- **Glass that moves with the picture.** The glass under the controls is drawn by the player as part of each frame, so it never lags the video and never copies frames off the graphics card. With the controls up over 4K60 Dolby Vision, the test PC dropped no frames.
- **Audio, subtitles and speed** live in one menu under the sliders button at the top right. Pick a language once and the show's next episodes start with it. Subtitles rise above the controls while they show.
- **Skip intro and Up Next.** Openings and credits marked by your server (Jellyfin's media segments) or named in the file's chapters get a Skip button; near the end of an episode the next one counts down, and **Not now** stops it.
- **Keys you know:** Space or K to pause, arrows to seek and set the volume, J and L for ten seconds, F for full screen, M to mute, I for what's playing and how.

## Settings

![Settings: Look, Playback, Account, Storage and About](docs/media/settings.jpg)

- **Look:** Liquid Glass or solid.
- **Playback:** picture quality, hardware decoding and whether the next episode plays by itself.
- **Account:** your Jellyfin account page and Sign out.
- **About:** your version, what's new, and updates from GitHub, checked and installed in place.

![What's new in Exo Cinema 1.0.0](docs/media/whats-new.jpg)

## How it works

Exo Cinema is a native Windows app built like Exo Launcher: WinUI 3 on .NET, with its interface in one Microsoft Edge WebView2 page that floats over the video. The picture comes from libmpv, which draws straight into the window.

- **Its own interface:** Exo Cinema's page, served from the app folder, not the web. It talks to your server only through the app.
- **Your server, directly:** Exo Cinema signs in to your Jellyfin server and plays from it. There is no Exo server in between.
- **Building it:** [docs/BUILDING.md](docs/BUILDING.md) says how the source fits together and how to build it.

## Privacy and safety

- **No Exo account, no ads, no analytics, no telemetry.** Exo Cinema has no server of its own. Your sign-in and settings stay in `%LocalAppData%\ExoCinema`.
- **What leaves your PC** is what watching needs: requests to your Jellyfin server (with where you are in what you watch), and the update check to GitHub. The [privacy policy](PRIVACY.md) lists every destination and what is sent.
- **Your password is never stored.** Exo Cinema keeps only the access token your server gives it, sealed with Windows' data protection for your Windows user.
- **Found a security problem?** Please [report it privately](https://github.com/UhhErix/ExoCinema/security/advisories/new), not in a public issue. See [SECURITY.md](SECURITY.md).

## Contributing

Bug reports, fixes and clearer wording are welcome. Read [CONTRIBUTING.md](CONTRIBUTING.md) first: it explains what you agree to when you send a change, since Exo Cinema isn't open source.

## License

Exo Cinema is **source-available and free for personal use**. It is not open source. The full terms are in [LICENSE](LICENSE); this is a plain summary, and LICENSE wins if they differ.

| You can | You need written permission to |
| --- | --- |
| Download, install and run the official app on your devices | Copy, fork outside GitHub, mirror or redistribute the source |
| Read the source to see what Exo Cinema does | Publish, sell or rebrand a changed version |
| Build it and run it on your own devices to check it against the release | |
| Send fixes as pull requests ([CONTRIBUTING.md](CONTRIBUTING.md)) | |

To ask for permission, [open an issue](https://github.com/UhhErix/ExoCinema/issues). The app is provided as is, without warranty.

**Open-source parts that ship with Exo Cinema** keep their own licenses, and their source is public:

- [libmpv](https://github.com/mpv-player/mpv) (LGPL), the player, with [FFmpeg](https://github.com/FFmpeg/FFmpeg) (LGPL) inside it. It ships as its own file you may replace with your own build.

Every bundled library, font and tool is listed in [Third-party notices](THIRD_PARTY_NOTICES.md).

**Not affiliated.** Exo Cinema is independent and not affiliated with or endorsed by the Jellyfin project, the mpv or FFmpeg projects, Microsoft, or any service it works with. Their names and logos belong to their owners. The video in the pictures above is Jellyfin's test footage ([CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/)), and the server and user in them are made up.

## Also from Exo

- **[Exo Launcher](https://github.com/UhhErix/ExoLauncher)** — every game store, Discord, music, captures and mouse in one app
- **[Exo Browser](https://github.com/UhhErix/ExoBrowser)** — private browser with Exo Shield and Exo Vault built in

---

[Help](docs/HELP.md) · [Report an issue](https://github.com/UhhErix/ExoCinema/issues) · [Support development](https://www.buymeacoffee.com/UhhErix)

© 2026 Erix ([UhhErix](https://github.com/UhhErix)).
