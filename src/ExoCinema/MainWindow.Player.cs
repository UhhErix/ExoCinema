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
        OnUi(() =>
        {
            if (name == "playback-restart") _jellyfin.OnPlaybackRestart();
            if (name == "end-file") _jellyfin.OnEnded();
            if (name == "file-loaded")
                foreach (var (url, title, lang) in _jellyfin.TakePendingSubtitles())
                    _mpv?.Command("sub-add", url, "auto", title, lang);
            Post(new { type = "event", name, data });
            if (timing != null) Post(new { type = "timing", timing });
        });
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
