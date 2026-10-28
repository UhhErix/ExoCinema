using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ExoCinema.Jellyfin;

/// <summary>
/// The signed-in Jellyfin account, kept at %LOCALAPPDATA%\ExoCinema\accounts.json. The access token is
/// sealed with Windows DPAPI for this Windows user; the password is never stored.
/// </summary>
internal static class AccountStore
{
    private static readonly string Dir = AppInfo.Data;
    private static readonly string FilePath = Path.Combine(Dir, "accounts.json");
    private static readonly byte[] Entropy = "Exo Cinema account"u8.ToArray();

    internal sealed record Saved(string Server, string ServerName, string UserId, string UserName, string SealedToken);

    private sealed record File(string DeviceId, List<Saved> Accounts);

    public static string DeviceId => Load().DeviceId;

    public static (Saved Account, string Token)? Current()
    {
        var a = Load().Accounts.FirstOrDefault();
        if (a is null) return null;
        try
        {
            var token = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(a.SealedToken), Entropy, DataProtectionScope.CurrentUser));
            return (a, token);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public static void Save(string server, string serverName, string userId, string userName, string token)
    {
        var sealedToken = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(token), Entropy, DataProtectionScope.CurrentUser));
        var f = Load();
        f.Accounts.RemoveAll(a => a.Server == server && a.UserId == userId);
        f.Accounts.Insert(0, new Saved(server, serverName, userId, userName, sealedToken));
        Write(f);
    }

    public static void Forget(string server, string userId)
    {
        var f = Load();
        f.Accounts.RemoveAll(a => a.Server == server && a.UserId == userId);
        Write(f);
    }

    private static File Load()
    {
        try
        {
            if (System.IO.File.Exists(FilePath))
                return JsonSerializer.Deserialize<File>(System.IO.File.ReadAllText(FilePath)) ?? Fresh();
        }
        catch (JsonException) { }
        var f = Fresh();
        Write(f);
        return f;
    }

    private static File Fresh() => new(Guid.NewGuid().ToString("N"), []);

    private static void Write(File f)
    {
        Directory.CreateDirectory(Dir);
        var tmp = FilePath + ".tmp";
        System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(f, new JsonSerializerOptions { WriteIndented = true }));
        System.IO.File.Move(tmp, FilePath, overwrite: true);
    }
}
