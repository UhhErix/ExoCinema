using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace ExoCinema;

/// <summary>
/// Exo Cinema's own shortcuts (Start menu and desktop) carry its app ID, System.AppUserModel.ID, so Windows ties the
/// running window to them: Windows' media flyout and Exo Launcher's Now Playing show "Exo Cinema" with its icon, and a
/// pinned taskbar icon stays one icon. Setup writes plain shortcuts; the app stamps the ones that point at itself.
/// The shell's interfaces are source-generated COM: built-in COM interop loses its vtable order when the app is
/// trimmed (a shortcut then read back with no target), a generated one can't.
/// </summary>
internal static partial class ShortcutIdentity
{
    public static int Stamp()
    {
        var exe = Environment.ProcessPath;
        if (exe is null) return 0;
        var stamped = 0;
        foreach (var folder in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.DesktopDirectory })
            foreach (var link in Directory.EnumerateFiles(Environment.GetFolderPath(folder), "Exo Cinema*.lnk"))
                if (StampOne(link, exe)) stamped++;
        return stamped;
    }

    private static readonly StrategyBasedComWrappers Wrappers = new();

    private static bool StampOne(string path, string exe)
    {
        Marshal.ThrowExceptionForHR(CoCreateInstance(ShellLinkClass, 0, 1 /* CLSCTX_INPROC_SERVER */, IUnknown, out var unknown));
        var link = Wrappers.GetOrCreateObjectForComInstance(unknown, CreateObjectFlags.UniqueInstance);
        Marshal.Release(unknown);
        try
        {
            var file = (IPersistFile)link;
            file.Load(path, 2 /* STGM_READWRITE */);
            var buffer = Marshal.AllocCoTaskMem(2 * 1024);
            string? target;
            try
            {
                ((IShellLinkW)link).GetPath(buffer, 1024, 0, 0);
                target = Marshal.PtrToStringUni(buffer);
            }
            finally { Marshal.FreeCoTaskMem(buffer); }
            if (!string.Equals(target, exe, StringComparison.OrdinalIgnoreCase)) return false;
            var store = (IPropertyStore)link;
            store.GetValue(AppIdKey, out var current);
            try { if (current.Type == 31 && Marshal.PtrToStringUni(current.Value) == Program.AppUserModelId) return false; }
            finally { PropVariantClear(ref current); }
            var value = new PropVariant { Type = 31 /* VT_LPWSTR */, Value = Marshal.StringToCoTaskMemUni(Program.AppUserModelId) };
            try
            {
                store.SetValue(AppIdKey, value);
                store.Commit();
            }
            finally { PropVariantClear(ref value); }
            file.Save(path, true);
            return true;
        }
        finally { ((ComObject)link).FinalRelease(); }
    }

    private static readonly Guid ShellLinkClass = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid IUnknown = new("00000000-0000-0000-C000-000000000046");
    private static readonly PropertyKey AppIdKey = new() { Format = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = 5 };

    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16), Guid("0000010B-0000-0000-C000-000000000046")]
    internal partial interface IPersistFile
    {
        void GetClassID(out Guid classId);
        [PreserveSig] int IsDirty();
        void Load(string file, uint mode);
        void Save(string? file, [MarshalAs(UnmanagedType.Bool)] bool remember);
    }

    [GeneratedComInterface, Guid("000214F9-0000-0000-C000-000000000046")]
    internal partial interface IShellLinkW
    {
        void GetPath(nint file, int max, nint findData, uint flags);
    }

    [GeneratedComInterface, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    internal partial interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(in PropertyKey key, out PropVariant value);
        void SetValue(in PropertyKey key, in PropVariant value);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PropertyKey
    {
        public Guid Format;
        public uint Id;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public nint Value;
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid classId, nint outer, uint context, in Guid iid, out nint obj);

    [LibraryImport("ole32.dll")]
    private static partial int PropVariantClear(ref PropVariant value);
}
