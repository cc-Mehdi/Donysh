using Android.App;
using Android.Content;
using Android.Provider;
using Donysh.Sms;

namespace Donysh.Companion;

[BroadcastReceiver(Enabled = true, Exported = true, Permission = "android.permission.BROADCAST_SMS")]
[IntentFilter(new[] { "android.provider.Telephony.SMS_RECEIVED" })]
public class SmsReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != "android.provider.Telephony.SMS_RECEIVED" || !CompanionSettings.Enabled) return;
        try {
            var parts = Telephony.Sms.Intents.GetMessagesFromIntent(intent);
            if (parts is null || parts.Length == 0 || parts.Length > 20) return;
            var sender = parts[0].OriginatingAddress ?? "";
            if (parts.Any(x => x.OriginatingAddress != sender)) return;
            var bank = CompanionSettings.BankFor(sender);
            if (bank is null) return;
            var body = string.Concat(parts.Select(x => x.MessageBody));
            if (body.Length > 2048 || !body.Contains("برداشت") || body.Contains("رمز") || body.Contains("واریز")) return;
            var parsed = BankSmsParser.Parse(body);
            // A valid but different bank template is not trusted under this sender rule.
            if (parsed is not null && parsed.Bank != bank) return;
            CompanionSettings.Queue.Enqueue(new(sender, body, parts[0].TimestampMillis), held: parsed is null);
            SyncJob.Schedule(context);
        } catch (Exception) {
            Preferences.Set("device-error", "ذخیره پیامک کامل نشد؛ پیامک را در برنامه پیامک گوشی بررسی کنید.");
        }
    }
}

[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter(new[] { Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced })]
public class RestartReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is not null && (intent?.Action == Intent.ActionBootCompleted || intent?.Action == Intent.ActionMyPackageReplaced)) SyncJob.Schedule(context);
    }
}
