using System.Text.Json;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace ExoCinema;

/// <summary>
/// Where the window was: %LOCALAPPDATA%\ExoCinema\window-placement.json, saved half a second after a move or resize
/// settles and when the window closes. --bounds overrides it for a start.
/// </summary>
public sealed partial class MainWindow
{
    private sealed record Placement(int X, int Y, int Width, int Height, bool Maximized);

    private static readonly string PlacementPath = Path.Combine(AppInfo.Data, "window-placement.json");

    private Placement? _normalPlacement;
    private Placement? _savedPlacement;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _placementSaveTimer;

    private void RestoreWindowPlacement()
    {
        if (_launch.Bounds is { } b)
        {
            AppWindow.MoveAndResize(b);
            return;
        }
        Placement? saved = null;
        try { if (File.Exists(PlacementPath)) saved = JsonSerializer.Deserialize<Placement>(File.ReadAllText(PlacementPath)); } catch { }
        if (saved is null) { CenterOnScreen(); return; }

        var area = DisplayArea.GetFromRect(new RectInt32(saved.X, saved.Y, saved.Width, saved.Height), DisplayAreaFallback.Nearest);
        var work = area.WorkArea;
        // Kept on a screen that still exists and inside its work area.
        var w = Math.Min(saved.Width, work.Width);
        var h = Math.Min(saved.Height, work.Height);
        var x = Math.Clamp(saved.X, work.X, work.X + work.Width - w);
        var y = Math.Clamp(saved.Y, work.Y, work.Y + work.Height - h);
        AppWindow.MoveAndResize(new RectInt32(x, y, w, h));
        _normalPlacement = _savedPlacement = saved with { X = x, Y = y, Width = w, Height = h };
        if (saved.Maximized && AppWindow.Presenter is OverlappedPresenter presenter)
        {
            // Maximize activates the window: started not to take the focus, it fills the work area instead and
            // maximizes the first time it is activated.
            if (Environment.GetEnvironmentVariable("EXO_CINEMA_NO_ACTIVATE") == "1")
            {
                AppWindow.MoveAndResize(work);
                void Once(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
                {
                    if (args.WindowActivationState == Microsoft.UI.Xaml.WindowActivationState.Deactivated) return;
                    Activated -= Once;
                    presenter.Maximize();
                }
                Activated += Once;
            }
            else presenter.Maximize();
        }
    }

    private void CenterOnScreen()
    {
        var work = DisplayArea.Primary.WorkArea;
        var scale = GetWindowScale();
        var w = Math.Min((int)(1440 * scale), work.Width);
        var h = Math.Min((int)(900 * scale), work.Height);
        AppWindow.MoveAndResize(new RectInt32(work.X + (work.Width - w) / 2, work.Y + (work.Height - h) / 2, w, h));
    }

    private void RememberWindowPlacement()
    {
        if (_launch.Bounds is not null || _fullScreen || AppWindow.Presenter is not OverlappedPresenter presenter) return;
        if (presenter.State == OverlappedPresenterState.Minimized) return;
        if (presenter.State == OverlappedPresenterState.Restored)
            _normalPlacement = new(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height, false);
        else if (_normalPlacement is not null)
            _normalPlacement = _normalPlacement with { Maximized = true };
        if (_normalPlacement is null || _normalPlacement == _savedPlacement) return;
        if (_placementSaveTimer is null)
        {
            _placementSaveTimer = DispatcherQueue.CreateTimer();
            _placementSaveTimer.Interval = TimeSpan.FromMilliseconds(500);
            _placementSaveTimer.IsRepeating = false;
            _placementSaveTimer.Tick += (_, _) => SaveWindowPlacement();
        }
        _placementSaveTimer.Stop();
        _placementSaveTimer.Start();
    }

    private void SaveWindowPlacement()
    {
        _placementSaveTimer?.Stop();
        if (_normalPlacement is not { } p || p == _savedPlacement) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PlacementPath)!);
            File.WriteAllText(PlacementPath, JsonSerializer.Serialize(p));
            _savedPlacement = p;
        }
        catch (IOException) { }
    }
}
