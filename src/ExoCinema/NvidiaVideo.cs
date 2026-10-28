using System.Runtime.InteropServices;

namespace ExoCinema;

/// <summary>
/// Whether NVIDIA's RTX Video Super Resolution is on, read the way Exo Launcher reads it (its Helpers/NvidiaVideo.cs,
/// from NVIDIA Control Panel's own calls): NvAPI_GetVideoStateEx 0x0B6EF8B9 with a 0x80-byte struct, version 0x10080,
/// component 29 at 4; supported is flags@0xC bit 0, on is enable@0x10, the quality is lastValueSet@0x58 (1-4, 5 Auto).
/// The setting is global and Exo Launcher's to change (PC › Display): Exo Cinema only reads it, never writes it.
/// </summary>
internal static unsafe class NvidiaVideo
{
    private const int SuperResolution = 29;
    private static readonly Lock Gate = new();
    private static bool? _available;

    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint QueryInterface(uint id);

    /// <summary>Super resolution's setting: 0 off, 1-4 quality, 5 Auto; null without an NVIDIA driver that has it.</summary>
    public static int? SuperResolutionSetting()
    {
        lock (Gate)
        {
            try
            {
                if (!Available() || AnyDisplay() is not { } handle) return null;
                var data = stackalloc byte[0x80];
                new Span<byte>(data, 0x80).Clear();
                *(uint*)data = 0x10080;
                *(uint*)(data + 4) = SuperResolution;
                var get = (delegate* unmanaged[Cdecl]<nint, byte*, int>)Resolve(0x0B6EF8B9);
                if (get(handle, data) != 0 || (*(uint*)(data + 0xC) & 1) == 0) return null;
                return *(int*)(data + 0x10) == 0 ? 0 : *(int*)(data + 0x58);
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { return null; }
        }
    }

    private static bool Available()
    {
        if (_available is { } known) return known;
        try
        {
            var init = (delegate* unmanaged[Cdecl]<int>)QueryInterface(0x0150E828);
            _available = init is not null && init() == 0;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { _available = false; }
        return _available.Value;
    }

    // The setting is global, so any screen on the NVIDIA card will do.
    private static nint? AnyDisplay()
    {
        var byName = (delegate* unmanaged[Cdecl]<byte*, nint*, int>)Resolve(0x35C29134);
        for (var i = 1; i <= 16; i++)
        {
            var name = System.Text.Encoding.ASCII.GetBytes($@"\\.\DISPLAY{i}" + "\0");
            nint handle = 0;
            fixed (byte* text = name)
                if (byName(text, &handle) == 0 && handle != 0) return handle;
        }
        return null;
    }

    private static nint Resolve(uint id)
    {
        var fn = QueryInterface(id);
        return fn != 0 ? fn : throw new EntryPointNotFoundException($"NvAPI 0x{id:X8} is not in this driver.");
    }
}
