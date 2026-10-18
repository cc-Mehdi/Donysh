namespace DonyshMobile.Core;

public static class SmsSyncSettings
{
    private const string RequireTitleKey = "sms-require-title-v1";
    private const string RecoverRecentKey = "sms-recover-recent-v1";
    private const string LastRecoveryKey = "sms-last-recovery-v1";

    public static bool RequireTitle
    {
        get => Preferences.Default.Get(RequireTitleKey, true);
        set => Preferences.Default.Set(RequireTitleKey, value);
    }

    public static bool RecoverRecent
    {
        get => Preferences.Default.Get(RecoverRecentKey, true);
        set => Preferences.Default.Set(RecoverRecentKey, value);
    }

    public static DateTimeOffset? LastRecoveryAtUtc
    {
        get
        {
            var unixMilliseconds = Preferences.Default.Get(LastRecoveryKey, 0L);
            if (unixMilliseconds <= 0) return null;
            try { return DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds); }
            catch (ArgumentOutOfRangeException) { return null; }
        }
        set
        {
            if (value is null) Preferences.Default.Remove(LastRecoveryKey);
            else Preferences.Default.Set(LastRecoveryKey, value.Value.ToUniversalTime().ToUnixTimeMilliseconds());
        }
    }
}
