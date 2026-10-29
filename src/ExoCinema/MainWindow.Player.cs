using System.Text.Json.Nodes;
using ExoCinema.Jellyfin;
using ExoCinema.Mpv;
using Microsoft.UI.Windowing;

namespace ExoCinema;

/// <summary>mpv: what the page sees of it and what Jellyfin is told.</summary>
public sealed partial class MainWindow
{
    private static readonly (string, MpvNative.Format)[] ObservedProperties =
    [
        ("pause", MpvNative.Format.Flag),
        ("duration", MpvNative.Format.Double),
        ("time-pos", MpvNative.Format.Double),
        ("demuxer-cache-time", MpvNative.Format.Double),
        ("paused-for-cache", MpvNative.Format.Flag),
        ("eof-reached", MpvNative.Format.Flag),
        ("volume", MpvNative.Format.Double),
        ("mute", MpvNative.Format.Flag),
        ("media-title", MpvNative.Format.String),
        ("video-codec", MpvNative.Format.String),
        ("hwdec-current", MpvNative.Format.String),
        ("video-params/gamma", MpvNative.Format.String),
        ("video-params/primaries", MpvNative.Format.String),
        ("video-params/w", MpvNative.Format.Int64),
        ("video-params/h", MpvNative.Format.Int64),
        ("container-fps", MpvNative.Format.Double),
        ("audio-codec-name", MpvNative.Format.String),
        ("track-list/count", MpvNative.Format.Int64),
        ("seeking", MpvNative.Format.Flag),
        ("core-idle", MpvNative.Format.Flag),
        ("video-params/pixelformat", MpvNative.Format.String),
        ("video-params/hw-pixelformat", MpvNative.Format.String),
        ("video-format", MpvNative.Format.String),
        ("osd-dimensions", MpvNative.Format.None), // the video's place in the window, for the glass
    ];

    // Timings: load and seek are measured from the command to mpv's playback-restart.
    private double _loadAt = -1, _seekAt = -1;
    private bool _firstFrameSeen;
    private double _lastTimePost;

    private bool _fullScreen;
    private (Windows.Graphics.RectInt32 Bounds, bool Maximized)? _beforeFullScreen;

    // What the player has open, for the seek bar's hover pictures (FramePeek), and the second mpv that draws them.
    private (string Url, string? Auth)? _playing;
    private FramePeek? _peek;

    public void Play(string url, string? authHeader, double startSeconds, string? title)
    {
        if (!TryStartMpv()) throw new JellyfinException("The player couldn't start. If your graphics driver is being updated, try again when it's done.");
        var mpv = _mpv!;
        _playing = (url, authHeader);
        _peek?.Stop();
        // Anime or not is the page's to say for each video (SetAnime); a new one starts as not.
        _anime = false;
        _animeOff = false;
        _loadAt = mpv.Now;
        _firstFrameSeen = false;
        // The Authorization header has commas in it, so it goes in as one list item rather than a parsed list.
        mpv.Command("change-list", "http-header-fields", "clr", "");
        if (authHeader != null) mpv.Command("change-list", "http-header-fields", "append", "Authorization: " + authHeader);
        mpv.Set("force-media-title", title ?? "");
        // keep-open pauses at the end of a file, and that pause would carry into the next one.
        mpv.Set("pause", "no");
        var options = startSeconds > 0 ? $"start={startSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}" : "";
        mpv.Command("loadfile", url, "replace", "-1", options);
        Post(new { type = "playing", title });
    }

    public void Stop()
    {
        _mpv?.Command("stop");
        // Out of the player: the hover pictures' mpv goes too, with its memory, until the next video is hovered.
        _playing = null;
        _peek?.Dispose();
        _peek = null;
        if (_restartWhenIdle) RestartMpv();
    }

    // The anime upscaler compiled ahead (Mpv/ShaderWarmup.cs): started 20 s after the app opens, or when anime plays
    // first, never while a game runs. mpv reads its shader cache only as it starts, so once the shaders are in it the
    // player is started again as soon as nothing plays; until then anime gets the driver's upscaler.
    private static string[] AnimeShaderFiles => AnimeChain().Select(f => Path.Combine(AnimeShaders, f)).ToArray();
    private bool _mpvWarm, _restartWhenIdle;
    private Task<bool>? _warmup;

    private void StartWarmup()
    {
        if (_closingWindow || !PlayerTuning.Strong || _warmup != null || _mpvWarm || !Directory.Exists(AnimeShaders) || GameRunning()) return;
        if (ShaderWarmup.Ready(AnimeShaderFiles)) { RestartWhenIdle(); return; }
        _warmup = ShaderWarmup.Run(AnimeShaderFiles, _log.Write, OnUi);
        _warmup.ContinueWith(t => OnUi(() =>
        {
            _warmup = null;
            if (t.Result) RestartWhenIdle();
        }));
    }

    private void RestartWhenIdle()
    {
        if (_playing is null) RestartMpv();
        else _restartWhenIdle = true;
    }

    /// <summary>A new mpv in place of the idle one, reading the shader cache afresh; what it was set to comes along.</summary>
    private void RestartMpv()
    {
        _restartWhenIdle = false;
        if (_closingWindow || _playing != null || _mpv is not { } old) return;
        lock (_pictureGate)
        {
            _mpv = null;
            old.Dispose();
            // What was applied to the old player is applied to the new one again.
            _shaderList = null;
            _animeChain = [];
            _upscaleFilter = "";
            _upscaleScale = 0;
            _pictureHigh = PlayerTuning.Strong;
            _glassOpts = "";
            _subPos = "";
        }
        if (!TryStartMpv()) return;
        if (_props.GetValueOrDefault("volume") is double volume) _mpv!.Set("volume", volume.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (_props.GetValueOrDefault("mute") is bool mute) _mpv!.Set("mute", mute ? "yes" : "no");
        _log.Write("player: started again with the anime upscaler compiled");
    }

    /// <summary>
    /// The picture at a time in the open video for the seek bar's hover preview. Not for a server conversion (an HLS
    /// stream): every picture would start another conversion on the server, so there only Jellyfin's own tiles show.
    /// </summary>
    private void Peek(JsonNode msg)
    {
        var t = (double)msg["t"]!;
        if (_playing is not { } now || now.Url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase))
        {
            Post(new { type = "peek", t, v = (int?)msg["v"] ?? 0, none = true });
            return;
        }
        _peek ??= NewPeek();
        _peek.Request(now.Url, now.Auth, t, (int)msg["w"]!, (int)msg["h"]!, (double?)msg["d"] ?? 0, (int?)msg["v"] ?? 0);
    }

    /// <summary>The page says whether the video playing is anime (its genres, where it was made, its audio).</summary>
    private void SetAnime(bool on)
    {
        if (on == _anime) return;
        _anime = on;
        // Off the UI thread: mpv takes new shaders at its next frame, and the call waits for that.
        Task.Run(() => ApplyPicture());
    }

    private FramePeek NewPeek()
    {
        // Drawn for the screen as the player draws for it, so a picture looks as the video does. The page is SDR, so on
        // an HDR screen (or before the player has a target) the pictures are plain SDR.
        var mpv = _mpv;
        string? gamma = mpv?.Get("video-target-params/gamma"), primaries = mpv?.Get("video-target-params/primaries"), peak = mpv?.Get("video-target-params/max-luma");
        var target = gamma is { Length: > 0 } and not ("pq" or "hlg" or "auto")
            ? (gamma!, primaries is { Length: > 0 } and not "auto" ? primaries : "bt.709", double.TryParse(peak, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p) && p >= 10 ? p.ToString("0", System.Globalization.CultureInfo.InvariantCulture) : "auto")
            : ("gamma2.2", "bt.709", "auto");
        var peek = new FramePeek(target, _log.Write, OnUi);
        peek.Ready += (asked, at, jpeg, v) => OnUi(() =>
        {
            if (_peek != peek) return;
            Post(jpeg is null ? new { type = "peek", t = asked, v, none = true } : (object)new { type = "peek", t = asked, v, at, jpg = Convert.ToBase64String(jpeg) });
        });
        return peek;
    }

    /// <summary>The open file's tracks, as the page's audio and subtitle menu needs them.</summary>
    private object Tracks()
    {
        var mpv = _mpv!;
        var count = int.TryParse(mpv.Get("track-list/count"), out var n) ? n : 0;
        var list = new List<object>();
        for (var i = 0; i < count; i++)
        {
            string? F(string field) => mpv.Get($"track-list/{i}/{field}");
            list.Add(new
            {
                type = F("type"), id = F("id"), lang = F("lang"), title = F("title"), codec = F("codec"),
                selected = F("selected") == "yes", external = F("external") == "yes", forced = F("forced") == "yes",
                channels = F("demux-channel-count"), profile = F("codec-profile"),
                isDefault = F("default") == "yes", hearing = F("hearing-impaired") == "yes",
            });
        }
        return list;
    }

    /// <summary>The open file's chapters (name and start), where a file marks its opening or credits.</summary>
    private object Chapters()
    {
        var mpv = _mpv!;
        var count = int.TryParse(mpv.Get("chapter-list/count"), out var n) ? n : 0;
        var list = new List<object>();
        for (var i = 0; i < count; i++)
            list.Add(new { title = mpv.Get($"chapter-list/{i}/title"), time = double.TryParse(mpv.Get($"chapter-list/{i}/time"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : 0 });
        return list;
    }

    private void OnProperty(string name, object? value)
    {
        if (name == "osd-dimensions")
        {
            QueueGlass();
            // The window changed size (or went full screen): the upscaler is sized again once it has settled.
            (_upscaleRecheck ??= new Timer(_ => ApplyPicture())).Change(400, Timeout.Infinite);
            return;
        }
        if (name == "paused-for-cache") LogBuffering(value is true);
        if (name == "time-pos")
        {
            if (value is double pos) OnUi(() => _jellyfin.OnTime(pos));
            // Every frame changes time-pos; the page interpolates between posts.
            var now = _mpv!.Now;
            if (now - _lastTimePost < 100) return;
            _lastTimePost = now;
        }
        OnUi(() =>
        {
            if (name == "pause" && value is bool paused) _jellyfin.OnPause(paused);
            _props[name] = value;
            MediaFollow(name);
            Post(new { type = "prop", name, value });
        });
    }

    private void OnMpvEvent(string name, object? data)
    {
        var now = _mpv!.Now;
        object? timing = null;
        if (name == "playback-restart")
        {
            // Where it landed, so the page can tell its latest seek from an earlier one that landed on the way.
            data = new { pos = double.TryParse(_mpv.Get("time-pos"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pos) ? pos : (double?)null };
            if (!_firstFrameSeen && _loadAt >= 0)
            {
                _firstFrameSeen = true;
                timing = new { what = "first-frame", ms = Math.Round(now - _loadAt, 1) };
                _log.Mark("first-frame", now - _loadAt);
            }
            else if (_seekAt >= 0)
            {
                timing = new { what = "seek", ms = Math.Round(now - _seekAt, 1) };
                _log.Mark("seek", now - _seekAt);
                _seekAt = -1;
            }
        }
        else if (name == "file-loaded" && _loadAt >= 0)
        {
            _log.Mark("file-loaded", now - _loadAt);
        }
        // A new video starts on the best picture this PC draws; once its decoder and size are known, upscale or not.
        if (name == "start-file") _lighterForThisVideo = false;
        if (name == "video-reconfig") ApplyPicture();
        OnUi(() =>
        {
            if (name == "playback-restart")
            {
                _jellyfin.OnPlaybackRestart();
                MediaFollow("seek");
            }
            if (name == "end-file") _jellyfin.OnEnded();
            if (name == "file-loaded")
                foreach (var (url, title, lang) in _jellyfin.TakePendingSubtitles())
                    _mpv?.Command("sub-add", url, "auto", title, lang);
            Post(new { type = "event", name, data });
            if (timing != null) Post(new { type = "timing", timing });
        });
    }

    // AI upscaling: on an RTX card with NVIDIA's RTX Video Super Resolution switched on (Exo Launcher's PC › Display;
    // Exo Cinema only reads it), a picture smaller than the screen is enlarged by the driver's upscaler through mpv's
    // Direct3D 11 video processor, as Edge and Chrome ask for it. Only for SDR video decoded on the card, with Picture
    // on High quality. With the switch off the driver's processor would only scale plainly, worse than mpv's own
    // scalers, so then nothing changes.
    private bool _upscaleOffNoted;
    private string _upscaleFilter = "";
    private Timer? _upscaleRecheck;
    private double _upscaleScale;

    // The picture, chosen for this PC instead of in Settings: High quality (mpv's high-quality scalers, and the AI
    // upscaling above) on a dedicated graphics card on mains power; Balanced on integrated graphics (mpv's own scalers),
    // on battery, and for the rest of a video once it drops frames (a stream too heavy for the card, something else
    // using it). A new video starts on High again. Called from mpv's events and the minute timer, so one at a time.
    private readonly Lock _pictureGate = new();
    private bool _pictureHigh = PlayerTuning.Strong, _lighterForThisVideo;
    private string _pictureSaid = "";
    private static readonly (string, string)[] Balanced = [("scale", "spline36"), ("cscale", "spline36"), ("dscale", "mitchell"), ("deband", "no")];

    private void ApplyPicture(string why = "")
    {
        if (_mpv is not { } mpv || _closingWindow) return;
        lock (_pictureGate)
        {
            var high = PlayerTuning.Strong && !_lighterForThisVideo && !PlayerTuning.OnBattery();
            if (high != _pictureHigh)
            {
                if (high) { mpv.Command("apply-profile", "high-quality"); mpv.Set("dscale", PlayerTuning.HighDownscale); }
                else foreach (var (name, value) in Balanced) mpv.Set(name, value);
                _pictureHigh = high;
                _log.Write($"picture: {(high ? "high quality" : "balanced")}{why}");
            }
            ApplyAnime(mpv);
            ApplyUpscaling(mpv);
            // The player's readings say how the picture is drawn right now.
            var said = (_pictureHigh ? "High quality" : "Balanced")
                + (_animeChain.Length > 0 ? " · ArtCNN" : _upscaleFilter.Length > 0 ? " · RTX Super Resolution" : "");
            if (said == _pictureSaid) return;
            _pictureSaid = said;
            OnUi(() => Post(new { type = "picture", text = said }));
        }
    }

    // Anime: ArtCNN (Artoriuz, MIT; Shaders/ArtCNN), a network trained on drawn art that doubles the picture's detail,
    // instead of the driver's upscaler. Picked on Exo's picture bench (2026-10-28: compressed copies of clean art scored
    // against the art itself): it came out closest to the original on every source, anime and film, 720p and 1080p, and
    // kept the backgrounds' texture that Anime4K wiped to paint. The page says when a video is anime (playerplus.js).
    // Changed to run on a picture shown at its own size or a little smaller too, with Exo's own pass after it to land
    // on twice the shown size: a 720p, 1080p or 1440p episode on a 1080p screen is drawn at up to 4K and shrunk back
    // (2:1 from 1080p and 1440p). Only for SDR video up to 1440p, on High quality, with no game running, once its
    // shaders are compiled (ShaderWarmup); a minute of dropped frames turns it off for that video.
    private static readonly string AnimeShaders = Path.Combine(AppContext.BaseDirectory, "Shaders", "ArtCNN");
    private bool _anime;
    private bool _animeOff;
    private string[] _animeChain = [];
    private string? _shaderList;

    // C4F16, the smaller of ArtCNN's real-time networks: within 0.03 dB of C4F32 on the bench, the same to the eye, and
    // several times lighter to run and to compile (37 s against 4 min the first time).
    private static string[] AnimeChain() => ["Exo_Supersample_ArtCNN_C4F16_DS.glsl", "Exo_Supersample_Downscale.glsl"];

    private void ApplyAnime(MpvPlayer mpv)
    {
        try
        {
            static double Num(string? s) => double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
            string[] chain = [];
            var height = Num(mpv.Get("video-params/h"));
            // Not compiled yet: the driver's upscaler meanwhile, and the compiling starts now if it hasn't.
            if (_anime && !_mpvWarm) OnUi(StartWarmup);
            if (_anime && _mpvWarm && _pictureHigh && !_animeOff && height > 0 && mpv.Get("video-params/gamma") is not ("pq" or "hlg") && !GameRunning())
            {
                // Up to 1440p, and shown at more than half its size: a 1440p episode on a 1080p screen is drawn at 4K too.
                // A 4K source is already sharper than the screen, and the passes at 4K and above are heavy.
                var shownH = Num(mpv.Get("osd-dimensions/h")) - Num(mpv.Get("osd-dimensions/mt")) - Num(mpv.Get("osd-dimensions/mb"));
                if (height <= 1600 && (shownH <= 0 || shownH / height > 0.55)) chain = AnimeChain();
            }
            var list = string.Join(";", chain.Select(f => Path.Combine(AnimeShaders, f)).Concat(File.Exists(GlassShader) ? [GlassShader] : []));
            _shaderList ??= File.Exists(GlassShader) ? GlassShader : "";
            if (list == _shaderList) return;
            if (mpv.Set("glsl-shaders", list) < 0) { _log.Write("anime: mpv refused the shaders"); return; }
            _shaderList = list;
            _animeChain = chain;
            _log.Write(chain.Length > 0 ? $"anime: ArtCNN, {height:0}p" : "anime: off");
        }
        catch (Exception e) { _log.Write($"anime: {e.Message}"); }
    }

    private void ApplyUpscaling(MpvPlayer mpv)
    {
        try
        {
            static double Num(string? s) => double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
            var want = "";
            double scale = 0;
            var height = Num(mpv.Get("video-params/h"));
            // ArtCNN draws anime itself: the driver's upscaler would run under it for nothing.
            if (_pictureHigh && _animeChain.Length == 0 && height > 0 && PlayerTuning.Vendor == "nvidia" && PlayerTuning.Gpu.Contains("RTX", StringComparison.OrdinalIgnoreCase)
                && mpv.Get("hwdec-current") == "d3d11va" && mpv.Get("video-params/gamma") is not ("pq" or "hlg") && !GameRunning())
            {
                // Exactly to the size the picture is shown at, so mpv doesn't scale it again: sized from the screen's
                // height, a maximized window (shorter than the screen) or a widescreen film (1920x800 on a 1080p screen
                // went up to 2592x1080) had mpv shrink the upscaled picture back a little, and shrinking a sharpened
                // picture by a sliver ripples across it: the video looked wavy. Fit to the window's video area
                // (osd-dimensions less its bars); the filter is rebuilt only when that size really changes.
                double V(string name) => Num(mpv.Get(name));
                var (dw, dh) = (V("video-params/dw"), V("video-params/dh"));
                var shownW = V("osd-dimensions/w") - V("osd-dimensions/ml") - V("osd-dimensions/mr");
                var shownH = V("osd-dimensions/h") - V("osd-dimensions/mt") - V("osd-dimensions/mb");
                var fit = dw > 0 && dh > 0 && shownW > 0 && shownH > 0 ? Math.Min(shownW / dw, shownH / dh) : 0;
                // Only to enlarge: drawn at twice the size and shrunk back (supersampled), a picture that already fills
                // the screen came out softer and further from the original than mpv's own on the picture bench.
                scale = Math.Round(Math.Min(4, fit), 4);
                if (_upscaleScale > 0 && Math.Abs(scale - _upscaleScale) * Math.Max(dw, dh) < 2) scale = _upscaleScale;
                var setting = scale >= 1.2 ? NvidiaVideo.SuperResolutionSetting() : null;
                if (setting > 0)
                    want = $"@exo-upscale:d3d11vpp=scaling-mode=nvidia:scale={scale.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)}";
                else if (scale >= 1.2 && !_upscaleOffNoted)
                {
                    // Said once: the picture would gain from it, but the driver's switch is off (or can't be read).
                    _upscaleOffNoted = true;
                    _log.Write($"upscaling: RTX Video Super Resolution is {(setting == 0 ? "off" : "unreadable")} in the driver; mpv scales instead");
                }
            }
            if (want == _upscaleFilter) return;
            if (_upscaleFilter.Length > 0) mpv.Command("vf", "remove", "@exo-upscale");
            if (want.Length > 0 && mpv.Command("vf", "add", want) < 0) want = "";
            _upscaleFilter = want;
            _upscaleScale = want.Length > 0 ? scale : 0;
            _log.Write(want.Length > 0 ? $"upscaling: RTX Video Super Resolution (driver quality {NvidiaVideo.SuperResolutionSetting()}), {height:0}p x{scale:0.##} in a {Num(mpv.Get("osd-dimensions/w")):0}x{Num(mpv.Get("osd-dimensions/h")):0} window" : "upscaling: off");
        }
        catch (Exception e) { _log.Write($"upscaling: {e.Message}"); }
    }

    // A game is running: Exo Launcher signals it with a named event while any library game runs (its
    // Services/GameRunningSignal.cs, agreed with it). The upscaler takes graphics time from the game, so it steps aside.
    // Without Exo Launcher the event doesn't exist, which reads as no game. Opened afresh each time: a handle kept open
    // would keep the event alive after Exo Launcher closed.
    private static bool GameRunning()
    {
        var handle = OpenEventW(0x00100000 /* SYNCHRONIZE */, false, @"Local\ExoLauncher.GameRunning");
        if (handle == IntPtr.Zero) return false;
        try { return WaitForSingleObject(handle, 0) == 0; }
        finally { CloseHandle(handle); }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr OpenEventW(uint access, bool inherit, string name);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    // While a video plays, a line a minute in app.log: the frames dropped and late in that minute, and how the video
    // plays, so "is it smooth?" has an answer after the fact.
    private Timer? _performance;
    private long _drops, _late;

    private void WatchPerformance() => _performance ??= new Timer(_ => { LogPerformance(); ApplyPicture(); }, null, 60_000, 60_000);

    // A minute with a frame dropped every second or so: lighter scalers for the rest of this video.
    private const int DropsThatLighten = 60;

    private void LogPerformance()
    {
        if (_mpv is not { } mpv || _closingWindow) return;
        try
        {
            long N(string name) => long.TryParse(mpv.Get(name), out var v) ? v : 0;
            long drops = N("frame-drop-count"), late = N("vo-delayed-frame-count");
            if (drops < _drops || late < _late) (_drops, _late) = (0, 0); // a new file counts from zero
            var (newDrops, newLate) = (drops - _drops, late - _late);
            (_drops, _late) = (drops, late);
            if (mpv.Get("pause") != "no" || mpv.Get("idle-active") == "yes") return;
            if (_pictureHigh && newDrops >= DropsThatLighten && !_lighterForThisVideo)
            {
                if (_animeChain.Length > 0)
                {
                    _animeOff = true;
                    _log.Write($"anime: ArtCNN off for this video, after {newDrops} dropped frames in a minute");
                    ApplyPicture();
                }
                else
                {
                    _lighterForThisVideo = true;
                    ApplyPicture($" for this video, after {newDrops} dropped frames in a minute");
                }
            }
            // Filling the buffer well ahead of the picture, mpv reads as fast as the link gives: that is its speed.
            // (Once the buffer is full it reads only what the video uses, which says nothing about the link.)
            static double Num(string? s) => double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
            var link = Num(mpv.Get("cache-speed")) * 8 / 1e6;
            var bitrate = (Num(mpv.Get("video-bitrate")) + Num(mpv.Get("audio-bitrate"))) / 1e6;
            if (bitrate > 0 && link > bitrate * 1.5) LinkSpeed.Record(link);
            var fps = double.TryParse(mpv.Get("container-fps"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : 0;
            _log.Write($"playing: {newDrops} dropped, {newLate} late in the last minute · {mpv.Get("hwdec-current")} · {mpv.Get("video-params/w")}x{mpv.Get("video-params/h")} {mpv.Get("video-format")} {mpv.Get("video-params/gamma")} · {fps:0.###} fps");
        }
        catch (Exception e) { _log.Write($"playing: {e.Message}"); }
    }

    // Each wait for the network goes in app.log with how fast data came and how fast the video needs it, so a
    // stream that buffers says whether the link or the server is short.
    private double _bufferingSince = -1;

    private void LogBuffering(bool waiting)
    {
        var mpv = _mpv!;
        if (waiting) { _bufferingSince = mpv.Now; return; }
        if (_bufferingSince < 0) return;
        var seconds = (mpv.Now - _bufferingSince) / 1000;
        _bufferingSince = -1;
        static double Num(string? s) => double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
        var speed = Num(mpv.Get("cache-speed")) * 8 / 1e6;
        var needs = (Num(mpv.Get("video-bitrate")) + Num(mpv.Get("audio-bitrate"))) / 1e6;
        _log.Write($"buffering: {seconds:0.0} s; the link gave {speed:0.0} Mb/s, the video needs {needs:0.0} Mb/s");
        // Waiting on the network, the link gives all it has: what it gave is its speed.
        if (seconds >= 1) LinkSpeed.Record(speed);
    }

    // Full screen is Exo Cinema's own: the window drops its frame and covers its monitor, moved with SWP_NOACTIVATE.
    // Windows' FullScreen presenter activated the window as it switched (it took the foreground from the user's app at
    // 16:25 on 2026-10-28), so a switch made behind a game (an episode ending, its player leaving full screen) brought
    // Exo Cinema in front of it. Now going to or from full screen never moves the foreground, in front or behind.
    private nint _styleBeforeFullScreen;
    private bool _maximizeWhenActive;

    private void SetFullScreen(bool on)
    {
        if (on == _fullScreen) return;
        _fullScreen = on;
        const int GWL_STYLE = -16;
        const nint WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000;
        const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_FRAMECHANGED = 0x0020, SWP_NOOWNERZORDER = 0x0200;
        const uint Quiet = SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_NOOWNERZORDER;
        if (on)
        {
            var maximized = IsMaximized;
            _beforeFullScreen = (new Windows.Graphics.RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height), maximized);
            if (maximized) ShowWindow(_hwnd, 4 /* SW_SHOWNOACTIVATE: restored, not activated */);
            // WinUI's presenter keeps a 1px border on the window (it put WS_BORDER back): asked to drop it, the client
            // fills the monitor exactly.
            if (AppWindow.Presenter is OverlappedPresenter frame) frame.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
            var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfoW(MonitorFromWindow(_hwnd, 2 /* MONITOR_DEFAULTTONEAREST */), ref info);
            _styleBeforeFullScreen = GetWindowLongPtrW(_hwnd, GWL_STYLE);
            SetWindowLongPtrW(_hwnd, GWL_STYLE, _styleBeforeFullScreen & ~(WS_CAPTION | WS_THICKFRAME));
            var m = info.rcMonitor;
            SetWindowPos(_hwnd, IntPtr.Zero, m.Left, m.Top, m.Right - m.Left, m.Bottom - m.Top, Quiet);
        }
        else
        {
            SetWindowLongPtrW(_hwnd, GWL_STYLE, _styleBeforeFullScreen);
            if (AppWindow.Presenter is OverlappedPresenter frame) frame.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
            if (_beforeFullScreen is { } before)
            {
                var b = before.Bounds;
                SetWindowPos(_hwnd, IntPtr.Zero, b.X, b.Y, b.Width, b.Height, Quiet);
                // Maximized again only as the active window (Windows' maximize activates); until then it fills the same space.
                if (before.Maximized)
                {
                    if (GetForegroundWindow() == _hwnd && AppWindow.Presenter is OverlappedPresenter p) p.Maximize();
                    else _maximizeWhenActive = true;
                }
            }
        }
        UpdateCaptionDragRegions();
        Post(new { type = "fullscreen", on });
    }

    private void MaximizeIfWaiting()
    {
        if (!_maximizeWhenActive || _fullScreen) return;
        _maximizeWhenActive = false;
        if (AppWindow.Presenter is OverlappedPresenter p) p.Maximize();
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public NativeRect rcMonitor, rcWork;
        public uint dwFlags;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFO info);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetWindowLongPtrW(IntPtr hwnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint SetWindowLongPtrW(IntPtr hwnd, int index, nint value);
}
