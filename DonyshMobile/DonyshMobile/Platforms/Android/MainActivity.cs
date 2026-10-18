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
        private bool _openingTitles;
        protected override void OnCreate(Bundle? savedInstanceState)
        {
#if DEBUG
            // Only a separately installed QA package can enter the synthetic-data test flow.
            Diagnostics.TemplateFlowSmokeCheck.IsRequested = PackageName == "ir.donysh.mobile.qa"
                && Intent?.GetBooleanExtra("donysh_template_check", false) == true;
#endif
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
                if (Shell.Current is not { } shell) return;
                if (_openingTitles || shell.CurrentPage is PendingTitlesPage)
                {
                    MainPage.ConsumePendingTitlesRequest();
                    return;
                }
                if (!MainPage.ConsumePendingTitlesRequest()) return;
                _openingTitles = true;
                try
                {
                    await shell.Navigation.PushAsync(new PendingTitlesPage(
                        new SmsMonitorStore(FileSystem.AppDataDirectory)));
                }
                catch (Exception exception)
                {
                    MainPage.RequestPendingTitles();
                    global::Android.Util.Log.Warn("DonyshTitle", $"Title navigation failed: {exception.GetType().Name}");
                }
                finally { _openingTitles = false; }
            });
        }
    }
}
