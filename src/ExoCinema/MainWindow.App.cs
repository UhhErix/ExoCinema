using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace ExoCinema;

/// <summary>Exo Cinema itself, for the page: the first run, What's New, updates and its own links.</summary>
public sealed partial class MainWindow
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly AppUpdater _updater = new();
    private bool _installing;

    // What the page may open: fixed here, never an address it supplies.
    private static readonly Dictionary<string, string> Links = new()
    {
        ["source"] = $"https://github.com/{AppUpdater.Repo}",
        ["issues"] = $"https://github.com/{AppUpdater.Repo}/issues",
        ["privacy"] = $"https://github.com/{AppUpdater.Repo}/blob/main/PRIVACY.md",
        ["notices"] = $"https://github.com/{AppUpdater.Repo}/blob/main/THIRD_PARTY_NOTICES.md",
    };

    private async Task AnswerAppAsync(JsonNode msg)
    {
        var id = (int)msg["id"]!;
        try
        {
            var data = await AppAsync((string)msg["op"]!, msg["args"]);
            PostJson($"{{\"type\":\"reply\",\"id\":{id},\"ok\":true,\"data\":{data?.ToJsonString() ?? "null"}}}");
        }
        catch (Exception e)
        {
            _log.Write($"app {(string?)msg["op"]}: {e}");
            Post(new { type = "reply", id, ok = false, error = Plain(e) });
        }
    }

    private async Task<JsonNode?> AppAsync(string op, JsonNode? args)
    {
        switch (op)
        {
            case "settings":
                return Info();
            case "set":
                if (args?["onboarded"] is { } onboarded) _settings.Onboarded = (bool)onboarded;
                if (args?["whatsNewSeen"] is { } seen) _settings.WhatsNewSeen = (string?)seen;
                _settings.Save();
                return Info();
            case "check-update":
                await _updater.CheckAsync();
                return UpdateState();
            case "open-check":
                // The check made as the window opened: a newer version is offered now, at the open, never later.
                return _openCheck is not null && await _openCheck is not null ? UpdateState() : null;
            case "install-update":
                _ = InstallUpdateAsync();
                return null;
            case "logs":
                // Copy logs, beside an update that failed: the end of app.log, which holds no address, key or title.
                return new JsonObject { ["text"] = _log.Tail(200) };
            case "open":
                if (Links.TryGetValue((string?)args?["link"] ?? "", out var url)) await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
                return null;
        }
        throw new InvalidOperationException("Unknown request: " + op);
    }

    private JsonObject Info() => new()
    {
        ["version"] = AppInfo.Version,
        ["onboarded"] = _settings.Onboarded,
        ["whatsNewSeen"] = _settings.WhatsNewSeen,
        ["update"] = UpdateState(),
    };

    private JsonObject UpdateState() => new()
    {
        ["latest"] = _updater.Latest,
        ["notes"] = _updater.Notes?.DeepClone(),
        ["installable"] = AppInfo.Installed,
        // The update sheet at open: the installed copy, and test copies (their own data), which download and check
        // the update but never run its Setup.
        ["offer"] = AppInfo.Installed || AppInfo.OwnData,
        // Update now already started installing this version and Exo Cinema opened on the old one again: the sheet
        // offers Not now from the start, so it can never hold anyone in a loop of failed restarts.
        ["retry"] = _updater.Latest is not null && _settings.UpdateTried == _updater.Latest,
    };

    // Updates are looked for once, as the window opens, and offered then (the user's rule): a release that comes out
    // while Exo Cinema is open waits for the next open, so nothing ever interrupts a film. Offline finds nothing and
    // shows nothing. Settings › About can still check by hand.
    private Task<string?>? _openCheck;

    private async Task<string?> CheckOnOpenAsync()
    {
        AppUpdater.CleanUp();
        // An update that took: this version is the one tried, or newer.
        if (_settings.UpdateTried is { } tried && (!System.Version.TryParse(tried, out var t) || t <= System.Version.Parse(AppInfo.Version)))
        {
            _settings.UpdateTried = null;
            _settings.Save();
        }
        try { return await _updater.CheckAsync(); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidDataException) { return null; }
    }

    private async Task InstallUpdateAsync()
    {
        if (_installing) return;
        _installing = true;
        try
        {
            if (!AppInfo.Installed && !AppInfo.OwnData) throw new InvalidOperationException(NotInstalled);
            if (_updater.Latest is null && await _updater.CheckAsync() is null) throw new InvalidOperationException("Exo Cinema is up to date.");
            var shown = -1;
            var setup = await _updater.DownloadAsync(p =>
            {
                var percent = (int)(p * 100);
                if (percent == shown) return;
                shown = percent;
                OnUi(() => Post(new { type = "update", state = "downloading", percent }));
            });
            // A test copy stops here, with the update downloaded and checked: its Setup would replace the installed app.
            if (!AppInfo.Installed) throw new InvalidOperationException(NotInstalled);
            OnUi(() => Post(new { type = "update", state = "installing" }));
            _settings.UpdateTried = _updater.Latest;
            _settings.Save();
            AppUpdater.Run(setup);
            // Setup closes Exo Cinema too, but closing first the usual way tells Jellyfin where playback stopped.
            await Task.Delay(350);
            PostMessageW(_hwnd, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception e)
        {
            _log.Write($"update: {e}");
            OnUi(() => Post(new { type = "update", state = "failed", message = Plain(e) }));
        }
        finally
        {
            _installing = false;
        }
    }

    private const string NotInstalled = "This copy of Exo Cinema wasn't installed by Setup, so it can't update itself.";

    private static string Plain(Exception e) => e switch
    {
        HttpRequestException { StatusCode: null } => "The update couldn't be reached. Check your connection and try again.",
        HttpRequestException or InvalidDataException or InvalidOperationException => e.Message,
        TaskCanceledException => "The update took too long to answer. Try again.",
        _ => "Something went wrong: " + e.Message,
    };

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
