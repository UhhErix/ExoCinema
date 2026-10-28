using System.Text.RegularExpressions;

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
        lock (_lines) _lines.Add($"{DateTime.Now:HH:mm:ss.fff} {Redact(text)}");
        Flush();
    }

    // The log gets attached to public issues, so nothing personal goes in it: every web address keeps only its scheme
    // and its kind of file (https://…/*.mkv), which drops the server, its access token, an add-on's link and the
    // title; the servers and add-ons in use are blanked wherever else they're named (a "host not found" error); and
    // the Windows user's folder reads %USERPROFILE%.
    private readonly List<Regex> _hidden = [];
    private static readonly Regex Address = new(@"\b[a-zA-Z][a-zA-Z0-9+.-]*://[^\s""'<>]+", RegexOptions.Compiled);
    private static readonly string Profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Blanks this server's or add-on's host in every line from now on.</summary>
    public void Hide(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return;
        if (!address.Contains("://")) address = "http://" + address.Trim();
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Host.Length == 0) return;
        var pattern = $@"(?<![\w.-]){Regex.Escape(uri.Host)}(?![\w-])";
        lock (_lines)
            if (!_hidden.Exists(r => r.ToString() == pattern)) _hidden.Add(new Regex(pattern, RegexOptions.IgnoreCase));
    }

    private string Redact(string text)
    {
        text = Clean(text);
        lock (_lines) foreach (var host in _hidden) text = host.Replace(text, "…");
        return text;
    }

    /// <summary>The addresses and the user's folder taken out, for app.log and crash.log alike.</summary>
    internal static string Clean(string text)
    {
        text = Address.Replace(text, m =>
        {
            var at = m.Value.IndexOf("://", StringComparison.Ordinal);
            var path = m.Value[(at + 3)..].Split('?', '#')[0];
            var name = path.Contains('/') ? path[(path.LastIndexOf('/') + 1)..] : "";
            var dot = name.LastIndexOf('.');
            var kind = dot > 0 && name.Length - dot is > 1 and <= 6 && name[(dot + 1)..].All(char.IsAsciiLetterOrDigit) ? $"/*{name[dot..]}" : "";
            return $"{m.Value[..at]}://…{kind}";
        });
        return Profile.Length > 0 ? text.Replace(Profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase) : text;
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
