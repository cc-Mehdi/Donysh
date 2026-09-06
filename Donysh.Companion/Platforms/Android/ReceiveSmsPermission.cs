namespace Donysh.Companion;

public sealed class ReceiveSmsPermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions => [(Android.Manifest.Permission.ReceiveSms, true)];
}
