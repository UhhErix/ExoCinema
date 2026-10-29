using ExoCinema.Ui;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace ExoCinema;

/// <summary>
/// The Bar's hit-testing, as Exo Launcher does it (its MainWindow.Caption.cs). WinUI's WebView2 ignores CSS
/// app-region, so the page measures its controls (window.js › "chrome") and the host turns the rest of the band
/// into Windows' own caption: drag, double-click to maximize, Aero Snap and Win+arrows all come from Windows. The
/// maximize button is the system's maximize region, so hovering it opens the Windows 11 snap layouts flyout; its
/// hover state goes to the page.
/// </summary>
public sealed partial class MainWindow
{
    private RectInt32[]? _captionInteractiveRegions;
    private RectInt32? _captionMaximizeButton;
    private RectInt32[] _appliedCaption = [];
    private InputNonClientPointerSource? _captionInput;
    private bool? _maximizePressedFrom;
    private long _captionRestoreWindowStart;
    private int _captionRestores;
    private bool? _postedMaximized;

    private double PageZoom => 1.0;

    private bool IsMaximized =>
        AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };

    internal void SetChromeLayout(RectInt32[]? interactiveRegions, RectInt32? maximizeButton)
    {
        _captionInteractiveRegions = interactiveRegions;
        _captionMaximizeButton = maximizeButton;
        UpdateCaptionDragRegions();
    }

    private void AttachCaptionInput()
    {
        var source = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
        _captionInput = source;
        // The XAML window keeps a default title-bar caption of its own and rewrites the caption rects on some
        // layout passes; left alone, the band would drag only where that default strip happens to be.
        source.RegionsChanged += (_, _) => DispatcherQueue.TryEnqueue(RestoreCaptionIfReplaced);
        source.PointerEntered += (_, e) =>
        {
            if (e.RegionKind == NonClientRegionKind.Maximize) Post(new { type = "maximize-hover", on = true });
        };
        source.PointerExited += (_, e) =>
        {
            if (e.RegionKind == NonClientRegionKind.Maximize) Post(new { type = "maximize-hover", on = false });
        };
        source.PointerPressed += (_, e) =>
        {
            if (e.RegionKind == NonClientRegionKind.Maximize) _log.Write($"maximize: pressed (maximized {IsMaximized})");
            if (e.RegionKind == NonClientRegionKind.Maximize) _maximizePressedFrom = IsMaximized;
        };
        source.PointerReleased += (_, e) =>
        {
            if (e.RegionKind == NonClientRegionKind.Maximize) _log.Write($"maximize: released, in region {e.IsPointInRegion}");
            if (e.RegionKind != NonClientRegionKind.Maximize || _maximizePressedFrom is not { } before) return;
            _maximizePressedFrom = null;
            if (!e.IsPointInRegion) return;
            // Windows normally performs the click on its own maximize region. Should it not have, the button
            // still does what it says.
            var check = DispatcherQueue.CreateTimer();
            check.Interval = TimeSpan.FromMilliseconds(200);
            check.IsRepeating = false;
            check.Tick += (_, _) =>
            {
                check.Stop();
                if (_closingWindow || IsMaximized != before) return;
                _log.Write("maximize: Windows didn't act; toggled by Exo Cinema");
                ToggleMaximize();
            };
            check.Start();
        };
    }

    private void RestoreCaptionIfReplaced()
    {
        if (_closingWindow || _captionInput is not { } source || _appliedCaption.Length == 0) return;
        RectInt32[] current;
        try { current = source.GetRegionRects(NonClientRegionKind.Caption); }
        catch { return; }
        if (current.AsSpan().SequenceEqual(_appliedCaption)) return;
        // Bounded: two writers must never ping-pong on the UI thread.
        var now = Environment.TickCount64;
        if (now - _captionRestoreWindowStart > 1000) { _captionRestoreWindowStart = now; _captionRestores = 0; }
        if (++_captionRestores > 8) return;
        UpdateCaptionDragRegions();
    }

    /// Region rects are physical client pixels; the page's rects are CSS pixels (DIPs).
    private void UpdateCaptionDragRegions()
    {
        if (_fullScreen)
        {
            // A full-screen window has no caption: nothing drags it, snaps it or maximizes it.
            _captionOverlay?.Clear();
            try
            {
                var input = _captionInput ?? InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
                input.ClearRegionRects(NonClientRegionKind.Caption);
                input.ClearRegionRects(NonClientRegionKind.Maximize);
                input.ClearRegionRects(NonClientRegionKind.Passthrough);
            }
            catch { }
            return;
        }
        try
        {
            var scale = GetWindowScale();
            // AppWindow.ClientSize is the only authority for the physical width.
            var width = 0;
            try { width = AppWindow.ClientSize.Width; } catch { }
            if (width <= 0) width = (int)Math.Round(RootGrid.ActualWidth * scale);
            CaptionDragRegions.Build(width, scale * PageZoom, _captionInteractiveRegions, _captionMaximizeButton,
                out var caption, out var passthrough, out var maximize);
            if (caption.Length == 0) return;
            var source = _captionInput ?? InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
            _appliedCaption = caption;
            HandCursorOverMaximize();
            source.SetRegionRects(NonClientRegionKind.Caption, caption);
            source.SetRegionRects(NonClientRegionKind.Passthrough, passthrough);
            if (maximize.Length > 0) source.SetRegionRects(NonClientRegionKind.Maximize, maximize);
            else source.ClearRegionRects(NonClientRegionKind.Maximize);
            // Over the windowed page, the same rects get pass-through windows so the hit-test reaches these regions.
            _captionOverlay?.Place(caption.Concat(maximize));
        }
        catch
        {
            /* best effort: the next layout, size or DPI change applies them again */
        }
    }

    /// <summary>
    /// The maximize button is Windows' own region (for snap layouts), and Windows shows the arrow there, as on its
    /// own caption buttons. Exo's buttons show the hand, so WinUI's input window over the Bar is asked for its cursor
    /// first: over the maximize region it is the hand.
    /// </summary>
    private void HandCursorOverMaximize()
    {
        var input = FindWindowEx(_hwnd, IntPtr.Zero, "InputNonClientPointerSource", null);
        if (input == IntPtr.Zero || input == _cursorHooked) return;
        _cursorHooked = input;
        _cursorProc = (h, msg, w, l) =>
        {
            const uint WM_SETCURSOR = 0x0020;
            const int HTMAXBUTTON = 9;
            if (msg == WM_SETCURSOR && (short)(l.ToInt64() & 0xFFFF) == HTMAXBUTTON)
            {
                SetCursor(LoadCursor(IntPtr.Zero, new IntPtr(32649))); // IDC_HAND
                return new IntPtr(1);
            }
            return CallWindowProc(_cursorNext, h, msg, w, l);
        };
        _cursorNext = SetWindowLongPtr(input, -4 /* GWLP_WNDPROC */, System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_cursorProc));
    }

    private IntPtr _cursorHooked, _cursorNext;
    private WndProcDelegate? _cursorProc; // kept alive while the window is subclassed

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern IntPtr CallWindowProc(IntPtr prev, IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SetCursor(IntPtr cursor);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);

    // Close goes as Windows' own close message: Window.Close() skips AppWindow.Closing, and with it telling Jellyfin
    // where playback stopped and saving the window's place.
    private void WindowCommand(string what)
    {
        if (what == "close")
        {
            PostMessageW(_hwnd, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
            return;
        }
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            if (what == "maximize") SetFullScreen(false);
            return;
        }
        switch (what)
        {
            case "minimize": presenter.Minimize(); break;
            case "maximize": ToggleMaximize(); break;
        }
    }

    private void ToggleMaximize()
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter) return;
        if (presenter.State == OverlappedPresenterState.Maximized) presenter.Restore();
        else presenter.Maximize();
    }

    private void NotifyWindowState(bool force = false)
    {
        var maximized = IsMaximized;
        if (!force && maximized == _postedMaximized) return;
        _postedMaximized = maximized;
        Post(new { type = "window", maximized });
    }
}
