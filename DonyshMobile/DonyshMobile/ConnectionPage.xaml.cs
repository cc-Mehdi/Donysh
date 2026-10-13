using DonyshMobile.Core;

namespace DonyshMobile;

public partial class ConnectionPage : ContentPage
{
    private readonly SmsMonitorStore _store;
    private readonly MobileApiClient _api = new();
    private bool _busy;
    private bool _settingsLoaded;

    public ConnectionPage(SmsMonitorStore store)
    {
        _store = store;
        InitializeComponent();
        ServerEntry.Text = MobileConnectionStore.Server;
        RequireTitleSwitch.IsToggled = SmsSyncSettings.RequireTitle;
        NotificationSettingsButton.IsVisible = DeviceInfo.Platform == DevicePlatform.Android;
        _settingsLoaded = true;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        SmsReceivedNotifier.SyncChanged += OnSyncChanged;
        await RefreshAsync();
    }

    protected override void OnDisappearing()
    {
        SmsReceivedNotifier.SyncChanged -= OnSyncChanged;
        base.OnDisappearing();
    }

    private void OnSyncChanged(object? sender, EventArgs e) =>
        Dispatcher.Dispatch(async () => await RefreshAsync());

    private void OnRequireTitleToggled(object? sender, ToggledEventArgs e)
    {
        if (!_settingsLoaded) return;
        SmsSyncSettings.RequireTitle = e.Value;
        SyncPlatform.Schedule();
#if ANDROID
        _ = DonyshMobile.Platforms.Android.TitleReminderNotifier.RefreshAsync(global::Android.App.Application.Context);
#endif
    }

    private void OnNotificationSettingsClicked(object? sender, EventArgs e) => AppInfo.ShowSettingsUI();

    private async void OnConnectClicked(object? sender, EventArgs e)
    {
        if (_busy) return;
        await RunBusyAsync(async () =>
        {
            try
            {
                var server = MobileApiClient.NormalizeServer(ServerEntry.Text)
                    ?? throw new ArgumentException("آدرس سایت باید با https:// شروع شود.");
                var result = await _api.PairAsync(server, PairCodeEntry.Text ?? string.Empty);
                if (result is null)
                {
                    await DisplayAlert("اتصال ناموفق", "کد اتصال نامعتبر یا منقضی شده است.", "باشه");
                    return;
                }
                await MobileConnectionStore.SaveAsync(new MobileConnection(
                    server, result.Token, $"{result.WorkspaceName} / {result.CategoryName}"));
                PairCodeEntry.Text = string.Empty;
                SyncPlatform.Schedule();
                await DisplayAlert("متصل شد", $"مقصد پیام‌ها: {result.WorkspaceName} / {result.CategoryName}", "باشه");
            }
            catch (Exception exception) when (exception is ArgumentException or HttpRequestException or InvalidDataException)
            {
                await DisplayAlert("اتصال ناموفق", exception.Message, "باشه");
            }
        });
    }

    private async void OnSyncClicked(object? sender, EventArgs e)
    {
        if (_busy) return;
        await RunBusyAsync(async () =>
        {
            var connection = await MobileConnectionStore.LoadAsync();
            var result = await _api.SendNextBatchAsync(_store, connection, DateTimeOffset.UtcNow, manualSend: true);
            if (result == SyncRunResult.ConnectionRequired)
                await DisplayAlert("اتصال لازم است", "کد اتصال جدیدی از سایت Donysh دریافت و وارد کنید.", "باشه");
            else if (result == SyncRunResult.NothingToSend)
                await DisplayAlert("صف آماده نیست", "پیام منتظری وجود ندارد یا هنوز زمان ارسال بسته بعدی نرسیده است.", "باشه");
            SyncPlatform.Schedule();
        });
    }

    private async void OnDisconnectClicked(object? sender, EventArgs e)
    {
        if (!await DisplayAlert("قطع اتصال", "توکن این گوشی حذف شود؟ پیام‌های ارسال‌نشده روی گوشی باقی می‌مانند.", "قطع اتصال", "انصراف")) return;
        MobileConnectionStore.Remove();
        await RefreshAsync();
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        _busy = true;
        BusyIndicator.IsVisible = BusyIndicator.IsRunning = true;
        try { await action(); }
        catch (Exception) { await DisplayAlert("خطای همگام‌سازی", "عملیات کامل نشد؛ پیام‌های صف حذف نشده‌اند.", "باشه"); }
        finally
        {
            _busy = false;
            BusyIndicator.IsVisible = BusyIndicator.IsRunning = false;
            await RefreshAsync();
        }
    }

    private async Task RefreshAsync()
    {
        var connection = await MobileConnectionStore.LoadAsync();
        var summary = await _store.GetSyncSummaryAsync();
        ConnectionStatusLabel.Text = connection is null ? "متصل نشده" : $"متصل به {connection.Destination}";
        QueueStatusLabel.Text = $"{summary.Pending} در انتظار · {summary.Sent} ارسال‌شده · {summary.Rejected} ردشده";
        LastStatusLabel.Text = summary.Status;
    }
}
