namespace DonyshMobile.Core;

public enum StartupDestination
{
    Login,
    Setup,
    Home
}

public static class StartupFlow
{
    public static StartupDestination Resolve(bool isConnected, bool setupFinishedForAccount, SetupStep setupStep)
    {
        if (!isConnected) return StartupDestination.Login;
        if (setupFinishedForAccount || setupStep == SetupStep.Complete) return StartupDestination.Home;
        return StartupDestination.Setup;
    }
}
