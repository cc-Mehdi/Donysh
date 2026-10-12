using System.Collections.Concurrent;
using Android.App;
using Android.App.Job;
using Android.Content;
using DonyshMobile.Core;

namespace DonyshMobile.Platforms.Android;

[Service(Permission = "android.permission.BIND_JOB_SERVICE", Exported = false)]
public sealed class SmsSyncJob : JobService
{
    private const int WatchdogJobId = 6202;
    private static readonly int[] ImmediateJobIds = [6201, 6203];
    private static readonly SemaphoreSlim SingleWorker = new(1, 1);
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _running = new();

    public static void Schedule(Context context) => Schedule(context, null, 60_000);

    private static void Schedule(Context context, int? previousJobId, long minimumLatencyMs)
    {
        var scheduler = (JobScheduler?)context.GetSystemService(JobSchedulerService);
        if (scheduler is null) return;
        var component = new ComponentName(context, Java.Lang.Class.FromType(typeof(SmsSyncJob)));

        if (!OperatingSystem.IsAndroidVersionAtLeast(24) || scheduler.GetPendingJob(WatchdogJobId) is null)
        {
            scheduler.Schedule(new JobInfo.Builder(WatchdogJobId, component)
                .SetRequiredNetworkType(NetworkType.Any)!
                .SetPersisted(true)!
                .SetPeriodic(15 * 60 * 1000)!
                .Build()!);
        }

        var jobId = ImmediateJobIds.FirstOrDefault(id => id != previousJobId
            && (!OperatingSystem.IsAndroidVersionAtLeast(24) || scheduler.GetPendingJob(id) is null));
        if (jobId == 0 || SingleWorker.CurrentCount == 0) return;
        scheduler.Schedule(new JobInfo.Builder(jobId, component)
            .SetRequiredNetworkType(NetworkType.Any)!
            .SetPersisted(true)!
            .SetMinimumLatency(Math.Max(60_000, minimumLatencyMs))!
            .SetBackoffCriteria(60_000, BackoffPolicy.Exponential)!
            .Build()!);
    }

    public override bool OnStartJob(JobParameters? parameters)
    {
        if (parameters is null || !SingleWorker.Wait(0)) return false;
        var cancellation = new CancellationTokenSource();
        _running[parameters.JobId] = cancellation;
        _ = Task.Run(async () =>
        {
            var shouldRetry = false;
            var nextDelayMs = 60_000L;
            try
            {
                var store = new SmsMonitorStore(FileSystem.AppDataDirectory);
                var connection = await MobileConnectionStore.LoadAsync();
                var result = await new MobileApiClient().SendNextBatchAsync(
                    store, connection, DateTimeOffset.UtcNow, cancellation.Token);
                var summary = await store.GetSyncSummaryAsync(cancellation.Token);
                shouldRetry = summary.Pending > 0 && result != SyncRunResult.ConnectionRequired;
                nextDelayMs = Math.Max(60_000, (long)(summary.NextAttemptAtUtc - DateTimeOffset.UtcNow).TotalMilliseconds);
                SmsReceivedNotifier.NotifySyncChanged();
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception exception)
            {
                global::Android.Util.Log.Error("DonyshSync", exception.ToString());
                shouldRetry = true;
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
                        _running.TryRemove(parameters.JobId, out _);
                        cancellation.Dispose();
                        SingleWorker.Release();
                        if (shouldRetry && ApplicationContext is { } context)
                            Schedule(context, parameters.JobId, nextDelayMs);
                    }
                });
            }
        });
        return true;
    }

    public override bool OnStopJob(JobParameters? parameters)
    {
        if (parameters is not null && _running.TryGetValue(parameters.JobId, out var cancellation))
            cancellation.Cancel();
        return true;
    }
}

[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter([Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced])]
public sealed class SyncRestartReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is not null) SmsSyncJob.Schedule(context);
    }
}
