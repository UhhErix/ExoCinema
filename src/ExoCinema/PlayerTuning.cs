using System.Runtime.InteropServices;

namespace ExoCinema;

/// <summary>
/// mpv's options, picked from the PC it runs on rather than from one machine. For now it only reads
/// the display adapter; the real app adds VRAM, HDR state and the driver's video upscaling.
/// </summary>
internal static class PlayerTuning
{
    public static string Gpu { get; } = PrimaryAdapter();

    public static string Vendor { get; } =
        Gpu.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ? "nvidia"
        : Gpu.Contains("AMD", StringComparison.OrdinalIgnoreCase) || Gpu.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ? "amd"
        : Gpu.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? "intel"
        : "other";

    // Integrated graphics get mpv's default scalers; dedicated cards get its high-quality profile.
    internal static bool Strong =>
        Vendor == "nvidia"
        || (Vendor == "amd" && Gpu.Contains("RX", StringComparison.OrdinalIgnoreCase))
        || (Vendor == "intel" && Gpu.Contains("Arc", StringComparison.OrdinalIgnoreCase));

    public static Dictionary<string, string> Options()
    {
        var o = new Dictionary<string, string>
        {
            ["vo"] = "gpu-next",
            ["gpu-api"] = "d3d11",
            ["gpu-context"] = "d3d11",
            ["hwdec"] = "auto-safe",
            // On an HDR display mpv switches the swapchain to HDR itself; on SDR it tone-maps.
            ["target-colorspace-hint"] = "yes",
            ["keep-open"] = "yes",
            ["idle"] = "yes",
            ["osc"] = "no",
            ["osd-level"] = "0",
            ["input-default-bindings"] = "no",
            ["input-vo-keyboard"] = "no",
            ["input-cursor"] = "no",
            ["cursor-autohide"] = "no",
            ["ytdl"] = "no",
            // Buffering: read as far ahead as memory allows, so a fast link banks minutes to ride out a slow spell;
            // after running dry, wait for 3 s of picture, so a link a little slower than the video pauses once, not
            // every second (1 s, mpv's own, paused 4 times where this pauses once); and a server that drops the
            // connection gets asked again from where it stopped.
            ["cache"] = "yes",
            ["demuxer-max-bytes"] = CacheBytes,
            ["demuxer-readahead-secs"] = "300",
            ["cache-pause-wait"] = "3",
            ["stream-lavf-o"] = "reconnect=1,reconnect_streamed=1,reconnect_on_network_error=1,reconnect_delay_max=5",
        };
        // The player's glass: mpv blurs the picture under the page's glass itself (MainWindow.Glass.cs).
        if (File.Exists(MainWindow.GlassShader)) o["glsl-shaders"] = MainWindow.GlassShader;
        if (Strong) o["profile"] = "high-quality";
        return o;
    }

    // The read-ahead buffer lives in memory: a minute and a half of a 4K remux on a 16 GB PC, half that on 8 GB.
    private static string CacheBytes
    {
        get
        {
            var gb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024.0 * 1024 * 1024);
            return gb >= 15 ? "1GiB" : gb >= 7 ? "512MiB" : "256MiB";
        }
    }

    /// <summary>Running on battery: the picture goes lighter (GetSystemPowerStatus; a desktop is always on mains).</summary>
    public static bool OnBattery() => GetSystemPowerStatus(out var s) && s.ACLineStatus == 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    public static object Describe() => new { gpu = Gpu, vendor = Vendor };

    private static string PrimaryAdapter()
    {
        var device = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        string? first = null;
        for (uint i = 0; EnumDisplayDevices(null, i, ref device, 0); i++)
        {
            first ??= device.DeviceString;
            if ((device.StateFlags & 0x4) != 0) return device.DeviceString; // DISPLAY_DEVICE_PRIMARY_DEVICE
            device.cb = Marshal.SizeOf<DISPLAY_DEVICE>();
        }
        return first ?? "unknown";
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? device, uint index, ref DISPLAY_DEVICE info, uint flags);
}
