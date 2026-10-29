using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ExoCinema.Mpv;

/// <summary>
/// One libmpv instance drawing into a window. Property changes and events arrive on mpv's own
/// event thread through <see cref="PropertyChanged"/> and <see cref="EventRaised"/>.
/// </summary>
internal sealed unsafe class MpvPlayer : IDisposable
{
    private IntPtr _ctx;
    private readonly Thread _events;
    private volatile bool _closing;
    // Calls from any thread share the handle; Dispose waits for those under way and turns later ones into no-ops, so a
    // player started again (MainWindow.RestartMpv) never has a timer or the glass call into one being destroyed.
    private readonly ReaderWriterLockSlim _use = new(LockRecursionPolicy.SupportsRecursion);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<string> _observed = [];

    public event Action<string, object?>? PropertyChanged;
    public event Action<string, object?>? EventRaised;
    public event Action<string>? Log;

    /// <summary>Milliseconds since this player was made; load and seek timings are read against it.</summary>
    public double Now => _clock.Elapsed.TotalMilliseconds;

    public MpvPlayer(IntPtr hwnd, IReadOnlyDictionary<string, string> options)
    {
        _ctx = MpvNative.Create();
        if (_ctx == IntPtr.Zero) throw new InvalidOperationException("mpv_create failed");

        Check(MpvNative.SetOptionString(_ctx, "wid", hwnd.ToInt64().ToString()), "wid");
        foreach (var (k, v) in options)
        {
            var r = MpvNative.SetOptionString(_ctx, k, v);
            if (r < 0) Log?.Invoke($"option {k}={v}: {Error(r)}");
        }
        Check(MpvNative.Initialize(_ctx), "initialize");
        MpvNative.RequestLogMessages(_ctx, "info");

        _events = new Thread(EventLoop) { IsBackground = true, Name = "mpv events" };
        _events.Start();
    }

    public void Observe(string name, MpvNative.Format format)
    {
        lock (_observed)
        {
            _observed.Add(name);
            MpvNative.ObserveProperty(_ctx, (ulong)_observed.Count, name, format);
        }
    }

    public int Command(params string[] args)
    {
        var ptrs = new IntPtr[args.Length + 1];
        _use.EnterReadLock();
        try
        {
            if (_ctx == IntPtr.Zero) return -1;
            for (var i = 0; i < args.Length; i++) ptrs[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            fixed (IntPtr* p = ptrs) return MpvNative.Command(_ctx, p);
        }
        finally
        {
            _use.ExitReadLock();
            foreach (var p in ptrs) if (p != IntPtr.Zero) Marshal.FreeCoTaskMem(p);
        }
    }

    public int Set(string name, string value)
    {
        _use.EnterReadLock();
        try { return _ctx == IntPtr.Zero ? -1 : MpvNative.SetPropertyString(_ctx, name, value); }
        finally { _use.ExitReadLock(); }
    }

    public string? Get(string name)
    {
        _use.EnterReadLock();
        try
        {
            if (_ctx == IntPtr.Zero) return null;
            var p = MpvNative.GetPropertyString(_ctx, name);
            if (p == IntPtr.Zero) return null;
            try { return MpvNative.Utf8(p); } finally { MpvNative.Free(p); }
        }
        finally { _use.ExitReadLock(); }
    }

    private void EventLoop()
    {
        while (!_closing)
        {
            var e = MpvNative.WaitEvent(_ctx, -1);
            switch (e->EventIdValue)
            {
                case MpvNative.EventId.None:
                    continue;
                case MpvNative.EventId.Shutdown:
                    return;
                case MpvNative.EventId.PropertyChange:
                    {
                        var prop = (MpvNative.EventProperty*)e->Data;
                        var name = MpvNative.Utf8(prop->Name) ?? "";
                        PropertyChanged?.Invoke(name, Read(prop->Format, prop->Data));
                        break;
                    }
                case MpvNative.EventId.LogMessage:
                    {
                        var m = (MpvNative.EventLogMessage*)e->Data;
                        var prefix = MpvNative.Utf8(m->Prefix) ?? "";
                        // Warnings from anything, and what the decoder says about hardware decoding.
                        if (m->LogLevel <= 30 || prefix is "vd" || prefix.StartsWith("hwdec") || prefix.StartsWith("ffmpeg/video"))
                            Log?.Invoke($"[{prefix}] {MpvNative.Utf8(m->Text)?.TrimEnd()}");
                        break;
                    }
                case MpvNative.EventId.EndFile:
                    {
                        var end = (MpvNative.EventEndFile*)e->Data;
                        EventRaised?.Invoke("end-file", new { reason = end->Reason, error = end->Error == 0 ? null : Error(end->Error) });
                        break;
                    }
                default:
                    EventRaised?.Invoke(Name(e->EventIdValue), null);
                    break;
            }
        }
    }

    private static object? Read(MpvNative.Format format, IntPtr data)
    {
        if (data == IntPtr.Zero) return null;
        return format switch
        {
            MpvNative.Format.String => MpvNative.Utf8(*(IntPtr*)data),
            MpvNative.Format.Flag => *(int*)data != 0,
            MpvNative.Format.Int64 => *(long*)data,
            MpvNative.Format.Double => *(double*)data,
            _ => null,
        };
    }

    private static string Name(MpvNative.EventId id) => id switch
    {
        MpvNative.EventId.StartFile => "start-file",
        MpvNative.EventId.FileLoaded => "file-loaded",
        MpvNative.EventId.VideoReconfig => "video-reconfig",
        MpvNative.EventId.AudioReconfig => "audio-reconfig",
        MpvNative.EventId.Seek => "seek",
        MpvNative.EventId.PlaybackRestart => "playback-restart",
        _ => id.ToString().ToLowerInvariant(),
    };

    private static string Error(int code) => MpvNative.Utf8(MpvNative.ErrorString(code)) ?? code.ToString();

    private static void Check(int r, string what)
    {
        if (r < 0) throw new InvalidOperationException($"mpv {what}: {Error(r)}");
    }

    public void Dispose()
    {
        if (_ctx == IntPtr.Zero) return;
        // mpv_wait_event must not run during mpv_terminate_destroy: wake the loop, let it leave, then destroy.
        _closing = true;
        MpvNative.Wakeup(_ctx);
        _events.Join(2000);
        IntPtr ctx;
        _use.EnterWriteLock();
        try
        {
            ctx = _ctx;
            _ctx = IntPtr.Zero;
        }
        finally { _use.ExitWriteLock(); }
        MpvNative.TerminateDestroy(ctx);
    }
}
