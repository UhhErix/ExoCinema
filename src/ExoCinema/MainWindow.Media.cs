using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json.Nodes;
using Windows.Media;
using Windows.Storage.Streams;

namespace ExoCinema;

/// <summary>
/// Windows' media controls: the keyboard's media keys, the player in Windows' volume and Quick Settings flyouts, and
/// Exo Launcher's Now Playing (which reads Windows' media sessions) see and steer what Exo Cinema plays: its title,
/// show and cover, play and pause, seeking, 10 s back and forward, and the next episode. Only while the player is
/// open: closed, Exo Cinema leaves the media keys to whatever else plays (Spotify, a browser).
/// </summary>
public sealed partial class MainWindow
{
    private SystemMediaTransportControls? _media;
    private bool _mediaOn;
    private int _mediaItem;
    private double _mediaTimelineAt = -1;

    private SystemMediaTransportControls? Media()
    {
        if (_media != null) return _media;
        try
        {
            var media = SystemMediaTransportControlsInterop.GetForWindow(_hwnd);
            media.IsPlayEnabled = media.IsPauseEnabled = true;
            media.IsFastForwardEnabled = media.IsRewindEnabled = true;
            media.ButtonPressed += (_, e) => OnUi(() => OnMediaButton(e.Button));
            // One jump to a picked time, not a drag: exact, not the nearest keyframe (which can be seconds away).
            media.PlaybackPositionChangeRequested += (_, e) => OnUi(() => MediaSeek(e.RequestedPlaybackPosition.TotalSeconds, "absolute"));
            _media = media;
        }
        catch (Exception e) { _log.Write($"media controls: {e.Message}"); }
        return _media;
    }

    /// <summary>The page says what plays (a title, a line under it, a cover on the signed-in server), whether a next
    /// episode follows, or that the player closed.</summary>
    private async void SetMedia(JsonNode msg)
    {
        if (Media() is not { } media) return;
        if ((bool?)msg["off"] == true)
        {
            _mediaOn = false;
            _mediaItem++;
            media.PlaybackStatus = MediaPlaybackStatus.Closed;
            media.DisplayUpdater.ClearAll();
            media.DisplayUpdater.Update();
            media.IsEnabled = false;
            return;
        }
        if (msg["next"] is JsonValue next)
        {
            media.IsNextEnabled = (bool)next;
            return;
        }
        var item = ++_mediaItem;
        _mediaOn = true;
        _mediaTimelineAt = -1;
        media.IsEnabled = true;
        media.IsNextEnabled = false;
        media.PlaybackStatus = _props.GetValueOrDefault("pause") is true ? MediaPlaybackStatus.Paused : MediaPlaybackStatus.Playing;
        var shown = media.DisplayUpdater;
        shown.ClearAll();
        shown.Type = MediaPlaybackType.Video;
        shown.VideoProperties.Title = (string?)msg["title"] ?? "";
        shown.VideoProperties.Subtitle = (string?)msg["subtitle"] ?? "";
        shown.Update();
        // The cover comes from the image cache, and only from the signed-in server, never from another address.
        if ((string?)msg["image"] is not { Length: > 0 } image || _images is null
            || _jellyfin.Server is not { } server || !image.StartsWith(server.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            if (await _images.GetAsync(image) is not { } bytes || item != _mediaItem) return;
            var file = await CoverFileAsync(bytes, item);
            if (item != _mediaItem) return;
            shown.Thumbnail = RandomAccessStreamReference.CreateFromFile(await Windows.Storage.StorageFile.GetFileFromPathAsync(file));
            shown.Update();
        }
        catch (Exception e) { _log.Write($"media controls: {e.Message}"); }
    }

    /// <summary>
    /// The cover as Windows' media session hands it on (to its flyout, Exo Launcher's mini player): a JPEG file at most
    /// 480 px tall. A stream in memory came out empty for readers in other processes, and the server may send WebP or
    /// AVIF, which not every reader decodes. One file per title, the last one removed.
    /// </summary>
    private static async Task<string> CoverFileAsync(byte[] bytes, int item)
    {
        var dir = Path.Combine(AppInfo.Data, "cache");
        Directory.CreateDirectory(dir);
        foreach (var old in Directory.EnumerateFiles(dir, "now-playing-*.jpg")) try { File.Delete(old); } catch (IOException) { }
        using var input = new InMemoryRandomAccessStream();
        await input.WriteAsync(bytes.AsBuffer());
        input.Seek(0);
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(input);
        var scale = Math.Min(1, 480.0 / decoder.PixelHeight);
        var transform = new Windows.Graphics.Imaging.BitmapTransform
        {
            ScaledWidth = (uint)Math.Max(1, Math.Round(decoder.PixelWidth * scale)),
            ScaledHeight = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * scale)),
            InterpolationMode = Windows.Graphics.Imaging.BitmapInterpolationMode.Fant,
        };
        var pixels = await decoder.GetPixelDataAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Ignore,
            transform, Windows.Graphics.Imaging.ExifOrientationMode.RespectExifOrientation, Windows.Graphics.Imaging.ColorManagementMode.ColorManageToSRgb);
        using var output = new InMemoryRandomAccessStream();
        var quality = new Windows.Graphics.Imaging.BitmapPropertySet { ["ImageQuality"] = new Windows.Graphics.Imaging.BitmapTypedValue(0.9f, Windows.Foundation.PropertyType.Single) };
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.JpegEncoderId, output, quality);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Ignore,
            transform.ScaledWidth, transform.ScaledHeight, 96, 96, pixels.DetachPixelData());
        await encoder.FlushAsync();
        var jpeg = new byte[output.Size];
        using (var read = output.GetInputStreamAt(0).AsStreamForRead()) read.ReadExactly(jpeg);
        var path = Path.Combine(dir, $"now-playing-{item}.jpg");
        await File.WriteAllBytesAsync(path, jpeg);
        return path;
    }

    private void OnMediaButton(SystemMediaTransportControlsButton button)
    {
        if (_mpv is not { } mpv || !_mediaOn) return;
        switch (button)
        {
            case SystemMediaTransportControlsButton.Play: mpv.Set("pause", "no"); break;
            case SystemMediaTransportControlsButton.Pause: mpv.Set("pause", "yes"); break;
            case SystemMediaTransportControlsButton.FastForward: MediaSeek(10, "relative"); break;
            case SystemMediaTransportControlsButton.Rewind: MediaSeek(-10, "relative"); break;
            case SystemMediaTransportControlsButton.Next: Post(new { type = "media-key", key = "next" }); break;
        }
    }

    private void MediaSeek(double to, string mode)
    {
        if (_mpv is not { } mpv || !_mediaOn) return;
        _seekAt = mpv.Now;
        mpv.Command("seek", to.ToString(System.Globalization.CultureInfo.InvariantCulture), mode);
    }

    /// <summary>mpv's pause and position, passed on: the status at once, the timeline on a change of state and every
    /// 5 s while it plays (Windows moves the position on between updates itself).</summary>
    private void MediaFollow(string name)
    {
        if (!_mediaOn || _media is not { } media) return;
        if (name == "pause")
        {
            media.PlaybackStatus = _props.GetValueOrDefault("pause") is true ? MediaPlaybackStatus.Paused : MediaPlaybackStatus.Playing;
            _mediaTimelineAt = -1;
        }
        if (name is not ("pause" or "duration" or "time-pos" or "seek")) return;
        if (_props.GetValueOrDefault("duration") is not double duration || duration <= 0) return;
        var now = Environment.TickCount64;
        if (name == "time-pos" && _mediaTimelineAt >= 0 && now - _mediaTimelineAt < 5000) return;
        _mediaTimelineAt = now;
        var position = _props.GetValueOrDefault("time-pos") is double p ? p : 0;
        media.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
        {
            StartTime = TimeSpan.Zero,
            EndTime = TimeSpan.FromSeconds(duration),
            MinSeekTime = TimeSpan.Zero,
            MaxSeekTime = TimeSpan.FromSeconds(duration),
            Position = TimeSpan.FromSeconds(Math.Clamp(position, 0, duration)),
        });
    }
}
