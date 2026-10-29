using System.Globalization;

namespace ExoCinema;

/// <summary>
/// How fast streams come in on this PC's connection, read from mpv while it is filling its buffer or waiting on the
/// network (MainWindow.Player.cs): Best passes over a stream that needs more than that, which would stop to buffer.
/// A running average, kept in link.txt beside the settings.
/// </summary>
internal static class LinkSpeed
{
    private static readonly string FilePath = Path.Combine(AppInfo.Data, "link.txt");
    private static double? _mbps = Load();

    /// <summary>The connection's usual speed in Mb/s, or null before anything has been streamed.</summary>
    public static double? Mbps => _mbps;

    public static void Record(double mbps)
    {
        if (mbps is <= 0.1 or > 10_000) return;
        _mbps = _mbps is { } old ? old * 0.7 + mbps * 0.3 : mbps;
        try { File.WriteAllText(FilePath, _mbps.Value.ToString("0.#", CultureInfo.InvariantCulture)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static double? Load()
    {
        try { return double.TryParse(File.ReadAllText(FilePath), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
