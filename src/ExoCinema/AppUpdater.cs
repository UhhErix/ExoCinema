using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ExoCinema;

/// <summary>
/// New versions, from this repository's releases on GitHub only. The check reads the tag the latest release
/// redirects to (no API, no rate limit). Install downloads that release's Setup and its SHA-256, checks the file
/// against it and against its own version details, and runs it silently: Setup closes Exo Cinema, swaps the new
/// version in and opens it again.
/// </summary>
internal sealed partial class AppUpdater
{
    public const string Repo = "UhhErix/ExoCinema";
    private const string SetupName = "ExoCinema-Setup.exe";
    private static readonly string[] Hosts = ["github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com"];
    private static readonly string Folder = Path.Combine(AppInfo.Data, "updates");

    private readonly HttpClient _http;

    /// <summary>The newer version found by the last check, with its What's New, or null.</summary>
    public string? Latest { get; private set; }
    public JsonNode? Notes { get; private set; }

    public AppUpdater()
    {
        _http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"ExoCinema/{AppInfo.Version}");
    }

    /// <summary>Setups from earlier updates, which are done with once Exo Cinema starts again.</summary>
    public static void CleanUp()
    {
        try { if (Directory.Exists(Folder)) Directory.Delete(Folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public async Task<string?> CheckAsync(CancellationToken ct = default)
    {
        using var r = await _http.GetAsync($"https://github.com/{Repo}/releases/latest", HttpCompletionOption.ResponseHeadersRead, ct);
        // No release yet answers 404, or a redirect to the release list.
        var tag = r.Headers.Location is { } to ? TagIn().Match(to.ToString()) : Match.Empty;
        if (!tag.Success || !System.Version.TryParse(tag.Groups[1].Value, out var latest) || latest <= System.Version.Parse(AppInfo.Version))
        {
            Latest = null;
            Notes = null;
            return null;
        }
        var version = latest.ToString(3);
        if (version != Latest)
        {
            Notes = null;
            try { Notes = CheckedNotes(await GetStringAsync(Asset(version, "whats-new.json"), 64 * 1024, ct), version); } catch (Exception e) when (e is HttpRequestException or InvalidDataException or TaskCanceledException) { }
        }
        Latest = version;
        return version;
    }

    /// <summary>Downloads and checks the latest Setup; returns its path once it is safe to run.</summary>
    public async Task<string> DownloadAsync(Action<double> progress, CancellationToken ct = default)
    {
        var version = Latest ?? throw new InvalidOperationException("No update to install.");
        var sumText = await GetStringAsync(Asset(version, SetupName + ".sha256"), 1024, ct);
        var sum = Regex.Match(sumText, "^[0-9a-fA-F]{64}").Value;
        if (sum.Length != 64) throw new InvalidDataException("The release's checksum couldn't be read.");

        Directory.CreateDirectory(Folder);
        var part = Path.Combine(Folder, $"ExoCinema-Setup-{version}.exe.part");
        var exe = Path.ChangeExtension(part, null);
        using (var r = await GetAsync(Asset(version, SetupName), ct))
        await using (var from = await r.Content.ReadAsStreamAsync(ct))
        await using (var to = File.Create(part))
        {
            var total = r.Content.Headers.ContentLength ?? 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[1 << 16];
            long done = 0;
            int n;
            while ((n = await from.ReadAsync(buffer, ct)) > 0)
            {
                await to.WriteAsync(buffer.AsMemory(0, n), ct);
                hash.AppendData(buffer, 0, n);
                done += n;
                if (total > 0) progress((double)done / total);
            }
            if (total > 0 && done != total) throw new InvalidDataException("The download stopped short. Try again.");
            if (!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(sum)))
                throw new InvalidDataException("The download doesn't match the release's checksum, so it wasn't run.");
        }
        var info = FileVersionInfo.GetVersionInfo(part);
        if (info.ProductName != "Exo Cinema" || info.FileDescription != "Exo Cinema Setup")
            throw new InvalidDataException("The download isn't Exo Cinema's Setup, so it wasn't run.");
        File.Move(part, exe, overwrite: true);
        return exe;
    }

    /// <summary>Runs Setup with no window of its own; it closes this copy and opens the new one.</summary>
    public static void Run(string setup)
    {
        var start = new ProcessStartInfo(setup, "/S") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppInfo.Data };
        start.Environment["EXO_CINEMA_SILENT_INSTALL"] = "1";
        Process.Start(start)?.Dispose();
    }

    private static string Asset(string version, string name) => $"https://github.com/{Repo}/releases/download/v{version}/{name}";

    // Redirects are followed by hand, and only to GitHub's own hosts.
    private async Task<HttpResponseMessage> GetAsync(string url, CancellationToken ct)
    {
        var uri = new Uri(url);
        for (var hop = 0; hop < 6; hop++)
        {
            if (uri.Scheme != "https" || !Hosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
                throw new HttpRequestException($"The update pointed somewhere other than GitHub ({uri.Host}).");
            var r = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)r.StatusCode is >= 300 and < 400 && r.Headers.Location is { } next)
            {
                uri = next.IsAbsoluteUri ? next : new Uri(uri, next);
                r.Dispose();
                continue;
            }
            if (!r.IsSuccessStatusCode)
            {
                r.Dispose();
                throw new HttpRequestException($"GitHub answered {(int)r.StatusCode}.");
            }
            return r;
        }
        throw new HttpRequestException("Too many redirects.");
    }

    private async Task<string> GetStringAsync(string url, int limit, CancellationToken ct)
    {
        using var r = await GetAsync(url, ct);
        var bytes = await r.Content.ReadAsByteArrayAsync(ct);
        if (bytes.Length > limit) throw new InvalidDataException("Too large.");
        return Encoding.UTF8.GetString(bytes);
    }

    // A release's What's New, as the page will draw it: its version, then 1 to 30 short items of plain text.
    private static JsonNode CheckedNotes(string text, string version)
    {
        var n = JsonNode.Parse(text) ?? throw new InvalidDataException("Empty.");
        if ((string?)n["version"] != version || n["items"] is not JsonArray items || items.Count is < 1 or > 30)
            throw new InvalidDataException("Not this release's notes.");
        foreach (var item in items)
        {
            static bool Plain(string? s, int max) => s is { Length: > 0 } && s.Length <= max && !s.Any(char.IsControl);
            if (!Plain((string?)item?["title"], 80) || !Plain((string?)item?["detail"], 240) || (string?)item?["icon"] is not { } icon || !Regex.IsMatch(icon, "^[A-Za-z]{1,32}$"))
                throw new InvalidDataException("An item isn't plain text.");
        }
        return n;
    }

    [GeneratedRegex(@"/releases/tag/v?(\d+\.\d+\.\d+)$")]
    private static partial Regex TagIn();
}
