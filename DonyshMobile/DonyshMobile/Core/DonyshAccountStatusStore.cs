using System.Text.Json;

namespace DonyshMobile.Core;

public sealed record DonyshAccountStatus(string Subject, string Label, bool? FinanceAvailable,
    bool LifeAvailable);

public static class DonyshAccountStatusStore
{
    private const string Key = "donysh-account-status-v1";
    public static async Task<DonyshAccountStatus?> LoadAsync()
    {
        try
        {
            var value = await SecureStorage.Default.GetAsync(Key);
            return value is null ? null : JsonSerializer.Deserialize<DonyshAccountStatus>(value);
        }
        catch { return null; }
    }
    public static Task SaveAsync(DonyshAccountStatus status) =>
        SecureStorage.Default.SetAsync(Key, JsonSerializer.Serialize(status));
}
