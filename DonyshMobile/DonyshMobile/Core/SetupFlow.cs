namespace DonyshMobile.Core;

public enum SetupStep
{
    Connection,
    Sender,
    Permission,
    Template,
    Complete
}

public static class SetupFlow
{
    public static SetupStep Resolve(bool isConnected, int senderCount, bool hasReceivePermission, bool hasTemplate)
    {
        if (!isConnected) return SetupStep.Connection;
        if (senderCount <= 0) return SetupStep.Sender;
        if (!hasReceivePermission) return SetupStep.Permission;
        return hasTemplate ? SetupStep.Complete : SetupStep.Template;
    }

    public static int Progress(SetupStep step) => step switch
    {
        SetupStep.Connection => 1,
        SetupStep.Sender or SetupStep.Permission => 2,
        SetupStep.Template => 3,
        _ => 3
    };
}
