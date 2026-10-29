using System.Globalization;
using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ExoCinema.Mpv;

/// <summary>
/// The seek bar's hover pictures for a video its server has none for. A second mpv opens the same video paused and
/// silent, jumps to the keyframe at the time under the pointer and draws it small on the graphics card, with the
/// player's own renderer and tone mapping, into a window that is never shown; the frame comes back as a JPEG. Only the
/// latest time asked for is drawn, so a pointer sweeping the bar never leaves work queued behind it. Made on the UI
/// thread, which owns the window; mpv runs on a thread of its own.
/// </summary>
internal sealed unsafe class FramePeek : IDisposable
{
    private readonly record struct Ask(string Url, string? Auth, double Seconds, int Width, int Height, double Duration, int Video);

    private readonly IntPtr _window;
    private readonly Thread _thread;
    private readonly AutoResetEvent _wake = new(false);
    private readonly Lock _gate = new();
    private readonly Action<string> _log;
    private readonly Action<Action> _onUi;
    private readonly (string Trc, string Prim, string Peak) _target;
    private Ask? _wanted;
    private bool _stop;
    private volatile bool _closing;

    // On the peek's own thread only.
    private IntPtr _ctx;
    private string? _open;   // the URL mpv has open
    private bool _unusable;  // that URL plays something other than the player's video, or won't open
    private (int W, int H) _size;

    /// <summary>A picture for the time asked: the time it really shows (its keyframe) and the JPEG, or nothing when
    /// this video can't give pictures; with the page's number for the video it asked about.</summary>
    public event Action<double, double, byte[]?, int>? Ready;

    /// <param name="target">The player's own output (transfer, primaries, peak), so a picture looks as the video does.</param>
    public FramePeek((string Trc, string Prim, string Peak) target, Action<string> log, Action<Action> onUi)
    {
        _target = target;
        _log = log;
        _onUi = onUi;
        // A popup that is never shown, can't be activated and stays off the taskbar: it only gives mpv somewhere to draw.
        _window = CreateWindowExW(WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW, "STATIC", null, WS_POPUP, 0, 0, 320, 180, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        _thread = new Thread(Run) { IsBackground = true, Name = "mpv peek" };
        _thread.Start();
    }

    /// <summary>The picture at <paramref name="seconds"/> in the video at <paramref name="url"/>, <paramref name="width"/> by
    /// <paramref name="height"/> device pixels. <paramref name="duration"/> is the player's, to tell its video from a
    /// host's refusal notice.</summary>
    public void Request(string url, string? auth, double seconds, int width, int height, double duration, int video)
    {
        lock (_gate) _wanted = new Ask(url, auth, seconds, Math.Clamp(width, 64, 1280), Math.Clamp(height, 36, 1280), duration, video);
        _wake.Set();
    }

    /// <summary>The player moved on: the open video is let go (its connection with it).</summary>
    public void Stop()
    {
        lock (_gate) { _wanted = null; _stop = true; }
        _wake.Set();
    }

    private void Run()
    {
        while (true)
        {
            // A minute without a hover: mpv goes, with its memory (about 50 MB); the next hover starts it again.
            if (!_wake.WaitOne(60_000))
            {
                if (_ctx != IntPtr.Zero) { MpvNative.TerminateDestroy(_ctx); _ctx = IntPtr.Zero; _open = null; _size = (0, 0); }
                continue;
            }
            if (_closing) break;
            Ask? ask;
            bool stop;
            lock (_gate) { ask = _wanted; _wanted = null; stop = _stop; _stop = false; }
            try
            {
                if (stop && _ctx != IntPtr.Zero && _open != null) { Command("stop"); _open = null; }
                if (ask is { } a) Draw(a);
            }
            catch (Exception e) { _log($"peek: {e.Message}"); }
        }
        if (_ctx != IntPtr.Zero) MpvNative.TerminateDestroy(_ctx);
        _onUi(() => DestroyWindow(_window));
    }

    private void Draw(Ask a)
    {
        if (_ctx == IntPtr.Zero && !Start()) { Ready?.Invoke(a.Seconds, -1, null, a.Video); return; }
        if ((a.Width, a.Height) != _size)
        {
            // mpv's window inside follows this one's size; the picture is drawn at exactly the size shown.
            SetWindowPos(_window, IntPtr.Zero, 0, 0, a.Width, a.Height, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
            _size = (a.Width, a.Height);
        }
        var t = a.Seconds.ToString("0.###", CultureInfo.InvariantCulture);
        if (a.Url != _open)
        {
            var started = Environment.TickCount64;
            Command("change-list", "http-header-fields", "clr", "");
            if (a.Auth != null) Command("change-list", "http-header-fields", "append", "Authorization: " + a.Auth);
            Command("loadfile", a.Url, "replace", "-1", "start=" + t);
            _open = a.Url;
            _unusable = !Landed(20_000);
            // A host that refuses the second connection plays a short notice instead: not this video.
            var duration = Number("duration");
            if (!_unusable && a.Duration > 0 && Math.Abs(duration - a.Duration) > Math.Max(5, a.Duration * 0.02)) _unusable = true;
            _log(_unusable ? "peek: this video gives no pictures" : $"peek: opened in {Environment.TickCount64 - started} ms");
        }
        else if (!_unusable)
        {
            Command("seek", t, "absolute+keyframes");
            if (!Landed(10_000)) { _open = null; Ready?.Invoke(a.Seconds, -1, null, a.Video); return; }
        }
        if (_unusable) { Ready?.Invoke(a.Seconds, -1, null, a.Video); return; }
        Ready?.Invoke(a.Seconds, Number("time-pos"), Shot(), a.Video);
    }

    private bool Start()
    {
        _ctx = MpvNative.Create();
        if (_ctx == IntPtr.Zero) return false;
        string[][] options =
        [
            ["wid", _window.ToInt64().ToString(CultureInfo.InvariantCulture)],
            ["vo", "gpu-next"], ["gpu-api", "d3d11"], ["gpu-context", "d3d11"], ["hwdec", "auto-safe"],
            ["target-trc", _target.Trc], ["target-prim", _target.Prim], ["target-peak", _target.Peak],
            // Its own shader cache, beside the player's: two mpvs never write the same files.
            ["gpu-shader-cache-dir", Path.Combine(PlayerTuning.ShaderCache, "peek")],
            // Still and silent: no sound, no subtitles, nothing drawn over the picture.
            ["pause", "yes"], ["aid", "no"], ["sid", "no"], ["osd-level", "0"], ["osc", "no"],
            ["idle", "yes"], ["keep-open", "always"], ["ytdl", "no"], ["load-stats-overlay", "no"], ["load-auto-profiles", "no"],
            ["input-default-bindings", "no"], ["input-vo-keyboard", "no"], ["input-cursor", "no"], ["cursor-autohide", "no"],
            // The keyframe at the time, not the exact frame: one frame decoded per picture.
            ["hr-seek", "no"], ["keepaspect", "no"],
            // Only what one picture needs is read, nothing ahead.
            ["cache", "no"], ["demuxer-readahead-secs", "0"], ["demuxer-max-bytes", "2MiB"], ["demuxer-max-back-bytes", "0"],
            ["stream-lavf-o", "reconnect=1,reconnect_streamed=1,reconnect_on_network_error=1,reconnect_delay_max=5"],
        ];
        foreach (var o in options)
            if (MpvNative.SetOptionString(_ctx, o[0], o[1]) < 0) _log($"peek: option {o[0]} refused");
        if (MpvNative.Initialize(_ctx) >= 0) return true;
        MpvNative.TerminateDestroy(_ctx);
        _ctx = IntPtr.Zero;
        return false;
    }

    /// <summary>Waits for the picture after a load or seek; false when the video ended or failed instead.</summary>
    private bool Landed(int ms)
    {
        var until = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < until && !_closing)
        {
            var e = MpvNative.WaitEvent(_ctx, 0.1);
            if (e->EventIdValue == MpvNative.EventId.PlaybackRestart) return true;
            if (e->EventIdValue is MpvNative.EventId.EndFile or MpvNative.EventId.Shutdown) return false;
        }
        return false;
    }

    /// <summary>The window as mpv draws it (the picture, at the window's size) as a JPEG.</summary>
    private byte[]? Shot()
    {
        var args = new[] { "screenshot-raw", "window", "rgba" };
        var ptrs = new IntPtr[args.Length + 1];
        MpvNative.Node result;
        int r;
        try
        {
            for (var i = 0; i < args.Length; i++) ptrs[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            fixed (IntPtr* p = ptrs) r = MpvNative.CommandRet(_ctx, p, &result);
        }
        finally
        {
            foreach (var p in ptrs) if (p != IntPtr.Zero) Marshal.FreeCoTaskMem(p);
        }
        if (r < 0) return null;
        try
        {
            if (result.Format != MpvNative.Format.NodeMap) return null;
            var map = (MpvNative.NodeList*)result.U;
            long w = 0, h = 0, stride = 0;
            MpvNative.ByteArray* data = null;
            for (var i = 0; i < map->Num; i++)
            {
                var value = map->Values[i];
                switch (MpvNative.Utf8(map->Keys[i]))
                {
                    case "w": w = value.U; break;
                    case "h": h = value.U; break;
                    case "stride": stride = value.U; break;
                    case "data": data = (MpvNative.ByteArray*)value.U; break;
                }
            }
            if (w <= 0 || h <= 0 || data == null) return null;
            var pixels = new byte[w * h * 4];
            for (var y = 0; y < h; y++)
                new ReadOnlySpan<byte>((byte*)data->Data + y * stride, (int)w * 4).CopyTo(pixels.AsSpan((int)(y * w * 4)));
            return Jpeg(pixels, (uint)w, (uint)h);
        }
        finally { MpvNative.FreeNodeContents(&result); }
    }

    private static byte[] Jpeg(byte[] pixels, uint w, uint h)
    {
        using var stream = new InMemoryRandomAccessStream();
        var quality = new BitmapPropertySet { ["ImageQuality"] = new BitmapTypedValue(0.88f, Windows.Foundation.PropertyType.Single) };
        var encoder = BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream, quality).AsTask().GetAwaiter().GetResult();
        encoder.SetPixelData(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Ignore, w, h, 96, 96, pixels);
        encoder.FlushAsync().AsTask().GetAwaiter().GetResult();
        var bytes = new byte[stream.Size];
        using var read = stream.GetInputStreamAt(0).AsStreamForRead();
        read.ReadExactly(bytes);
        return bytes;
    }

    private void Command(params string[] args)
    {
        var ptrs = new IntPtr[args.Length + 1];
        try
        {
            for (var i = 0; i < args.Length; i++) ptrs[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            fixed (IntPtr* p = ptrs) MpvNative.Command(_ctx, p);
        }
        finally
        {
            foreach (var p in ptrs) if (p != IntPtr.Zero) Marshal.FreeCoTaskMem(p);
        }
    }

    private double Number(string name)
    {
        var p = MpvNative.GetPropertyString(_ctx, name);
        if (p == IntPtr.Zero) return -1;
        try { return double.TryParse(MpvNative.Utf8(p), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : -1; }
        finally { MpvNative.Free(p); }
    }

    /// <summary>Lets the thread finish what it is drawing and close mpv there; the window goes after it, on the UI thread.</summary>
    public void Dispose()
    {
        _closing = true;
        _wake.Set();
    }

    private const uint WS_POPUP = 0x80000000, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080;
    private const uint SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_NOOWNERZORDER = 0x0200;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string cls, string? name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
