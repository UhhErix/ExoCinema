using System.Runtime.InteropServices;

namespace ExoCinema.Mpv;

// The part of libmpv's client API (client.h) the player uses.
internal static unsafe partial class MpvNative
{
    private const string Lib = "libmpv-2.dll";

    public enum Format
    {
        None = 0,
        String = 1,
        Flag = 3,
        Int64 = 4,
        Double = 5,
        Node = 6,
        NodeArray = 7,
        NodeMap = 8,
        ByteArray = 9,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Node
    {
        public IntPtr U; // string, int64, double, flag, node_list* or byte_array*, by Format
        public Format Format;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NodeList
    {
        public int Num;
        public Node* Values;
        public IntPtr* Keys;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ByteArray
    {
        public IntPtr Data;
        public nuint Size;
    }

    public enum EventId
    {
        None = 0,
        Shutdown = 1,
        LogMessage = 2,
        GetPropertyReply = 3,
        SetPropertyReply = 4,
        CommandReply = 5,
        StartFile = 6,
        EndFile = 7,
        FileLoaded = 8,
        Idle = 11,
        Tick = 14,
        ClientMessage = 16,
        VideoReconfig = 17,
        AudioReconfig = 18,
        Seek = 20,
        PlaybackRestart = 21,
        PropertyChange = 22,
        QueueOverflow = 24,
        Hook = 25,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Event
    {
        public EventId EventIdValue;
        public int Error;
        public ulong ReplyUserdata;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EventProperty
    {
        public IntPtr Name;
        public Format Format;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EventEndFile
    {
        public int Reason;
        public int Error;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EventLogMessage
    {
        public IntPtr Prefix;
        public IntPtr Level;
        public IntPtr Text;
        public int LogLevel;
    }

    [LibraryImport(Lib, EntryPoint = "mpv_create")]
    public static partial IntPtr Create();

    [LibraryImport(Lib, EntryPoint = "mpv_initialize")]
    public static partial int Initialize(IntPtr ctx);

    [LibraryImport(Lib, EntryPoint = "mpv_terminate_destroy")]
    public static partial void TerminateDestroy(IntPtr ctx);

    [LibraryImport(Lib, EntryPoint = "mpv_set_option_string", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int SetOptionString(IntPtr ctx, string name, string data);

    [LibraryImport(Lib, EntryPoint = "mpv_set_property_string", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int SetPropertyString(IntPtr ctx, string name, string data);

    [LibraryImport(Lib, EntryPoint = "mpv_get_property_string", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr GetPropertyString(IntPtr ctx, string name);

    [LibraryImport(Lib, EntryPoint = "mpv_observe_property", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int ObserveProperty(IntPtr ctx, ulong userdata, string name, Format format);

    [LibraryImport(Lib, EntryPoint = "mpv_command")]
    public static partial int Command(IntPtr ctx, IntPtr* args);

    [LibraryImport(Lib, EntryPoint = "mpv_command_ret")]
    public static partial int CommandRet(IntPtr ctx, IntPtr* args, Node* result);

    [LibraryImport(Lib, EntryPoint = "mpv_free_node_contents")]
    public static partial void FreeNodeContents(Node* node);

    [LibraryImport(Lib, EntryPoint = "mpv_command_async")]
    public static partial int CommandAsync(IntPtr ctx, ulong userdata, IntPtr* args);

    [LibraryImport(Lib, EntryPoint = "mpv_request_log_messages", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int RequestLogMessages(IntPtr ctx, string minLevel);

    [LibraryImport(Lib, EntryPoint = "mpv_wait_event")]
    public static partial Event* WaitEvent(IntPtr ctx, double timeout);

    [LibraryImport(Lib, EntryPoint = "mpv_wakeup")]
    public static partial void Wakeup(IntPtr ctx);

    [LibraryImport(Lib, EntryPoint = "mpv_free")]
    public static partial void Free(IntPtr data);

    [LibraryImport(Lib, EntryPoint = "mpv_error_string")]
    public static partial IntPtr ErrorString(int error);

    public static string? Utf8(IntPtr p) => p == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(p);
}
