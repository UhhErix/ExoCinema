using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Web.WebView2.Core;

namespace ExoCinema.Jellyfin;

/// <summary>
/// Jellyfin's images, kept on disk at %LOCALAPPDATA%\ExoCinema\cache\images. Every image URL the page asks for
/// carries the image's tag, so a file for a URL never goes stale: the second time a cover is shown it comes from
/// disk at once, whatever the server's speed. Requests for the same image at once share one download.
/// </summary>
internal sealed class ImageCache
{
    private static readonly string Dir = Path.Combine(AppInfo.Data, "cache", "images");

    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        // Image servers can take seconds to answer each request (2 s seen); many at once overlap that wait.
        MaxConnectionsPerServer = 32,
        EnableMultipleHttp2Connections = true,
    })
    {
        // A slow server's first download of a big image can take most of a minute; once on disk it never waits again.
        Timeout = TimeSpan.FromMinutes(2),
        DefaultRequestVersion = HttpVersion.Version20,
        DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
    };

    private readonly ConcurrentDictionary<string, Task<byte[]?>> _inFlight = new();
    private readonly CoreWebView2Environment _env;

    public ImageCache(CoreWebView2 core, CoreWebView2Environment env)
    {
        _env = env;
        Directory.CreateDirectory(Dir);
        core.AddWebResourceRequestedFilter("*/Items/*/Images/*", CoreWebView2WebResourceContext.Image);
        core.WebResourceRequested += OnRequest;
    }

    private async void OnRequest(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var url = e.Request.Uri;
        // Only tagged images: an untagged URL can change under the same address.
        if (!url.Contains("tag=", StringComparison.OrdinalIgnoreCase)) return;
        var deferral = e.GetDeferral();
        try
        {
            var bytes = await GetAsync(url);
            if (bytes is null) return; // let the WebView fetch it itself
            var stream = new MemoryStream(bytes, writable: false).AsRandomAccessStream();
            e.Response = _env.CreateWebResourceResponse(stream, 200, "OK",
                $"Content-Type: {Sniff(bytes)}\r\nCache-Control: public, max-age=31536000, immutable\r\nAccess-Control-Allow-Origin: *");
        }
        catch
        {
            /* fall through to the network */
        }
        finally
        {
            deferral.Complete();
        }
    }

    internal Task<byte[]?> GetAsync(string url)
    {
        var path = Path.Combine(Dir, Key(url));
        if (File.Exists(path))
        {
            try { return Task.FromResult<byte[]?>(File.ReadAllBytes(path)); } catch (IOException) { }
        }
        return _inFlight.GetOrAdd(url, _ => DownloadAsync(url, path));
    }

    private async Task<byte[]?> DownloadAsync(string url, string path)
    {
        try
        {
            using var res = await Http.GetAsync(url);
            if (!res.IsSuccessStatusCode) return null;
            var bytes = await res.Content.ReadAsByteArrayAsync();
            var tmp = path + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes);
            File.Move(tmp, path, overwrite: true);
            return bytes;
        }
        catch
        {
            return null;
        }
        finally
        {
            _inFlight.TryRemove(url, out _);
        }
    }

    /// <summary>Bytes the cache holds on disk.</summary>
    public static long SizeOnDisk()
    {
        try { return new DirectoryInfo(Dir).EnumerateFiles().Sum(f => f.Length); } catch (IOException) { return 0; }
    }

    /// <summary>Removes every cached image; returns what is left (files in use stay).</summary>
    public static long Clear()
    {
        try { foreach (var f in new DirectoryInfo(Dir).EnumerateFiles()) try { f.Delete(); } catch (IOException) { } } catch (IOException) { }
        return SizeOnDisk();
    }

    private static string Key(string url) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..32];

    private static string Sniff(byte[] b) =>
        b.Length > 12 && b[0] == 0x89 && b[1] == 0x50 ? "image/png"
        : b.Length > 12 && b[0] == 'R' && b[1] == 'I' && b[8] == 'W' && b[9] == 'E' ? "image/webp"
        : b.Length > 12 && b[4] == 'f' && b[5] == 't' && b[6] == 'y' && b[7] == 'p' ? "image/avif"
        : "image/jpeg";
}
