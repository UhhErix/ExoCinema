# Exo Cinema privacy statement

**Exo Cinema is local-first. It has no Exo account, no ads and no usage tracking. It does send diagnostic reports when something breaks, on by default; the section "Crash, freeze and error reports" says exactly what each one holds.**

Exo's servers that Exo Cinema talks to are its update address, which tells it when a new version is out, and the report service. Nothing you watch or do in it is sent to Exo or its developer.

## What stays on this PC

Exo Cinema keeps its data in `%LocalAppData%\ExoCinema`, beside the app (in `app`), never inside it, so updates never touch it:

- `settings.json`: whether the first run is done, and the last version whose What's New you saw;
- `hdr-films.json`: only while Exo Cinema has switched a screen's HDR on for a film, that screen's device name, so the next start can switch HDR back off if this run ended before it could. It is removed once HDR is back off;
- the interface's own storage (`WebView2`): your choices in Settings, the volume, the languages you picked for each show, and a copy of Home and the title pages you opened, so they draw at once next time;
- the pictures of films and shows (`cache\images`), so Home draws at once, and the seek bar's pictures of the videos you played (`cache\scrub`, 256 MB at most), so they show at once next time. **Settings > Storage > Clear** empties both;
- the player's shaders built for your graphics card (`cache\shaders`), so it doesn't build them again at every start, and the poster of what's playing (`cache\now-playing-…jpg`) for Windows' media controls, replaced with each title;
- `link.txt`: how fast streams have come in on your connection, one number, so **Best** picks a stream your connection carries smoothly;
- `watch\ledger.dat`: Exo Watch, what you watched, where you left off and what you saved, by each title's IMDb or TMDB ID and its name, from what you play here; and `sync\cinema.dat`: the choices your Exo profile will carry to your other PCs (Material, Reduce motion, Source, Play next episode, each show's languages, and your add-ons' addresses). Both are sealed with Windows' own protection for your Windows user, and nothing in either leaves this PC;
- `jellyfin.json`, if you connect to a Jellyfin server: its address and name, your user name and ID, an ID for this PC, and the key the server hands back, sealed with Windows' own protection for your Windows user. Your password is never kept. None of it goes through your Exo profile.
- `app.log`, `app.prev.log`, `crash.log` and `window-placement.json`: what Exo Cinema did (timings, buffering, errors, failed add-on and server answers), what went wrong if it crashed, and where its window was. Each run starts a new `app.log`; the run before is kept as `app.prev.log`, and each file stops at 512 KB. `app.log` never holds a web address, a key, your Jellyfin server's address, an add-on's address, a title or a file name: each address is cut to its kind of file (`https://…/*.mkv`), and a file name is replaced by `<file>`.
- `reports` and `reports-state.json`: diagnostic reports waiting to be sent, and how many were sent today. A report is deleted once it is sent, or once it is dropped.
- While the player is open, the title, the show and the poster of what's playing go to Windows' media controls on this PC, which show them in Windows' media flyouts and to apps that read them, such as Exo Launcher. They don't leave the PC; closing the player clears them.
- WebView2's form memory is off: nothing you type into Exo Cinema (an add-on link, your Jellyfin password) is kept by it.

Signing out forgets the sign-in. Uninstalling with **Also remove my sign-in and settings** deletes the whole folder; without it, the folder stays for a reinstall.

## What leaves your PC

### Cinemeta and its pictures

What films and shows there are comes from Cinemeta, Stremio's public catalog (`v3-cinemeta.strem.io`, and `cinemeta-catalogs.strem.io` for its lists): Home's popular and featured rows, what you search for, and each title's details and episodes when you open it, by its IMDb ID. Their pictures come from MetaHub (`images.metahub.space` and `episodes.metahub.space`) and TMDB's picture host (`image.tmdb.org`), fetched by Exo Cinema itself and kept in `cache\images`. These hosts see your IP address and what you look at, as any website does; there's no account with them and nothing else is sent. A title Exo Watch knows only by its TMDB ID (Jellyfin gives some titles no other) is looked up once on Wikidata's public query service (`query.wikidata.org`) by that ID, for its IMDb ID, so it has its pictures and page; the answers are kept sealed in `watch\imdb-ids.dat`.

### Your Jellyfin server, if you connect to one

**Settings > Jellyfin**'s server field starts as `https://jlly.cc`, filled in and editable; type your own server's address over it if you have one. Nothing is sent to any server, that one included, until you choose **Connect**. **Connect** sends your username and password to the address in the field, and to nowhere else, over the connection that address uses: an `http://` address that isn't on this PC or your own network is not encrypted, and Settings says so. An address typed without `https://` or `http://` is tried over https, and over http only if it's on your own network.

Connected, Exo Cinema asks that server, and no other, for your libraries, Continue Watching, Next Up, favorites, each title and episode you open, and what you search for. It tells the server what you play and where you are in it, at its start, every 30 seconds, on a seek, on pause and at its stop, and what you mark watched or favorite, with this PC's name and an ID for it, which the server shows among its devices. That record is the server's, under its owner's rules, and other apps on your account can read it. It plays the file from the server (or from where the server says a file is kept, without your key), and its pictures are kept in the picture cache with the rest. Nothing of it goes to Exo or into Exo Watch. **Sign out** ends the session on the server and forgets the sign-in here.
**Create an account**, **Set up AIOStreams** and **Get PublicMetaDB** on that screen open jlly.cc, AIOStreams' setup page and publicmetadb.com in your default browser. Exo Cinema sends nothing to them and stores nothing from them.

### Your add-ons, if you add any

**Settings > Add-ons** is empty unless you add one. Exo Cinema asks each add-on you add for what it offers: its rows on Home, its search, a title's details, and the streams and subtitles of what you play (by the title's ID, with the season and episode). It plays from the address an add-on answers with, and for the seek bar's pictures reads that stream's keyframes too (about 5% of the file, once per file). Each add-on, and the hosts it sends you to, see your IP address. Your add-ons' addresses are kept in `sync\cinema.dat`, and go to your other PCs only inside your Exo profile, end-to-end encrypted.

### Exo Watch

Continue Watching, the Watchlist and History are Exo Cinema's own (Exo Watch, `watch\ledger.dat`): what you play here keeps where you left off, every 30 seconds and when you stop, and marks it watched once you're past nine tenths. None of it goes to a service.


### Crash, freeze and error reports (on by default)
Exo Cinema sends a diagnostic report to Exo's report service (`reports.getexo.app`, on Exo's Cloudflare account) when:
- it crashes; the report is sent at its next start;
- its window doesn't answer for five seconds (a freeze); it is sent straight away;
- a film ends with a playback error from the player; sent straight away;
- the Jellyfin server doesn't answer three times in a row; sent straight away;
- one of your add-ons fails to load (not Cinemeta, Stremio's public catalog); sent straight away.

A report holds these five things and nothing else: the app's name (`cinema`), Exo Cinema's version, the Windows build (for example `10.0.26300.0`), the name of the error (for example `InvalidOperationException`, `Hang`, `PlaybackFailed`, `ServerUnreachable` or `AddonFailed`), and up to about 8 KB of log lines from around the error. Before anything leaves this PC, the lines lose every address (your Jellyfin server's, an add-on's and its keys, any web address), file paths, file names, titles, your Windows user name and your PC's name, email addresses, numbers of nine or more digits, and anything that looks like a token. A line that still looks personal is removed whole. The service checks each report again and refuses one that still holds any of these.

Not sent, and not kept: an install ID, an Exo account (there is none here), a watch history, what you searched for. The report service sees the IP address a report comes from while it answers, as any server does; it does not store it. Each error name is sent at most once a day, and this PC sends at most five reports a day. A report is stored for 30 days, then deleted. Exo uses reports only to find and fix what breaks in Exo Cinema.

### Microsoft

Exo Cinema's interface runs on Microsoft Edge WebView2, part of Windows. WebView2 updates itself and its components through Microsoft, and may send diagnostic data according to your Windows privacy settings. If WebView2 is missing, Setup downloads Microsoft's installer from `go.microsoft.com` and checks Microsoft's signature on it.

## Updates

As Exo Cinema opens, and only then, it asks Exo's update address (`updates.getexo.app`, run by Exo on Cloudflare) which release is the newest, and when a newer one is out, reads that release's list of changes (`whats-new.json`). If that address doesn't answer, it asks GitHub instead (`github.com/UhhErix/ExoCinema/releases/latest`). **Settings > About > Check for updates** asks at once. The update address and GitHub see your IP address while they answer, and "ExoCinema/" with the version you run; nothing else is sent. The update address keeps no logs and stores nothing about you.

When you choose **Install update**, Exo Cinema downloads that release's Setup and its SHA-256 through the update address, which hands the files over from Exo's own copy on Cloudflare (from GitHub, `github.com` and its download hosts `objects.githubusercontent.com` and `release-assets.githubusercontent.com`, only in the minutes before a new release's copy is there). It falls back to GitHub directly if the address doesn't answer. It checks the file against that checksum and against its own version details, keeps it in `%LocalAppData%\ExoCinema\updates` and runs it. Setup installs over the version you have and keeps your data; the downloaded Setup is deleted the next time Exo Cinema starts.

## Questions

Open an issue at <https://github.com/UhhErix/ExoCinema/issues>. Issues are public: leave out anything personal, your add-ons' addresses included.
