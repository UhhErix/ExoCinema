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

    public void Play(string url, string? authHeader, double startSeconds, string? title)
    {
        if (!TryStartMpv()) throw new JellyfinException("The player couldn't start. If your graphics driver is being updated, try again when it's done.");
        var mpv = _mpv!;
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

    public void Stop() => _mpv?.Command("stop");

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
                channels = F("demux-channel-count"),
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
                if (high) mpv.Command("apply-profile", "high-quality");
                else foreach (var (name, value) in Balanced) mpv.Set(name, value);
                _pictureHigh = high;
                _log.Write($"picture: {(high ? "high quality" : "balanced")}{why}");
            }
            ApplyUpscaling(mpv);
            // The player's readings say how the picture is drawn right now.
            var said = (_pictureHigh ? "High quality" : "Balanced") + (_upscaleFilter.Length > 0 ? " · RTX Super Resolution" : "");
            if (said == _pictureSaid) return;
            _pictureSaid = said;
            OnUi(() => Post(new { type = "picture", text = said }));
        }
    }

    private void ApplyUpscaling(MpvPlayer mpv)
    {
        try
        {
            static double Num(string? s) => double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
            var want = "";
            double scale = 0;
            var height = Num(mpv.Get("video-params/h"));
            if (_pictureHigh && height > 0 && PlayerTuning.Vendor == "nvidia" && PlayerTuning.Gpu.Contains("RTX", StringComparison.OrdinalIgnoreCase)
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
                scale = dw > 0 && dh > 0 && shownW > 0 && shownH > 0 ? Math.Round(Math.Min(4, Math.Min(shownW / dw, shownH / dh)), 4) : 0;
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
            _log.Write(want.Length > 0 ? $"upscaling: RTX Video Super Resolution, {height:0}p x{scale:0.##} in a {Num(mpv.Get("osd-dimensions/w")):0}x{Num(mpv.Get("osd-dimensions/h")):0} window" : "upscaling: off");
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
                _lighterForThisVideo = true;
                ApplyPicture($" for this video, after {newDrops} dropped frames in a minute");
            }
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
    }

    private void SetFullScreen(bool on)
    {
        if (on == _fullScreen) return;
        _fullScreen = on;
        if (on)
        {
            _beforeFullScreen = (new Windows.Graphics.RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height), IsMaximized);
            AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        }
        else
        {
            AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            ApplyWindowChrome();
            // Back exactly where it was: the presenter's own restore lands a frame's width smaller.
            if (_beforeFullScreen is { } before)
            {
                AppWindow.MoveAndResize(before.Bounds);
                if (before.Maximized && AppWindow.Presenter is OverlappedPresenter p) p.Maximize();
            }
        }
        Post(new { type = "fullscreen", on });
    }
}
