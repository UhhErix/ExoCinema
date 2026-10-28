using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace ExoCinema.Jellyfin;

/// <summary>
/// A Jellyfin server, signed in as one user. Calls the routes Jellyfin 10.9 and later name
/// (/Items?userId=, /UserViews, /UserItems/Resume) and falls back to the older /Users/{id}/... ones
/// when a server answers 404. Items come back as Jellyfin's own JSON for the page to read.
/// </summary>
internal sealed class JellyfinClient
{
    public const string ClientName = "Exo Cinema";
    public static readonly string ClientVersion = AppInfo.Version;

    // Fields every list asks for: what the cards, the detail page and resume need.
    private const string Fields = "Overview,Genres,DateCreated,ProductionYear,OfficialRating,CommunityRating,RunTimeTicks,UserData,ImageTags,BackdropImageTags,ParentBackdropImageTags,SeriesPrimaryImageTag,ParentThumbImageTag,ChildCount,RecursiveItemCount,MediaSourceCount";

    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    })
    { Timeout = TimeSpan.FromSeconds(20) };

    // For looking at where a remote URL would send the player, without going there.
    private static readonly HttpClient Head = new(new SocketsHttpHandler { AllowAutoRedirect = false })
    { Timeout = TimeSpan.FromSeconds(10) };

    public string Server { get; }
    public string DeviceId { get; }
    public string? Token { get; private set; }
    public string? UserId { get; private set; }

    public JellyfinClient(string server, string deviceId, string? token = null, string? userId = null)
    {
        Server = Normalize(server);
        DeviceId = deviceId;
        Token = token;
        UserId = userId;
    }

    /// <summary>"jellyfin.example.com" → "https://jellyfin.example.com"; a trailing slash or /web goes.</summary>
    public static string Normalize(string url)
    {
        url = url.Trim();
        if (!url.Contains("://")) url = "https://" + url;
        url = url.TrimEnd('/');
        if (url.EndsWith("/web", StringComparison.OrdinalIgnoreCase)) url = url[..^4];
        return url;
    }

    /// <summary>Jellyfin's own header: who the client is, and the token once signed in.</summary>
    public string AuthHeader =>
        $"MediaBrowser Client=\"{ClientName}\", Device=\"{Escape(Environment.MachineName)}\", DeviceId=\"{DeviceId}\", Version=\"{ClientVersion}\""
        + (Token is null ? "" : $", Token=\"{Token}\"");

    private static string Escape(string s) => s.Replace("\"", "");

    public Task<JsonNode> PublicInfoAsync(CancellationToken ct = default) => SendAsync(HttpMethod.Get, "/System/Info/Public", null, ct);

    public async Task<bool> QuickConnectEnabledAsync(CancellationToken ct = default)
    {
        try { return (bool?)await SendAsync(HttpMethod.Get, "/QuickConnect/Enabled", null, ct) ?? false; }
        catch (JellyfinException) { return false; }
    }

    public async Task<JsonNode> SignInAsync(string username, string password, CancellationToken ct = default)
    {
        var r = await SendAsync(HttpMethod.Post, "/Users/AuthenticateByName", new JsonObject { ["Username"] = username, ["Pw"] = password }, ct);
        Adopt(r);
        return r;
    }

    public Task<JsonNode> QuickConnectStartAsync(CancellationToken ct = default) => SendAsync(HttpMethod.Post, "/QuickConnect/Initiate", null, ct);

    public Task<JsonNode> QuickConnectStateAsync(string secret, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Get, $"/QuickConnect/Connect?secret={Uri.EscapeDataString(secret)}", null, ct);

    public async Task<JsonNode> QuickConnectFinishAsync(string secret, CancellationToken ct = default)
    {
        var r = await SendAsync(HttpMethod.Post, "/Users/AuthenticateWithQuickConnect", new JsonObject { ["Secret"] = secret }, ct);
        Adopt(r);
        return r;
    }

    private void Adopt(JsonNode auth)
    {
        Token = (string?)auth["AccessToken"];
        UserId = (string?)auth["User"]?["Id"];
    }

    public async Task SignOutAsync(CancellationToken ct = default)
    {
        try { await SendAsync(HttpMethod.Post, "/Sessions/Logout", null, ct); } catch (JellyfinException) { }
        Token = null;
        UserId = null;
    }

    public Task<JsonNode> MeAsync(CancellationToken ct = default) => SendAsync(HttpMethod.Get, "/Users/Me", null, ct);

    public Task<JsonNode> ViewsAsync(CancellationToken ct = default) =>
        GetWithFallbackAsync($"/UserViews?userId={UserId}", $"/Users/{UserId}/Views", ct);

    public Task<JsonNode> ResumeAsync(int limit, CancellationToken ct = default) =>
        GetWithFallbackAsync(
            $"/UserItems/Resume?userId={UserId}&limit={limit}&mediaTypes=Video&fields={Fields}&enableImageTypes=Primary,Backdrop,Thumb&imageTypeLimit=1",
            $"/Users/{UserId}/Items/Resume?limit={limit}&mediaTypes=Video&fields={Fields}",
            ct);

    public Task<JsonNode> NextUpAsync(int limit, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Get, $"/Shows/NextUp?userId={UserId}&limit={limit}&fields={Fields}&enableResumable=false&enableRewatching=false", null, ct);

    public Task<JsonNode> LatestAsync(string parentId, int limit, CancellationToken ct = default) =>
        GetWithFallbackAsync(
            $"/Items/Latest?userId={UserId}&parentId={parentId}&limit={limit}&fields={Fields}&groupItems=true",
            $"/Users/{UserId}/Items/Latest?parentId={parentId}&limit={limit}&fields={Fields}",
            ct);

    public Task<JsonNode> ItemsAsync(string parentId, int start, int limit, string sortBy, CancellationToken ct = default)
    {
        var q = $"parentId={parentId}&startIndex={start}&limit={limit}&sortBy={sortBy}&sortOrder={(sortBy == "SortName" ? "Ascending" : "Descending")}&recursive=true&includeItemTypes=Movie,Series,Video,MusicVideo&fields={Fields}&enableTotalRecordCount=true";
        return GetWithFallbackAsync($"/Items?userId={UserId}&{q}", $"/Users/{UserId}/Items?{q}", ct);
    }

    /// <summary>
    /// Titles matching what is typed, with only what the search list draws: on a large library the full fields
    /// (each show's episode counts among them) and the count of every match took the server 3 to 13 seconds.
    /// </summary>
    public Task<JsonNode> SearchAsync(string term, int limit, CancellationToken ct = default)
    {
        var q = $"searchTerm={Uri.EscapeDataString(term)}&limit={limit}&recursive=true&includeItemTypes=Movie,Series,Episode"
            + "&enableTotalRecordCount=false&enableUserData=false&enableImageTypes=Primary&imageTypeLimit=1";
        return GetWithFallbackAsync($"/Items?userId={UserId}&{q}", $"/Users/{UserId}/Items?{q}", ct);
    }

    public Task<JsonNode> ItemAsync(string id, CancellationToken ct = default) =>
        GetWithFallbackAsync($"/Items/{id}?userId={UserId}", $"/Users/{UserId}/Items/{id}", ct);

    public Task<JsonNode> SeasonsAsync(string seriesId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Get, $"/Shows/{seriesId}/Seasons?userId={UserId}&fields={Fields}", null, ct);

    /// <summary>Jellyfin's intro and credits marks for an item (10.10 and later; an older server answers 404).</summary>
    public Task<JsonNode> SegmentsAsync(string itemId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Get, $"/MediaSegments/{itemId}?includeSegmentTypes=Intro&includeSegmentTypes=Outro", null, ct);

    /// <summary>An episode and the ones after it in the show's order.</summary>
    public Task<JsonNode> EpisodesFromAsync(string seriesId, string startItemId, int limit, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Get, $"/Shows/{seriesId}/Episodes?userId={UserId}&startItemId={startItemId}&limit={limit}&fields={Fields}", null, ct);

    public Task<JsonNode> EpisodesAsync(string seriesId, string seasonId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Get, $"/Shows/{seriesId}/Episodes?userId={UserId}&seasonId={seasonId}&fields={Fields}", null, ct);

    /// <summary>
    /// Asks the server how to play an item. mpv plays nearly every container and codec, so the
    /// profile says so and the server answers with direct play; it transcodes only what it must.
    /// </summary>
    /// <param name="throughServer">The server reads the source and repackages it (streams copied, not re-encoded)
    /// instead of this PC opening the file: for sources whose links only work from the server's own connection.</param>
    public Task<JsonNode> PlaybackInfoAsync(string itemId, long startTicks, bool throughServer = false, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["UserId"] = UserId,
            ["StartTimeTicks"] = startTicks,
            // Never: the server would open the source first, and links made for the first connection that opens
            // them (add-on streams, some IPTV) would then fail for this PC.
            ["AutoOpenLiveStream"] = false,
            ["EnableDirectPlay"] = !throughServer,
            ["EnableDirectStream"] = !throughServer,
            ["AllowVideoStreamCopy"] = true,
            ["AllowAudioStreamCopy"] = true,
            ["EnableTranscoding"] = true,
            ["DeviceProfile"] = new JsonObject
            {
                ["Name"] = ClientName,
                ["MaxStreamingBitrate"] = 400_000_000,
                ["MaxStaticBitrate"] = 400_000_000,
                // Through the server, Exo says it plays no file as it is, so the server can only offer its own stream.
                ["DirectPlayProfiles"] = throughServer ? new JsonArray() : new JsonArray(new JsonObject { ["Type"] = "Video" }, new JsonObject { ["Type"] = "Audio" }),
                ["TranscodingProfiles"] = new JsonArray(new JsonObject
                {
                    ["Type"] = "Video",
                    ["Container"] = "ts",
                    ["Protocol"] = "hls",
                    // mpv decodes all of these, so the server copies them as they are and re-encodes nothing.
                    ["VideoCodec"] = "hevc,h264,av1,vp9,mpeg2video",
                    ["AudioCodec"] = "aac,ac3,eac3,mp3,opus,flac",
                    ["Context"] = "Streaming",
                }),
                ["SubtitleProfiles"] = new JsonArray(
                    new JsonObject { ["Format"] = "srt", ["Method"] = "External" },
                    new JsonObject { ["Format"] = "ass", ["Method"] = "External" },
                    new JsonObject { ["Format"] = "vtt", ["Method"] = "External" },
                    new JsonObject { ["Format"] = "srt", ["Method"] = "Embed" },
                    new JsonObject { ["Format"] = "ass", ["Method"] = "Embed" },
                    new JsonObject { ["Format"] = "pgssub", ["Method"] = "Embed" },
                    new JsonObject { ["Format"] = "dvdsub", ["Method"] = "Embed" }),
            },
        };
        var q = throughServer ? "&enableDirectPlay=false&enableDirectStream=false" : "";
        return SendAsync(HttpMethod.Post, $"/Items/{itemId}/PlaybackInfo?userId={UserId}{q}", body, ct);
    }

    /// <summary>The URL mpv opens. The token travels in mpv's Authorization header, never in the URL.</summary>
    public string StreamUrl(string itemId, JsonNode source)
    {
        if ((bool?)source["SupportsDirectPlay"] == true || (bool?)source["SupportsDirectStream"] == true)
        {
            // A source at another address (Protocol Http, IsRemote) is opened by this PC at that address, as Jellyfin's
            // own clients do, so a link made for the first connection that opens it is made for this one.
            if ((bool?)source["IsRemote"] == true && (string?)source["Protocol"] == "Http"
                && Uri.TryCreate((string?)source["Path"], UriKind.Absolute, out var remote) && remote.Scheme is "https" or "http")
                return remote.AbsoluteUri;
            return $"{Server}/Videos/{itemId}/stream?static=true&mediaSourceId={Uri.EscapeDataString((string)source["Id"]!)}";
        }
        var transcode = (string?)source["TranscodingUrl"] ?? throw new JellyfinException("The server can't play this item.");
        return Server + transcode;
    }

    /// <summary>
    /// Looks at where a remote stream URL would send the player, without following the redirect.
    /// Some hosts mint a link tied to the connection that asked for it and answer anyone else with
    /// a notice video; when this URL does, the notice's own words come back, else null.
    /// </summary>
    public async Task<string?> RemoteNoticeAsync(string url, CancellationToken ct = default) => (await ProbeRemoteAsync(url, ct)).Notice;

    /// <summary>
    /// One look at a remote link: the notice it plays when the host refuses it, or the address it redirects to, which
    /// the player can open directly instead of asking the redirect again on every request.
    /// </summary>
    public async Task<(string? Notice, string? Target)> ProbeRemoteAsync(string url, CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, url);
            using var res = await Head.SendAsync(req, ct);
            if (res.Headers.Location is not { IsAbsoluteUri: true } loc) return (null, null);
            if (!loc.Host.StartsWith("slate.", StringComparison.OrdinalIgnoreCase))
                return (null, loc.Scheme is "https" or "http" ? loc.AbsoluteUri : null);
            foreach (var pair in loc.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                if (kv[0] == "body" && kv.Length == 2)
                    return (string.Join(' ', Uri.UnescapeDataString(kv[1].Replace('+', ' ')).Split(null as char[], StringSplitOptions.RemoveEmptyEntries)), null);
            }
            return ("The stream host tied this link to another connection. Open the title again to ask for a fresh one.", null);
        }
        catch { return (null, null); }
    }

    /// <summary>Whether a URL is on this server, so it may carry the sign-in token.</summary>
    public bool IsOwnUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && Uri.TryCreate(Server, UriKind.Absolute, out var s)
        && u.Scheme == s.Scheme && string.Equals(u.Host, s.Host, StringComparison.OrdinalIgnoreCase) && u.Port == s.Port;

    public Task ReportStartAsync(JsonObject body, CancellationToken ct = default) => SendAsync(HttpMethod.Post, "/Sessions/Playing", body, ct);
    public Task ReportProgressAsync(JsonObject body, CancellationToken ct = default) => SendAsync(HttpMethod.Post, "/Sessions/Playing/Progress", body, ct);
    public Task ReportStoppedAsync(JsonObject body, CancellationToken ct = default) => SendAsync(HttpMethod.Post, "/Sessions/Playing/Stopped", body, ct);

    public Task SetPlayedAsync(string itemId, bool played, CancellationToken ct = default) =>
        SendAsync(played ? HttpMethod.Post : HttpMethod.Delete, $"/UserPlayedItems/{itemId}?userId={UserId}", null, ct);

    private async Task<JsonNode> GetWithFallbackAsync(string modern, string legacy, CancellationToken ct)
    {
        try { return await SendAsync(HttpMethod.Get, modern, null, ct); }
        catch (JellyfinException e) when (e.Status == HttpStatusCode.NotFound) { return await SendAsync(HttpMethod.Get, legacy, null, ct); }
    }

    private async Task<JsonNode> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, Server + path);
        req.Headers.TryAddWithoutValidation("Authorization", AuthHeader);
        req.Headers.Accept.ParseAdd("application/json");
        if (body != null) req.Content = JsonContent.Create(body);
        HttpResponseMessage res;
        try
        {
            res = await Http.SendAsync(req, ct);
        }
        catch (HttpRequestException e)
        {
            throw new JellyfinException($"Can't reach {new Uri(Server).Host}: {e.HttpRequestError switch
            {
                HttpRequestError.NameResolutionError => "the address wasn't found",
                HttpRequestError.SecureConnectionError => "its certificate isn't trusted",
                HttpRequestError.ConnectionError => "it didn't answer",
                _ => e.Message,
            }}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new JellyfinException($"{new Uri(Server).Host} took too long to answer.");
        }
        using (res)
        {
            if (!res.IsSuccessStatusCode)
            {
                throw new JellyfinException(res.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => Token is null ? "Wrong username or password." : "Signed out by the server. Sign in again.",
                    HttpStatusCode.Forbidden => "This account isn't allowed to do that.",
                    HttpStatusCode.NotFound => "Not found on the server.",
                    _ => $"The server answered {(int)res.StatusCode}.",
                }, res.StatusCode);
            }
            var text = await res.Content.ReadAsStringAsync(ct);
            return string.IsNullOrWhiteSpace(text) ? new JsonObject() : JsonNode.Parse(text) ?? new JsonObject();
        }
    }
}

internal sealed class JellyfinException(string message, HttpStatusCode? status = null) : Exception(message)
{
    public HttpStatusCode? Status { get; } = status;
}
