# Changelog

## 1.0.6 — October 28, 2026

Your links and keys stay yours, on screen and in the log.

- **Your add-on link stays private.** Settings > Playback > Stream addon shows a saved link as dots, as a password field does, so it never shows in a screenshot or a stream; pasting a new link or clearing the field works as before. _Verified: on screen._
- **A log that's safe to share.** `app.log` wrote whole stream addresses, with the server's address and its access token in them. Now every web address is cut to its scheme and kind of file (`https://…/*.mkv`), the servers and add-ons in use are blanked wherever else they're named, and your Windows user folder reads `%USERPROFILE%`; `crash.log` gets the same. WebView2's form memory is off too, so nothing typed into the app is kept by it. _Verified: end-to-end; after a play and an unreachable server, the log held no address, token or user name._
- **Newer mpv and FFmpeg.** zhongfly's LGPL libmpv build of October 28: mpv v0.41.0-1320-gad5877eda and FFmpeg N-128134-gc69954c34 (from v0.41.0-1308 and N-128097). _Verified: 4K60 Dolby Vision with no dropped frames; 720p with RTX Super Resolution; the player's glass on the graphics card._
- **Up to date.** Every NuGet package (Windows App SDK 2.6.4, WebView2 SDK 1.0.4314.35) is on its newest version. .NET's 10.0.14 runtime comes in the next release, together with Exo Launcher's. _Checked in code only._

## 1.0.5 — October 28, 2026

Settings with one right answer are now just done.

- **The picture picks itself.** Settings > Playback > Picture is gone. Exo Cinema draws High quality (mpv's high-quality scalers, with RTX Video Super Resolution where it's on) on a dedicated graphics card on mains power, and Balanced on integrated graphics and on battery. A video that drops a frame a second for a minute goes Balanced for the rest of it; the next one starts on High again. The player's readings (I) say which. _Verified: on an RTX 3070, a 720p clip played on High with RTX Super Resolution; made to drop 62 frames in a minute, it went Balanced and the upscaler stepped aside; the next video started on High; a minimized window dropped none. On battery: checked in code only, as the test PC is a desktop._
- **Hardware decoding, always.** Its switch is gone: video is decoded on the graphics card, and mpv falls back to software for anything the card can't decode, as it did with the switch on. _Verified: Direct3D 11 decoding on the test clip._
- **First run:** the Playback step now sets Source and Play next episode.

## 1.0.4 — October 28, 2026

AI upscaling on RTX cards, and updates that wait for the folder.

- **AI upscaling on RTX cards.** A video smaller than the screen (720p on a 1080p screen, 1080p on a 1440p or 4K one) is enlarged by NVIDIA's RTX Video Super Resolution as it plays, through mpv's Direct3D 11 video processor, as Edge and Chrome ask for it: sharper edges and finer detail than any scaler can draw. It takes Picture on High quality, SDR video decoded on the card, and RTX Video Super Resolution on in Exo Launcher's PC > Display (or NVIDIA's own settings); Exo Cinema reads that switch and never changes it. With it off, mpv's own high-quality scalers enlarge the picture as before. _Verified: on an RTX 3070, the same frame scaled 2x had four times the edge detail with it; 720p played upscaled to 1080p with no frames dropped._
- **Games come first.** While Exo Launcher has a game running, the upscaler steps aside so the game keeps the graphics card, and comes back when the game ends. _Verified: with Exo Launcher's game signal set and cleared._
- **Updates that wait.** An update stopped at "Exo Cinema's folder is in use" when one of the app's WebView2 helpers was still closing; Setup now tries again for up to 20 seconds. Found by Exo Browser, whose updates failed the same way. _Verified: an update with the app folder held for 6 seconds finished in 9._

## 1.0.3 — October 28, 2026

The best version of every title, or your pick.

- **The best version, every time.** A title your server has in more than one version (4K and 1080p, a remux and a smaller copy) played whichever the server listed first. Play now takes the best one for this PC: one it can play as it is before one the server would convert, then the sharpest picture this PC is built for (above 1080p only on a dedicated graphics card), then the highest bitrate. Up Next and autoplay take it too. _Verified: end-to-end with a test server listing 1080p before 4K; the 4K version played._
- **Or choose it yourself.** Settings > Playback > Source on Ask shows every version when you press Play: its picture (4K, Dolby Vision, HEVC), its name, size, bitrate and sound, the best one marked; Escape or Cancel puts it away, and a title with one version plays without asking. _Verified: end-to-end; picking 1080p played the 1080p version._

## 1.0.2 — October 27, 2026

Skip where you look, rows that page, and no white boxes.

- **Skip where you look.** Skip intro, Skip credits and Up Next sat at the right end of the control bar, near the middle of a wide screen; they sit at the right of the screen now, higher and clear of the controls, and Skip is larger (56 px, 16 px words, up from 44 and 13). _Verified: on screen._
- **Rows that page.** Every row has Exo Launcher's Store arrows beside its name: each moves the row nine tenths of its width, one turns off at its end, and a row that fits shows none. _Verified: end-to-end._
- **Pictures that lift.** Pointing at a poster, a wide card or an episode lifts its picture 3 px toward you and lights it a little, as Exo Launcher's covers do; nothing grows, and Windows' reduce motion keeps it still. Rows have room above for the lift. _Verified: end-to-end._
- **No white boxes.** Glass ringed whatever had the focus inside it, so the search field wore a white box as you typed, and a field clicked into, or a button clicked and then a key pressed (Space to pause), wore Chromium's ring. Rings now show only while you move with Tab, and a click puts them away. _Verified: with the page focused, the field, a choice and search by button and by Ctrl+K show none; after Tab the ring shows._
- **Play starts playing.** The player opens on the first click of Play or Resume, so a second click, or a double click, landed on the picture and paused the video (or went full screen) before it began. The picture takes no clicks while a video opens. _Verified: end-to-end._
- **How playback went, in the log.** While a video plays, `app.log` gets a line a minute with the frames dropped and late in that minute, the decoder, the size, the format and the frame rate. _Verified: end-to-end._

## 1.0.1 — October 27, 2026

Search that answers and says it's looking.

- **Search answers sooner.** Each search asked the server for every detail a title's page uses, the episode counts of every show among them, and for a count of every match; on a server with a 10,000-title library that took up to 13 seconds. It now asks only for what the list shows: about a second for a common word, a third of a second once the server has seen it. A word the server hasn't searched lately can still take it several seconds on a library that size. _Verified: against a real server with a 10,000-title library, 12.9 s to 1.3 s cold and 0.36 s warm; posters and years still show._
- **Search shows it's looking.** A spinner turns in the field while the server looks, a search you've typed past is called off so the server works only on the latest, and the list says No matches, or why the search failed, instead of staying empty. _Verified: end-to-end._
- **No ring on What's New.** It opened with Continue focused, and with nothing pressed yet the page drew its white keyboard ring around the button; the sheet takes the focus now, and Enter or Escape still continue. _Verified: on screen._

## 1.0.0 — October 27, 2026

The first release: your Jellyfin library in Exo's look, with a native player.

- **Your Jellyfin library.** Sign in with your server's address, user name and password, or with Quick Connect where the server offers it. Home shows Continue Watching, Next Up and what's new in each library; the Bar holds your libraries; a show's page lists its seasons and episodes; Ctrl+K searches the whole server. _Verified: end-to-end; Quick Connect checked in code only._
- **Progress that follows you.** Where you stop, pauses and what you finish go back to Jellyfin as you watch, so every Jellyfin app picks up where you left off, and Watched on a title's page marks it on the server. _Verified: end-to-end._
- **A native player.** libmpv draws the picture under the interface, with hardware decoding, HDR and Dolby Vision tone mapped for the screen, and mpv's high-quality scaling on a dedicated graphics card (Settings > Playback > Picture picks Balanced for lighter scaling). _Verified: 4K60 Dolby Vision played with no dropped frames on the test PC._
- **Liquid Glass over the picture.** The controls blur and dim the video under them as it plays, drawn by the player on the graphics card, so the glass moves with every frame and costs playback nothing. _Verified: on screen; 0 frames dropped with the controls up in 4K60 Dolby Vision, where copying frames to the page had dropped 193 in 8 seconds._
- **Audio, subtitles and speed** from one menu at the top right, under its button. Each show remembers the audio and subtitle languages you pick, and the subtitles rise above the controls while they show. _Verified: end-to-end._
- **Skip intro and Up Next.** Skip openings and credits from Jellyfin's media segments, or the file's own chapter names; near the end of an episode the next one counts down and plays unless you choose Not now (Settings > Playback > Play next episode turns that off). _Verified: end-to-end, with test segments and a test next episode._
- **Steady over a slow link.** The player reads ahead as far as memory allows (up to 1 GB, by how much the PC has), waits for 3 seconds of picture before playing on after it runs dry, and reconnects when a server drops the stream. Each wait goes in `app.log` with how fast the link was and how fast the video needs it. _Verified: on a link 10% slower than the video, one pause instead of four._
- **A Home that opens at once.** Rows and pictures are kept on this PC, so Home draws before the server answers and the pictures load in two steps. _Verified: end-to-end._
- **First run, What's New and updates.** A first run signs you in and sets Picture, Play next episode and the look; after an update What's New shows what changed; Settings > About checks GitHub for new versions and installs them. _Verified: the first run end-to-end on a fresh install; the update check against GitHub; installing an update checked in code only, since 1.0 is the first release._
