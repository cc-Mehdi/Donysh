using Microsoft.Maui.ApplicationModel;

namespace DonyshMobile.Platforms.Android;

public sealed class PostNotificationsPermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        OperatingSystem.IsAndroidVersionAtLeast(33)
            ? [(global::Android.Manifest.Permission.PostNotifications, true)]
            : [];
}
