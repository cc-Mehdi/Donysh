using System.Collections.ObjectModel;
using DonyshMobile.Core;
using DonyshMobile.UI;
#if ANDROID
using DonyshMobile.Platforms.Android;
#endif

namespace DonyshMobile;

public partial class MainPage : DonyshPage
{
    private static int _pendingTitlesRequested;
    public static void RequestPendingTitles() => Interlocked.Exchange(ref _pendingTitlesRequested, 1);
    public static bool ConsumePendingTitlesRequest() => Interlocked.Exchange(ref _pendingTitlesRequested, 0) == 1;
#if DEBUG && WINDOWS
    private readonly SmsMonitorStore _store = WinUI.StartupSmokeCheck.CreateStore();
#else
    private readonly SmsMonitorStore _store = new(FileSystem.AppDataDirectory);
#endif
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly MobileApiClient _api = new();
    private readonly List<SenderItem> _allSenders = [];
    private bool _subscribed, _hasPermission, _entered, _busy, _isRefreshing, _openingPage, _requestingPermission;
    private bool _setupWizardChecked;
    private string _search = string.Empty;
    private int _nextStep = -1;
    private SetupStep _setupStep = SetupStep.Connection;

    public ObservableCollection<SenderItem> Senders { get; } = [];
    public bool IsRefreshing { get => _isRefreshing; set { _isRefreshing = value; OnPropertyChanged(); } }
    public string PermissionStatusText { get; private set; } = "در حال بررسی…";
    public string PermissionButtonText { get; private set; } = "فعال‌سازی";
    public string SenderCount => UiText.Digits(_allSenders.Count);
    public string PendingCount { get; private set; } = "۰";
    public string SentCount { get; private set; } = "۰";
    public string ConnectionText { get; private set; } = "در حال بررسی اتصال";
    public string WorkspaceText { get; private set; } = string.Empty;
    public bool HasConnection { get; private set; }
    public string NextStepText { get; private set; } = "فرستنده‌های بانکی خود را انتخاب کنید.";
    public string NextStepAction { get; private set; } = "شروع کنیم";
    public string UntitledText { get; private set; } = string.Empty;
    public bool HasUntitled { get; private set; }
    public bool HasNextStep => _nextStep >= 0;
    public bool NeedsPermissionAttention => DeviceInfo.Platform == DevicePlatform.Android && !_hasPermission;
    public bool ShowSenderSearch => _allSenders.Count >= 5;

    public MainPage()
    {
        InitializeComponent();
        BindingContext = this;
#if DEBUG && WINDOWS
        if (WinUI.StartupSmokeCheck.IsEnabled) _setupWizardChecked = true;
#endif
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try { await ActivateAsync(); }
        catch (Exception exception)
        {
            UiSafety.Log(exception);
            await UiSafety.AlertAsync(this, "صفحه کامل آماده نشد", "پیام‌ها محفوظ‌اند؛ دوباره صفحه را باز کنید.");
        }
        finally { await CompletePageLoadingAsync(); }
    }

    private async Task ActivateAsync()
    {
        if (!_subscribed)
        {
            SmsReceivedNotifier.MessageStored += OnMessageStored;
            SmsReceivedNotifier.SyncChanged += OnMessageStored;
            _subscribed = true;
        }
        await RefreshPermissionAsync();
        await RecoverRecentMessagesAsync();
        await RefreshAsync();
        SyncPlatform.Schedule();
        if (!_entered) { _entered = true; await UiMotion.EnterAsync(PageContent); }
        if (ConsumePendingTitlesRequest())
        {
            try { await Navigation.PushAsync(new PendingTitlesPage(_store)); }
            catch (Exception)
            {
                RequestPendingTitles();
                await UiSafety.AlertAsync(this, "صفحه باز نشد", "عنوان‌گذاری پیام‌ها را دوباره از صفحهٔ اصلی باز کنید.");
            }
            return;
        }
        if (!_setupWizardChecked && !SetupWizardPage.WasFinished && _setupStep != SetupStep.Complete)
        {
            _setupWizardChecked = true;
            await OpenPageAsync(() => new SetupWizardPage(_store));
            return;
        }
        if (_setupStep == SetupStep.Complete && !SetupWizardPage.WasFinished)
            SetupWizardPage.MarkFinished();
#if ANDROID
        if (_hasPermission) await RequestNotificationPermissionAsync();
        await TitleReminderNotifier.RefreshAsync(global::Android.App.Application.Context);
#endif
    }

    protected override void OnDisappearing()
    {
        if (_subscribed)
        {
            SmsReceivedNotifier.MessageStored -= OnMessageStored;
            SmsReceivedNotifier.SyncChanged -= OnMessageStored;
            _subscribed = false;
        }
        base.OnDisappearing();
    }

    private async void OnPermissionClicked(object? sender, EventArgs e)
    {
        if (_requestingPermission) return;
        _requestingPermission = true;
        try
        {
#if ANDROID
            var status = await Permissions.CheckStatusAsync<ReceiveSmsPermission>();
            if (status == PermissionStatus.Granted && SmsSyncSettings.RecoverRecent
                && await Permissions.CheckStatusAsync<ReadSmsPermission>() != PermissionStatus.Granted)
                await Permissions.RequestAsync<ReadSmsPermission>();
            else if (status == PermissionStatus.Granted) AppInfo.ShowSettingsUI();
            else status = await Permissions.RequestAsync<ReceiveSmsPermission>();
            if (status != PermissionStatus.Granted)
                await UiSafety.AlertAsync(this, "اجازهٔ دریافت پیامک", "در تنظیمات گوشی، اجازهٔ دریافت پیامک را برای دونیش فعال کنید.");
            else
            {
                if (SmsSyncSettings.RecoverRecent
                    && await Permissions.CheckStatusAsync<ReadSmsPermission>() != PermissionStatus.Granted)
                    await Permissions.RequestAsync<ReadSmsPermission>();
                await RequestNotificationPermissionAsync();
                await RecoverRecentMessagesAsync(force: true);
            }
#else
            await UiSafety.AlertAsync(this, "دریافت روی گوشی", "دریافت پیامک به گوشی اندرویدی نیاز دارد. روی ویندوز می‌توانید قالب‌ها و تنظیمات را بررسی کنید.");
#endif
            await RefreshPermissionAsync();
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            UiSafety.Log(exception);
            await UiSafety.AlertAsync(this, "دسترسی فعال نشد", "از تنظیمات گوشی، بخش برنامه‌ها و سپس اجازه‌های دونیش را بررسی کنید.");
        }
        finally { _requestingPermission = false; }
    }

#if ANDROID
    private static async Task RequestNotificationPermissionAsync()
    {
        if (!SmsSyncSettings.RequireTitle || !OperatingSystem.IsAndroidVersionAtLeast(33)
            || Preferences.Default.Get("title-notification-permission-asked", false)) return;
        Preferences.Default.Set("title-notification-permission-asked", true);
        try { await Permissions.RequestAsync<PostNotificationsPermission>(); }
        catch (PermissionException) { }
    }
#endif

    private void OnToggleManualClicked(object? sender, EventArgs e)
    {
        ManualPanel.IsVisible = !ManualPanel.IsVisible;
        if (ManualPanel.IsVisible) SenderEntry.Focus();
    }

    private async void OnAddSenderMenuClicked(object? sender, EventArgs e)
    {
        if (_busy) return;
        var choice = await DonyshSelectionSheet.ShowAsync(this, "افزودن فرستنده",
        new DonyshSelectionOption[]
        {
            new("انتخاب از پیامک‌های گوشی", AppIcons.Search),
            new("ورود دستی شماره یا نام", AppIcons.Edit)
        });
        if (choice == 0) OnPickSenderClicked(sender, e);
        else if (choice == 1) OnToggleManualClicked(sender, e);
    }

    private async void OnAddSenderClicked(object? sender, EventArgs e)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            await AddSenderAsync(SenderEntry.Text ?? string.Empty);
        }
        catch (Exception)
        {
            await DisplayAlert("ثبت نشد", "اطلاعات روی گوشی قابل خواندن یا ذخیره‌کردن نیست. برنامه را دوباره باز کنید؛ داده‌ها حذف نشده‌اند.", "باشه");
        }
        finally { _busy = false; }
    }

    private async Task AddSenderAsync(string address)
    {
        var result = await _store.AddSenderAsync(address);
        if (result == AddSenderResult.Added)
        {
            SenderEntry.Text = string.Empty;
            ManualPanel.IsVisible = false;
            UiMotion.Feedback();
            await RefreshAsync();
            return;
        }
        var message = result switch
        {
            AddSenderResult.Duplicate => "این فرستنده قبلاً در فهرست شما ثبت شده است.",
            AddSenderResult.LimitReached => "حداکثر ۵۰ فرستنده قابل ثبت است.",
            _ => "شماره یا نام فرستنده را به‌درستی وارد کنید."
        };
        await DisplayAlert("فرستنده ثبت نشد", message, "باشه");
    }

    private async void OnPickSenderClicked(object? sender, EventArgs e)
    {
#if ANDROID
        if (_busy) return;
        _busy = true;
        try
        {
            var permission = await Permissions.RequestAsync<ReadSmsPermission>();
            if (permission != PermissionStatus.Granted)
            {
                await DisplayAlert("انتخاب از پیامک‌ها", "برای نمایش فرستنده‌ها اجازهٔ خواندن پیامک لازم است. می‌توانید شماره را هم دستی وارد کنید.", "باشه");
                return;
            }
            var addresses = await Task.Run(() => SmsConversationCatalog.RecentSenders(global::Android.App.Application.Context));
            await Navigation.PushAsync(new SenderPickerPage(addresses, AddSenderAsync));
        }
        catch (Exception)
        {
            await DisplayAlert("فهرست باز نشد", "خواندن فهرست پیامک‌ها ممکن نشد. شماره را دستی وارد کنید.", "باشه");
        }
        finally { _busy = false; }
#else
        ManualPanel.IsVisible = true;
        SenderEntry.Focus();
        await Task.CompletedTask;
#endif
    }

    private async void OnSenderActionsClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: Guid id }) return;
        var item = _allSenders.Find(x => x.Id == id);
        if (item is null) return;
        var action = await DonyshSelectionSheet.ShowAsync(this, item.Address,
        new DonyshSelectionOption[]
        {
            new("پیام‌ها", AppIcons.Message),
            new("قالب‌های پیامک", AppIcons.Template),
            new("حذف فرستنده", AppIcons.Delete, true)
        });
        if (action == 0) await Navigation.PushAsync(new MessagesPage(_store, id, item.Address));
        else if (action == 1) await Navigation.PushAsync(new TemplatesPage(_store, id, item.Address));
        else if (action == 2 && await DisplayAlert("حذف از گوشی",
                     $"«{item.Address}» و همهٔ پیام‌هایش روی گوشی، حتی پیام‌های ارسال‌نشده، حذف شوند؟ خرج‌های ثبت‌شده در سامانه باقی می‌مانند.", "حذف", "انصراف"))
        {
            try { await _store.RemoveSenderAsync(id); await RefreshAsync(); }
            catch (Exception) { await DisplayAlert("حذف نشد", "امکان ذخیرهٔ تغییر وجود ندارد. دوباره تلاش کنید.", "باشه"); }
        }
    }

    private async void OnSenderSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is CollectionView list) list.SelectedItem = null;
        if (e.CurrentSelection.FirstOrDefault() is SenderItem item)
            await OpenPageAsync(() => new MessagesPage(_store, item.Id, item.Address));
    }

    private async void OnConnectionClicked(object? sender, EventArgs e) => await OpenPageAsync(() =>
    {
#if DEBUG && WINDOWS
        return WinUI.StartupSmokeCheck.CreateConnectionPage(_store);
#else
        return new ConnectionPage(_store);
#endif
    });
    private async void OnPendingTitlesClicked(object? sender, EventArgs e) => await OpenPageAsync(() => new PendingTitlesPage(_store));
    private async void OnMissedMessagesClicked(object? sender, EventArgs e) =>
        await OpenPageAsync(() => new MissedMessagesPage(_store));

    private async Task OpenPageAsync(Func<Page> createPage)
    {
        if (_openingPage || (Shell.Current is { } shell && shell.CurrentPage != this)) return;
        _openingPage = true;
        try { await Navigation.PushAsync(createPage()); }
        catch (Exception exception)
        {
            UiSafety.Log(exception);
            await UiSafety.AlertAsync(this, "صفحه باز نشد", "دوباره تلاش کنید؛ پیام‌های ذخیره‌شده تغییری نکرده‌اند.");
        }
        finally { _openingPage = false; }
    }
    private void OnNextStepClicked(object? sender, EventArgs e)
    {
        switch (_nextStep)
        {
            case 0: _ = OpenPageAsync(() => new SetupWizardPage(_store)); break;
            case 1: OnPendingTitlesClicked(sender, e); break;
        }
    }

    private void OnSenderSearchChanged(object? sender, TextChangedEventArgs e) { _search = e.NewTextValue ?? string.Empty; FilterSenders(); }
    private void FilterSenders()
    {
        var normalized = SenderAddress.Normalize(_search);
        Senders.Clear();
        foreach (var item in _allSenders.Where(x => string.IsNullOrWhiteSpace(_search)
                     || x.Address.Contains(_search, StringComparison.OrdinalIgnoreCase)
                     || (normalized.Length > 0 && SenderAddress.Normalize(x.Address).Contains(normalized, StringComparison.Ordinal))))
            Senders.Add(item);
    }

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        try { await RefreshPermissionAsync(); await RecoverRecentMessagesAsync(force: true); await RefreshAsync(); }
        catch (Exception exception)
        {
            UiSafety.Log(exception);
            await UiSafety.AlertAsync(this, "به‌روزرسانی کامل نشد", "دوباره تلاش کنید؛ پیام‌های ذخیره‌شده تغییری نکرده‌اند.");
        }
        finally { IsRefreshing = false; }
    }
    private void OnMessageStored(object? sender, EventArgs e) => Dispatcher.Dispatch(async () => await RefreshAsync());

    private async void OnWorkspaceShortcutClicked(object? sender, EventArgs e)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var connection = await MobileConnectionStore.LoadAsync();
            if (connection is null) return;
            var list = await _api.GetWorkspacesAsync(connection);
            var labels = list.Workspaces.Select(x => x.Type == "shared" ? $"{x.Name} · مشترک" : $"{x.Name} · شخصی").ToList();
            var selected = await DonyshSelectionSheet.ShowAsync(this, "فضای مالی پیش‌فرض", labels,
                list.Workspaces.FindIndex(x => x.Id == list.WorkspaceId));
            if (!selected.HasValue || list.Workspaces[selected.Value].Id == list.WorkspaceId) return;
            var chosen = list.Workspaces[selected.Value];
            var queue = await _store.GetSyncSummaryAsync();
            if (queue.Pending > 0 && !await DisplayAlert("تغییر مقصد پیام‌های منتظر",
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
            await RefreshAsync();
        }
        catch (UnauthorizedAccessException)
        {
            await UiSafety.AlertAsync(this, "ورود دوباره لازم است", "نشست این گوشی پایان یافته است. از تنظیمات دوباره وارد حساب دونیش شوید.");
        }
        catch (Exception exception)
        {
            UiSafety.Log(exception);
            await UiSafety.AlertAsync(this, "فضای مالی تغییر نکرد", "اینترنت را بررسی کنید و دوباره تلاش کنید.");
        }
        finally { _busy = false; }
    }

    private async Task RecoverRecentMessagesAsync(bool force = false)
    {
        if (!SmsSyncSettings.RecoverRecent) return;
        try
        {
            var result = await SyncPlatform.RecoverRecentAsync(_store, force);
            if (result.Added <= 0) return;
            SmsReceivedNotifier.NotifySyncChanged();
            SyncPlatform.Schedule();
        }
        catch (Exception exception)
        {
            // Live reception and the durable queue remain available if the provider is restricted.
            UiSafety.Log(exception);
        }
    }

    private async Task RefreshAsync()
    {
        if (!await _refreshGate.WaitAsync(0)) { IsRefreshing = false; return; }
        try
        {
            var summaries = await _store.GetSenderSummariesAsync();
            var sync = await _store.GetSyncSummaryAsync();
            var untitled = await _store.GetUntitledMessagesAsync();
#if DEBUG && WINDOWS
            var connection = await WinUI.StartupSmokeCheck.LoadConnectionAsync();
#else
            var connection = await MobileConnectionStore.LoadAsync();
#endif
            var hasTemplate = false;
            foreach (var summary in summaries)
            {
                if ((await _store.GetTemplatesAsync(summary.Id)).Count == 0) continue;
                hasTemplate = true;
                break;
            }
            var canReceive = DeviceInfo.Platform != DevicePlatform.Android || _hasPermission;
            _setupStep = SetupFlow.Resolve(connection is not null, summaries.Count, canReceive, hasTemplate);
            _allSenders.Clear();
            _allSenders.AddRange(summaries.Select(x => new SenderItem(x.Id, x.Address,
                x.MessageCount == 0 ? "منتظر اولین پیامک" : $"{UiText.Digits(x.MessageCount)} پیام · {UiText.Date(x.LastMessageAtUtc!.Value)}")));
            FilterSenders();
            PendingCount = UiText.Digits(sync.Pending);
            SentCount = UiText.Digits(sync.Sent);
            HasUntitled = SmsSyncSettings.RequireTitle && untitled.Count > 0;
            UntitledText = $"{UiText.Digits(untitled.Count)} پیام، منتظر عنوان شما";
            ConnectionText = connection is null ? "هنوز به سامانه متصل نشده‌اید" : $"متصل به {connection.Destination}";
            HasConnection = connection is not null;
            WorkspaceText = connection?.Destination.Split(" / ", 2, StringSplitOptions.TrimEntries)[0] ?? string.Empty;
            (_nextStep, NextStepText, NextStepAction) = _setupStep != SetupStep.Complete
                ? (0, "راه‌اندازی هنوز کامل نشده است؛ قدم بعدی را ادامه دهید.", "ادامهٔ راه‌اندازی")
                : HasUntitled
                    ? (1, "چند پیام تازه دارید؛ با نوشتن عنوان، آن‌ها را آمادهٔ ارسال کنید.", "ثبت عنوان پیام‌ها")
                    : (-1, "دریافت و ارسال آماده است. پیام‌های تازه طبق تنظیمات شما بررسی می‌شوند.", string.Empty);
            OnPropertyChanged(string.Empty);
        }
        catch (Exception)
        {
            await UiSafety.AlertAsync(this, "اطلاعات باز نشد", "اطلاعات ذخیره‌شده قابل خواندن نیست. داده‌ها پاک نشده‌اند؛ برنامه را دوباره باز کنید.");
        }
        finally { _refreshGate.Release(); IsRefreshing = false; }
    }

    private async Task RefreshPermissionAsync()
    {
#if ANDROID
        _hasPermission = await Permissions.CheckStatusAsync<ReceiveSmsPermission>() == PermissionStatus.Granted;
        PermissionStatusText = _hasPermission ? "فعال؛ پیام‌های تازه ذخیره می‌شوند." : "برای شروع، اجازهٔ دریافت را فعال کنید.";
        PermissionButtonText = _hasPermission ? "تنظیمات" : "فعال‌سازی";
#else
#if DEBUG && WINDOWS
        _hasPermission = WinUI.StartupSmokeCheck.IsEnabled;
#else
        _hasPermission = false;
#endif
        PermissionStatusText = "دریافت پیامک روی گوشی اندرویدی فعال می‌شود.";
        PermissionButtonText = "راهنما";
        await Task.CompletedTask;
#endif
        OnPropertyChanged(nameof(PermissionStatusText));
        OnPropertyChanged(nameof(PermissionButtonText));
    }
}

public sealed record SenderItem(Guid Id, string Address, string Details);
