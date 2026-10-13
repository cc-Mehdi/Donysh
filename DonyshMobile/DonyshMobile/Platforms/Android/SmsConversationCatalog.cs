using Android.Content;
using Android.Provider;
using DonyshMobile.Core;

namespace DonyshMobile.Platforms.Android;

public static class SmsConversationCatalog
{
    public static IReadOnlyList<string> RecentSenders(Context context)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] columns = ["address"];
        using var cursor = context.ContentResolver?.Query(Telephony.Sms.Inbox.ContentUri!, columns,
            null, null, "date DESC");
        if (cursor is null) return [];
        var addressIndex = cursor.GetColumnIndex("address");
        if (addressIndex < 0) return [];
        var scanned = 0;
        while (cursor.MoveToNext() && scanned++ < 1000 && result.Count < 100)
        {
            var address = cursor.GetString(addressIndex)?.Trim();
            var normalized = SenderAddress.Normalize(address);
            if (string.IsNullOrWhiteSpace(address) || normalized.Length == 0) continue;
            result.TryAdd(normalized, address);
        }
        return result.Values.ToList();
    }
}
