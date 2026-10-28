# Getting started with Exo Cinema

## Install

1. Download **ExoCinema-Setup.exe** from the [latest release](https://github.com/UhhErix/ExoCinema/releases/latest) and check its SHA-256 against the one on the release page:

   ```powershell
   Get-FileHash .\ExoCinema-Setup.exe -Algorithm SHA256
   ```

2. Run it and choose **Install**. **Options** adds a desktop shortcut. The installer isn't code-signed yet, so Windows SmartScreen may warn the first time: choose **More info**, then **Run anyway**.
3. Choose **Open Exo Cinema**.

Exo Cinema installs for your Windows user only, in `%LocalAppData%\ExoCinema\app`, with no administrator rights. It needs Windows 11 x64 and WebView2, which Windows 11 has; Setup installs WebView2 if it's missing.

## First run

- **Jellyfin:** your server's address (as you'd type it in a browser, such as `jellyfin.example.com` or `http://192.168.1.20:8096`), your user name and your password. When the server is found, its name and Jellyfin version show under the address. If your server has Quick Connect on, **Use Quick Connect** shows a code to approve from another Jellyfin app where you're signed in.
- **Playback:** Source (Best, or Ask to pick a version when your server has several) and whether the next episode plays by itself.
- **Your look:** Glass or Solid.

Everything can be changed later in Settings, from your picture at the top right.

## The library

- **Home:** Continue Watching, Next Up and what's new in each library. It draws at once from what Exo Cinema kept last time, then fills in from your server.
- **Rows** page with the arrows beside their names.
- **Your libraries** are in the Bar. A show's page lists its seasons and episodes; **Watched** marks a title on your server, **From the start** plays it from the beginning.
- **Search:** Ctrl+K (or /) searches your whole server as you type. Arrows pick a result, Enter opens it.
- **Back:** Backspace or your mouse's back button.

## The player

| Key | Does |
| --- | --- |
| Space or K | Play or pause |
| ← and → | Back or forward 5 seconds |
| J and L | Back or forward 10 seconds |
| ↑ and ↓ | Volume |
| M | Mute |
| F, or a double click | Full screen |
| I | What's playing and how (picture, decoder, video, frame rate) |
| Esc | Leave full screen, or go back |

- **Media keys:** while the player is open, your keyboard's play, pause and next keys work even with another window in front, and Windows' media controls (and Exo Launcher's Now Playing) show what's playing. Closing the player gives the keys back to your music.
- **Audio, subtitles and speed:** the sliders button at the top right. Each show remembers the languages you pick for its next episodes.
- **Skip intro** and **Skip credits** show while an opening or credits play, when your server has them marked (Jellyfin's media segments) or the file names them in its chapters.
- **Up Next** shows near the end of an episode. With **Play next episode** on, the next one plays when the count runs out; **Not now** stops it.
- **The picture** is picked for your PC as it plays, with no setting: High quality (mpv's high-quality scaling) on a dedicated graphics card on mains power; Balanced on integrated graphics, on battery, and for the rest of a video that keeps dropping frames. Video is decoded on the graphics card, and in software when the card can't decode it. **I** shows which.
- **AI upscaling:** on High quality, an NVIDIA RTX card enlarges video smaller than your screen with RTX Video Super Resolution, when that's on in Exo Launcher's PC > Display (or NVIDIA's own settings). It steps aside while a game runs.
- **Buffering:** the player reads ahead as far as memory allows and waits for a few seconds of picture before playing on. If it buffers often, `%LocalAppData%\ExoCinema\app.log` notes each wait with how fast the link was and how fast the video needs it.

## Settings

- **Look > Material:** Glass, or Solid for plain dark panels.
- **Playback:** Source (Best plays the best version of a title for this PC, Ask lets you pick one when your server has several) and Play next episode.
- **Account:** your Jellyfin account page (opens in your browser) and Sign out.
- **Storage:** the pictures kept from your server, and **Clear** to empty them.
- **About:** your version, **What's new**, **Check for updates** and **Install update**, and links to report a problem, the privacy statement and the licenses.

## Updates

Exo Cinema checks for a new version 20 seconds after it starts and every twelve hours, and tells you once when one is out. **Settings > About > Install update** downloads it, checks it and installs it; Exo Cinema closes and opens again on the new version, signed in as before.

## Uninstall

Windows **Settings > Apps > Installed apps > Exo Cinema > Uninstall**. Your sign-in and settings stay for a reinstall unless you choose **Also remove my sign-in and settings**.

## Something wrong?

[Report a problem](https://github.com/UhhErix/ExoCinema/issues/new/choose). Say what you were watching (the kind of file, not its name, is enough) and attach the end of `%LocalAppData%\ExoCinema\app.log`. Exo Cinema keeps your server's address, links, keys and titles out of it.
