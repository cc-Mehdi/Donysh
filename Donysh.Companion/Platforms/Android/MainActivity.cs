using Android.App;
using Android.Content.PM;

namespace Donysh.Companion;

[Activity(Theme = "@style/Maui.MainTheme.NoActionBar", MainLauncher = true, Exported = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                           ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity { }
