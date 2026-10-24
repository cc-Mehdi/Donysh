using Android.App;
using Android.App.Job;
using Android.Content;
using AndroidX.Core.App;
using DonyshMobile.Core;

namespace DonyshMobile.Platforms.Android;

public static class LifeReminderScheduler
{
    private const string ChannelId = "donysh-life-reminders";
    private const string StoredIds = "life-reminder-ids-v1";
    private const int JobId = 6260;

    public static async Task RefreshAsync(LifeConnection connection)
    {
        var context = global::Android.App.Application.Context;
        ScheduleJob(context);
        var reminders = await new LifeMobileClient().RemindersAsync(connection);
        var alarm = (AlarmManager?)context.GetSystemService(Context.AlarmService);
        var notifications = (NotificationManager?)context.GetSystemService(Context.NotificationService);
        if (alarm is null || notifications is null) return;
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            notifications.CreateNotificationChannel(new NotificationChannel(ChannelId, "یادآورهای دنیش زندگی", NotificationImportance.High));
        var next = new HashSet<int>();
        foreach (var reminder in reminders)
        {
            if (reminder.RemindAtUtc <= DateTimeOffset.UtcNow) continue;
            var id = StableId(reminder.Id);
            next.Add(id);
            var pending = Pending(context, id, reminder);
            if (OperatingSystem.IsAndroidVersionAtLeast(23))
                alarm.SetAndAllowWhileIdle(AlarmType.RtcWakeup, reminder.RemindAtUtc.ToUnixTimeMilliseconds(), pending);
            else alarm.Set(AlarmType.RtcWakeup, reminder.RemindAtUtc.ToUnixTimeMilliseconds(), pending);
        }
        var previous = Preferences.Default.Get(StoredIds, "");
        foreach (var raw in previous.Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(raw, out var id) && !next.Contains(id))
            {
                alarm.Cancel(Pending(context, id, null));
                notifications.Cancel(id);
            }
        Preferences.Default.Set(StoredIds, string.Join(',', next));
    }

    public static void Clear()
    {
        var context = global::Android.App.Application.Context;
        var alarm = (AlarmManager?)context.GetSystemService(Context.AlarmService);
        var notifications = (NotificationManager?)context.GetSystemService(Context.NotificationService);
        foreach (var raw in Preferences.Default.Get(StoredIds, "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(raw, out var id))
            {
                alarm?.Cancel(Pending(context, id, null));
                notifications?.Cancel(id);
            }
        Preferences.Default.Remove(StoredIds);
    }

    public static void ScheduleJob(Context context)
    {
        try
        {
            var scheduler = (JobScheduler?)context.GetSystemService(Context.JobSchedulerService);
            if (scheduler is null || OperatingSystem.IsAndroidVersionAtLeast(24) && scheduler.GetPendingJob(JobId) is not null) return;
            var component = new ComponentName(context, Java.Lang.Class.FromType(typeof(LifeReminderJob)));
            scheduler.Schedule(new JobInfo.Builder(JobId, component).SetRequiredNetworkType(NetworkType.Any)!
                .SetPersisted(true)!.SetPeriodic(6 * 60 * 60 * 1000)!.Build()!);
        }
        catch (Exception exception) { global::Android.Util.Log.Warn("DonyshLife", $"Reminder job failed: {exception.GetType().Name}"); }
    }

    private static PendingIntent Pending(Context context, int id, LifeReminder? reminder)
    {
        var intent = new Intent(context, typeof(LifeReminderReceiver));
        intent.SetAction("ir.donysh.mobile.LIFE_REMINDER");
        intent.PutExtra("title", reminder?.Title);
        intent.PutExtra("time", reminder?.StartLabel);
        intent.PutExtra("id", id);
        var flags = PendingIntentFlags.UpdateCurrent;
        if (OperatingSystem.IsAndroidVersionAtLeast(23)) flags |= PendingIntentFlags.Immutable;
        return PendingIntent.GetBroadcast(context, id, intent, flags)!;
    }

    private static int StableId(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return 100_000 + (int)(BitConverter.ToUInt32(bytes, 0) % 1_000_000);
    }

    [BroadcastReceiver(Enabled = true, Exported = false)]
    public sealed class LifeReminderReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (context is null || intent?.Action != "ir.donysh.mobile.LIFE_REMINDER") return;
            if (OperatingSystem.IsAndroidVersionAtLeast(33)
                && context.CheckSelfPermission(global::Android.Manifest.Permission.PostNotifications)
                    != global::Android.Content.PM.Permission.Granted) return;
            var manager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
            if (manager is null) return;
            var open = new Intent(context, typeof(MainActivity));
            open.PutExtra("open_life", true);
            open.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
            var flags = PendingIntentFlags.UpdateCurrent;
            if (OperatingSystem.IsAndroidVersionAtLeast(23)) flags |= PendingIntentFlags.Immutable;
            var pending = PendingIntent.GetActivity(context, 6270, open, flags);
            var builder = new NotificationCompat.Builder(context, ChannelId);
            builder.SetSmallIcon(Resource.Drawable.ic_donysh_notification);
            builder.SetColor(global::Android.Graphics.Color.Rgb(8, 169, 157));
            builder.SetContentTitle(intent.GetStringExtra("title") ?? "برنامهٔ دنیش زندگی");
            builder.SetContentText($"زمان شروع: {intent.GetStringExtra("time")}");
            builder.SetContentIntent(pending);
            builder.SetAutoCancel(true);
            builder.SetPriority((int)NotificationPriority.High);
            var notification = builder.Build();
            if (notification is not null) manager.Notify(intent.GetIntExtra("id", 0), notification);
        }
    }
}

[Service(Permission = "android.permission.BIND_JOB_SERVICE", Exported = false)]
public sealed class LifeReminderJob : JobService
{
    public override bool OnStartJob(JobParameters? parameters)
    {
        if (parameters is null) return false;
        _ = Task.Run(async () =>
        {
            try
            {
                var connection = await LifeConnectionStore.LoadAsync();
                if (connection is not null) await LifeReminderScheduler.RefreshAsync(connection);
                else LifeReminderScheduler.Clear();
            }
            catch (UnauthorizedAccessException) { LifeReminderScheduler.Clear(); }
            catch (Exception exception) { global::Android.Util.Log.Warn("DonyshLife", $"Reminder refresh failed: {exception.GetType().Name}"); }
            finally { JobFinished(parameters, false); }
        });
        return true;
    }
    public override bool OnStopJob(JobParameters? parameters) => true;
}
