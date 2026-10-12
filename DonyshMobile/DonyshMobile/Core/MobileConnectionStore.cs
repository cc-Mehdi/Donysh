using System.Text.Json;

namespace DonyshMobile.Core;

public static class MobileConnectionStore
{
    private const string ConnectionKey = "donysh-mobile-connection-v1";
    private const string ServerKey = "donysh-mobile-server";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Server
    {
        get => Preferences.Default.Get(ServerKey, "https://donysh.ir");
        set => Preferences.Default.Set(ServerKey, value);
    }

    public static async Task<MobileConnection?> LoadAsync()
    {
        var json = await SecureStorage.Default.GetAsync(ConnectionKey);
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<MobileConnection>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }

    public static async Task SaveAsync(MobileConnection connection)
    {
        Server = connection.Server;
        await SecureStorage.Default.SetAsync(ConnectionKey, JsonSerializer.Serialize(connection, JsonOptions));
    }

    public static void Remove() => SecureStorage.Default.Remove(ConnectionKey);
}
