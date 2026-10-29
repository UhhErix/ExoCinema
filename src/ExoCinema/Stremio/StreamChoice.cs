using System.Text.RegularExpressions;

namespace ExoCinema.Stremio;

/// <summary>
/// An add-on stream as the source sheet shows it and as Best ranks it: what its name, description and file name say
/// about the picture (height, HDR, codec, 10-bit), where it came from (remux, Blu-ray, web, a camera in a cinema), the
/// sound, its size, whether the debrid service already holds it (cached streams start at once; the rest must download
/// first) and, for a torrent, how many share it.
/// </summary>
internal sealed partial record StreamChoice(string Url, string Label, int Height, string? Range, string? Codec, bool TenBit, string? Source, string? Audio, int Channels, long? Size, bool? Cached, int Seeders, int Order)
{
    public static StreamChoice From(AddonClient.Stream s, int order)
    {
        var t = s.Text;
        int height = Res2160().IsMatch(t) ? 2160 : Res1440().IsMatch(t) ? 1440 : Res1080().IsMatch(t) ? 1080 : Res720().IsMatch(t) ? 720 : Res480().IsMatch(t) ? 480 : 0;
        string? range = Dv().IsMatch(t) ? "Dolby Vision" : Hdr10Plus().IsMatch(t) ? "HDR10+" : Hdr().IsMatch(t) ? "HDR10" : null;
        string? codec = Hevc().IsMatch(t) ? "hevc" : Av1().IsMatch(t) ? "av1" : Avc().IsMatch(t) ? "h264" : null;
        string? source = Cam().IsMatch(t) ? "Cam" : Remux().IsMatch(t) ? "Remux" : BluRay().IsMatch(t) ? "Blu-ray" : WebDl().IsMatch(t) ? "WEB-DL" : WebRip().IsMatch(t) ? "WEBRip" : Hdtv().IsMatch(t) ? "HDTV" : null;
        string? audio = Atmos().IsMatch(t) ? "Atmos" : TrueHd().IsMatch(t) ? "TrueHD" : DtsHd().IsMatch(t) ? "DTS-HD" : DdPlus().IsMatch(t) ? "E-AC-3" : Dts().IsMatch(t) ? "DTS" : Dd().IsMatch(t) ? "AC-3" : Aac().IsMatch(t) ? "AAC" : null;
        var channels = Ch71().IsMatch(t) ? 8 : Ch51().IsMatch(t) ? 6 : Ch20().IsMatch(t) ? 2 : 0;
        var size = s.Size ?? SizeIn(t);
        bool? cached = Uncached().IsMatch(t) ? false : CachedMark().IsMatch(t) ? true : null;
        var seeders = SeedersText().Match(t) is { Success: true } m && int.TryParse(m.Groups[1].Value, out var n) ? n : 0;
        // The add-on's own name for the stream: its first line, without the decorations some add-ons put around it.
        var label = Decor().Replace(t.Split('\n')[0], " ").Trim();
        return new StreamChoice(s.Url, label, height, range, codec, TenBitText().IsMatch(t), source, audio, channels, size, cached, seeders, order);
    }

    private static long? SizeIn(string t)
    {
        var m = SizeText().Match(t);
        if (!m.Success || !double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n)) return null;
        // As the add-on writes it: GB in thousands, GiB in 1024s.
        var unit = m.Groups[2].Value.ToUpperInvariant();
        return (long)(n * unit switch { "GIB" => 1L << 30, "GB" => 1e9, "MIB" => 1L << 20, _ => 1e6 });
    }

    /// <summary>The stream's average bitrate in Mb/s, from its size and the title's running time.</summary>
    public double? Mbps(double seconds) => Size is > 0 && seconds > 60 ? Size.Value * 8 / seconds / 1e6 : null;

    /// <summary>
    /// Best first, for this PC: one that can start now (not waiting on the debrid service to download it) and isn't a
    /// camera copy; what this connection has carried smoothly before (a stream needing more than the link gives stops to
    /// buffer); the most detailed picture the PC is built for (above 1080p only on a dedicated graphics card); one the
    /// graphics card can decode (10-bit H.264, common in anime, decodes on none, and then isn't upscaled either); the
    /// cleanest source, HDR, the fullest sound; the most shared; then the add-on's own order.
    /// </summary>
    public static List<StreamChoice> Rank(IEnumerable<StreamChoice> all, double seconds, double? linkMbps, bool strong)
    {
        var cap = strong ? int.MaxValue : 1080;
        bool Fits(StreamChoice s) => linkMbps is not > 0 || s.Mbps(seconds) is not { } need || need <= linkMbps * 0.85;
        static int SourceRank(string? s) => s switch { "Remux" => 6, "Blu-ray" => 5, "WEB-DL" => 4, null => 3, "WEBRip" => 3, "HDTV" => 2, _ => -10 };
        static int AudioRank(string? a) => a switch { "Atmos" => 6, "TrueHD" => 5, "DTS-HD" => 4, "E-AC-3" => 3, "DTS" => 2, "AC-3" => 2, "AAC" => 1, _ => 0 };
        return all
            .OrderByDescending(s => s.Cached != false)
            .ThenByDescending(s => s.Source != "Cam")
            .ThenByDescending(Fits)
            .ThenByDescending(s => s.Height <= cap ? s.Height : -s.Height)
            .ThenByDescending(s => !(s.TenBit && s.Codec == "h264"))
            .ThenByDescending(s => SourceRank(s.Source))
            .ThenByDescending(s => s.Range != null)
            .ThenByDescending(s => AudioRank(s.Audio))
            .ThenByDescending(s => s.Channels)
            .ThenByDescending(s => s.Seeders)
            .ThenBy(s => s.Order)
            .ToList();
    }

    [GeneratedRegex(@"(?i)\b(2160p|4k|uhd)\b")] private static partial Regex Res2160();
    [GeneratedRegex(@"(?i)\b1440p\b")] private static partial Regex Res1440();
    [GeneratedRegex(@"(?i)\b(1080p|fhd)\b")] private static partial Regex Res1080();
    [GeneratedRegex(@"(?i)\b720p\b")] private static partial Regex Res720();
    [GeneratedRegex(@"(?i)\b(480p|576p|sd)\b")] private static partial Regex Res480();
    [GeneratedRegex(@"(?i)\b(dv|dovi|dolby[ .]?vision)\b")] private static partial Regex Dv();
    [GeneratedRegex(@"(?i)\bhdr10(\+|plus)")] private static partial Regex Hdr10Plus();
    [GeneratedRegex(@"(?i)\bhdr(10)?\b")] private static partial Regex Hdr();
    [GeneratedRegex(@"(?i)\b(hevc|[xh]\.?265)\b")] private static partial Regex Hevc();
    [GeneratedRegex(@"(?i)\bav1\b")] private static partial Regex Av1();
    [GeneratedRegex(@"(?i)\b(avc|[xh]\.?264)\b")] private static partial Regex Avc();
    [GeneratedRegex(@"(?i)\b(cam|camrip|hdcam|ts|telesync|hdts|tc|telecine)\b")] private static partial Regex Cam();
    [GeneratedRegex(@"(?i)\bremux\b")] private static partial Regex Remux();
    [GeneratedRegex(@"(?i)\b(blu-?ray|bdrip|brrip|bdremux)\b")] private static partial Regex BluRay();
    [GeneratedRegex(@"(?i)\bweb-?dl\b")] private static partial Regex WebDl();
    [GeneratedRegex(@"(?i)\b(webrip|web)\b")] private static partial Regex WebRip();
    [GeneratedRegex(@"(?i)\bhdtv\b")] private static partial Regex Hdtv();
    [GeneratedRegex(@"(?i)\batmos\b")] private static partial Regex Atmos();
    [GeneratedRegex(@"(?i)\btrue-?hd\b")] private static partial Regex TrueHd();
    [GeneratedRegex(@"(?i)\bdts[-. ]?(hd|x|ma)\b")] private static partial Regex DtsHd();
    [GeneratedRegex(@"(?i)(\bddp|\bdd\+|\be-?ac-?3\b|\bdolby digital plus\b)")] private static partial Regex DdPlus();
    [GeneratedRegex(@"(?i)\bdts\b")] private static partial Regex Dts();
    [GeneratedRegex(@"(?i)\b(dd|ac-?3|dolby digital)\b")] private static partial Regex Dd();
    [GeneratedRegex(@"(?i)\baac\b")] private static partial Regex Aac();
    // Not between word edges: release names run them on ("DDP5.1", "TrueHD7.1").
    [GeneratedRegex(@"(?<!\d)7[ .]1(?!\d)")] private static partial Regex Ch71();
    [GeneratedRegex(@"(?<!\d)5[ .]1(?!\d)")] private static partial Regex Ch51();
    [GeneratedRegex(@"(?<!\d)2[ .]0(?!\d)")] private static partial Regex Ch20();
    [GeneratedRegex(@"(?i)(\d+(?:\.\d+)?)\s*(GB|GiB|MB|MiB)\b")] private static partial Regex SizeText();
    // Debrid marks: "[RD+]", "⚡" or "Cached" for a stream the service holds; "[RD download]", "⏳" or "Uncached" for one
    // it would have to fetch first.
    [GeneratedRegex(@"(?i)(\[(RD|AD|PM|DL|TB|ED|OC|PKP|EN|DB|TRB)\+\]|⚡|\bcached\b|\binstant\b)")] private static partial Regex CachedMark();
    [GeneratedRegex(@"(?i)(\[(RD|AD|PM|DL|TB|ED|OC|PKP|EN|DB|TRB)\s*download\]|⏳|\buncached\b|\bnot cached\b)")] private static partial Regex Uncached();
    [GeneratedRegex(@"👤\s*(\d+)")] private static partial Regex SeedersText();
    [GeneratedRegex(@"(?i)\b(10[ -]?bit|hi10p?|yuv420p10)\b")] private static partial Regex TenBitText();
    [GeneratedRegex(@"[\p{So}\p{Cs}\[\]|]+")] private static partial Regex Decor();
}
