namespace ExoCinema;

/// <summary>Where Exo Cinema keeps its things, and which version it is.</summary>
internal static class AppInfo
{
    private static readonly string Default = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExoCinema");

    /// <summary>
    /// %LOCALAPPDATA%\ExoCinema: the sign-in, settings, caches and logs, beside the app folder Setup installs into.
    /// EXO_CINEMA_DATA points a test copy somewhere else, so it never touches yours.
    /// </summary>
    public static string Data { get; } = Environment.GetEnvironmentVariable("EXO_CINEMA_DATA") is { Length: > 0 } other
        ? Path.GetFullPath(other)
        : Default;

    public static bool OwnData => !string.Equals(Data, Default, StringComparison.OrdinalIgnoreCase);

    /// <summary>X.Y.Z, from the repository's VERSION file through the project.</summary>
    public static string Version { get; } = typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Running from the copy Setup installed, the only one an update may replace.</summary>
    public static bool Installed { get; } = string.Equals(
        Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), Path.Combine(Default, "app"), StringComparison.OrdinalIgnoreCase);
}
