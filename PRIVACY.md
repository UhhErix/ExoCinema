# Exo Cinema privacy statement

**Exo Cinema is local-first. It has no Exo account, no ads, no analytics and no telemetry.**

Exo Cinema has no server of its own. Nothing you watch or do in it is sent to Exo or its developer.

## What stays on this PC

Exo Cinema keeps its data in `%LocalAppData%\ExoCinema`, beside the app (in `app`), never inside it, so updates never touch it:

- your Jellyfin sign-in (`accounts.json`): the server's address and name, your user name and an access token from the server, sealed with Windows' own protection for your Windows user. Your password is never stored;
- `settings.json`: whether the first run is done, and the last version whose What's New you saw;
- the interface's own storage (`WebView2`): your choices in Settings, the volume, the languages you picked for each show, the last server you signed in to, and a copy of Home and the title pages you opened, so they draw at once next time;
- pictures from your server (`cache\images`), so Home draws before the server answers. **Settings > Storage > Clear** empties it;
- `app.log`, `crash.log` and `window-placement.json`: what Exo Cinema did (timings, buffering, errors), what went wrong if it crashed, and where its window was. `app.log` never holds a web address, a key or your server's or add-on's name: each address is cut to its kind of file (`https://…/*.mkv`), so the log is safe to attach to an issue.
- WebView2's form memory is off: nothing you type into Exo Cinema (a server address, an add-on link) is kept by it.

Signing out forgets the sign-in. Uninstalling with **Also remove my sign-in and settings** deletes the whole folder; without it, the folder stays for a reinstall.

## What leaves your PC

### Your Jellyfin server

Exo Cinema talks to the Jellyfin server you sign in to, and only to it, for your libraries: what's in them, their pictures, search, and the video you play. While you watch, it tells the server where you are in the video, when you pause and when you stop, as every Jellyfin app does, so the server keeps your place and marks what you finish as watched, and your other Jellyfin apps pick up where you left off. **Watched** on a title's page tells the server too. The server sees your user, your IP address, the name of this PC and a random ID Exo Cinema made for it. What it does with that is up to the server and its owner.

### A stream add-on, if you add one

**Settings > Playback > Stream addon** is empty unless you fill it. With an add-on's address there, Exo Cinema asks that add-on for streams of what you play (the title's IMDb ID, and the season and episode), and plays from the address it answers with. That add-on, and the host it sends you to, see your IP address.

### Your Jellyfin account page

**Settings > Account > Jellyfin account** opens your server's own account page in your web browser.

### Microsoft

Exo Cinema's interface runs on Microsoft Edge WebView2, part of Windows. WebView2 updates itself and its components through Microsoft, and may send diagnostic data according to your Windows privacy settings. If WebView2 is missing, Setup downloads Microsoft's installer from `go.microsoft.com` and checks Microsoft's signature on it.

## Updates

Twenty seconds after Exo Cinema starts, and every twelve hours while it runs, it asks GitHub which release is the newest (`github.com/UhhErix/ExoCinema/releases/latest`), and when a newer one is out, reads that release's list of changes (`whats-new.json`). **Settings > About > Check for updates** asks at once. GitHub sees your IP address and "ExoCinema/" with the version you run; nothing else is sent.

When you choose **Install update**, Exo Cinema downloads that release's Setup and its SHA-256 from GitHub (`github.com`, which hands the files over from its own download host), checks the file against that checksum and against its own version details, keeps it in `%LocalAppData%\ExoCinema\updates` and runs it. Setup installs over the version you have and keeps your data; the downloaded Setup is deleted the next time Exo Cinema starts.

## Questions

Open an issue at <https://github.com/UhhErix/ExoCinema/issues>. Issues are public: leave out anything personal, your server's address included.
