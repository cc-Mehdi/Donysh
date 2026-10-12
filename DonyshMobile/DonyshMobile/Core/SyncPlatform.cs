namespace DonyshMobile.Core;

public static class SyncPlatform
{
    public static void Schedule()
    {
#if ANDROID
        DonyshMobile.Platforms.Android.SmsSyncJob.Schedule(global::Android.App.Application.Context);
#endif
    }
}
