using Android.App;
using Android.App.Job;
using Android.Content;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Donysh.Sms;

namespace Donysh.Companion;

[Service(Permission = "android.permission.BIND_JOB_SERVICE", Exported = true)]
public class SyncJob : JobService
{
    private const int ImmediateId = 4101, WatchdogId = 4102;
    private static readonly SemaphoreSlim SingleWorker = new(1, 1);
    private readonly Dictionary<int, CancellationTokenSource> running = new();

    public static void Schedule(Context context)
    {
        if (!CompanionSettings.Enabled) return;
        var scheduler = (JobScheduler?)context.GetSystemService(JobSchedulerService);
        if (scheduler is null) return;
        var component = new ComponentName(context, Java.Lang.Class.FromType(typeof(SyncJob)));
        // The periodic watchdog also covers process death between acknowledging a batch and scheduling its successor.
        if (scheduler.GetPendingJob(WatchdogId) is null)
            scheduler.Schedule(new JobInfo.Builder(WatchdogId, component).SetRequiredNetworkType(NetworkType.Any)!
                .SetPersisted(true)!.SetPeriodic(15 * 60 * 1000)!.Build()!);
        if (scheduler.GetPendingJob(ImmediateId) is not null || SingleWorker.CurrentCount == 0) return;
        try {
            var state = CompanionSettings.Queue.Snapshot();
            if (!state.Items.Any(x => !x.Held)) return;
            var delay = Math.Max(1000, (state.NextAttempt - DateTimeOffset.UtcNow).TotalMilliseconds);
            scheduler.Schedule(new JobInfo.Builder(ImmediateId, component).SetRequiredNetworkType(NetworkType.Any)!
                .SetPersisted(true)!.SetMinimumLatency((long)delay)!.Build()!);
        } catch (Exception) { Preferences.Set("device-error", "صف قابل خواندن نیست؛ اطلاعات آن پاک نشده است."); }
    }

    public override bool OnStartJob(JobParameters? parameters)
    {
        if (parameters is null || !CompanionSettings.Enabled || !SingleWorker.Wait(0)) return false;
        var cancellation = new CancellationTokenSource();
        running[parameters.JobId] = cancellation;
        _ = Task.Run(async () => {
            try { await SendOneBatchAsync(cancellation.Token); }
            catch (Exception) {
                try { CompanionSettings.Queue.Fail(DateTimeOffset.UtcNow); }
                catch (Exception) { Preferences.Set("device-error", "صف قابل ذخیره نیست؛ اطلاعات را در گوشی بررسی کنید."); }
            }
            finally {
                MainThread.BeginInvokeOnMainThread(() => {
                    try {
                        // Reschedule the one-shot job through Android, not by replacing its still-running ID.
                        // The durable NextAttempt gate also applies to Android's retries and watchdog wakeups.
                        var pending = CompanionSettings.Enabled && CompanionSettings.Queue.Snapshot().Items.Any(x => !x.Held);
                        if (!cancellation.IsCancellationRequested)
                            JobFinished(parameters, pending && parameters.JobId == ImmediateId);
                    } catch (Exception) {
                        if (!cancellation.IsCancellationRequested) JobFinished(parameters, true);
                    } finally {
                        running.Remove(parameters.JobId);
                        cancellation.Dispose();
                        SingleWorker.Release();
                    }
                });
            }
        });
        return true;
    }

    public override bool OnStopJob(JobParameters? parameters)
    {
        if (parameters is not null && running.TryGetValue(parameters.JobId, out var cancellation)) cancellation.Cancel();
        return CompanionSettings.Enabled;
    }

    private static async Task SendOneBatchAsync(CancellationToken ct)
    {
        var connection = await CompanionSettings.ConnectionAsync();
        if (!CompanionSettings.Enabled) return;
        if (connection is null) { CompanionSettings.Enabled = false; return; }
        var queue = CompanionSettings.Queue;
        var batch = queue.Take(DateTimeOffset.UtcNow);
        if (batch.Count == 0) return;
        using var client = CompanionSettings.Http();
        using var request = new HttpRequestMessage(HttpMethod.Post, connection.Server + "/api/mobile/sms") {
            Content = JsonContent.Create(new SmsBatch(batch))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.Token);
        using var response = await client.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) {
            CompanionSettings.Enabled = false;
            Preferences.Set("device-error", "دسترسی اتصال یا دسته مقصد معتبر نیست. اتصال را در سایت بررسی کنید؛ صف محفوظ است.");
            return;
        }
        if (!response.IsSuccessStatusCode) {
            var retry = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow);
            queue.Fail(DateTimeOffset.UtcNow, retry);
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.NotFound) {
                CompanionSettings.Enabled = false;
                Preferences.Set("device-error", "نسخه API یا داده ارسالی سازگار نیست؛ ارسال متوقف شد و صف محفوظ است.");
            }
            return;
        }
        var result = await response.Content.ReadFromJsonAsync<SmsBatchResult>(ct);
        if (result?.Results is null || result.Results.Count != batch.Count
            || result.Results.Select(x => x.Key).Distinct().Count() != batch.Count
            || result.Results.Any(x => !batch.Any(b => b.Key == x.Key) || x.Status is not ("created" or "duplicate" or "rejected")))
            throw new InvalidDataException("Unconfirmed acknowledgement");
        queue.Complete(result.Results, DateTimeOffset.UtcNow);
    }
}
