using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ExoCinema.Ui;

/// <summary>
/// A backdrop that is nothing: WinUI otherwise fills the window behind its content with the theme's solid color,
/// which covered mpv's picture (mpv draws into the same window, under WinUI's content). With this, what is not
/// drawn by the page shows mpv. The same idea as WinUIEx's TransparentTintBackdrop (MIT).
/// </summary>
internal sealed class TransparentBackdrop : SystemBackdrop
{
    private static Windows.UI.Composition.Compositor? _compositor;
    private static object? _queueController;

    /// <summary>Whether the backdrop took (null) or why not.</summary>
    public static event Action<Exception?>? Connected;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(target, xamlRoot);
        try
        {
            target.SystemBackdrop = Compositor().CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
            Connected?.Invoke(null);
        }
        catch (Exception e)
        {
            Connected?.Invoke(e);
        }
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        base.OnTargetDisconnected(target);
        target.SystemBackdrop = null;
    }

    // The system compositor needs a Windows.System dispatcher queue on this thread; WinUI only makes its own kind.
    private static Windows.UI.Composition.Compositor Compositor()
    {
        if (_compositor is not null) return _compositor;
        if (Windows.System.DispatcherQueue.GetForCurrentThread() is null)
        {
            var options = new DispatcherQueueOptions { Size = Marshal.SizeOf<DispatcherQueueOptions>(), ThreadType = 2, ApartmentType = 0 };
            CreateDispatcherQueueController(options, out var controller);
            _queueController = WinRT.MarshalInspectable<object>.FromAbi(controller);
        }
        return _compositor = new Windows.UI.Composition.Compositor();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int Size;
        public int ThreadType;    // DQTYPE_THREAD_CURRENT
        public int ApartmentType; // DQTAT_COM_NONE
    }

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out IntPtr controller);
}
