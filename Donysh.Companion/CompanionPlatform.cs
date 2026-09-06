namespace Donysh.Companion;

// Native SMS APIs must never be loaded by the Windows preview build.
public static class CompanionPlatform
{
#if ANDROID
    public static bool SupportsSms => true;
    public static void Schedule() => SyncJob.Schedule(Android.App.Application.Context);
    public static Task<PermissionStatus> RequestSmsPermissionAsync() => Permissions.RequestAsync<ReceiveSmsPermission>();
#else
    public static bool SupportsSms => false;
    public static void Schedule() { }
    public static Task<PermissionStatus> RequestSmsPermissionAsync() => Task.FromResult(PermissionStatus.Denied);
#endif
}
