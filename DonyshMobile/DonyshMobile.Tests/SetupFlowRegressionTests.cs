using DonyshMobile.Core;

internal static class SetupFlowRegressionTests
{
    public static void Run()
    {
        Assert(SetupFlow.Resolve(false, 0, false, false) == SetupStep.Connection,
            "A new user starts by connecting Donysh");
        Assert(SetupFlow.Resolve(false, 3, true, true) == SetupStep.Connection,
            "A missing connection remains the first required step");
        Assert(SetupFlow.Resolve(true, 0, false, false) == SetupStep.Sender,
            "Sender selection follows connection");
        Assert(SetupFlow.Resolve(true, 1, false, false) == SetupStep.Permission,
            "SMS permission is completed inside the sender stage");
        Assert(SetupFlow.Resolve(true, 1, true, false) == SetupStep.Template,
            "Template creation follows sender monitoring");
        Assert(SetupFlow.Resolve(true, 1, true, true) == SetupStep.Complete,
            "Setup finishes only after connection, sender, permission and template");
        Assert(SetupFlow.Progress(SetupStep.Connection) == 1
               && SetupFlow.Progress(SetupStep.Sender) == 2
               && SetupFlow.Progress(SetupStep.Permission) == 2
               && SetupFlow.Progress(SetupStep.Template) == 3
               && SetupFlow.Progress(SetupStep.Complete) == 3,
            "Progress keeps permission within the sender step");
        Console.WriteLine("PASS Setup flow order and progress regressions");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
