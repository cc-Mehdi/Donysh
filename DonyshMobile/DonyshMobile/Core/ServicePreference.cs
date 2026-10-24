namespace DonyshMobile.Core;

public enum DonyshService { Finance, Life }

public static class ServicePreference
{
    private const string Key = "donysh-default-service-v1";
    public static DonyshService? Default
    {
        get => Preferences.Default.Get(Key, "") switch
        {
            "finance" => DonyshService.Finance,
            "life" => DonyshService.Life,
            _ => null
        };
        set => Preferences.Default.Set(Key, value switch
        {
            DonyshService.Finance => "finance",
            DonyshService.Life => "life",
            _ => ""
        });
    }
}
