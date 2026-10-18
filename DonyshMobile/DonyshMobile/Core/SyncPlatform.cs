namespace DonyshMobile.Core;

public static class SyncPlatform
{
    public static void Schedule()
    {
#if ANDROID
        var context = global::Android.App.Application.Context;
        DonyshMobile.Platforms.Android.SmsSyncJob.Schedule(context);
        DonyshMobile.Platforms.Android.SmsRecoveryJob.Schedule(context);
#endif
    }

    public static void ConfigureRecovery(bool enabled)
    {
        SmsSyncSettings.RecoverRecent = enabled;
#if ANDROID
        DonyshMobile.Platforms.Android.SmsRecoveryJob.Schedule(
            global::Android.App.Application.Context, immediate: enabled);
#endif
    }

    public static Task<SmsRecoveryResult> RecoverRecentAsync(
        SmsMonitorStore store,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
#if ANDROID
        return DonyshMobile.Platforms.Android.SmsInboxRecovery.RecoverAsync(
            global::Android.App.Application.Context, store, force, cancellationToken);
#else
        return Task.FromResult(new SmsRecoveryResult(0, 0, Skipped: true));
#endif
    }

    public static async Task<IReadOnlyList<SmsCaptureCandidate>> FindMissingMessagesAsync(
        SmsMonitorStore store,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        IReadOnlyCollection<string> senderAddresses,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
#if ANDROID
        var candidates = await DonyshMobile.Platforms.Android.SmsInboxRecovery.FindAsync(
            global::Android.App.Application.Context, fromUtc, toUtc, senderAddresses, cancellationToken);
        return await store.FindUnstoredCandidatesAsync(candidates, cancellationToken);
#else
        await Task.CompletedTask;
        return [];
#endif
    }
}
