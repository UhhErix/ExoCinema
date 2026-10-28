using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ExoCinema;

/// <summary>
/// New versions, from Exo's update address first: it names the latest release and hands its files over from GitHub
/// (or from its own copy when GitHub can't), so a renamed account or a moved repository never strands an installed
/// Exo Cinema again (the ImAvgErix rename stranded Exo Launcher 3.16). When the address doesn't answer, the check
/// reads the tag GitHub's latest release redirects to and the files come from GitHub, as before. Either way Install
/// downloads that release's Setup and its SHA-256, checks the file against it and against its own version details,
/// and runs it silently: Setup closes Exo Cinema, swaps the new version in and opens it again.
/// </summary>
internal sealed partial class AppUpdater
{
    public const string Repo = "UhhErix/ExoCinema";
    private const string Exo = "https://updates.getexo.app/v1/cinema";
    private const string SetupName = "ExoCinema-Setup.exe";
    private static readonly string[] Hosts = ["updates.getexo.app", "github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com"];
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
        var found = await LatestFromExoAsync(ct) ?? await LatestFromGitHubAsync(ct);
        if (found is not { } latest || latest <= System.Version.Parse(AppInfo.Version))
        {
            Latest = null;
            Notes = null;
            return null;
        }
        var version = latest.ToString(3);
        if (version != Latest)
        {
            Notes = null;
            try { Notes = CheckedNotes(await GetAssetStringAsync(version, "whats-new.json", 64 * 1024, ct), version); } catch (Exception e) when (e is HttpRequestException or InvalidDataException or TaskCanceledException) { }
        }
        Latest = version;
        return version;
    }

    // Exo's address: {"app":"cinema","tag":"vX.Y.Z",...}. Only the tag is taken from it; the files' addresses are made
    // here. Ten seconds, then GitHub.
    private async Task<System.Version?> LatestFromExoAsync(CancellationToken ct)
    {
        try
        {
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
            wait.CancelAfter(TimeSpan.FromSeconds(10));
            var n = JsonNode.Parse(await GetStringAsync($"{Exo}/latest", 4 * 1024, wait.Token));
            var tag = (string?)n?["tag"];
            return (string?)n?["app"] == "cinema" && tag is not null && TagOnly().Match(tag) is { Success: true } m
                && System.Version.TryParse(m.Groups[1].Value, out var v) ? v : null;
        }
        catch (Exception e) when (e is HttpRequestException or InvalidDataException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    // GitHub's latest release redirects to its tag (no API, no rate limit); no release yet answers 404, or a redirect
    // to the release list.
    private async Task<System.Version?> LatestFromGitHubAsync(CancellationToken ct)
    {
        using var r = await _http.GetAsync($"https://github.com/{Repo}/releases/latest", HttpCompletionOption.ResponseHeadersRead, ct);
        var tag = r.Headers.Location is { } to ? TagIn().Match(to.ToString()) : Match.Empty;
        return tag.Success && System.Version.TryParse(tag.Groups[1].Value, out var v) ? v : null;
    }

    /// <summary>Downloads and checks the latest Setup; returns its path once it is safe to run.</summary>
    public async Task<string> DownloadAsync(Action<double> progress, CancellationToken ct = default)
    {
        var version = Latest ?? throw new InvalidOperationException("No update to install.");
        var sumText = await GetAssetStringAsync(version, SetupName + ".sha256", 1024, ct);
        var sum = Regex.Match(sumText, "^[0-9a-fA-F]{64}").Value;
        if (sum.Length != 64) throw new InvalidDataException("The release's checksum couldn't be read.");

        Directory.CreateDirectory(Folder);
        var part = Path.Combine(Folder, $"ExoCinema-Setup-{version}.exe.part");
        var exe = Path.ChangeExtension(part, null);
        using (var r = await GetAssetAsync(version, SetupName, ct))
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

    // A release file from Exo's address, else straight from GitHub. The checksum decides what runs, wherever it came from.
    private static string[] AssetUrls(string version, string name) =>
        [$"{Exo}/download/v{version}/{name}", $"https://github.com/{Repo}/releases/download/v{version}/{name}"];

    private async Task<HttpResponseMessage> GetAssetAsync(string version, string name, CancellationToken ct)
    {
        var urls = AssetUrls(version, name);
        try { return await GetAsync(urls[0], ct); }
        catch (HttpRequestException) { return await GetAsync(urls[1], ct); }
    }

    private async Task<string> GetAssetStringAsync(string version, string name, int limit, CancellationToken ct)
    {
        var urls = AssetUrls(version, name);
        try { return await GetStringAsync(urls[0], limit, ct); }
        catch (HttpRequestException) { return await GetStringAsync(urls[1], limit, ct); }
    }

    // Redirects are followed by hand, and only to Exo's update address and GitHub's own hosts.
    private async Task<HttpResponseMessage> GetAsync(string url, CancellationToken ct)
    {
        var uri = new Uri(url);
        for (var hop = 0; hop < 6; hop++)
        {
            if (uri.Scheme != "https" || !Hosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
                throw new HttpRequestException($"The update pointed somewhere other than Exo's update address or GitHub ({uri.Host}).");
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
                throw new HttpRequestException($"{uri.Host} answered {(int)r.StatusCode}.");
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

    [GeneratedRegex(@"^v(\d+\.\d+\.\d+)$")]
    private static partial Regex TagOnly();
}
