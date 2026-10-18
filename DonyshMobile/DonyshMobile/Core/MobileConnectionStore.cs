using System.Text.Json;

namespace DonyshMobile.Core;

public static class MobileConnectionStore
{
    private const string ConnectionKey = "donysh-mobile-connection-v1";
    private const string ServerKey = "donysh-mobile-server";
    private const string InstallationKey = "donysh-mobile-installation-v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Server
    {
        get => Preferences.Default.Get(ServerKey, "https://donysh.ir");
        set => Preferences.Default.Set(ServerKey, value);
    }

    public static async Task<MobileConnection?> LoadAsync()
    {
        try
        {
            var json = await SecureStorage.Default.GetAsync(ConnectionKey);
            if (string.IsNullOrWhiteSpace(json)) return null;
            var connection = JsonSerializer.Deserialize<MobileConnection>(json, JsonOptions);
            return connection is not null && MobileApiClient.NormalizeServer(connection.Server) is not null
                && connection.Token is { Length: 64 } && connection.Token.All(Uri.IsHexDigit)
                ? connection : null;
        }
        // Restored or temporarily unavailable platform credentials must never crash SMS capture.
        // Leave the secure record untouched so unlocking/restoring the key can recover it.
        catch (Exception) { return null; }
    }

    public static async Task SaveAsync(MobileConnection connection)
    {
        var server = MobileApiClient.NormalizeServer(connection.Server)
            ?? throw new ArgumentException("آدرس سامانه باید امن و معتبر باشد.", nameof(connection));
        if (connection.Token is not { Length: 64 } || !connection.Token.All(Uri.IsHexDigit))
            throw new ArgumentException("کد دسترسی سامانه معتبر نیست.", nameof(connection));
        await SecureStorage.Default.SetAsync(ConnectionKey, JsonSerializer.Serialize(connection with { Server = server }, JsonOptions));
        Server = server;
    }

    public static void Remove() => SecureStorage.Default.Remove(ConnectionKey);

    public static async Task<string> GetInstallationIdAsync()
    {
        try
        {
            var existing = await SecureStorage.Default.GetAsync(InstallationKey);
            if (existing is { Length: 64 } && existing.All(Uri.IsHexDigit)) return existing;
            var created = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            await SecureStorage.Default.SetAsync(InstallationKey, created);
            return created;
        }
        catch (Exception)
        {
            // Secure storage can briefly be unavailable immediately after device restore.
            throw new InvalidOperationException("حافظهٔ امن گوشی در دسترس نیست.");
        }
    }
}
