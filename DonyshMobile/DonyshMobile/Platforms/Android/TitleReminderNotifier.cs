using Android.App;
using Android.Content;
using AndroidX.Core.App;
using DonyshMobile.Core;

namespace DonyshMobile.Platforms.Android;

public static class TitleReminderNotifier
{
    private const string ChannelId = "donysh-sms-titles";
    private const int NotificationId = 45012;
    private const int ReplyRequestId = 45013;
    public const string OpenTitlesExtra = "open_pending_titles";
    internal const string ReplyAction = "ir.donysh.mobile.SET_SMS_TITLE";
    internal const string ReplyKey = "donysh_sms_title";
    internal const string MessageIdExtra = "donysh_sms_id";

    public static async Task RefreshAsync(Context context)
    {
        try { await RefreshCoreAsync(context); }
        catch (Exception exception) { global::Android.Util.Log.Warn("DonyshTitle", $"Title reminder failed: {exception.GetType().Name}"); }
    }

    private static async Task RefreshCoreAsync(Context context)
    {
        var manager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
        if (manager is null) return;
        if (!SmsSyncSettings.RequireTitle)
        {
            manager.Cancel(NotificationId);
            return;
        }

        var directory = context.ApplicationContext?.FilesDir?.AbsolutePath;
        if (string.IsNullOrWhiteSpace(directory)) return;
        var messages = await new SmsMonitorStore(directory).GetUntitledMessagesAsync();
        var count = messages.Count;
        if (count == 0)
        {
            manager.Cancel(NotificationId);
            return;
        }
        if (OperatingSystem.IsAndroidVersionAtLeast(33)
            && context.CheckSelfPermission(global::Android.Manifest.Permission.PostNotifications)
                != global::Android.Content.PM.Permission.Granted) return;

        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            var channel = new NotificationChannel(ChannelId, "عنوان‌گذاری پیامک‌ها", NotificationImportance.High)
            {
                Description = "یادآوری ثبت عنوان پیامک‌های تازه"
            };
            manager.CreateNotificationChannel(channel);
        }

        var openIntent = new Intent(context, typeof(MainActivity));
        openIntent.PutExtra(OpenTitlesExtra, true);
        openIntent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        var flags = PendingIntentFlags.UpdateCurrent;
        if (OperatingSystem.IsAndroidVersionAtLeast(23)) flags |= PendingIntentFlags.Immutable;
        var pending = PendingIntent.GetActivity(context, 0, openIntent, flags);
        var builder = new NotificationCompat.Builder(context, ChannelId);
        builder.SetSmallIcon(Resource.Drawable.ic_donysh_notification);
        builder.SetColor(global::Android.Graphics.Color.Rgb(81, 67, 216));
        builder.SetContentTitle("پیامک جدید در دونیش");
        builder.SetContentText($"{DonyshMobile.UI.UiText.Digits(count)} پیام منتظر عنوان است. عنوان را همین‌جا ثبت کنید.");
        builder.SetContentIntent(pending);
        builder.SetAutoCancel(false);
        builder.SetVisibility((int)NotificationVisibility.Private);
        builder.SetOnlyAlertOnce(true);
        builder.SetPriority((int)NotificationPriority.High);
        var first = messages[0];
        var replyIntent = new Intent(context, typeof(TitleReplyReceiver));
        replyIntent.SetAction(ReplyAction);
        replyIntent.PutExtra(MessageIdExtra, first.Id.ToString("D"));
        var replyFlags = PendingIntentFlags.UpdateCurrent;
        if (OperatingSystem.IsAndroidVersionAtLeast(31)) replyFlags |= PendingIntentFlags.Mutable;
        var replyPending = PendingIntent.GetBroadcast(context, ReplyRequestId, replyIntent, replyFlags);
        var remoteInputBuilder = new AndroidX.Core.App.RemoteInput.Builder(ReplyKey);
        remoteInputBuilder.SetLabel("عنوان خرج");
        var remoteInput = remoteInputBuilder.Build()!;
        if (replyPending is not null)
        {
            var actionBuilder = new NotificationCompat.Action.Builder(
                Resource.Drawable.ic_donysh_notification, "ثبت عنوان", replyPending);
            actionBuilder.AddRemoteInput(remoteInput);
            var replyAction = actionBuilder.Build();
            if (replyAction is not null) builder.AddAction(replyAction);
        }
        var notification = builder.Build();
        if (notification is not null) manager.Notify(NotificationId, notification);
    }
}

[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class TitleReplyReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != TitleReminderNotifier.ReplyAction
            || !Guid.TryParse(intent.GetStringExtra(TitleReminderNotifier.MessageIdExtra), out var messageId)) return;
        var title = AndroidX.Core.App.RemoteInput.GetResultsFromIntent(intent)?
            .GetCharSequence(TitleReminderNotifier.ReplyKey)?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(title)) return;
        var pending = GoAsync();
        _ = Task.Run(async () =>
        {
            try
            {
                var directory = context.ApplicationContext?.FilesDir?.AbsolutePath;
                if (!string.IsNullOrWhiteSpace(directory)
                    && await new SmsMonitorStore(directory).SetTitleAsync(messageId, title))
                {
                    SyncPlatform.Schedule();
                    SmsReceivedNotifier.NotifySyncChanged();
                }
                await TitleReminderNotifier.RefreshAsync(context);
            }
            catch (Exception exception)
            {
                global::Android.Util.Log.Warn("DonyshTitle", $"Inline title failed: {exception.GetType().Name}");
            }
            finally { pending?.Finish(); }
        });
    }
}

[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter([Intent.ActionUserPresent])]
public sealed class SmsUnlockReminderReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != Intent.ActionUserPresent) return;
        var pending = GoAsync();
        _ = Task.Run(async () =>
        {
            try { await TitleReminderNotifier.RefreshAsync(context); }
            catch (Exception exception) { global::Android.Util.Log.Error("DonyshTitle", $"Title reminder failed: {exception.GetType().Name}"); }
            finally { pending?.Finish(); }
        });
    }
}
