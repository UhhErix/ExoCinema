using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace ExoCinema.Stremio;

/// <summary>
/// A Stremio-style add-on: given its manifest URL, asks it for the playable streams of a movie or
/// episode. The list is answered for this PC's connection, so a link the host ties to whoever asked
/// for it is made for this PC — where a copy minted on a server's own connection would not play.
/// </summary>
internal sealed class AddonClient
{
    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    })
    { Timeout = TimeSpan.FromSeconds(15) };

    private readonly string _base;

    private AddonClient(string baseUrl) => _base = baseUrl;

    /// <summary>"stremio://host/cfg/manifest.json" or the https form → "https://host/cfg".</summary>
    public static AddonClient? From(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        url = url.Trim();
        if (url.StartsWith("stremio://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url["stremio://".Length..];
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return null;
        const string manifest = "/manifest.json";
        if (url.EndsWith(manifest, StringComparison.OrdinalIgnoreCase)) url = url[..^manifest.Length];
        return new AddonClient(url.TrimEnd('/'));
    }

    /// <summary>Playable http(s) addresses for a movie ("tt123") or episode ("tt123:1:2"), as listed.</summary>
    public async Task<List<string>> StreamsAsync(string type, string id, CancellationToken ct = default)
    {
        var res = await Http.GetFromJsonAsync<JsonNode>($"{_base}/stream/{type}/{id}.json", ct);
        var urls = new List<string>();
        foreach (var s in res?["streams"] as JsonArray ?? [])
            if ((string?)s?["url"] is { } u && Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                urls.Add(u);
        return urls;
    }

    /// <summary>One stream as the add-on describes it: its address, every line it says about it (name, description,
    /// file name) and the file's size when given.</summary>
    public sealed record Stream(string Url, string Text, long? Size);

    /// <summary>The same list with what the add-on says about each stream, for choosing between them.</summary>
    public async Task<List<Stream>> StreamListAsync(string type, string id, CancellationToken ct = default)
    {
        var res = await Http.GetFromJsonAsync<JsonNode>($"{_base}/stream/{type}/{id}.json", ct);
        var list = new List<Stream>();
        foreach (var s in res?["streams"] as JsonArray ?? [])
        {
            if ((string?)s?["url"] is not { } u || !Uri.TryCreate(u, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) continue;
            var hints = s["behaviorHints"];
            var text = string.Join('\n', new[] { (string?)s["name"], (string?)s["description"] ?? (string?)s["title"], (string?)hints?["filename"] }.Where(t => !string.IsNullOrWhiteSpace(t)));
            list.Add(new Stream(u, text, hints?["videoSize"] is JsonValue v && v.TryGetValue<long>(out var size) ? size : null));
        }
        return list;
    }
}
