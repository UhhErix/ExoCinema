using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using ExoCinema.Jellyfin;
using ExoCinema.Mpv;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics;

namespace ExoCinema;

/// <summary>
/// Exo Cinema's one window, on Exo Launcher's stack: WinUI 3 with one WebView2 carrying the whole UI. libmpv draws
/// the picture into this window's own HWND and the page sits over it with a transparent background; all input goes
/// to the page, which drives mpv through the bridge (OnWebMessage).
/// </summary>
public sealed partial class MainWindow : Window, IPlayerSurface
{
    private readonly LaunchArgs _launch;
    private readonly IntPtr _hwnd;
    private readonly AppLog _log = new();
    private readonly JellyfinHost _jellyfin;
    private readonly Dictionary<string, object?> _props = [];
    private MpvPlayer? _mpv;
    private CoreWebView2? _core;
    private CoreWebView2Controller? _controller;
    private ImageCache? _images;
    private Ui.CaptionOverlay? _captionOverlay;
    private bool _closingWindow;

    public MainWindow(LaunchArgs launch)
    {
        _launch = launch;
        InitializeComponent();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _log.Mark("process", (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds);

        Ui.TransparentBackdrop.Connected += e => _log.Write(e is null ? "backdrop: transparent" : $"backdrop: {e}");
        ApplyWindowChrome();
        AttachCaptionInput();
        // The taskbar's and Alt+Tab's icon: the Ticket (tools/make-logo.py).
        try { AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "ExoCinema.ico")); } catch { }
        RestoreWindowPlacement();

        _jellyfin = new JellyfinHost(this);
        _jellyfin.ReportFailed += message => _log.Write($"jellyfin report: {message}");
        _jellyfin.Log += message => _log.Write(message);

        // Keys go to the page whenever the window is active (focus otherwise stays on WinUI's hidden input window).
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated || _controller is null) return;
            SendXamlToBack();
            try { _controller.MoveFocus(CoreWebView2MoveFocusReason.Programmatic); } catch (COMException) { }
        };
        AppWindow.Changed += (_, e) =>
        {
            if (_controller is not null && (e.DidPresenterChange || e.DidSizeChange || e.DidVisibilityChange)) SendXamlToBack();
            if (e.DidPresenterChange || e.DidSizeChange) NotifyWindowState();
            if (e.DidPositionChange || e.DidSizeChange) RememberWindowPlacement();
        };
        AppWindow.Closing += OnClosing;
    }

    /// <summary>Shows the window; with EXO_CINEMA_NO_ACTIVATE=1 it does so without taking the focus.</summary>
    public void Show()
    {
        if (Environment.GetEnvironmentVariable("EXO_CINEMA_NO_ACTIVATE") == "1") AppWindow.Show(activateWindow: false);
        else Activate();
        _log.Mark("window", (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds);
        _ = StartAsync().ContinueWith(t => App.WriteCrash("start", t.Exception), TaskContinuationOptions.OnlyOnFaulted);
    }

    public void BringForward()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } p) p.Restore();
        Activate();
    }

    private void ApplyWindowChrome()
    {
        // Exo's chrome: no Windows caption. The page draws the Bar, window controls included; the host turns the
        // Bar's empty space into Windows' own caption (MainWindow.Caption.cs).
        ExtendsContentIntoTitleBar = true;
        SystemBackdrop ??= new Ui.TransparentBackdrop();
        if (AppWindow.Presenter is OverlappedPresenter op)
        {
            op.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
            op.PreferredMinimumWidth = 640;
            op.PreferredMinimumHeight = 400;
        }
        var tb = AppWindow.TitleBar;
        tb.ExtendsContentIntoTitleBar = true;
        tb.IconShowOptions = IconShowOptions.HideIconAndSystemMenu;
        tb.PreferredHeightOption = TitleBarHeightOption.Collapsed;
        var transparent = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        tb.BackgroundColor = transparent;
        tb.InactiveBackgroundColor = transparent;
        tb.ButtonBackgroundColor = transparent;
        tb.ButtonInactiveBackgroundColor = transparent;
        var dark = 1;
        DwmSetWindowAttribute(_hwnd, 20, ref dark, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
    }

    private async Task StartAsync()
    {
        StartMpv();
        await StartWebAsync();
        _ = WatchForUpdatesAsync();
        if (_launch.FullScreen) SetFullScreen(true);
        if (_launch.Media is { } media) Play(media, null, 0, null);
    }

    private void StartMpv()
    {
        _mpv = new MpvPlayer(_hwnd, PlayerTuning.Options());
        _mpv.Log += OnMpvLog;
        foreach (var (name, format) in ObservedProperties) _mpv.Observe(name, format);
        _mpv.PropertyChanged += OnProperty;
        _mpv.EventRaised += OnMpvEvent;
        WatchPerformance();
        _log.Mark("mpv", _mpv.Now);
    }

    private async Task StartWebAsync()
    {
        var options = new CoreWebView2EnvironmentOptions();
        if (Environment.GetEnvironmentVariable("EXO_CINEMA_CDP") is { Length: > 0 } port)
            options.AdditionalBrowserArguments = $"--remote-debugging-port={port}";
        var data = Path.Combine(AppInfo.Data, "WebView2");
        var env = await CoreWebView2Environment.CreateWithOptionsAsync(null, data, options);
        // A windowed WebView2, a child of this window: WinUI's WebView2 control always paints an opaque base color
        // (microsoft-ui-xaml#6527, still open; its transparent becomes the theme's page color), which would hide
        // mpv's picture. A windowed one honors a transparent background, as in Stremio's shell.
        var controllerOptions = env.CreateCoreWebView2ControllerOptions();
        controllerOptions.DefaultBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        _controller = await env.CreateCoreWebView2ControllerAsync(CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)_hwnd), controllerOptions);
        _controller.DefaultBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        PlaceWeb();
        SendXamlToBack();
        _captionOverlay = new Ui.CaptionOverlay(_hwnd);
        UpdateCaptionDragRegions();
        var core = _core = _controller.CoreWebView2;
#if DEBUG
        // Dev builds change the page's files under a running cache; start from them as they are on disk.
        await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache);
#endif
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.SetVirtualHostNameToFolderMapping("cinema.exo", Path.Combine(AppContext.BaseDirectory, "wwwroot"), CoreWebView2HostResourceAccessKind.Deny);
        core.WebMessageReceived += OnWebMessage;
        _images = new ImageCache(core, env);
        core.ContainsFullScreenElementChanged += (_, _) => SetFullScreen(core.ContainsFullScreenElement);
        var ready = new TaskCompletionSource();
        core.NavigationCompleted += (_, _) => ready.TrySetResult();
        core.Navigate("https://cinema.exo/index.html");
        await ready.Task;
        _log.Mark("web", (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds);
    }

    private void OnWebMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var msg = JsonNode.Parse(e.WebMessageAsJson)!;
        var mpv = _mpv!;
        switch ((string?)msg["cmd"])
        {
            case "ready":
                // The page loaded after mpv started: give it everything it missed.
                Post(new { type = "hello", tuning = PlayerTuning.Describe(), picture = _pictureSaid, timings = _log.Marks, app = Info() });
                NotifyWindowState(force: true);
                Post(new { type = "fullscreen", on = _fullScreen });
                foreach (var (name, value) in _props) Post(new { type = "prop", name, value });
                break;
            case "toggle-pause":
                mpv.Command("cycle", "pause");
                break;
            case "seek":
                _seekAt = mpv.Now;
                mpv.Command("seek", ((double)msg["to"]!).ToString(System.Globalization.CultureInfo.InvariantCulture), (string?)msg["mode"] ?? "absolute+keyframes");
                break;
            case "set":
                mpv.Set((string)msg["name"]!, (string)msg["value"]!);
                break;
            case "command":
                mpv.Command(msg["args"]!.AsArray().Select(a => (string)a!).ToArray());
                break;
            case "load":
                Play((string)msg["url"]!, null, 0, null);
                break;
            case "tracks":
                Post(new { type = "tracks", list = Tracks() });
                break;
            case "chapters":
                Post(new { type = "chapters", list = Chapters() });
                break;
            case "get":
                Post(new { type = "value", name = (string)msg["name"]!, value = mpv.Get((string)msg["name"]!) });
                break;
            case "fullscreen":
                SetFullScreen((bool)msg["on"]!);
                break;
            case "glass":
                SetGlass(msg);
                break;
            case "chrome":
                SetChromeLayout(Rects(msg["regions"]), Rect(msg["maximize"]));
                break;
            case "window":
                WindowCommand((string)msg["do"]!);
                break;
            case "jf":
                _ = AnswerAsync(msg);
                break;
            case "app":
                _ = AnswerAppAsync(msg);
                break;
            case "jellyfin-web":
                // Only the signed-in server's own account page, never an address the page supplies.
                if (_jellyfin.Server is { } server)
                    _ = Windows.System.Launcher.LaunchUriAsync(new Uri($"{server}/web/#/mypreferencesmenu"));
                break;
        }
    }

    private async Task AnswerAsync(JsonNode msg)
    {
        var id = (int)msg["id"]!;
        try
        {
            var data = await _jellyfin.HandleAsync((string)msg["op"]!, msg["args"]);
            PostJson($"{{\"type\":\"reply\",\"id\":{id},\"ok\":true,\"data\":{data?.ToJsonString() ?? "null"}}}");
        }
        catch (Exception e)
        {
            _log.Write($"jf {(string?)msg["op"]}: {e}");
            Post(new { type = "reply", id, ok = false, error = e is JellyfinException ? e.Message : "Something went wrong: " + e.Message });
        }
    }

    private static RectInt32? Rect(JsonNode? n) =>
        n is null ? null : new RectInt32((int)Math.Round((double)n["x"]!), (int)Math.Round((double)n["y"]!), (int)Math.Round((double)n["w"]!), (int)Math.Round((double)n["h"]!));

    private static RectInt32[]? Rects(JsonNode? n) => n?.AsArray().Select(Rect).OfType<RectInt32>().ToArray();

    private void Post(object message) => PostJson(JsonSerializer.Serialize(message));

    private void PostJson(string json)
    {
        if (_closingWindow) return;
        try { _core?.PostWebMessageAsJson(json); } catch (COMException) { } catch (InvalidOperationException) { }
    }

    /// <summary>Runs on the UI thread, from any thread.</summary>
    private void OnUi(Action action)
    {
        if (!_closingWindow) DispatcherQueue.TryEnqueue(() => action());
    }

    private double GetWindowScale() => GetDpiForWindow(_hwnd) / 96.0;

    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        PlaceWeb();
        UpdateCaptionDragRegions();
    }

    /// <summary>
    /// WinUI's content (an empty, transparent grid here) is a child window of its own. Wherever it is in the window
    /// order, it takes pointer input over the whole window through its composition input routing, so real clicks
    /// never reached the page (posted ones did). It holds nothing, so it is hidden; the page is the only input.
    /// </summary>
    private void SendXamlToBack()
    {
        var bridge = FindWindowEx(_hwnd, IntPtr.Zero, "Microsoft.UI.Content.DesktopChildSiteBridge", null);
        if (bridge == IntPtr.Zero) return;
        SetWindowPos(bridge, new IntPtr(1) /* HWND_BOTTOM */, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // NOSIZE|NOMOVE|NOACTIVATE
        ShowWindow(bridge, 0); // SW_HIDE
    }

    /// <summary>The page fills the client area (physical pixels).</summary>
    private void PlaceWeb()
    {
        if (_controller is null) return;
        GetClientRect(_hwnd, out var r);
        _controller.Bounds = new Windows.Foundation.Rect(0, 0, r.Right - r.Left, r.Bottom - r.Top);
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        _log.Write("closing");
        // Tell the server where playback stopped before mpv goes (a short wait, so closing stays quick).
        Task.Run(_jellyfin.ShutdownAsync).Wait(1500);
        _closingWindow = true;
        _performance?.Dispose();
        SaveWindowPlacement();
        _mpv?.Dispose();
        _mpv = null;
        _captionOverlay?.Dispose();
        _controller?.Close();
        _log.Flush();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? name);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int cmd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
