using Android.App;
using Android.App.Job;
using Android.Content;
using DonyshMobile.Core;

namespace DonyshMobile.Platforms.Android;

[Service(Permission = "android.permission.BIND_JOB_SERVICE", Exported = false)]
public sealed class SmsRecoveryJob : JobService
{
    private const int PeriodicJobId = 6220;
    private const int ImmediateJobId = 6221;
    private static readonly SemaphoreSlim SingleWorker = new(1, 1);
    private CancellationTokenSource? _running;

    public static void Schedule(Context context, bool immediate = false)
    {
        try
        {
            var scheduler = (JobScheduler?)context.GetSystemService(JobSchedulerService);
            if (scheduler is null) return;
            if (!SmsSyncSettings.RecoverRecent)
            {
                scheduler.Cancel(PeriodicJobId);
                scheduler.Cancel(ImmediateJobId);
                return;
            }

            var component = new ComponentName(context, Java.Lang.Class.FromType(typeof(SmsRecoveryJob)));
            if (!OperatingSystem.IsAndroidVersionAtLeast(24) || scheduler.GetPendingJob(PeriodicJobId) is null)
            {
                scheduler.Schedule(new JobInfo.Builder(PeriodicJobId, component)
                    .SetPersisted(true)!
                    .SetPeriodic(15 * 60 * 1000)!
                    .Build()!);
            }

            if (immediate && (!OperatingSystem.IsAndroidVersionAtLeast(24)
                              || scheduler.GetPendingJob(ImmediateJobId) is null))
            {
                scheduler.Schedule(new JobInfo.Builder(ImmediateJobId, component)
                    .SetPersisted(true)!
                    .SetMinimumLatency(30_000)!
                    .Build()!);
            }
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Warn("DonyshRecovery", $"Recovery scheduling failed: {exception.GetType().Name}");
        }
    }

    public override bool OnStartJob(JobParameters? parameters)
    {
        if (parameters is null || !SingleWorker.Wait(0)) return false;
        _running = new CancellationTokenSource();
        var cancellation = _running;
        _ = Task.Run(async () =>
        {
            try
            {
                var store = new SmsMonitorStore(FileSystem.AppDataDirectory);
                var context = ApplicationContext ?? this;
                var result = await SmsInboxRecovery.RecoverAsync(context, store,
                    cancellationToken: cancellation.Token);
                if (result.Added > 0)
                {
                    SmsReceivedNotifier.Notify();
                    SmsSyncJob.Schedule(context);
                    await TitleReminderNotifier.RefreshAsync(context);
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception exception)
            {
                global::Android.Util.Log.Error("DonyshRecovery", $"SMS recovery failed: {exception.GetType().Name}");
            }
            finally
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        if (!cancellation.IsCancellationRequested) JobFinished(parameters, false);
                    }
                    finally
                    {
                        cancellation.Dispose();
                        _running = null;
                        SingleWorker.Release();
                    }
                });
            }
        });
        return true;
    }

    public override bool OnStopJob(JobParameters? parameters)
    {
        _running?.Cancel();
        return true;
    }
}
