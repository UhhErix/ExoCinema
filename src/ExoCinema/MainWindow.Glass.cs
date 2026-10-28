using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace ExoCinema;

/// <summary>
/// Glass over video. Exo's glass blurs what is behind it, but mpv draws the picture under the page, where the page's
/// backdrop-filter can't see it; so mpv blurs and tones the picture itself under the page's glass shapes as it draws
/// each frame (Shaders/exo-glass.glsl). Nothing is copied off the GPU, so the glass costs no frames and moves with
/// the picture. The page sends the shapes in device pixels; they go to mpv in the video's own rectangle, and the
/// subtitles rise above the controls while they show.
/// </summary>
public sealed partial class MainWindow
{
    public static string GlassShader { get; } = Path.Combine(AppContext.BaseDirectory, "Shaders", "exo-glass.glsl");
    private const int GlassShapes = 8; // as many as the shader holds

    private JsonNode? _glass;  // the page's latest shapes
    private int _glassRuns;    // 0 idle, 1 applying, more: changed while applying
    private string _glassOpts = "", _subPos = "";
    private bool _glassFailed;

    private void SetGlass(JsonNode msg)
    {
        Volatile.Write(ref _glass, msg);
        QueueGlass();
    }

    private void QueueGlass()
    {
        if (Interlocked.Increment(ref _glassRuns) == 1) Task.Run(ApplyGlass);
    }

    // Off the UI thread: mpv takes an option change at its next frame, and the call waits for that. Only the latest
    // shapes go; any that came while one was applying are skipped.
    private void ApplyGlass()
    {
        do
        {
            Volatile.Write(ref _glassRuns, 1);
            try { ApplyGlass(Volatile.Read(ref _glass)); }
            catch (Exception e) { _log.Write($"glass: {e.Message}"); }
        }
        while (Interlocked.CompareExchange(ref _glassRuns, 0, 1) != 1);
    }

    private void ApplyGlass(JsonNode? msg)
    {
        if (_mpv is not { } mpv || msg is null) return;
        double Osd(string name) => double.TryParse(mpv.Get("osd-dimensions/" + name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
        // The video's rectangle in the window: the margins mpv leaves around it.
        double left = Osd("ml"), top = Osd("mt"), height = Osd("h");

        var shapes = msg["shapes"] as JsonArray ?? [];
        var n = _glassFailed ? 0 : Math.Min(shapes.Count, GlassShapes);
        var o = new StringBuilder(Inv($"exoN={n},exoScale={(double?)msg["scale"] ?? 1:0.###}"));
        for (var i = 0; i < n; i++)
        {
            var s = shapes[i]!;
            double F(string key) => (double?)s[key] ?? 0;
            o.Append(Inv($",exo{i}x={F("x") - left:0.#},exo{i}y={F("y") - top:0.#},exo{i}w={F("w"):0.#},exo{i}h={F("h"):0.#},exo{i}r={F("r"):0.#},exo{i}k={F("k"):0},exo{i}a={F("a"):0.##}"));
        }
        var opts = o.ToString();
        if (opts != _glassOpts && mpv.Set("glsl-shader-opts", opts) >= 0) _glassOpts = opts;

        // The subtitles clear the controls while they show (mpv keeps 22 lines of 720 under them by itself).
        var lift = (double?)msg["lift"] ?? 0;
        var pos = height > 0 && lift > 0 ? Math.Clamp(100 - (lift - 22 * height / 720) / height * 100, 50, 100) : 100;
        var subPos = pos.ToString("0.#", CultureInfo.InvariantCulture);
        if (subPos != _subPos && mpv.Set("sub-pos", subPos) >= 0) _subPos = subPos;
    }

    /// <summary>mpv couldn't build the glass shader (an old GPU or driver): the page's glass goes still and dark.</summary>
    private void OnMpvLog(string text)
    {
        _log.Write(text);
        if (_glassFailed || !text.StartsWith("[vo/gpu-next") || !text.Contains("shader", StringComparison.OrdinalIgnoreCase)) return;
        _glassFailed = true;
        OnUi(() => Post(new { type = "video-glass", ok = false }));
        QueueGlass();
    }

    private static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
