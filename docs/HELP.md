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

- **Jellyfin:** the server's address starts as `https://jlly.cc`; type your own server's address over it if you have one, then your username and password, and choose **Connect**. **Create an account**, **Set up AIOStreams** and **Get PublicMetaDB** open those sites in your browser. "Connected as you on your server" means it really worked, and that one sign-in is all it takes: your library is there from then on. Skip it and Cinemeta's films and shows are there instead.
- **Exo account:** sign in with Google, Apple or email to bring your add-ons and Exo Watch from your other PCs. **Continue** without it: everything works on this PC alone.
- **Add-ons:** asked only if you didn't connect to a server. Paste an add-on's link (it ends in `manifest.json`) and choose **Add**. Cinemeta is built in, so Home has films and shows without one; an add-on with streams is what plays them.
- **Playback:** Source (Best, or Ask to pick from every stream your add-ons offer) and whether the next episode plays by itself.
- **Your look:** Glass or Solid, and Reduce motion.

Everything can be changed later in Settings, from the button at the top right.

## Jellyfin

**Settings › Jellyfin** connects to your server. If **Connect** can't, it says why:

| It says | What to do |
| --- | --- |
| Can't reach the address | Check the address and that the server is up. Use `https://` or `http://` if yours needs it. |
| That address isn't a Jellyfin server | Something else answered there. Check the address and any path after it. |
| Wrong username or password | Type them again. The password is cleared each time. |
| The server answered with an error | The server or what's in front of it has a problem. Try again, or look at the server. |

- **Connected,** Home, Movies, Shows and search show your server, and Cinemeta's rows go. Add-ons you added stay, after the server's rows. **Watched** and **Watchlist** on a server's title are the server's played and favorites; an episode's check mark shows as you point at its row.
- **Keeping in step:** where you left off, what's played and your favorites are on the server. Cinema reads them each time and reports what you play, so another Jellyfin app sees it, if that app also reads from and reports to the server. To check an app: watch a few minutes in it, and see whether Cinema's Continue Watching shows the place.
- **It plays the original file,** never a conversion, so it needs a connection that carries the file's bitrate.
- **Not encrypted** beside your name means your server's address is plain `http://`, so anyone on the way can read what passes. Use `https://` if you can.
- **Sign out** ends the session on the server and forgets it here. Signed out by the server (a password change, say), Settings asks you to connect again.

## The library

- **Home:** Continue Watching (where you left off, and each show's next episode), your Watchlist, then your add-ons' rows and Cinemeta's. It draws at once from what Exo Cinema kept last time, then fills in.
- **Rows** page with the arrows beside their names.
- **Movies and Shows** in the Bar show one list at a time, as a grid that grows as you scroll; the genre menu beside the lists narrows it. **Watchlist** and **History** join them once they hold something.
- **A title's page:** a show lists its seasons and episodes; **Watched** marks a film watched, **Watchlist** saves a title, **From the start** plays it from the beginning.
- **Search:** Ctrl+K (or /) searches your Jellyfin server and your add-ons as you type, or Cinemeta and your add-ons when you aren't connected. Arrows pick a result, Enter opens it.
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
- **The seek bar:** hover it for a picture of that moment, drawn on the spot from the video.
- **Audio, subtitles and speed:** the sliders button at the top right. A video starts on its best recording in the language you'd expect (Japanese for anime), with full subtitles in your language when the audio isn't, or only signs and songs for a dub. Each show remembers the languages you pick for its next episodes.
- **Skip intro** and **Skip credits** show while an opening or credits play, when the file names them in its chapters.
- **Up Next** shows near the end of an episode. With **Play next episode** on, the next one plays when the count runs out; **Not now** stops it.
- **The picture** is picked for your PC as it plays, with no setting: High quality (mpv's high-quality scaling) on a dedicated graphics card on mains power; Balanced on integrated graphics, on battery, and for the rest of a video that keeps dropping frames. Video is decoded on the graphics card, and in software when the card can't decode it. **I** shows which.
- **Anime upscaling:** on High quality, anime is redrawn by ArtCNN at up to 4K and shrunk back to your screen. Exo Cinema builds it for your graphics card once, in the background, and uses the driver's upscaler until then. **I** shows ArtCNN when it's on.
- **AI upscaling:** on High quality, an NVIDIA RTX card enlarges a film or show smaller than your screen with RTX Video Super Resolution, when that's on in Exo Launcher's PC > Display (or NVIDIA's own settings). Both step aside while a game runs.
- **HDR for films:** automatic, with no setting. A film or show with an HDR picture turns HDR on for the screen its window is on, when that screen can show HDR and has it off, and no game is running. HDR goes back off when the title ends, or when you close the player or Exo. When the next HDR title, such as the next episode, starts within about ten seconds of the last one ending, HDR stays on through the change so it doesn't flicker. A screen already in HDR, or one without it, is never touched. _Checked in code only._
- **Buffering:** the player reads ahead as far as memory allows and waits for a few seconds of picture before playing on. If it buffers often, `%LocalAppData%\ExoCinema\app.log` notes each wait with how fast the link was and how fast the video needs it.

## Settings

- **Look:** **Material**, Glass or Solid for plain dark panels; **Reduce motion**, which keeps things from sliding, growing or looping (only fades). It is Exo Cinema's own switch: Windows' animation effects setting doesn't change it.
- **Playback:** Source (Best plays the best stream for this PC and your connection, ranking every add-on's streams by their details; Ask lets you pick, with every stream they offer) and Play next episode.
- **Add-ons:** your add-ons in order, each with its name and host. Paste a link and choose **Add**; the arrows move one up or down (its rows on Home move with it); **Remove** asks once more. Signed in, the list is your Exo account's, on every PC.
- **Exo account:** who's signed in, **Profile** and **Sign out**.
- **Storage:** the pictures kept from Cinemeta and your add-ons, and **Clear** to empty them.
- **About:** your version, **What's new**, **Check for updates** and **Install update**, and links to report a problem, the privacy statement and the licenses. **Diagnostics** says what the crash, freeze and error reports send. They are on by default and there is no switch for them; see the privacy statement for what each one holds, and that it is deleted after 30 days.

## Updates

When a new version is out, Exo Cinema offers it as it opens: **Update available** shows the version, and **Update now** downloads it, checks it and installs it; Exo Cinema closes and opens again on the new version, with everything as you left it, and What's New shows what changed. It never interrupts while it's open: a version that comes out then is offered at the next open. If an update fails, the sheet says why, with **Copy logs** for a problem report, **Try again** and **Not now**. **Settings > About > Check for updates** looks at any time.

## Uninstall

Windows **Settings > Apps > Installed apps > Exo Cinema > Uninstall**. Your add-ons, Exo Watch and settings stay for a reinstall unless you choose **Also remove my sign-in and settings**.

## Something wrong?

[Report a problem](https://github.com/UhhErix/ExoCinema/issues/new/choose). Say what you were watching (the kind of file, not its name, is enough) and attach the end of `%LocalAppData%\ExoCinema\app.log`. Exo Cinema keeps your server's address, links, keys and titles out of it.

Exo Cinema also sends a crash, freeze or playback error report by default, with a few stripped log lines (the PRIVACY.md section "Crash, freeze and error reports" lists every field and what is removed). Such a report is never a usage record, and a crash is reported at the next start.
