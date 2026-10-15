namespace DonyshMobile.UI;

internal static class UiSafety
{
    public static void Log(Exception exception)
    {
        // Never include SMS text, pairing codes or connection tokens in diagnostics.
#if ANDROID
        global::Android.Util.Log.Warn("DonyshUi", $"Page operation failed: {exception.GetType().Name}");
#else
        System.Diagnostics.Debug.WriteLine($"Page operation failed: {exception.GetType().Name}");
#endif
    }

    public static async Task AlertAsync(Page page, string title, string message)
    {
        try { await page.DisplayAlert(title, message, "باشه"); }
        catch (Exception exception) { Log(exception); }
    }
}
