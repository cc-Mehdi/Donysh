namespace DonyshMobile.UI;

public static class UiPreferences
{
    public static int Theme
    {
        get => Math.Clamp(Preferences.Default.Get("ui-theme", 0), 0, 2);
        set
        {
            Preferences.Default.Set("ui-theme", Math.Clamp(value, 0, 2));
            ApplyTheme();
        }
    }

    public static bool Motion
    {
        get => Preferences.Default.Get("ui-motion", true);
        set => Preferences.Default.Set("ui-motion", value);
    }

    public static bool Haptics
    {
        get => Preferences.Default.Get("ui-haptics", true);
        set => Preferences.Default.Set("ui-haptics", value);
    }

    public static void ApplyTheme()
    {
        var selected = Theme switch { 1 => AppTheme.Light, 2 => AppTheme.Dark, _ => AppTheme.Unspecified };
        void Apply()
        {
            if (Application.Current is { } app && app.UserAppTheme != selected)
                app.UserAppTheme = selected;
        }
        if (MainThread.IsMainThread) Apply();
        else MainThread.BeginInvokeOnMainThread(Apply);
    }
}
