using Microsoft.Win32;

namespace ExoCinema;

/// <summary>
/// Exo Cinema's line in Windows' Installed apps, kept true from the app itself: its version, who publishes it and its
/// links, as Setup writes them (tools/ExoCinema.nsi). A build put in place without Setup (an update applied by hand)
/// left the old version and name there. Only the entry Setup made for this very copy is touched, and only when it is
/// there; a copy run from anywhere else changes nothing.
/// </summary>
internal static class InstalledAppsEntry
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ExoCinema";

    private static readonly (string Name, string Value)[] Values =
    [
        ("Publisher", "Exo"),
        ("URLInfoAbout", "https://getexo.app/cinema"),
        ("URLUpdateInfo", "https://getexo.app/cinema"),
        ("HelpLink", "https://discord.gg/44mXWhaHP4"),
    ];

    /// <summary>Returns how many values it put right.</summary>
    public static int Refresh()
    {
        if (!AppInfo.Installed) return 0;
        using var entry = Registry.CurrentUser.OpenSubKey(Key, writable: true);
        if (entry?.GetValue("InstallLocation") is not string location
            || !string.Equals(Path.TrimEndingDirectorySeparator(location), Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase))
            return 0;
        var fixedCount = 0;
        foreach (var (name, value) in Values.Append(("DisplayVersion", AppInfo.Version)))
        {
            if (entry.GetValue(name) as string == value) continue;
            entry.SetValue(name, value, RegistryValueKind.String);
            fixedCount++;
        }
        return fixedCount;
    }
}
