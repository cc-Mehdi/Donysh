namespace DonyshMobile.Core;

public static class SmsSyncSettings
{
    private const string RequireTitleKey = "sms-require-title-v1";

    public static bool RequireTitle
    {
        get => Preferences.Default.Get(RequireTitleKey, true);
        set => Preferences.Default.Set(RequireTitleKey, value);
    }
}
