using DonyshMobile.Core;
using DonyshMobile.UI;
#if ANDROID
using DonyshMobile.Platforms.Android;
#endif

namespace DonyshMobile;

public partial class ConnectionPage : DonyshPage
{
    private readonly SmsMonitorStore _store;
    private readonly MobileApiClient _api;
    private readonly IConnectivity _connectivity;
    private readonly Func<Task<MobileConnection?>> _loadConnectionAsync;
    private readonly Func<string, string, string, Task> _showAlertAsync;
    private readonly Func<string, string, string, string, Task<bool>> _confirmAsync;
    private readonly bool _setupMode;
    private bool _busy, _settingsLoaded, _entered, _showPairing, _connected;
    private bool _visible, _connectivitySubscribed, _openingTitles;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public ConnectionPage(SmsMonitorStore store)
        : this(store, new MobileApiClient(), Connectivity.Current, MobileConnectionStore.LoadAsync)
    {
    }

    public ConnectionPage(SmsMonitorStore store, bool setupMode)
        : this(store, new MobileApiClient(), Connectivity.Current, MobileConnectionStore.LoadAsync,
            setupMode: setupMode)
    {
    }

    // The runtime regression check supplies isolated credentials, network state and HTTP transport.
    internal ConnectionPage(SmsMonitorStore store, MobileApiClient api, IConnectivity connectivity,
        Func<Task<MobileConnection?>> loadConnectionAsync,
        Func<string, string, string, Task>? showAlertAsync = null,
        Func<string, string, string, string, Task<bool>>? confirmAsync = null,
        bool setupMode = false)
    {
        _store = store;
        _api = api;
        _connectivity = connectivity;
        _loadConnectionAsync = loadConnectionAsync;
        _showAlertAsync = showAlertAsync ?? DisplayAlert;
        _confirmAsync = confirmAsync ?? DisplayAlert;
        _setupMode = setupMode;
        InitializeComponent();
        ServerEntry.Text = MobileConnectionStore.Server;
        ServerPanel.IsVisible = ServerEntry.Text != "https://donysh.ir";
        RequireTitleSwitch.IsToggled = SmsSyncSettings.RequireTitle;
        RecoverRecentSwitch.IsToggled = SmsSyncSettings.RecoverRecent;
        ThemePicker.SelectedIndex = UiPreferences.Theme;
        MotionSwitch.IsToggled = UiPreferences.Motion;
        HapticsSwitch.IsToggled = UiPreferences.Haptics;
        NotificationSettingsButton.IsVisible = DeviceInfo.Platform == DevicePlatform.Android;
        if (_setupMode)
        {
            Title = "ورود به دُنیش";
            PageHeadingLabel.Text = "وارد حساب دُنیش شوید";
            PageDescriptionLabel.Text = "با حساب دُنیش وارد شوید. اگر این نخستین بار است، راه‌اندازی پس از ورود آغاز می‌شود.";
            ConnectionStatusCard.IsVisible = false;
            TitleSettingsCard.IsVisible = false;
            RecoverySettingsCard.IsVisible = false;
            AppearanceCard.IsVisible = false;
            PrivacyCard.IsVisible = false;
            PairingCard.IsVisible = true;
            LoginBrand.IsVisible = true;
        }
        _settingsLoaded = true;
        UpdateActions();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_visible) return;
        _visible = true;
        SmsReceivedNotifier.SyncChanged += OnSyncChanged;
        try
        {
            _connectivity.ConnectivityChanged += OnConnectivityChanged;
            _connectivitySubscribed = true;
        }
        catch (Exception exception) { UiSafety.Log(exception); }
        try
        {
            await RefreshAsync();
            await RefreshRecoveryStatusAsync();
            if (!_entered) { _entered = true; await UiMotion.EnterAsync(ConnectionContent); }
        }
        catch (Exception exception) { UiSafety.Log(exception); }
        finally { await CompletePageLoadingAsync(); }
    }

    protected override void OnDisappearing()
    {
        _visible = false;
        SmsReceivedNotifier.SyncChanged -= OnSyncChanged;
        if (_connectivitySubscribed)
        {
            try { _connectivity.ConnectivityChanged -= OnConnectivityChanged; }
            catch (Exception exception) { UiSafety.Log(exception); }
            _connectivitySubscribed = false;
        }
        PairCodeEntry.Text = string.Empty;
        PasswordEntry.Text = string.Empty;
        base.OnDisappearing();
    }

    private void OnSyncChanged(object? sender, EventArgs e) => QueueRefresh();
    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e) => QueueRefresh();
    private void QueueRefresh()
    {
        if (!_visible) return;
        try { Dispatcher.Dispatch(async () => { if (_visible) await RefreshAsync(); }); }
        catch (Exception exception) { UiSafety.Log(exception); }
    }
    private void OnChangeConnectionClicked(object? sender, EventArgs e) { _showPairing = !_showPairing; PairingCard.IsVisible = _showPairing || !_connected; }
    private void OnToggleServerClicked(object? sender, EventArgs e) => ServerPanel.IsVisible = !ServerPanel.IsVisible;
    private void OnToggleLegacyPairingClicked(object? sender, EventArgs e) =>
        LegacyPairingPanel.IsVisible = !LegacyPairingPanel.IsVisible;
    private async void OnPendingTitlesClicked(object? sender, EventArgs e)
    {
        if (_openingTitles) return;
        _openingTitles = true;
        try { await Navigation.PushAsync(new PendingTitlesPage(_store)); }
        catch (Exception exception)
        {
            UiSafety.Log(exception);
            await AlertAsync("صفحه باز نشد", "دوباره برای عنوان‌گذاری پیام‌ها تلاش کنید.", "باشه");
        }
        finally { _openingTitles = false; }
    }
    private async void OnNotificationSettingsClicked(object? sender, EventArgs e)
    {
        try { AppInfo.ShowSettingsUI(); }
        catch (Exception exception)
        {
            UiSafety.Log(exception);
            await AlertAsync("تنظیمات باز نشد", "از تنظیمات گوشی، بخش برنامه‌ها و سپس دُنیش را باز کنید.", "باشه");
        }
    }
    private async void OnThemeChanged(object? sender, EventArgs e)
    {
        if (!_settingsLoaded) return;
        var selected = Math.Clamp(ThemePicker.SelectedIndex, 0, 2);
        try
        {
            UiPreferences.Theme = selected;
            // Re-read the persisted value after the visual tree has applied the new
            // application theme. This prevents the custom picker reverting to its
            // initial "system" selection during Android's theme redraw.
            await Task.Yield();
            ThemePicker.SelectedIndex = UiPreferences.Theme;
        }
        catch (Exception exception)
        {
            UiSafety.Log(exception);
            await AlertAsync("پوسته تغییر نکرد", "دوباره یکی از حالت‌های روشن، تیره یا هماهنگ با گوشی را انتخاب کنید.", "باشه");
        }
    }
    private async void OnMotionToggled(object? sender, ToggledEventArgs e) => await SaveSettingAsync(() => UiPreferences.Motion = e.Value);
    private async void OnHapticsToggled(object? sender, ToggledEventArgs e) => await SaveSettingAsync(() => UiPreferences.Haptics = e.Value);

    private async void OnRecoverRecentToggled(object? sender, ToggledEventArgs e)
    {
        if (!_settingsLoaded) return;
        try
        {
            SyncPlatform.ConfigureRecovery(e.Value);
#if ANDROID
            if (e.Value && await Permissions.CheckStatusAsync<ReadSmsPermission>() != PermissionStatus.Granted)
                await Permissions.RequestAsync<ReadSmsPermission>();
#endif
            await RefreshRecoveryStatusAsync();
        }
        catch (Exception exception)
        {
            UiSafety.Log(exception);
            RecoveryStatusLabel.Text = "تنظیم بازیابی کامل نشد؛ دوباره تلاش کنید.";
        }
    }

    private async Task RefreshRecoveryStatusAsync()
    {
        if (!SmsSyncSettings.RecoverRecent)
        {
            RecoveryStatusLabel.Text = "خاموش است؛ فقط پیامک‌هایی که همان لحظه دریافت شوند ثبت می‌شوند.";
            return;
        }
#if ANDROID
        var permission = await Permissions.CheckStatusAsync<ReadSmsPermission>();
        RecoveryStatusLabel.Text = permission == PermissionStatus.Granted
            ? "فعال است؛ بررسی خودکار و بدون ثبت دوبارهٔ پیام‌ها انجام می‌شود."
            : "روشن است؛ برای شروع بازیابی، اجازهٔ خواندن پیامک را فعال کنید.";
#else
        await Task.CompletedTask;
        RecoveryStatusLabel.Text = "بازیابی پیامک روی گوشی اندرویدی انجام می‌شود.";
#endif
    }

    private async void OnRequireTitleToggled(object? sender, ToggledEventArgs e)
    {
        await SaveSettingAsync(() =>
        {
            SmsSyncSettings.RequireTitle = e.Value;
            SyncPlatform.Schedule();
#if ANDROID
            _ = DonyshMobile.Platforms.Android.TitleReminderNotifier.RefreshAsync(global::Android.App.Application.Context);
#endif
        });
    }

    private async Task SaveSettingAsync(Action action)
    {
        if (!_settingsLoaded) return;
        try { action(); }
        catch (Exception exception)
        {
            UiSafety.Log(exception);
            await AlertAsync("تنظیم ذخیره نشد", "دوباره تلاش کنید؛ پیام‌های ذخیره‌شده تغییری نکرده‌اند.", "باشه");
        }
    }

    private async void OnLoginClicked(object? sender, EventArgs e)
    {
        if (_busy) return;
        var loggedIn = false;
        await RunBusyAsync(async () =>
        {
            var server = MobileApiClient.NormalizeServer(ServerEntry.Text);
            if (server is null)
            {
                await AlertAsync("آدرس معتبر نیست", "یک نشانی معتبر و امن برای سایت وارد کنید؛ مانند https://donysh.ir", "باشه");
                return;
            }
            var email = EmailEntry.Text?.Trim() ?? string.Empty;
            var password = PasswordEntry.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
            {
                await AlertAsync("اطلاعات ورود کامل نیست", "رایانامه و گذرواژهٔ حساب دُنیش را وارد کنید.", "باشه");
                return;
            }
            var host = new Uri(server).Host;
            if (!host.Equals("donysh.ir", StringComparison.OrdinalIgnoreCase)
                && !await ConfirmAsync("بررسی مقصد ورود", $"اطلاعات ورود به «{host}» فرستاده می‌شود. این سایت مورد اعتماد شماست؟", "ورود", "انصراف")) return;
            await MobileApiClient.RunWithSyncLockAsync(async () =>
            {
                var queue = await _store.GetSyncSummaryAsync();
                if (queue.Pending > 0 && !await ConfirmAsync("مقصد پیام‌های منتظر",
                        $"{UiText.Digits(queue.Pending)} پیام منتظر دارید. پس از ورود، این پیام‌ها به فضای مالی انتخاب‌شده فرستاده می‌شوند. ادامه می‌دهید؟",
                        "ادامه", "انصراف")) return;
                var installationId = await MobileConnectionStore.GetInstallationIdAsync();
                var previousConnection = await _loadConnectionAsync();
                var previousToken = previousConnection is not null
                    && MobileApiClient.NormalizeServer(previousConnection.Server) == server
                    ? previousConnection.Token : null;
                var result = await _api.LoginAsync(server, email, password, installationId,
                    string.IsNullOrWhiteSpace(DeviceInfo.Name) ? "گوشی همراه" : DeviceInfo.Name,
                    previousToken);
                PasswordEntry.Text = string.Empty;
                if (result is null)
                {
                    await AlertAsync("ورود انجام نشد", "رایانامه یا گذرواژه درست نیست؛ اگر چند بار تلاش کرده‌اید کمی صبر کنید.", "باشه");
                    return;
                }
                var workspaceId = result.WorkspaceId;
                var workspaceName = result.WorkspaceName;
                var categoryName = result.CategoryName;
                if (result.Workspaces.Count > 1)
                {
                    var labels = result.Workspaces.Select(x => x.Type == "shared" ? $"{x.Name} · مشترک" : $"{x.Name} · شخصی").ToList();
                    var choice = await DonyshSelectionSheet.ShowAsync(this, "فضای مالی پیش‌فرض", labels,
                        result.Workspaces.FindIndex(x => x.Id == result.WorkspaceId));
                    if (choice.HasValue && result.Workspaces[choice.Value].Id != workspaceId)
                    {
                        var provisional = new MobileConnection(server, result.Token,
                            $"{workspaceName} / {categoryName}", workspaceId, result.DisplayName);
                        var destination = await _api.ChangeDestinationAsync(provisional,
                            result.Workspaces[choice.Value].Id);
                        workspaceId = destination.WorkspaceId;
                        workspaceName = destination.WorkspaceName;
                        categoryName = destination.CategoryName;
                    }
                }
                await MobileConnectionStore.SaveAsync(new MobileConnection(server, result.Token,
                    $"{workspaceName} / {categoryName}", workspaceId, result.DisplayName, result.UserId));
                var restored = false;
                try { restored = await SmsConfigurationSync.TrySyncAsync(_store); }
                catch (Exception exception) { UiSafety.Log(exception); }
                _showPairing = false;
                UiMotion.Feedback();
                SyncPlatform.Schedule();
                if (!_setupMode)
                    await AlertAsync("ورود انجام شد", restored
                        ? $"فرستنده‌ها و قالب‌ها بازیابی شدند. فضای پیش‌فرض: {workspaceName}"
                        : $"فضای پیش‌فرض: {workspaceName}", "باشه");
                loggedIn = true;
            });
        });
        PasswordEntry.Text = string.Empty;
        if (loggedIn && _setupMode)
        {
            try { await Navigation.PopAsync(); }
            catch (Exception exception) { UiSafety.Log(exception); }
        }
    }

    private async void OnWorkspaceClicked(object? sender, EventArgs e)
    {
        if (_busy) return;
        await RunBusyAsync(async () => await ChooseWorkspaceAsync());
    }

    private async Task ChooseWorkspaceAsync()
    {
        var connection = await _loadConnectionAsync();
        if (connection is null) return;
        var list = await _api.GetWorkspacesAsync(connection);
        var labels = list.Workspaces.Select(x => x.Type == "shared" ? $"{x.Name} · مشترک" : $"{x.Name} · شخصی").ToList();
        var selected = await DonyshSelectionSheet.ShowAsync(this, "فضای مالی پیش‌فرض", labels,
            list.Workspaces.FindIndex(x => x.Id == list.WorkspaceId));
        if (!selected.HasValue) return;
        var chosen = list.Workspaces[selected.Value];
        if (chosen.Id == list.WorkspaceId) return;
        var queue = await _store.GetSyncSummaryAsync();
        if (queue.Pending > 0 && !await ConfirmAsync("تغییر مقصد پیام‌های منتظر",
                $"{UiText.Digits(queue.Pending)} پیام منتظر نیز به «{chosen.Name}» فرستاده می‌شود. ادامه می‌دهید؟",
                "تغییر فضا", "انصراف")) return;
        await MobileApiClient.RunWithSyncLockAsync(async () =>
        {
            var result = await _api.ChangeDestinationAsync(connection, chosen.Id);
            await MobileConnectionStore.SaveAsync(connection with
            {
                Destination = $"{result.WorkspaceName} / {result.CategoryName}",
                WorkspaceId = result.WorkspaceId
            });
        });
        SyncPlatform.Schedule();
        UiMotion.Feedback();
    }

    private async void OnConnectClicked(object? sender, EventArgs e)
    {
        if (_busy) return;
        var paired = false;
        await RunBusyAsync(async () =>
        {
            var server = MobileApiClient.NormalizeServer(ServerEntry.Text);
            if (server is null)
            {
                await AlertAsync("آدرس معتبر نیست", "یک نشانی معتبر و امن برای سایت وارد کنید؛ مانند https://donysh.ir", "باشه");
                return;
            }
            var host = new Uri(server).Host;
            if (!host.Equals("donysh.ir", StringComparison.OrdinalIgnoreCase)
                && !await ConfirmAsync("بررسی مقصد اتصال", $"کد اتصال و پیام‌های انتخاب‌شده به «{host}» فرستاده می‌شوند. این سایت مورد اعتماد شماست؟", "اتصال", "انصراف")) return;
            await MobileApiClient.RunWithSyncLockAsync(async () =>
            {
            var queue = await _store.GetSyncSummaryAsync();
            if (queue.Pending > 0 && !await ConfirmAsync("مقصد پیام‌های منتظر",
                    $"{UiText.Digits(queue.Pending)} پیام هنوز روی گوشی منتظر ارسال است. با اتصال جدید، این پیام‌ها به فضای مالیِ کد جدید ارسال می‌شوند. ادامه می‌دهید؟", "ادامهٔ اتصال", "انصراف")) return;
            try
            {
                var installationId = await MobileConnectionStore.GetInstallationIdAsync();
                var previousConnection = await _loadConnectionAsync();
                var previousToken = previousConnection is not null
                    && MobileApiClient.NormalizeServer(previousConnection.Server) == server
                    ? previousConnection.Token : null;
                var result = await _api.PairAsync(server, PairCodeEntry.Text ?? string.Empty,
                    installationId, previousToken);
                if (result is null)
                {
                    await AlertAsync("کد پذیرفته نشد", "کد اتصال نامعتبر یا منقضی شده است. در سایت یک کد تازه بسازید.", "باشه");
                    return;
                }
                await MobileConnectionStore.SaveAsync(new MobileConnection(server, result.Token,
                    $"{result.WorkspaceName} / {result.CategoryName}", UserId: result.UserId));
                var restored = false;
                try { restored = await SmsConfigurationSync.TrySyncAsync(_store); }
                catch (Exception exception) { UiSafety.Log(exception); }
                PairCodeEntry.Text = string.Empty;
                _showPairing = false;
                UiMotion.Feedback();
                SyncPlatform.Schedule();
                if (!_setupMode)
                    await AlertAsync("گوشی متصل شد", restored
                        ? $"فرستنده‌ها و قالب‌ها بازیابی شدند. مقصد پیام‌ها: {result.WorkspaceName} / {result.CategoryName}"
                        : $"مقصد پیام‌ها: {result.WorkspaceName} / {result.CategoryName}", "باشه");
                paired = true;
            }
            catch (ArgumentException)
            {
                await AlertAsync("کد را بررسی کنید", "کد اتصال ۳۲ نویسه دارد. آن را کامل از سایت کپی کنید.", "باشه");
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or System.Text.Json.JsonException or OperationCanceledException)
            {
                await AlertAsync("اتصال کامل نشد", "اینترنت، آدرس سایت و کد اتصال را بررسی کنید و دوباره تلاش کنید.", "باشه");
            }
            });
        });
        if (paired && _setupMode)
        {
            try { await Navigation.PopAsync(); }
            catch (Exception exception) { UiSafety.Log(exception); }
        }
    }

    private async void OnSyncClicked(object? sender, EventArgs e)
    {
        if (_busy) return;
        await RunBusyAsync(async () =>
        {
            var network = ReadNetworkAccess();
            if (network != NetworkAccess.Internet)
            {
                await AlertAsync(network == NetworkAccess.Unknown ? "وضعیت اینترنت مشخص نیست" : "منتظر اینترنت",
                    "پیام‌ها روی گوشی محفوظ‌اند. اتصال اینترنت را بررسی کنید و دوباره تلاش کنید.", "باشه");
                return;
            }
            var untitled = await _store.GetUntitledMessagesAsync();
            if (SmsSyncSettings.RequireTitle && untitled.Count > 0
                && !await ConfirmAsync("ارسال دستی بدون عنوان", "پیام بی‌عنوان هم در صف وجود دارد. در این نوبت اجازه می‌دهید بدون عنوان ارسال شود؟", "ارسال دستی", "انصراف")) return;
            var result = await _api.SendNextBatchAsync(_store, _loadConnectionAsync, DateTimeOffset.UtcNow, manualSend: true);
            if (result == SyncRunResult.ConnectionRequired)
                await AlertAsync("ورود دوباره لازم است", "نشست این گوشی پایان یافته است. دوباره با حساب دُنیش وارد شوید.", "باشه");
            else if (result == SyncRunResult.NothingToSend)
                await AlertAsync("صف فعلاً آماده نیست", "پیام منتظری وجود ندارد یا هنوز یک دقیقه از آخرین نوبت نگذشته است.", "باشه");
            else if (result == SyncRunResult.RetryScheduled)
                await AlertAsync("ارسال تأیید نشد", "پیام‌ها در صف باقی مانده‌اند و در نوبت بعد دوباره بررسی می‌شوند.", "باشه");
            else if (result == SyncRunResult.Sent) UiMotion.Feedback();
            SyncPlatform.Schedule();
        });
    }

    private async void OnDisconnectClicked(object? sender, EventArgs e)
    {
        if (_busy) return;
        await RunBusyAsync(async () =>
        {
            if (!await ConfirmAsync("قطع اتصال این گوشی", "اطلاعات اتصال از گوشی پاک می‌شود؛ پیام‌های منتظر باقی می‌مانند. برای لغو کامل دسترسی گوشی، آن را در بخش «همراه دُنیش» سایت هم غیرفعال کنید.", "قطع اتصال", "انصراف")) return;
            await MobileApiClient.RunWithSyncLockAsync(async () =>
            {
                var connection = await _loadConnectionAsync();
                if (connection is not null) await _api.LogoutAsync(connection);
                MobileConnectionStore.Remove();
                _showPairing = false;
            });
        });
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        try { UpdateActions(); await action(); }
        catch (Exception exception)
        {
            UiSafety.Log(exception);
            await AlertAsync("عملیات کامل نشد", "پیام‌ها حذف نشده‌اند. اتصال را بررسی کنید و دوباره تلاش کنید.", "باشه");
        }
        finally { _busy = false; await RefreshAsync(); }
    }

    private void UpdateActions()
    {
        BusyCard.IsVisible = BusyIndicator.IsRunning = _busy;
        ConnectButton.IsEnabled = LoginButton.IsEnabled = PairCodeEntry.IsEnabled = ServerEntry.IsEnabled = !_busy;
        EmailEntry.IsEnabled = PasswordEntry.IsEnabled = !_busy;
        ManualSyncButton.IsEnabled = !_busy && _connected;
        DisconnectButton.IsEnabled = ChangeConnectionButton.IsEnabled = WorkspaceButton.IsEnabled = !_busy && _connected;
    }

    private async Task RefreshAsync()
    {
        await _refreshGate.WaitAsync();
        try
        {
            var connection = await _loadConnectionAsync();
            var summary = await _store.GetSyncSummaryAsync();
            _connected = connection is not null;
            PairingCard.IsVisible = _setupMode || !_connected || _showPairing;
            ConnectionStatusLabel.Text = connection is null ? "گوشی هنوز متصل نشده است" : $"مقصد: {connection.Destination}";
            NetworkStatusLabel.Text = ReadNetworkAccess() switch
            {
                NetworkAccess.Internet => "اینترنت در دسترس است",
                NetworkAccess.Unknown => "وضعیت اینترنت مشخص نیست؛ پیام‌ها روی گوشی محفوظ‌اند",
                _ => "بدون اینترنت؛ پیام‌های منتظر روی گوشی می‌مانند"
            };
            QueueStatusLabel.Text = UiText.Digits($"{summary.Pending} منتظر · {summary.Sent} ارسال‌شده · {summary.Rejected} نیازمند بررسی");
            LastStatusLabel.Text = summary.Status;
        }
        catch (Exception exception)
        {
            UiSafety.Log(exception);
            LastStatusLabel.Text = "خواندن اطلاعات کامل نشد؛ داده‌ها حذف نشده‌اند. برنامه را دوباره باز کنید.";
        }
        finally
        {
            _refreshGate.Release();
            try { UpdateActions(); }
            catch (Exception exception) { UiSafety.Log(exception); }
        }
    }

    private NetworkAccess ReadNetworkAccess()
    {
        try { return _connectivity.NetworkAccess; }
        catch (Exception exception) { UiSafety.Log(exception); return NetworkAccess.Unknown; }
    }

    private async Task AlertAsync(string title, string message, string cancel)
    {
        try { await _showAlertAsync(title, message, cancel); }
        catch (Exception exception)
        {
            UiSafety.Log(exception);
            // Native dialogs may be unavailable while the activity is stopping.
            // Keep a local status instead of throwing out of an async UI handler.
            LastStatusLabel.Text = message;
        }
    }

    private async Task<bool> ConfirmAsync(string title, string message, string accept, string cancel)
    {
        try { return await _confirmAsync(title, message, accept, cancel); }
        catch (Exception exception) { UiSafety.Log(exception); return false; }
    }
}
