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

        var candidates = await Task.Run(() => ReadInbox(context, cutoff, cancellationToken), cancellationToken);
        var added = await store.CaptureBatchAsync(candidates, cancellationToken);
        SmsSyncSettings.LastRecoveryAtUtc = now;
        return new SmsRecoveryResult(candidates.Count, added);
    }

    private static IReadOnlyList<SmsCaptureCandidate> ReadInbox(
        Context context,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        string[] columns = ["address", "body", "date"];
        var selection = "date >= ?";
        var arguments = new[] { cutoff.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) };
        using var cursor = context.ContentResolver?.Query(
            Telephony.Sms.Inbox.ContentUri!, columns, selection, arguments, "date ASC");
        if (cursor is null) return [];

        var addressIndex = cursor.GetColumnIndex("address");
        var bodyIndex = cursor.GetColumnIndex("body");
        var dateIndex = cursor.GetColumnIndex("date");
        if (addressIndex < 0 || bodyIndex < 0 || dateIndex < 0) return [];

        var result = new List<SmsCaptureCandidate>();
        while (result.Count < MaxMessagesPerScan && cursor.MoveToNext())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var address = cursor.GetString(addressIndex)?.Trim();
            var body = cursor.GetString(bodyIndex)?.Trim();
            var receivedAt = cursor.GetLong(dateIndex);
            if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(body) || receivedAt <= 0) continue;
            try
            {
                result.Add(new SmsCaptureCandidate(address, body,
                    DateTimeOffset.FromUnixTimeMilliseconds(receivedAt), RecoveredFromInbox: true));
            }
            catch (ArgumentOutOfRangeException)
            {
                // Ignore a malformed provider row without aborting the remaining recovery scan.
            }
        }
        return result;
    }
}
