using Android.App;
using Android.Content;
using AndroidX.Core.App;
using DonyshMobile.Core;

namespace DonyshMobile.Platforms.Android;

public static class TitleReminderNotifier
{
    private const string ChannelId = "donysh-sms-titles";
    private const int NotificationId = 45012;
    public const string OpenTitlesExtra = "open_pending_titles";

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
        var count = (await new SmsMonitorStore(directory).GetUntitledMessagesAsync()).Count;
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
        builder.SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo);
        builder.SetContentTitle("پیامک جدید در دونیش");
        builder.SetContentText($"{count} پیام منتظر ثبت عنوان است. برای عنوان‌گذاری ضربه بزنید.");
        builder.SetContentIntent(pending);
        builder.SetAutoCancel(false);
        builder.SetVisibility((int)NotificationVisibility.Private);
        builder.SetOnlyAlertOnce(true);
        builder.SetPriority((int)NotificationPriority.High);
        var notification = builder.Build();
        if (notification is not null) manager.Notify(NotificationId, notification);
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
