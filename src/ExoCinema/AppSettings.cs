using System.Text.Json;
using ExoCinema.Jellyfin;

namespace ExoCinema;

/// <summary>
/// What Exo Cinema remembers about itself, in settings.json beside the sign-in: whether the first run is done and
/// the last version whose What's New was seen. The page keeps its own preferences.
/// </summary>
internal sealed class AppSettings
{
    private static readonly string FilePath = Path.Combine(AppInfo.Data, "settings.json");

    public bool Onboarded { get; set; }
    public string? WhatsNewSeen { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) is { } saved)
                return saved;
        }
        catch (JsonException) { }
        catch (IOException) { }

        // No file yet. Signed in already means an earlier Exo Cinema: no first run, and this version's What's New
        // once. Otherwise a new install: the first run, and nothing new to tell.
        var signedIn = AccountStore.Current() is not null;
        var s = new AppSettings { Onboarded = signedIn, WhatsNewSeen = signedIn ? "0.0.0" : AppInfo.Version };
        s.Save();
        return s;
    }

    public void Save()
    {
        Directory.CreateDirectory(AppInfo.Data);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, FilePath, overwrite: true);
    }
}
