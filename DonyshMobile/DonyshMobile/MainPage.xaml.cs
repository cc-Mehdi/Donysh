using System.Collections.ObjectModel;
using DonyshMobile.Core;
using DonyshMobile.UI;
#if ANDROID
using DonyshMobile.Platforms.Android;
#endif

namespace DonyshMobile;

public partial class MainPage : ContentPage
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
    private readonly List<SenderItem> _allSenders = [];
    private bool _subscribed, _hasPermission, _entered, _busy, _isRefreshing;
    private string _search = string.Empty;
    private int _nextStep;

    public ObservableCollection<SenderItem> Senders { get; } = [];
    public bool IsRefreshing { get => _isRefreshing; set { _isRefreshing = value; OnPropertyChanged(); } }
    public string PermissionStatusText { get; private set; } = "در حال بررسی…";
    public string PermissionButtonText { get; private set; } = "فعال‌سازی";
    public string SenderCount => UiText.Digits(_allSenders.Count);
    public string PendingCount { get; private set; } = "۰";
    public string SentCount { get; private set; } = "۰";
    public string ConnectionText { get; private set; } = "در حال بررسی اتصال";
    public string NextStepText { get; private set; } = "فرستنده‌های بانکی خود را انتخاب کنید.";
    public string NextStepAction { get; private set; } = "شروع کنیم";
    public string UntitledText { get; private set; } = string.Empty;
    public bool HasUntitled { get; private set; }

    public MainPage()
    {
        InitializeComponent();
        BindingContext = this;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_subscribed)
        {
            SmsReceivedNotifier.MessageStored += OnMessageStored;
            SmsReceivedNotifier.SyncChanged += OnMessageStored;
            _subscribed = true;
        }
        await RefreshPermissionAsync();
        await RefreshAsync();
        SyncPlatform.Schedule();
        if (!_entered) { _entered = true; await UiMotion.EnterAsync(PageContent); }
        if (ConsumePendingTitlesRequest())
        {
            try { await Navigation.PushAsync(new PendingTitlesPage(_store, promptFirstMessage: true)); }
            catch (Exception)
            {
                RequestPendingTitles();
                await DisplayAlert("صفحه باز نشد", "عنوان‌گذاری پیام‌ها را دوباره از صفحهٔ اصلی باز کنید.", "باشه");
            }
            return;
        }
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
#if ANDROID
        try
        {
            var status = await Permissions.CheckStatusAsync<ReceiveSmsPermission>();
            if (status == PermissionStatus.Granted) AppInfo.ShowSettingsUI();
            else status = await Permissions.RequestAsync<ReceiveSmsPermission>();
            if (status != PermissionStatus.Granted)
                await DisplayAlert("اجازهٔ دریافت پیامک", "در تنظیمات گوشی، اجازهٔ دریافت پیامک را برای دونیش فعال کنید.", "باشه");
            else await RequestNotificationPermissionAsync();
        }
        catch (PermissionException)
        {
            await DisplayAlert("دسترسی فعال نشد", "اجازهٔ دریافت پیامک را در تنظیمات گوشی بررسی کنید.", "باشه");
        }
#else
        await DisplayAlert("دریافت روی گوشی", "دریافت پیامک به گوشی اندرویدی نیاز دارد. روی ویندوز می‌توانید قالب‌ها و تنظیمات را بررسی کنید.", "باشه");
#endif
        await RefreshPermissionAsync();
        await RefreshAsync();
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
        var action = await DisplayActionSheet(item.Address, "انصراف", "حذف فرستنده", "پیام‌ها", "قالب‌های پیامک");
        if (action == "پیام‌ها") await Navigation.PushAsync(new MessagesPage(_store, id, item.Address));
        else if (action == "قالب‌های پیامک") await Navigation.PushAsync(new TemplatesPage(_store, id, item.Address));
        else if (action == "حذف فرستنده" && await DisplayAlert("حذف از گوشی",
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
            await Navigation.PushAsync(new MessagesPage(_store, item.Id, item.Address));
    }

    private async void OnConnectionClicked(object? sender, EventArgs e) => await Navigation.PushAsync(new ConnectionPage(_store));
    private async void OnPendingTitlesClicked(object? sender, EventArgs e) => await Navigation.PushAsync(new PendingTitlesPage(_store));
    private void OnNextStepClicked(object? sender, EventArgs e)
    {
        switch (_nextStep)
        {
            case 0: OnPickSenderClicked(sender, e); break;
            case 1: OnPermissionClicked(sender, e); break;
            case 2: OnConnectionClicked(sender, e); break;
            case 3: OnPendingTitlesClicked(sender, e); break;
            default: OnConnectionClicked(sender, e); break;
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

    private async void OnRefreshing(object? sender, EventArgs e) { await RefreshPermissionAsync(); await RefreshAsync(); }
    private void OnMessageStored(object? sender, EventArgs e) => Dispatcher.Dispatch(async () => await RefreshAsync());

    private async Task RefreshAsync()
    {
        if (!await _refreshGate.WaitAsync(0)) { IsRefreshing = false; return; }
        try
        {
            var summaries = await _store.GetSenderSummariesAsync();
            var sync = await _store.GetSyncSummaryAsync();
            var untitled = await _store.GetUntitledMessagesAsync();
            var connection = await MobileConnectionStore.LoadAsync();
            _allSenders.Clear();
            _allSenders.AddRange(summaries.Select(x => new SenderItem(x.Id, x.Address,
                x.MessageCount == 0 ? "منتظر اولین پیامک" : $"{UiText.Digits(x.MessageCount)} پیام · {UiText.Date(x.LastMessageAtUtc!.Value)}")));
            FilterSenders();
            PendingCount = UiText.Digits(sync.Pending);
            SentCount = UiText.Digits(sync.Sent);
            HasUntitled = SmsSyncSettings.RequireTitle && untitled.Count > 0;
            UntitledText = $"{UiText.Digits(untitled.Count)} پیام، منتظر عنوان شما";
            ConnectionText = connection is null ? "هنوز به سامانه متصل نشده‌اید" : $"متصل به {connection.Destination}";
            (_nextStep, NextStepText, NextStepAction) = summaries.Count == 0
                ? (0, "بانک یا شماره‌ای را انتخاب کنید تا پیامک‌های تازه‌اش اینجا ثبت شود.", "انتخاب اولین فرستنده")
                : !_hasPermission ? (1, "برای ثبت پیام‌های تازه، اجازهٔ دریافت پیامک را فعال کنید.", "فعال‌سازی دریافت")
                : connection is null ? (2, "پیام‌ها روی گوشی ذخیره می‌شوند. برای ثبت خرج، گوشی را به سامانه وصل کنید.", "اتصال به دونیش")
                : HasUntitled ? (3, "چند پیام تازه دارید؛ با نوشتن عنوان، آن‌ها را آمادهٔ ارسال کنید.", "ثبت عنوان پیام‌ها")
                : (4, "دریافت و ارسال آماده است. پیام‌های تازه طبق تنظیمات شما بررسی می‌شوند.", "وضعیت ارسال و تنظیمات");
            OnPropertyChanged(string.Empty);
        }
        catch (Exception)
        {
            await DisplayAlert("اطلاعات باز نشد", "اطلاعات ذخیره‌شده قابل خواندن نیست. داده‌ها پاک نشده‌اند؛ برنامه را دوباره باز کنید.", "باشه");
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
        _hasPermission = false;
        PermissionStatusText = "دریافت پیامک روی گوشی اندرویدی فعال می‌شود.";
        PermissionButtonText = "راهنما";
        await Task.CompletedTask;
#endif
        OnPropertyChanged(nameof(PermissionStatusText));
        OnPropertyChanged(nameof(PermissionButtonText));
    }
}

public sealed record SenderItem(Guid Id, string Address, string Details);
