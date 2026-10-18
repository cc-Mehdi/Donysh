using Android.App;
using Android.Content;
using Android.Provider;
using DonyshMobile.Core;

namespace DonyshMobile.Platforms.Android;

[BroadcastReceiver(
    Enabled = true,
    Exported = true,
    Permission = global::Android.Manifest.Permission.BroadcastSms)]
[IntentFilter([Telephony.Sms.Intents.SmsReceivedAction], Priority = 1000)]
public sealed class SmsBroadcastReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != Telephony.Sms.Intents.SmsReceivedAction)
        {
            return;
        }

        var messages = Telephony.Sms.Intents.GetMessagesFromIntent(intent);
        if (messages is null || messages.Length == 0)
        {
            return;
        }

        var captures = messages
            .Where(x => x is not null)
            .GroupBy(x => x.DisplayOriginatingAddress ?? x.OriginatingAddress ?? string.Empty)
            .Select(group => new
            {
                Address = group.Key,
                Body = string.Concat(group.Select(x => x.MessageBody ?? string.Empty)),
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(group.Min(x => x.TimestampMillis))
            })
            .Where(x => x.Address.Length > 0 && x.Body.Length > 0)
            .ToList();

        if (captures.Count == 0)
        {
            return;
        }

        // Schedule a provider-backed safety scan before asynchronous storage work.
        // If an OEM stops this cold process early, the inbox scan can recover the SMS later.
        SmsRecoveryJob.Schedule(context, immediate: true);

        var pendingResult = GoAsync();
        _ = Task.Run(async () =>
        {
            try
            {
                var directory = context.ApplicationContext?.FilesDir?.AbsolutePath;
                if (string.IsNullOrWhiteSpace(directory))
                {
                    return;
                }

                var store = new SmsMonitorStore(directory);
                var storedAny = await store.CaptureBatchAsync(captures.Select(capture =>
                    new SmsCaptureCandidate(capture.Address, capture.Body, capture.Timestamp)).ToList()) > 0;

                if (storedAny)
                {
                    SmsReceivedNotifier.Notify();
                    await TitleReminderNotifier.RefreshAsync(context);
                    SmsSyncJob.Schedule(context);
                }
            }
            catch (Exception exception)
            {
                global::Android.Util.Log.Error("DonyshSms", $"SMS capture failed: {exception.GetType().Name}");
            }
            finally
            {
                pendingResult?.Finish();
            }
        });
    }
}
