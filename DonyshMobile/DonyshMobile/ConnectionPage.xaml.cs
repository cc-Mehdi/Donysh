using DonyshMobile.Core;
using DonyshMobile.UI;

namespace DonyshMobile;

public partial class ConnectionPage : ContentPage
{
    private readonly SmsMonitorStore _store;
    private readonly MobileApiClient _api = new();
    private bool _busy, _settingsLoaded, _entered, _showPairing, _connected;

    public ConnectionPage(SmsMonitorStore store)
    {
        _store = store;
        InitializeComponent();
        ServerEntry.Text = MobileConnectionStore.Server;
        ServerPanel.IsVisible = ServerEntry.Text != "https://donysh.ir";
        RequireTitleSwitch.IsToggled = SmsSyncSettings.RequireTitle;
        ThemePicker.SelectedIndex = UiPreferences.Theme;
        MotionSwitch.IsToggled = UiPreferences.Motion;
        HapticsSwitch.IsToggled = UiPreferences.Haptics;
        NotificationSettingsButton.IsVisible = DeviceInfo.Platform == DevicePlatform.Android;
        _settingsLoaded = true;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        SmsReceivedNotifier.SyncChanged += OnSyncChanged;
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
        await RefreshAsync();
        if (!_entered) { _entered = true; await UiMotion.EnterAsync(ConnectionContent); }
    }

    protected override void OnDisappearing()
    {
        SmsReceivedNotifier.SyncChanged -= OnSyncChanged;
        Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
        PairCodeEntry.Text = string.Empty;
        base.OnDisappearing();
    }

    private void OnSyncChanged(object? sender, EventArgs e) => Dispatcher.Dispatch(async () => await RefreshAsync());
    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e) => Dispatcher.Dispatch(async () => await RefreshAsync());
    private void OnChangeConnectionClicked(object? sender, EventArgs e) { _showPairing = !_showPairing; PairingCard.IsVisible = _showPairing || !_connected; }
    private void OnToggleServerClicked(object? sender, EventArgs e) => ServerPanel.IsVisible = !ServerPanel.IsVisible;
    private async void OnPendingTitlesClicked(object? sender, EventArgs e) => await Navigation.PushAsync(new PendingTitlesPage(_store));
    private void OnNotificationSettingsClicked(object? sender, EventArgs e) => AppInfo.ShowSettingsUI();
    private void OnThemeChanged(object? sender, EventArgs e) { if (_settingsLoaded) UiPreferences.Theme = ThemePicker.SelectedIndex; }
    private void OnMotionToggled(object? sender, ToggledEventArgs e) { if (_settingsLoaded) UiPreferences.Motion = e.Value; }
    private void OnHapticsToggled(object? sender, ToggledEventArgs e) { if (_settingsLoaded) UiPreferences.Haptics = e.Value; }

    private void OnRequireTitleToggled(object? sender, ToggledEventArgs e)
    {
        if (!_settingsLoaded) return;
        SmsSyncSettings.RequireTitle = e.Value;
        SyncPlatform.Schedule();
#if ANDROID
        _ = DonyshMobile.Platforms.Android.TitleReminderNotifier.RefreshAsync(global::Android.App.Application.Context);
#endif
    }

    private async void OnConnectClicked(object? sender, EventArgs e)
    {
        if (_busy) return;
        await RunBusyAsync(async () =>
        {
            var server = MobileApiClient.NormalizeServer(ServerEntry.Text);
            if (server is null)
            {
                await DisplayAlert("آدرس معتبر نیست", "یک نشانی معتبر و امن برای سایت وارد کنید؛ مانند https://donysh.ir", "باشه");
                return;
            }
            var host = new Uri(server).Host;
            if (!host.Equals("donysh.ir", StringComparison.OrdinalIgnoreCase)
                && !await DisplayAlert("بررسی مقصد اتصال", $"کد اتصال و پیام‌های انتخاب‌شده به «{host}» فرستاده می‌شوند. این سایت مورد اعتماد شماست؟", "اتصال", "انصراف")) return;
            await MobileApiClient.RunWithSyncLockAsync(async () =>
            {
            var queue = await _store.GetSyncSummaryAsync();
            if (queue.Pending > 0 && !await DisplayAlert("مقصد پیام‌های منتظر",
                    $"{UiText.Digits(queue.Pending)} پیام هنوز روی گوشی منتظر ارسال است. با اتصال جدید، این پیام‌ها به فضای مالیِ کد جدید ارسال می‌شوند. ادامه می‌دهید؟", "ادامهٔ اتصال", "انصراف")) return;
            try
            {
                var result = await _api.PairAsync(server, PairCodeEntry.Text ?? string.Empty);
                if (result is null)
                {
                    await DisplayAlert("کد پذیرفته نشد", "کد اتصال نامعتبر یا منقضی شده است. در سایت یک کد تازه بسازید.", "باشه");
                    return;
                }
                await MobileConnectionStore.SaveAsync(new MobileConnection(server, result.Token, $"{result.WorkspaceName} / {result.CategoryName}"));
                PairCodeEntry.Text = string.Empty;
                _showPairing = false;
                UiMotion.Feedback();
                SyncPlatform.Schedule();
                await DisplayAlert("گوشی متصل شد", $"مقصد پیام‌ها: {result.WorkspaceName} / {result.CategoryName}", "باشه");
            }
            catch (ArgumentException)
            {
                await DisplayAlert("کد را بررسی کنید", "کد اتصال ۳۲ نویسه دارد. آن را کامل از سایت کپی کنید.", "باشه");
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidDataException)
            {
                await DisplayAlert("اتصال کامل نشد", "اینترنت، آدرس سایت و کد اتصال را بررسی کنید و دوباره تلاش کنید.", "باشه");
            }
            });
        });
    }

    private async void OnSyncClicked(object? sender, EventArgs e)
    {
        if (_busy) return;
        await RunBusyAsync(async () =>
        {
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                await DisplayAlert("منتظر اینترنت", "پیام‌ها روی گوشی محفوظ‌اند و پس از اتصال دوباره ارسال می‌شوند.", "باشه");
                return;
            }
            var untitled = await _store.GetUntitledMessagesAsync();
            if (SmsSyncSettings.RequireTitle && untitled.Count > 0
                && !await DisplayAlert("ارسال دستی بدون عنوان", "پیام بی‌عنوان هم در صف وجود دارد. در این نوبت اجازه می‌دهید بدون عنوان ارسال شود؟", "ارسال دستی", "انصراف")) return;
            var result = await _api.SendNextBatchAsync(_store, MobileConnectionStore.LoadAsync, DateTimeOffset.UtcNow, manualSend: true);
            if (result == SyncRunResult.ConnectionRequired)
                await DisplayAlert("اتصال لازم است", "در سایت یک کد اتصال تازه بسازید و وارد کنید.", "باشه");
            else if (result == SyncRunResult.NothingToSend)
                await DisplayAlert("صف فعلاً آماده نیست", "پیام منتظری وجود ندارد یا هنوز یک دقیقه از آخرین نوبت نگذشته است.", "باشه");
            else if (result == SyncRunResult.Sent) UiMotion.Feedback();
            SyncPlatform.Schedule();
        });
    }

    private async void OnDisconnectClicked(object? sender, EventArgs e)
    {
        if (_busy || !await DisplayAlert("قطع اتصال این گوشی", "اطلاعات اتصال از گوشی پاک می‌شود؛ پیام‌های منتظر باقی می‌مانند. برای لغو کامل دسترسی گوشی، آن را در بخش «همراه دونیش» سایت هم غیرفعال کنید.", "قطع اتصال", "انصراف")) return;
        await RunBusyAsync(() => MobileApiClient.RunWithSyncLockAsync(() =>
        {
            MobileConnectionStore.Remove();
            _showPairing = false;
            return Task.CompletedTask;
        }));
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        _busy = true;
        UpdateActions();
        try { await action(); }
        catch (Exception) { await DisplayAlert("عملیات کامل نشد", "پیام‌ها حذف نشده‌اند. برنامه را دوباره باز کنید و اتصال را بررسی کنید.", "باشه"); }
        finally { _busy = false; await RefreshAsync(); UpdateActions(); }
    }

    private void UpdateActions()
    {
        BusyCard.IsVisible = BusyIndicator.IsRunning = _busy;
        ConnectButton.IsEnabled = PairCodeEntry.IsEnabled = ServerEntry.IsEnabled = !_busy;
        ManualSyncButton.IsEnabled = !_busy && _connected;
        DisconnectButton.IsEnabled = ChangeConnectionButton.IsEnabled = !_busy && _connected;
    }

    private async Task RefreshAsync()
    {
        try
        {
            var connection = await MobileConnectionStore.LoadAsync();
            var summary = await _store.GetSyncSummaryAsync();
            _connected = connection is not null;
            PairingCard.IsVisible = !_connected || _showPairing;
            ConnectionStatusLabel.Text = connection is null ? "گوشی هنوز متصل نشده است" : $"مقصد: {connection.Destination}";
            NetworkStatusLabel.Text = Connectivity.Current.NetworkAccess == NetworkAccess.Internet ? "اینترنت در دسترس است" : "بدون اینترنت؛ پیام‌های منتظر روی گوشی می‌مانند";
            QueueStatusLabel.Text = UiText.Digits($"{summary.Pending} منتظر · {summary.Sent} ارسال‌شده · {summary.Rejected} نیازمند بررسی");
            LastStatusLabel.Text = summary.Status;
        }
        catch (Exception)
        {
            LastStatusLabel.Text = "خواندن اطلاعات کامل نشد؛ داده‌ها حذف نشده‌اند. برنامه را دوباره باز کنید.";
        }
        UpdateActions();
    }
}
