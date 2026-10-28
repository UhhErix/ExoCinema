namespace ExoCinema;

/// <summary>Timings and mpv warnings, kept for the page and written to %LOCALAPPDATA%\ExoCinema\app.log.</summary>
internal sealed class AppLog
{
    private readonly List<string> _lines = [];
    private readonly string _path = Path.Combine(AppInfo.Data, "app.log");

    public Dictionary<string, double> Marks { get; } = [];

    public void Mark(string what, double ms)
    {
        lock (_lines)
        {
            Marks[what] = Math.Round(ms, 1);
            _lines.Add($"{DateTime.Now:HH:mm:ss.fff} {what} {ms:0.0} ms");
        }
        Flush();
    }

    public void Write(string text)
    {
        lock (_lines) _lines.Add($"{DateTime.Now:HH:mm:ss.fff} {text}");
        Flush();
    }

    public void Flush()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            lock (_lines) File.WriteAllLines(_path, _lines);
        }
        catch (IOException) { }
    }
}
