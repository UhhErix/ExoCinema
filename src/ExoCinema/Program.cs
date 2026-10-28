using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using WinRT;

namespace ExoCinema;

public static class Program
{
    public const string AppUserModelId = "UhhErix.ExoCinema";

    [DllImport("Microsoft.ui.xaml.dll")]
    private static extern void XamlCheckProcessRequirements();

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appID);

    [STAThread]
    public static void Main(string[] args)
    {
        try { SetCurrentProcessExplicitAppUserModelID(AppUserModelId); } catch { }

        // Custom-main WinUI apps must initialize WinRT wrappers before the first Windows App SDK API.
        ComWrappersSupport.InitializeComWrappers();

        // Its own folder as the working folder, so nothing holds the app folder open when Setup swaps in an update.
        Directory.CreateDirectory(AppInfo.Data);
        Environment.CurrentDirectory = AppInfo.Data;

        // One Exo Cinema: a second start brings the first one forward. A test copy with its own data is apart.
        var current = AppInstance.GetCurrent();
        var main = AppInstance.FindOrRegisterForKey(AppInfo.OwnData
            ? "ExoCinema.Main." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(AppInfo.Data.ToLowerInvariant())))[..12]
            : "ExoCinema.Main");
        if (!main.IsCurrent)
        {
            main.RedirectActivationToAsync(current.GetActivatedEventArgs()).AsTask().GetAwaiter().GetResult();
            return;
        }
        main.Activated += (_, _) => App.MainAppWindow?.DispatcherQueue.TryEnqueue(() => App.MainAppWindow?.BringForward());

        XamlCheckProcessRequirements();
        Application.Start(p =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
    }
}

/// <summary>
/// A file or URL to play, then optional --bounds x,y,w,h and --fullscreen. EXO_CINEMA_CDP=port opens the
/// WebView's debug port; EXO_CINEMA_NO_ACTIVATE=1 shows the window without taking the focus.
/// </summary>
public sealed record LaunchArgs(string? Media, Windows.Graphics.RectInt32? Bounds, bool FullScreen)
{
    public static LaunchArgs Parse(string[] args)
    {
        string? media = null;
        Windows.Graphics.RectInt32? bounds = null;
        var full = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--bounds" when i + 1 < args.Length:
                    var p = args[++i].Split(',').Select(int.Parse).ToArray();
                    bounds = new Windows.Graphics.RectInt32(p[0], p[1], p[2], p[3]);
                    break;
                case "--fullscreen":
                    full = true;
                    break;
                default:
                    if (!args[i].StartsWith("--")) media ??= args[i];
                    break;
            }
        }
        return new LaunchArgs(media, bounds, full);
    }
}
