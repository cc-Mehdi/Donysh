using System.Collections.ObjectModel;
using System.ComponentModel;
using DonyshMobile.Core;
using DonyshMobile.UI;

namespace DonyshMobile;

public partial class MessagesPage : DonyshPage
{
    private readonly SmsMonitorStore _store;
    private readonly Guid _senderId;
    private readonly Action _scheduleSync;
    private readonly Func<string, string, string, string, Task<bool>> _confirmAsync;
    private readonly Guid? _focusMessageId;
    private readonly List<MessageItem> _allMessages = [];
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private bool _subscribed, _entered, _editing, _retrying;
    private string _search = string.Empty;
    private int _statusFilter;
    public string SenderAddress { get; }
    public ObservableCollection<MessageItem> Messages { get; } = [];
    public string MessageCountText => $"{UiText.Digits(_allMessages.Count)} پیام در هفت روز گذشته";
    public string VisibleMessageCountText => $"{UiText.Digits(Messages.Count)} نتیجه";
    public bool HasRetryableMessages => Messages.Any(x => x.CanRetry);

    public MessagesPage(SmsMonitorStore store, Guid senderId, string senderAddress, int initialFilter = 0,
        Guid? focusMessageId = null)
        : this(store, senderId, senderAddress, ScheduleDeliveryWork,
            initialFilter: initialFilter, focusMessageId: focusMessageId)
    {
    }

    internal MessagesPage(SmsMonitorStore store, Guid senderId, string senderAddress, Action scheduleSync,
        Func<string, string, string, string, Task<bool>>? confirm = null, int initialFilter = 0,
        Guid? focusMessageId = null)
    {
        _store = store;
        _senderId = senderId;
        _scheduleSync = scheduleSync;
        _confirmAsync = confirm ?? DisplayAlert;
        _statusFilter = initialFilter;
        _focusMessageId = focusMessageId;
        SenderAddress = senderAddress;
        InitializeComponent();
        BindingContext = this;
        UpdateFilterChipStyles();
    }

    private static void ScheduleDeliveryWork()
    {
        SyncPlatform.Schedule();
#if ANDROID
        _ = Platforms.Android.TitleReminderNotifier.RefreshAsync(global::Android.App.Application.Context);
#endif
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            if (!_subscribed)
            {
                SmsReceivedNotifier.MessageStored += OnMessageStored;
                SmsReceivedNotifier.SyncChanged += OnMessageStored;
                _subscribed = true;
            }
            await RefreshAsync();
            if (_focusMessageId is Guid focusId && Messages.FirstOrDefault(x => x.Id == focusId) is { } focused)
            {
                focused.IsExpanded = true;
                Dispatcher.Dispatch(() => MessagesList.ScrollTo(focused, position: ScrollToPosition.Center, animate: false));
            }
            if (!_entered) { _entered = true; await UiMotion.EnterAsync(MessagesContent); }
        }
        catch (Exception exception) { UiSafety.Log(exception); }
        finally { await CompletePageLoadingAsync(); }
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

    private void OnMessageStored(object? sender, EventArgs e) => Dispatcher.Dispatch(async () => await RefreshAsync());
    private void OnSearchChanged(object? sender, TextChangedEventArgs e) { _search = e.NewTextValue?.Trim() ?? string.Empty; FilterMessages(); }
    private void OnStatusFilterClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: string value } || !int.TryParse(value, out var filter)) return;
        _statusFilter = filter;
        UpdateFilterChipStyles();
        FilterMessages();
    }

    private void UpdateFilterChipStyles()
    {
        if (StatusFilterChips is null) return;
        foreach (var button in StatusFilterChips.Children.OfType<Button>())
        {
            var selected = button.CommandParameter is string value
                           && int.TryParse(value, out var filter) && filter == _statusFilter;
            button.SetAppThemeColor(Button.BackgroundColorProperty,
                Color.FromArgb(selected ? "#5B4CF0" : "#EEEFFD"),
                Color.FromArgb(selected ? "#7568F5" : "#292C56"));
            button.SetAppThemeColor(Button.TextColorProperty,
                selected ? Colors.White : Color.FromArgb("#453BB7"),
                selected ? Colors.White : Color.FromArgb("#DDD9FF"));
        }
    }

    private void FilterMessages()
    {
        Messages.Clear();
        foreach (var item in _allMessages.Where(x => (_statusFilter switch
                 {
                     1 => x.Status == SmsDeliveryStatus.Pending,
                     2 => x.Status == SmsDeliveryStatus.Sent,
                     3 => x.Status == SmsDeliveryStatus.Rejected,
                     4 => x.Status == SmsDeliveryStatus.Excluded,
                     5 => x.CanEditTitle && x.Untitled,
                     _ => true
                 }) && (string.IsNullOrWhiteSpace(_search) || x.Body.Contains(_search, StringComparison.OrdinalIgnoreCase)
                        || x.Title.Contains(_search, StringComparison.OrdinalIgnoreCase))))
            Messages.Add(item);
        OnPropertyChanged(nameof(MessageCountText));
        OnPropertyChanged(nameof(VisibleMessageCountText));
        OnPropertyChanged(nameof(HasRetryableMessages));
    }

    private async void OnRefreshRequested(object? sender, EventArgs e)
    {
        try { await RefreshAsync(waitForCurrent: true); }
        finally { MessagesRefreshView.IsRefreshing = false; }
    }

    private async Task RefreshAsync(bool waitForCurrent = false)
    {
        if (waitForCurrent) await _refreshGate.WaitAsync();
        else if (!await _refreshGate.WaitAsync(0)) return;
        try
        {
            var stored = await _store.GetMessagesAsync(_senderId);
            var templates = await _store.GetTemplatesAsync(_senderId);
            var expandedIds = _allMessages.Where(x => x.IsExpanded).Select(x => x.Id).ToHashSet();
            var items = await Task.Run(() =>
            {
                var matchers = templates.Select(SmsTemplateParser.CreateMatcher).Where(x => x is not null).ToArray();
                return stored.Select(message =>
                {
                    var match = matchers.Select(parse => parse!(message.Body)).FirstOrDefault(p => p is not null);
                    var transactionType = match is null ? "خوانده نشد" : match.Kind == TransactionKind.Withdrawal ? "برداشت" : "واریز";
                    var amount = match is null ? "مبلغ نامشخص" : UiText.Digits($"{match.AmountRial:N0} ریال");
                    var untitled = string.IsNullOrWhiteSpace(message.Title);
                    var delivery = message.DeliveryStatus switch
                    {
                        SmsDeliveryStatus.Sent => "دریافت توسط سامانه تأیید شد",
                        SmsDeliveryStatus.Rejected => "قالب پذیرفته نشد؛ پیام روی گوشی باقی است",
                        SmsDeliveryStatus.Excluded => "از ارسال کنار گذاشته شده است",
                        _ when untitled && SmsSyncSettings.RequireTitle => "منتظر نوشتن عنوان شما",
                        _ when message.DeliveryAttempts > 0 => $"در صف تلاش دوباره · {UiText.Digits(message.DeliveryAttempts)} تلاش",
                        _ => "در صف ارسال به سامانه"
                    };
                    var item = new MessageItem(message.Id, untitled ? "بدون عنوان" : message.Title!, message.Body,
                        UiText.Date(message.ReceivedAtUtc), transactionType, amount, delivery, message.DeliveryStatus,
                        message.DeliveryStatus == SmsDeliveryStatus.Pending, untitled, message.LastDeliveryError is not null,
                        message.RecoveredFromInbox);
                    item.IsExpanded = expandedIds.Contains(message.Id);
                    return item;
                }).ToList();
            });
            _allMessages.Clear();
            _allMessages.AddRange(items);
            FilterMessages();
        }
        catch (Exception exception)
        {
            UiSafety.Log(exception);
            await UiSafety.AlertAsync(this, "پیام‌ها باز نشد", "خواندن اطلاعات کامل نشد. برنامه را دوباره باز کنید؛ پیام‌ها حذف نشده‌اند.");
        }
        finally { _refreshGate.Release(); }
    }

    private async void OnRetryMessageClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: Guid id } && _allMessages.Any(x => x.Id == id && x.CanRetry))
            await RequeueAsync([id], confirm: false);
    }

    private async void OnRetryVisibleClicked(object? sender, EventArgs e) =>
        await RequeueAsync(Messages.Where(x => x.CanRetry).Select(x => x.Id).ToArray(), confirm: true);

    private async void OnExcludeMessageClicked(object? sender, EventArgs e)
    {
        if (_retrying || _editing || sender is not Button { CommandParameter: Guid id }
            || _allMessages.Find(x => x.Id == id) is not { CanExclude: true } item) return;
        if (!await _confirmAsync("کنار گذاشتن پیام",
                $"«{item.Title}» از صف ارسال کنار گذاشته شود؟ پیام در بخش «کنار گذاشته‌شده» می‌ماند و هر زمان بخواهید قابل بازگرداندن است.",
                "کنار گذاشتن", "انصراف")) return;

        _retrying = true;
        try
        {
            var changed = 0;
            await MobileApiClient.RunWithSyncLockAsync(async () =>
                changed = await _store.ExcludeMessagesAsync(_senderId, [id]));
            await RefreshAsync(waitForCurrent: true);
            RetryStatusLabel.Text = changed > 0
                ? "پیام کنار گذاشته شد و برای سامانه ارسال نمی‌شود."
                : "وضعیت پیام تغییر کرده است و کنار گذاشته نشد.";
            RetryStatusLabel.IsVisible = true;
            if (changed > 0)
            {
                UiMotion.Feedback();
                SmsReceivedNotifier.NotifySyncChanged();
            }
        }
        catch (Exception exception)
        {
            UiSafety.Log(exception);
            RetryStatusLabel.Text = "کنار گذاشتن پیام کامل نشد؛ پیام محفوظ است.";
            RetryStatusLabel.IsVisible = true;
        }
        finally { _retrying = false; }
    }

    private async Task RequeueAsync(IReadOnlyCollection<Guid> ids, bool confirm)
    {
        if (_retrying || _editing || ids.Count == 0) return;
        _retrying = true;
        RetryVisibleMessagesButton.IsEnabled = false;
        RetryStatusLabel.IsVisible = false;
        try
        {
            if (confirm && !await _confirmAsync("بازگرداندن به صف",
                    $"{UiText.Digits(ids.Count)} پیام ارسال‌نشده در همین فهرست دوباره در صف قرار بگیرند؟ پیام‌های تأییدشده تغییری نمی‌کنند. ارسال در نوبت بعد و مطابق تنظیمات عنوان انجام می‌شود.",
                    "بازگرداندن به صف", "انصراف")) return;
            var changed = 0;
            // Wait for any in-flight receipt and recheck stored status; never replay a delivered SMS.
            await MobileApiClient.RunWithSyncLockAsync(async () =>
                changed = await _store.RequeueMessagesAsync(_senderId, ids));
            await RefreshAsync(waitForCurrent: true);
            RetryStatusLabel.Text = changed > 0
                ? $"{UiText.Digits(changed)} پیام دوباره در صف قرار گرفت. فاصلهٔ ارسال و تنظیم عنوان همچنان رعایت می‌شوند."
                : "پیام‌ها از قبل در صف‌اند یا وضعیت آن‌ها تغییر کرده است؛ پیام تأییدشده دوباره ارسال نمی‌شود.";
            RetryStatusLabel.IsVisible = true;
            if (changed > 0)
            {
                UiMotion.Feedback();
                SmsReceivedNotifier.NotifySyncChanged();
                _scheduleSync();
            }
        }
        catch (Exception exception)
        {
            UiSafety.Log(exception);
            RetryStatusLabel.Text = "بازگرداندن به صف کامل نشد؛ پیام‌ها محفوظ‌اند. دوباره تلاش کنید.";
            RetryStatusLabel.IsVisible = true;
        }
        finally { _retrying = false; RetryVisibleMessagesButton.IsEnabled = true; }
    }

    private async void OnSetTitleClicked(object? sender, EventArgs e)
    {
        if (_editing || _retrying || sender is not Button { CommandParameter: Guid id }) return;
        _editing = true;
        try
        {
            var item = _allMessages.Find(x => x.Id == id);
            if (item is null) return;
            var excerpt = item.Body.Length > 150 ? item.Body[..150] + "…" : item.Body;
            var suggestions = await _store.GetTitleSuggestionsAsync(_senderId, item.Body);
            var title = await DonyshTitleSheet.ShowAsync(this, "عنوان تراکنش", excerpt,
                item.Untitled ? string.Empty : item.Title, suggestions);
            if (title is null) return;
            if (!await _store.SetTitleAsync(id, title))
            {
                await DisplayAlert("عنوان ثبت نشد", "یک عنوان کوتاه، غیرخالی و تک‌خطی بنویسید. پیام باید هنوز منتظر ارسال باشد.", "باشه");
                return;
            }
            UiMotion.Feedback();
            await RefreshAsync();
            _scheduleSync();
        }
        catch (Exception) { await DisplayAlert("ثبت کامل نشد", "پیام روی گوشی باقی است. دوباره تلاش کنید.", "باشه"); }
        finally { _editing = false; }
    }

    private void OnDetailsClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: Guid id } && _allMessages.Find(x => x.Id == id) is { } item)
            item.IsExpanded = !item.IsExpanded;
    }

    private async void OnTemplatesClicked(object? sender, EventArgs e) => await Navigation.PushAsync(new TemplatesPage(_store, _senderId, SenderAddress));
}

public sealed class MessageItem : INotifyPropertyChanged
{
    private bool _isExpanded;

    public MessageItem(Guid id, string title, string body, string receivedAt, string transactionType,
        string amountText, string deliveryStatus, SmsDeliveryStatus status, bool canEditTitle, bool untitled,
        bool hasDeliveryError = false, bool recoveredFromInbox = false) =>
        (Id, Title, Body, ReceivedAt, TransactionType, AmountText, DeliveryStatus, Status, CanEditTitle, Untitled, HasDeliveryError, RecoveredFromInbox) =
        (id, title, body, receivedAt, transactionType, amountText, deliveryStatus, status, canEditTitle, untitled, hasDeliveryError, recoveredFromInbox);

    public Guid Id { get; }
    public string Title { get; }
    public string Body { get; }
    public string ReceivedAt { get; }
    public string TransactionType { get; }
    public string AmountText { get; }
    public string ParsedSummary => $"{TransactionType} · {AmountText}";
    public string DeliveryStatus { get; }
    public SmsDeliveryStatus Status { get; }
    public bool CanEditTitle { get; }
    public bool Untitled { get; }
    public bool HasDeliveryError { get; }
    public bool RecoveredFromInbox { get; }
    public bool CanRetry => Status is SmsDeliveryStatus.Rejected or SmsDeliveryStatus.Excluded
        || (Status == SmsDeliveryStatus.Pending && HasDeliveryError);
    public bool CanExclude => Status is SmsDeliveryStatus.Pending or SmsDeliveryStatus.Rejected;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailsAction)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailsIcon)));
        }
    }
    public string DetailsAction => IsExpanded ? "بستن جزئیات" : "نمایش جزئیات";
    public string DetailsIcon => IsExpanded ? AppIcons.ExpandLess : AppIcons.ExpandMore;
    public string StatusIcon => Status switch
    {
        SmsDeliveryStatus.Sent => AppIcons.CheckCircle,
        SmsDeliveryStatus.Rejected => AppIcons.Notification,
        SmsDeliveryStatus.Excluded => AppIcons.Delete,
        _ when Untitled && SmsSyncSettings.RequireTitle => AppIcons.Title,
        _ => AppIcons.Schedule
    };
    public Color StatusBackground
    {
        get
        {
            var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
            return Status switch
            {
                SmsDeliveryStatus.Sent => Color.FromArgb(dark ? "#123A34" : "#DDF6EC"),
                SmsDeliveryStatus.Rejected => Color.FromArgb(dark ? "#43291B" : "#FFF0E2"),
                SmsDeliveryStatus.Excluded => Color.FromArgb(dark ? "#263246" : "#E9EDF3"),
                _ when Untitled && SmsSyncSettings.RequireTitle => Color.FromArgb(dark ? "#40341C" : "#FFF4D9"),
                _ => Color.FromArgb(dark ? "#292C56" : "#EEEFFD")
            };
        }
    }
    public Color StatusForeground
    {
        get
        {
            var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
            return Status switch
            {
                SmsDeliveryStatus.Sent => Color.FromArgb(dark ? "#8FE0CD" : "#07695D"),
                SmsDeliveryStatus.Rejected => Color.FromArgb(dark ? "#FFC18F" : "#A34816"),
                SmsDeliveryStatus.Excluded => Color.FromArgb(dark ? "#C2CDDC" : "#526176"),
                _ when Untitled && SmsSyncSettings.RequireTitle => Color.FromArgb(dark ? "#FFD98A" : "#8A5B00"),
                _ => Color.FromArgb(dark ? "#DDD9FF" : "#453BB7")
            };
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
