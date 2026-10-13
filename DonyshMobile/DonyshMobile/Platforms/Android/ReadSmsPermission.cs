using Microsoft.Maui.ApplicationModel;

namespace DonyshMobile.Platforms.Android;

public sealed class ReadSmsPermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
    [
        (global::Android.Manifest.Permission.ReadSms, true)
    ];
}
