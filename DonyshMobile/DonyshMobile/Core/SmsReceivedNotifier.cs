namespace DonyshMobile.Core;

public static class SmsReceivedNotifier
{
    public static event EventHandler? MessageStored;

    public static void Notify() => MessageStored?.Invoke(null, EventArgs.Empty);
}
