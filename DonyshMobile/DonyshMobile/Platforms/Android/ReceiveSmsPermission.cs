using Microsoft.Maui.ApplicationModel;

namespace DonyshMobile.Platforms.Android;

public sealed class ReceiveSmsPermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
    [
        (global::Android.Manifest.Permission.ReceiveSms, true)
    ];
}
