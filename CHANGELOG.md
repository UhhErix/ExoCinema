# Changelog

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
