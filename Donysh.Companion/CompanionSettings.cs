using Donysh.Sms;
using System.Text.Json;

namespace Donysh.Companion;

public static class CompanionSettings
{
    public sealed record Connection(string Server, string Token, string Destination);
    public static async Task<Connection?> ConnectionAsync()
    {
        var json = await SecureStorage.GetAsync("connection");
        return json is null ? null : JsonSerializer.Deserialize<Connection>(json);
    }
    public static Task SaveConnectionAsync(Connection connection) => SecureStorage.SetAsync("connection", JsonSerializer.Serialize(connection));
    public static Outbox Queue => new(Path.Combine(FileSystem.AppDataDirectory, "sms-outbox.json"));
    public static bool Enabled { get => Preferences.Get("enabled", false); set => Preferences.Set("enabled", value); }
    public static string Server { get => Preferences.Get("server", "https://donysh.ir"); set => Preferences.Set("server", value); }
    public static string Destination { get => Preferences.Get("destination", "متصل نشده"); set => Preferences.Set("destination", value); }
    public static string MellatSenders { get => Preferences.Get("mellat-senders", ""); set => Preferences.Set("mellat-senders", value); }
    public static string BluSenders { get => Preferences.Get("blu-senders", ""); set => Preferences.Set("blu-senders", value); }
    public static string? BankFor(string sender)
    {
        bool Contains(string entries) => entries.Split(new[] { ',', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(x => string.Equals(x, sender.Trim(), StringComparison.OrdinalIgnoreCase));
        var mellat = Contains(MellatSenders); var blu = Contains(BluSenders);
        return mellat == blu ? null : mellat ? "mellat" : "blu";
    }
    public static HttpClient Http() => new(new HttpClientHandler { AllowAutoRedirect = false }) {
        Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 32768
    };
}
