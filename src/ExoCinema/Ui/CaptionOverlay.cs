using System.Runtime.InteropServices;
using Windows.Graphics;

namespace ExoCinema.Ui;

/// <summary>
/// Invisible child windows over the Bar's empty space and its maximize button. The page's WebView2 is a windowed
/// one (WinUI's own WebView2 can't be transparent, microsoft-ui-xaml#6527, and mpv's picture is under it), so it
/// takes every pointer message in its area; these windows sit over it where the caption is and answer
/// HTTRANSPARENT, which hands the hit-test to the WinUI window under them. Its InputNonClientPointerSource regions
/// (MainWindow.Caption.cs, as in Exo Launcher) then make the space Windows' own caption and maximize button: drag,
/// double-click, Aero Snap, the system menu and the snap layouts flyout.
/// </summary>
internal sealed class CaptionOverlay : IDisposable
{
    private const string ClassName = "ExoCinemaCaptionOverlay";
    private static readonly WndProc Proc = WindowProc; // kept alive for the class's lifetime
    private static bool _registered;

    private readonly IntPtr _parent;
    private readonly List<IntPtr> _windows = [];

    public CaptionOverlay(IntPtr parent)
    {
        _parent = parent;
        if (_registered) return;
        var wc = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(Proc),
            hInstance = GetModuleHandle(null),
            lpszClassName = ClassName,
        };
        RegisterClassEx(ref wc);
        _registered = true;
    }

    /// <summary>Places one window over each rect (client pixels), on top of the page.</summary>
    public void Place(IEnumerable<RectInt32> rects)
    {
        var list = rects.Where(r => r.Width > 0 && r.Height > 0).ToList();
        while (_windows.Count < list.Count)
        {
            const int WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_CLIPSIBLINGS = 0x04000000;
            const int WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
            var h = CreateWindowEx(WS_EX_LAYERED | WS_EX_NOACTIVATE, ClassName, "", WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS,
                0, 0, 1, 1, _parent, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
            // Alpha 1 of 255: nothing to see, but still a window the pointer can land on.
            SetLayeredWindowAttributes(h, 0, 1, 2); // LWA_ALPHA
            _windows.Add(h);
        }
        for (var i = 0; i < _windows.Count; i++)
        {
            const uint SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40, SWP_HIDEWINDOW = 0x80;
            if (i < list.Count)
            {
                var r = list[i];
                SetWindowPos(_windows[i], IntPtr.Zero /* HWND_TOP */, r.X, r.Y, r.Width, r.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
            else
            {
                SetWindowPos(_windows[i], IntPtr.Zero, 0, 0, 0, 0, SWP_NOACTIVATE | SWP_HIDEWINDOW);
            }
        }
    }

    public void Clear() => Place([]);

    private static IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        const uint WM_NCHITTEST = 0x0084, WM_MOUSEACTIVATE = 0x0021;
        if (msg == WM_NCHITTEST) return new IntPtr(-1); // HTTRANSPARENT: the window under it answers
        if (msg == WM_MOUSEACTIVATE) return new IntPtr(3); // MA_NOACTIVATE
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        foreach (var h in _windows) DestroyWindow(h);
        _windows.Clear();
    }

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(int exStyle, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);
}
