using System.Text.Json.Nodes;
using ExoCinema.Stremio;

namespace ExoCinema.Jellyfin;

/// <summary>What Jellyfin needs from the player.</summary>
internal interface IPlayerSurface
{
    void Play(string url, string? authHeader, double startSeconds, string? title);
    void Stop();
}

/// <summary>
/// The page's Jellyfin calls (sign-in, Home, library, play) and the playback reports that keep the
/// server, and so every other Jellyfin app, at the same place: start, progress every 10 s and on
/// pause and seek, and stopped.
/// </summary>
internal sealed class JellyfinHost(IPlayerSurface player)
{
    private JellyfinClient? _client;
    private Playback? _now;
    private System.Threading.Timer? _progressTimer;

    private sealed class Playback
    {
        public required string ItemId;
        public required string MediaSourceId;
        public required string? PlaySessionId;
        public required string PlayMethod;
        public double Position;
        // A transcode's clock starts at 0 where the server began, not at the file's start.
        public double Offset;
        public bool Paused;
        public bool Started;
    }

    public async Task<JsonNode?> HandleAsync(string op, JsonNode? args)
    {
        switch (op)
        {
            case "session":
                return await RestoreAsync();
            case "probe":
                {
                    var c = new JellyfinClient((string)args!["server"]!, AccountStore.DeviceId);
                    var info = await c.PublicInfoAsync();
                    return new JsonObject
                    {
                        ["server"] = c.Server,
                        ["name"] = (string?)info["ServerName"],
                        ["version"] = (string?)info["Version"],
                        ["quickConnect"] = await c.QuickConnectEnabledAsync(),
                    };
                }
            case "sign-in":
                {
                    var c = new JellyfinClient((string)args!["server"]!, AccountStore.DeviceId);
                    var info = await c.PublicInfoAsync();
                    var auth = await c.SignInAsync((string)args["username"]!, (string?)args["password"] ?? "");
                    return Adopt(c, info, auth);
                }
            case "quick-connect-start":
                {
                    var c = new JellyfinClient((string)args!["server"]!, AccountStore.DeviceId);
                    return await c.QuickConnectStartAsync();
                }
            case "quick-connect-poll":
                {
                    var c = new JellyfinClient((string)args!["server"]!, AccountStore.DeviceId);
                    var secret = (string)args["secret"]!;
                    var state = await c.QuickConnectStateAsync(secret);
                    if ((bool?)state["Authenticated"] != true) return new JsonObject { ["done"] = false };
                    var info = await c.PublicInfoAsync();
                    return Adopt(c, info, await c.QuickConnectFinishAsync(secret));
                }
            case "sign-out":
                if (_client is { } signedIn)
                {
                    await StopReportingAsync();
                    AccountStore.Forget(signedIn.Server, signedIn.UserId!);
                    await signedIn.SignOutAsync();
                    _client = null;
                }
                return null;
        }

        var client = _client ?? throw new JellyfinException("Not signed in.");
        return op switch
        {
            "home" => await HomeAsync(client),
            "home-top" => await HomeTopAsync(client),
            "latest" => await client.LatestAsync((string)args!["viewId"]!, 16),
            "items" => await client.ItemsAsync((string)args!["parentId"]!, (int?)args["start"] ?? 0, (int?)args["limit"] ?? 100, (string?)args["sortBy"] ?? "SortName"),
            "item" => await client.ItemAsync((string)args!["id"]!),
            "seasons" => await client.SeasonsAsync((string)args!["seriesId"]!),
            "episodes" => await client.EpisodesAsync((string)args!["seriesId"]!, (string)args["seasonId"]!),
            "search" => await SearchAsync(client, (string)args!["term"]!),
            "played" => await SetPlayedAsync(client, (string)args!["id"]!, (bool)args["played"]!),
            "play" => await PlayAsync(client, (string)args!["id"]!, (bool?)args["resume"] ?? true, (string?)args["addon"], (string?)args["source"]),
            "sources" => await SourcesAsync(client, (string)args!["id"]!, (string?)args["addon"]),
            "stop" => await StopAsync(),
            "segments" => await SegmentsAsync(client, (string)args!["id"]!),
            "next" => await NextEpisodeAsync(client, (string)args!["id"]!),
            "cache-size" => new JsonObject { ["bytes"] = ImageCache.SizeOnDisk() },
            "cache-clear" => new JsonObject { ["bytes"] = ImageCache.Clear() },
            "playback-info" => Diagnose(await client.PlaybackInfoAsync((string)args!["id"]!, 0, (bool?)args["throughServer"] ?? false)),
            _ => throw new JellyfinException($"Unknown call {op}."),
        };
    }

    private async Task<JsonNode?> RestoreAsync()
    {
        if (AccountStore.Current() is not { } saved) return null;
        var c = new JellyfinClient(saved.Account.Server, AccountStore.DeviceId, saved.Token, saved.Account.UserId);
        _client = c;
        try
        {
            var me = await c.MeAsync();
            return Describe(c, saved.Account.ServerName, (string?)me["Name"] ?? saved.Account.UserName, imageTag: (string?)me["PrimaryImageTag"]);
        }
        catch (JellyfinException e) when (e.Status is System.Net.HttpStatusCode.Unauthorized)
        {
            AccountStore.Forget(c.Server, c.UserId!);
            _client = null;
            return null;
        }
        catch (JellyfinException)
        {
            // Offline or the server is down: stay signed in and say so.
            return Describe(c, saved.Account.ServerName, saved.Account.UserName, offline: true);
        }
    }

    private JsonNode Adopt(JellyfinClient c, JsonNode info, JsonNode auth)
    {
        var serverName = (string?)info["ServerName"] ?? new Uri(c.Server).Host;
        var userName = (string?)auth["User"]?["Name"] ?? "";
        AccountStore.Save(c.Server, serverName, c.UserId!, userName, c.Token!);
        _client = c;
        return Describe(c, serverName, userName, imageTag: (string?)auth["User"]?["PrimaryImageTag"]);
    }

    private static JsonObject Describe(JellyfinClient c, string serverName, string userName, bool offline = false, string? imageTag = null) => new()
    {
        ["imageTag"] = imageTag,
        ["server"] = c.Server,
        ["serverName"] = serverName,
        ["userId"] = c.UserId,
        ["userName"] = userName,
        ["offline"] = offline,
    };

    /// <summary>
    /// Home's first part: the libraries, Continue Watching and Next Up. The page draws these as soon as they come
    /// and asks for each library's newest row by itself ("latest"), so one slow list never holds up the rest.
    /// </summary>
    private static async Task<JsonNode> HomeTopAsync(JellyfinClient c)
    {
        var views = c.ViewsAsync();
        var resume = c.ResumeAsync(16);
        var nextUp = c.NextUpAsync(16);
        await Task.WhenAll(views, resume, nextUp);
        return new JsonObject
        {
            ["resume"] = resume.Result["Items"]?.DeepClone(),
            ["nextUp"] = nextUp.Result["Items"]?.DeepClone(),
            ["views"] = views.Result["Items"]?.DeepClone(),
        };
    }

    private static async Task<JsonNode> HomeAsync(JellyfinClient c)
    {
        var views = c.ViewsAsync();
        var resume = c.ResumeAsync(16);
        var nextUp = c.NextUpAsync(16);
        await Task.WhenAll(views, resume, nextUp);

        var rows = new JsonArray();
        var latest = new List<(string Name, string Id, Task<JsonNode> Items)>();
        foreach (var v in (views.Result["Items"] as JsonArray ?? []))
        {
            var type = (string?)v!["CollectionType"];
            if (type is "movies" or "tvshows" or "homevideos" or "musicvideos" or null)
                latest.Add(((string)v["Name"]!, (string)v["Id"]!, c.LatestAsync((string)v["Id"]!, 16)));
        }
        await Task.WhenAll(latest.Select(l => l.Items));
        foreach (var l in latest)
            rows.Add(new JsonObject { ["kind"] = "latest", ["title"] = $"New in {l.Name}", ["viewId"] = l.Id, ["items"] = l.Items.Result.DeepClone() });

        return new JsonObject
        {
            ["resume"] = resume.Result["Items"]?.DeepClone(),
            ["nextUp"] = nextUp.Result["Items"]?.DeepClone(),
            ["views"] = views.Result["Items"]?.DeepClone(),
            ["latest"] = rows,
        };
    }

    private static async Task<JsonNode?> SetPlayedAsync(JellyfinClient c, string id, bool played)
    {
        await c.SetPlayedAsync(id, played);
        return await c.ItemAsync(id);
    }

    private int _playGeneration;

    private async Task<JsonNode?> PlayAsync(JellyfinClient c, string id, bool resume, string? addonUrl, string? sourceId)
    {
        // Stop (Back) during the lookups below cancels this play: it must not start behind the library.
        var generation = ++_playGeneration;
        await StopReportingAsync();
        var item = await c.ItemAsync(id);
        // An episode carries little of its show's own (its genres, where it was made): those come from the series, read
        // alongside, for the player to tell anime (playerplus.js).
        var series = (string?)item["Type"] == "Episode" && (string?)item["SeriesId"] is { } seriesId ? SeriesAsync(c, seriesId) : Task.FromResult<JsonNode?>(null);
        var startTicks = resume ? (long?)item["UserData"]?["PlaybackPositionTicks"] ?? 0 : 0;
        var info = await c.PlaybackInfoAsync(id, startTicks);
        // The version picked in the source sheet, or the best one for this PC.
        var sources = (info["MediaSources"] as JsonArray ?? []).OfType<JsonNode>().ToList();
        // A stream of the add-on's picked in the sheet ("addon:" and its place in the list) stands for the one source.
        var pickedStream = sourceId?.StartsWith("addon:") == true && int.TryParse(sourceId[6..], out var n) ? n : (int?)null;
        var source = sources.FirstOrDefault(s => sourceId != null && (string?)s["Id"] == sourceId) ?? Ranked(sources).FirstOrDefault()
            ?? throw new JellyfinException("The server has no file for this item.");
        var direct = (bool?)source["SupportsDirectPlay"] == true || (bool?)source["SupportsDirectStream"] == true;
        var url = direct ? c.StreamUrl(id, source) : c.Server + (string)source["TranscodingUrl"]!;
        if (direct && (bool?)source["IsRemote"] == true && (string?)source["Protocol"] == "Http")
        {
            // The server's stored link was minted on its own connection; a stream add-on answers with
            // one made for this PC, which a requester-tied host accepts. Try it before the stored link.
            if (await AddonUrlAsync(c, item, addonUrl, pickedStream) is { } own) url = own;
            else
            {
                // A refused remote link plays back as a short notice video, which mpv would treat as the show. Peek first.
                var probe = await c.ProbeRemoteAsync(url);
                if (probe.Notice is { } notice)
                {
                    Log?.Invoke($"remote stream refused: {notice}");
                    throw new JellyfinException(notice);
                }
                // The player opens where the link leads, not the redirect, which it would otherwise ask again per request.
                if (probe.Target is { } target) url = target;
            }
        }
        // A remote address can carry account tokens in its path; only its host belongs in the log.
        var where = c.IsOwnUrl(url) ? new Uri(url).GetLeftPart(UriPartial.Path) : new Uri(url).GetLeftPart(UriPartial.Authority);
        Log?.Invoke($"play: protocol {(string?)source["Protocol"]}, direct {direct}, {where}");

        if (generation != _playGeneration) return null;

        // Subtitle files beside the video (an .srt or .ass the server holds) are added once mpv has opened it.
        _pendingSubtitles = ((source["MediaStreams"] as JsonArray) ?? [])
            .Where(s => (string?)s?["Type"] == "Subtitle" && (bool?)s["IsExternal"] == true && (string?)s["DeliveryUrl"] is { Length: > 0 })
            .Select(s => (Url: c.Server + (string)s!["DeliveryUrl"]!, Title: (string?)s["DisplayTitle"] ?? (string?)s["Title"] ?? "", Lang: (string?)s["Language"] ?? ""))
            .ToList();

        _now = new Playback
        {
            ItemId = id,
            MediaSourceId = (string)source["Id"]!,
            PlaySessionId = (string?)info["PlaySessionId"],
            PlayMethod = direct ? "DirectPlay" : (bool?)source["SupportsDirectStream"] == true ? "DirectStream" : "Transcode",
            Position = startTicks / 1e7,
            Offset = direct ? 0 : startTicks / 1e7,
        };
        // A transcode already starts at StartTimeTicks; only direct play seeks in mpv.
        // The token goes only to this server, never to another address a source points at.
        player.Play(url, c.IsOwnUrl(url) ? c.AuthHeader : null, direct ? startTicks / 1e7 : 0, Title(item));
        var shown = item.DeepClone();
        if (await series is { } show)
        {
            shown["SeriesGenres"] = show["Genres"]?.DeepClone();
            shown["SeriesProductionLocations"] = show["ProductionLocations"]?.DeepClone();
            shown["SeriesOriginalTitle"] = show["OriginalTitle"]?.DeepClone();
        }
        return new JsonObject { ["item"] = shown, ["playMethod"] = _now.PlayMethod, ["start"] = startTicks / 1e7, ["mediaSourceId"] = _now.MediaSourceId };
    }

    private static async Task<JsonNode?> SeriesAsync(JellyfinClient c, string seriesId)
    {
        try { return await c.ItemAsync(seriesId); }
        catch (JellyfinException) { return null; }
    }

    /// <summary>
    /// The add-on's own link for this item — made for this PC's connection, unlike the server's stored copy: the one
    /// picked in the source sheet, else the best for this PC (StreamChoice.Rank), else the next best, the first one
    /// that is not refused. Null without an add-on or a usable answer.
    /// </summary>
    private async Task<string?> AddonUrlAsync(JellyfinClient c, JsonNode item, string? addonUrl, int? picked)
    {
        try
        {
            if (await AddonChoicesAsync(c, item, addonUrl) is not { Count: > 0 } choices) return null;
            if (picked is { } i && i >= 0 && i < choices.Count) choices = [choices[i], .. choices.Where((_, k) => k != i)];
            foreach (var s in choices)
                if (await c.ProbeRemoteAsync(s.Url) is { Notice: null } p)
                {
                    Log?.Invoke($"addon: {Describe(s)}");
                    return p.Target ?? s.Url;
                }
            Log?.Invoke("addon: every link was refused");
        }
        catch (Exception e) { Log?.Invoke($"addon: {e.Message}"); }
        return null;
    }

    // The add-on's streams for the item last asked about, best first, so the sheet's numbers mean the same at play.
    private (string ItemId, List<StreamChoice> Choices)? _addonChoices;

    private async Task<List<StreamChoice>?> AddonChoicesAsync(JellyfinClient c, JsonNode item, string? addonUrl)
    {
        var id = (string?)item["Id"] ?? "";
        if (_addonChoices is { } kept && kept.ItemId == id) return kept.Choices;
        if (AddonClient.From(addonUrl) is not { } addon || await StreamIdAsync(c, item) is not { } sid) return null;
        var list = await addon.StreamListAsync(sid.Type, sid.Id);
        var seconds = ((long?)item["RunTimeTicks"] ?? 0) / 1e7;
        var ranked = StreamChoice.Rank(list.Select(StreamChoice.From), seconds, LinkSpeed.Mbps, PlayerTuning.Strong);
        _addonChoices = (id, ranked);
        return ranked;
    }

    // What was picked, for app.log: the stream's qualities, never its address.
    private static string Describe(StreamChoice s) =>
        string.Join(" ", new[] { s.Height > 0 ? $"{s.Height}p" : "?p", s.Range, s.Codec, s.Source, s.Audio, s.Size is { } b ? $"{b / 1e9:0.0} GB" : null }.Where(x => x != null));

    /// <summary>The stream-catalog id for this item: a movie's imdb, or a series' imdb with season and episode.</summary>
    private static async Task<(string Type, string Id)?> StreamIdAsync(JellyfinClient c, JsonNode item)
    {
        var imdb = (string?)item["ProviderIds"]?["Imdb"];
        switch ((string?)item["Type"])
        {
            case "Movie" when imdb != null:
                return ("movie", imdb);
            case "Episode":
                // The stream id is the series' imdb with season and episode; the series item carries it.
                if ((string?)item["SeriesId"] is { } seriesId)
                    try { imdb = (string?)(await c.ItemAsync(seriesId))["ProviderIds"]?["Imdb"] ?? imdb; } catch { }
                if (imdb != null && (int?)item["ParentIndexNumber"] is { } s && (int?)item["IndexNumber"] is { } e)
                    return ("series", $"{imdb}:{s}:{e}");
                return null;
            default:
                return null;
        }
    }

    /// <summary>The server's playback answer for a dev check, with every token and signature removed.</summary>
    private static JsonNode Diagnose(JsonNode info)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(info.ToJsonString(),
            "(?i)((api_?key|token|sig|signature|playsessionid|liveStreamId|e?xp(ires)?)=)[^&\"]+", "$1…");
        var clean = JsonNode.Parse(text)!;
        foreach (var m in clean["MediaSources"] as JsonArray ?? [])
        {
            m!.AsObject().Remove("MediaStreams");
            m.AsObject().Remove("MediaAttachments");
        }
        return clean;
    }

    private static string Title(JsonNode item) =>
        (string?)item["Type"] == "Episode"
            ? $"{(string?)item["SeriesName"]} · S{(int?)item["ParentIndexNumber"]}:E{(int?)item["IndexNumber"]} {(string?)item["Name"]}"
            : (string?)item["Name"] ?? "";

    private List<(string Url, string Title, string Lang)> _pendingSubtitles = [];

    /// <summary>The current file's external subtitles, once: the player adds them when the file has opened.</summary>
    public List<(string Url, string Title, string Lang)> TakePendingSubtitles()
    {
        var subs = _pendingSubtitles;
        _pendingSubtitles = [];
        return subs;
    }

    private static async Task<JsonNode> SegmentsAsync(JellyfinClient c, string id)
    {
        try { return (await c.SegmentsAsync(id))["Items"]?.DeepClone() ?? new JsonArray(); }
        catch (JellyfinException) { return new JsonArray(); } // a server without segments
    }

    /// <summary>The episode after this one, or null for a movie or a show's last episode.</summary>
    private static async Task<JsonNode?> NextEpisodeAsync(JellyfinClient c, string id)
    {
        var item = await c.ItemAsync(id);
        if ((string?)item["Type"] != "Episode" || (string?)item["SeriesId"] is not { } series) return null;
        var list = (await c.EpisodesFromAsync(series, id, 2))["Items"] as JsonArray;
        return list is { Count: > 1 } ? list[1]!.DeepClone() : null;
    }

    /// <summary>
    /// An item's versions, best for this PC first, as the source sheet lists them. A title whose one version is a stream
    /// add-on's link lists the add-on's streams instead: those are its real versions.
    /// </summary>
    private async Task<JsonNode> SourcesAsync(JellyfinClient c, string id, string? addonUrl)
    {
        var info = await c.PlaybackInfoAsync(id, 0);
        var sources = (info["MediaSources"] as JsonArray ?? []).OfType<JsonNode>().ToList();
        if (sources is [var only] && (bool?)only["IsRemote"] == true && (string?)only["Protocol"] == "Http" && addonUrl != null)
        {
            _addonChoices = null;
            try
            {
                var item = await c.ItemAsync(id);
                if (await AddonChoicesAsync(c, item, addonUrl) is { Count: > 1 } choices)
                {
                    var seconds = ((long?)item["RunTimeTicks"] ?? 0) / 1e7;
                    return new JsonArray(choices.Select((s, i) => (JsonNode)new JsonObject
                    {
                        ["id"] = $"addon:{i}",
                        ["name"] = s.Label,
                        ["best"] = i == 0,
                        ["direct"] = true,
                        ["size"] = s.Size,
                        ["bitrate"] = s.Mbps(seconds) is { } mbps ? (long)(mbps * 1e6) : null,
                        ["height"] = s.Height > 0 ? s.Height : null,
                        ["videoCodec"] = s.Codec,
                        ["range"] = s.Range switch { "Dolby Vision" => "DOVI", "HDR10+" => "HDR10Plus", "HDR10" => "HDR10", _ => null },
                        ["audioCodec"] = s.Audio switch { "Atmos" => "atmos", "TrueHD" => "truehd", "DTS-HD" => "dtshd", "DTS" => "dts", "E-AC-3" => "eac3", "AC-3" => "ac3", "AAC" => "aac", _ => null },
                        ["channels"] = s.Channels > 0 ? s.Channels : null,
                        ["origin"] = s.Source,
                        ["notReady"] = s.Cached == false,
                    }).ToArray());
                }
            }
            catch (Exception e) { Log?.Invoke($"addon: {e.Message}"); }
        }
        return new JsonArray(Ranked(sources).Select((s, i) => (JsonNode)Describe(s, i == 0)).ToArray());
    }

    // Best first: what plays straight from the server before what it would convert, then the most detailed picture
    // this PC is built for (above 1080p only on a dedicated graphics card), then the highest bitrate.
    private static IEnumerable<JsonNode> Ranked(List<JsonNode> sources)
    {
        static JsonNode? Video(JsonNode s) => (s["MediaStreams"] as JsonArray)?.FirstOrDefault(m => (string?)m?["Type"] == "Video");
        var cap = PlayerTuning.Strong ? int.MaxValue : 1080;
        return sources
            .OrderByDescending(s => (bool?)s["SupportsDirectPlay"] == true || (bool?)s["SupportsDirectStream"] == true)
            .ThenByDescending(s => ((int?)Video(s)?["Height"] ?? 0) is var h && h <= cap ? h : -h)
            .ThenByDescending(s => (long?)s["Bitrate"] ?? 0);
    }

    private static JsonObject Describe(JsonNode s, bool best)
    {
        var streams = (s["MediaStreams"] as JsonArray ?? []).OfType<JsonNode>().ToList();
        var video = streams.FirstOrDefault(m => (string?)m["Type"] == "Video");
        var audio = streams.FirstOrDefault(m => (string?)m["Type"] == "Audio" && (bool?)m["IsDefault"] == true)
            ?? streams.FirstOrDefault(m => (string?)m["Type"] == "Audio");
        return new JsonObject
        {
            ["id"] = (string?)s["Id"],
            ["name"] = (string?)s["Name"],
            ["best"] = best,
            ["direct"] = (bool?)s["SupportsDirectPlay"] == true || (bool?)s["SupportsDirectStream"] == true,
            ["size"] = (long?)s["Size"],
            ["bitrate"] = (long?)s["Bitrate"],
            ["height"] = (int?)video?["Height"],
            ["videoCodec"] = (string?)video?["Codec"],
            ["range"] = (string?)video?["VideoRangeType"] ?? (string?)video?["VideoRange"],
            ["audioCodec"] = (string?)audio?["Codec"],
            ["channels"] = (int?)audio?["Channels"],
            ["language"] = (string?)audio?["Language"],
        };
    }

    private CancellationTokenSource? _searching;

    // Only the latest search is worth the server's time: one typed past is called off (the page drops its answer).
    private async Task<JsonNode?> SearchAsync(JellyfinClient c, string term)
    {
        using var mine = new CancellationTokenSource();
        Interlocked.Exchange(ref _searching, mine)?.Cancel();
        try { return await c.SearchAsync(term, 20, mine.Token); }
        catch (OperationCanceledException) when (mine.IsCancellationRequested) { return null; }
        finally { Interlocked.CompareExchange(ref _searching, null, mine); }
    }

    private async Task<JsonNode?> StopAsync()
    {
        _playGeneration++;
        await StopReportingAsync();
        player.Stop();
        return null;
    }

    // From the player. These arrive on the UI thread.

    public void OnPlaybackRestart()
    {
        if (_now is not { } p || _client is not { } c) return;
        if (!p.Started)
        {
            p.Started = true;
            _ = SafeAsync(c.ReportStartAsync(Report(p)));
            _progressTimer = new System.Threading.Timer(_ => { if (_now == p) _ = SafeAsync(c.ReportProgressAsync(Report(p))); }, null, 10_000, 10_000);
        }
        else
        {
            _ = SafeAsync(c.ReportProgressAsync(Report(p, "TimeUpdate")));
        }
    }

    public void OnTime(double seconds)
    {
        if (_now is { } p) p.Position = p.Offset + seconds;
    }

    public void OnPause(bool paused)
    {
        if (_now is not { } p || _client is not { } c || p.Paused == paused) return;
        p.Paused = paused;
        if (p.Started) _ = SafeAsync(c.ReportProgressAsync(Report(p, paused ? "Pause" : "Unpause")));
    }

    public void OnEnded() => _ = StopReportingAsync();

    public Task ShutdownAsync() => StopReportingAsync();

    private async Task StopReportingAsync()
    {
        var p = _now;
        _now = null;
        _progressTimer?.Dispose();
        _progressTimer = null;
        if (p is { Started: true } && _client is { } c) await SafeAsync(c.ReportStoppedAsync(Report(p)));
    }

    private static JsonObject Report(Playback p, string? eventName = null)
    {
        var o = new JsonObject
        {
            ["ItemId"] = p.ItemId,
            ["MediaSourceId"] = p.MediaSourceId,
            ["PlaySessionId"] = p.PlaySessionId,
            ["PositionTicks"] = (long)(p.Position * 1e7),
            ["IsPaused"] = p.Paused,
            ["IsMuted"] = false,
            ["PlayMethod"] = p.PlayMethod,
            ["CanSeek"] = true,
        };
        if (eventName != null) o["EventName"] = eventName;
        return o;
    }

    /// <summary>The signed-in server's address, or null when signed out.</summary>
    public string? Server => _client?.Server;

    /// <summary>The sign-in for a request to this server's scrub pictures (trickplay tiles); null for anything else.</summary>
    public string? TrickplayAuth(string url) =>
        _client is { } c && c.IsOwnUrl(url) && new Uri(url).AbsolutePath.Contains("/Trickplay/", StringComparison.OrdinalIgnoreCase) ? c.AuthHeader : null;

    public event Action<string>? ReportFailed;
    public event Action<string>? Log;

    private async Task SafeAsync(Task t)
    {
        try { await t; }
        catch (Exception e) { ReportFailed?.Invoke(e.Message); }
    }
}
