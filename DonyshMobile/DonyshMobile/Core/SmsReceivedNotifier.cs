namespace DonyshMobile.Core;

public static class SmsReceivedNotifier
{
    public static event EventHandler? MessageStored;
    public static event EventHandler? SyncChanged;

    public static void Notify() => MessageStored?.Invoke(null, EventArgs.Empty);
    public static void NotifySyncChanged() => SyncChanged?.Invoke(null, EventArgs.Empty);
}
