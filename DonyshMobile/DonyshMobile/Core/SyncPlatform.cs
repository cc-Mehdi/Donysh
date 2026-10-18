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
}
