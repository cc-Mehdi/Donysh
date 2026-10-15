using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using DonyshMobile.Core;

namespace DonyshMobile
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            if (Intent?.GetBooleanExtra(Platforms.Android.TitleReminderNotifier.OpenTitlesExtra, false) == true)
                MainPage.RequestPendingTitles();
            base.OnCreate(savedInstanceState);
            // Native prompts inherit the same Persian font as the MAUI screens.
            Theme?.ApplyStyle(Resource.Style.DonyshFontOverlay, true);
        }

        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);
            if (intent?.GetBooleanExtra(Platforms.Android.TitleReminderNotifier.OpenTitlesExtra, false) != true) return;
            MainPage.RequestPendingTitles();
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (Shell.Current is null || !MainPage.ConsumePendingTitlesRequest()) return;
                await Shell.Current.Navigation.PushAsync(new PendingTitlesPage(
                    new SmsMonitorStore(FileSystem.AppDataDirectory), promptFirstMessage: true));
            });
        }
    }
}
