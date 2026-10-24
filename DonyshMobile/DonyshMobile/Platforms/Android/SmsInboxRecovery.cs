using System.Globalization;
using Android.Content;
using Android.Content.PM;
using Android.Provider;
using DonyshMobile.Core;

namespace DonyshMobile.Platforms.Android;

public static class SmsInboxRecovery
{
    private static readonly TimeSpan RecoveryWindow = TimeSpan.FromHours(24);
    private static readonly TimeSpan Overlap = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(2);
    private const int MaxMessagesPerScan = 1000;
    private const int MaxAddressLength = 64;
    private const int MaxBodyLength = 2048;

    public static async Task<SmsRecoveryResult> RecoverAsync(
        Context context,
        SmsMonitorStore store,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(store);
        if (!SmsSyncSettings.RecoverRecent) return new SmsRecoveryResult(0, 0, Skipped: true);
        if (OperatingSystem.IsAndroidVersionAtLeast(23)
            && context.CheckSelfPermission(global::Android.Manifest.Permission.ReadSms) != Permission.Granted)
            return new SmsRecoveryResult(0, 0, PermissionRequired: true);

        var now = DateTimeOffset.UtcNow;
        var lastRecovery = SmsSyncSettings.LastRecoveryAtUtc;
        if (!force && lastRecovery is { } last && last <= now && now - last < MinimumInterval)
            return new SmsRecoveryResult(0, 0, Skipped: true);

        var cutoff = now.Subtract(RecoveryWindow);
        if (!force && lastRecovery is { } previous && previous > cutoff && previous <= now.AddMinutes(5))
            cutoff = previous.Subtract(Overlap);

        var candidates = await Task.Run(
            () => ReadInbox(context, cutoff, now, null, cancellationToken), cancellationToken);
        var added = await store.CaptureBatchAsync(candidates, cancellationToken);
        SmsSyncSettings.LastRecoveryAtUtc = now;
        return new SmsRecoveryResult(candidates.Count, added);
    }

    public static Task<IReadOnlyList<SmsCaptureCandidate>> FindAsync(
        Context context,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        IReadOnlyCollection<string> senderAddresses,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(senderAddresses);
        if (fromUtc >= toUtc) throw new ArgumentOutOfRangeException(nameof(fromUtc));
        if (senderAddresses.Count is 0 or > 50) return Task.FromResult<IReadOnlyList<SmsCaptureCandidate>>([]);
        if (OperatingSystem.IsAndroidVersionAtLeast(23)
            && context.CheckSelfPermission(global::Android.Manifest.Permission.ReadSms) != Permission.Granted)
            throw new UnauthorizedAccessException("اجازهٔ خواندن پیامک فعال نیست.");

        var selected = senderAddresses
            .Select(SenderAddress.Normalize)
            .Where(address => address.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        return Task.Run(
            () => ReadInbox(context, fromUtc.ToUniversalTime(), toUtc.ToUniversalTime(), selected, cancellationToken),
            cancellationToken);
    }

    private static IReadOnlyList<SmsCaptureCandidate> ReadInbox(
        Context context,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        IReadOnlySet<string>? selectedSenders,
        CancellationToken cancellationToken)
    {
        string[] columns = ["address", "body", "date", "date_sent"];
        var selection = "date >= ? AND date <= ?";
        var arguments = new[]
        {
            fromUtc.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            toUtc.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)
        };
        using var cursor = context.ContentResolver?.Query(
            Telephony.Sms.Inbox.ContentUri!, columns, selection, arguments, "date DESC");
        if (cursor is null) return [];

        var addressIndex = cursor.GetColumnIndex("address");
        var bodyIndex = cursor.GetColumnIndex("body");
        var dateIndex = cursor.GetColumnIndex("date");
        var sentIndex = cursor.GetColumnIndex("date_sent");
        if (addressIndex < 0 || bodyIndex < 0 || dateIndex < 0) return [];

        var result = new List<SmsCaptureCandidate>();
        while (result.Count < MaxMessagesPerScan && cursor.MoveToNext())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var address = cursor.GetString(addressIndex)?.Trim();
            var body = cursor.GetString(bodyIndex)?.Trim();
            var receivedAt = cursor.GetLong(dateIndex);
            var sentAt = sentIndex >= 0 && !cursor.IsNull(sentIndex) ? cursor.GetLong(sentIndex) : 0;
            if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(body) || receivedAt <= 0
                || address.Length > MaxAddressLength || body.Length > MaxBodyLength || address.Any(char.IsControl)) continue;
            if (selectedSenders is not null && !selectedSenders.Contains(SenderAddress.Normalize(address))) continue;
            try
            {
                result.Add(new SmsCaptureCandidate(address, body,
                    SmsTimestampPolicy.InboxTimestamp(receivedAt, sentAt), RecoveredFromInbox: true));
            }
            catch (ArgumentOutOfRangeException)
            {
                // Ignore a malformed provider row without aborting the remaining recovery scan.
            }
        }
        return result;
    }
}
