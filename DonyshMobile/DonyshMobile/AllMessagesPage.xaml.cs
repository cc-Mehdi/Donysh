using System.Collections.ObjectModel;
using DonyshMobile.Core;
using DonyshMobile.UI;

namespace DonyshMobile;

public partial class AllMessagesPage : DonyshPage
{
    private readonly SmsMonitorStore _store;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly List<AllMessageItem> _allMessages = [];
    private string _filter;
    private bool _subscribed;
    private bool _entered;

    public ObservableCollection<AllMessageItem> Messages { get; } = [];

    public AllMessagesPage(SmsMonitorStore store, bool reviewOnly = false)
    {
        _store = store;
        _filter = reviewOnly ? "review" : "all";
        InitializeComponent();
        BindingContext = this;
        UpdateFilters();
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

    private void OnFilterClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: string filter }) return;
        _filter = filter;
        UpdateFilters();
        FilterMessages();
    }

    private void UpdateFilters()
    {
        foreach (var button in new[] { AllFilter, ReviewFilter, PendingFilter, SentFilter })
        {
            var selected = Equals(button.CommandParameter, _filter);
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
        foreach (var item in _allMessages.Where(x => _filter switch
                 {
                     "review" => x.Status == SmsDeliveryStatus.Rejected,
                     "pending" => x.Status == SmsDeliveryStatus.Pending,
                     "sent" => x.Status == SmsDeliveryStatus.Sent,
                     _ => true
                 }))
            Messages.Add(item);
    }

    private async Task RefreshAsync()
    {
        if (!await _refreshGate.WaitAsync(0)) return;
        try
        {
            var inbox = await _store.GetVisibleInboxAsync();
            var matchersBySender = inbox.Templates.GroupBy(x => x.SenderId).ToDictionary(
                group => group.Key,
                group => group.Select(SmsTemplateParser.CreateMatcher).Where(x => x is not null).ToArray());
            var items = new List<AllMessageItem>();
            foreach (var message in inbox.Messages)
            {
                matchersBySender.TryGetValue(message.SenderId, out var matchers);
                var parsed = matchers?.Select(parse => parse!(message.Body)).FirstOrDefault(x => x is not null);
                var amount = parsed is null ? "پیام بانکی" : UiText.Digits($"{parsed.AmountRial:N0} ریال");
                items.Add(new AllMessageItem(message.Id, message.SenderId, message.SenderAddress, message.Body,
                    message.Title, UiText.Date(message.ReceivedAtUtc), message.ReceivedAtUtc, amount, message.DeliveryStatus));
            }
            _allMessages.Clear();
            _allMessages.AddRange(items.OrderByDescending(x => x.ReceivedAtUtc));
            FilterMessages();
        }
        catch (Exception exception)
        {
            UiSafety.Log(exception);
            await UiSafety.AlertAsync(this, "پیام‌ها باز نشد", "خواندن پیام‌های ذخیره‌شده کامل نشد. دوباره تلاش کنید.");
        }
        finally { _refreshGate.Release(); }
    }

    private async void OnRefreshRequested(object? sender, EventArgs e)
    {
        try { await RefreshAsync(); }
        finally { MessagesRefreshView.IsRefreshing = false; }
    }

    private async void OnMessageSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is CollectionView list) list.SelectedItem = null;
        if (e.CurrentSelection.FirstOrDefault() is not AllMessageItem item) return;
        var initialFilter = item.Status switch
        {
            SmsDeliveryStatus.Rejected => 3,
            SmsDeliveryStatus.Sent => 2,
            SmsDeliveryStatus.Excluded => 4,
            _ when item.IsUntitled => 5,
            _ => 1
        };
        await Navigation.PushAsync(new MessagesPage(_store, item.SenderId, item.Sender, initialFilter, item.Id));
    }

    private async void OnHomeClicked(object? sender, EventArgs e) => await Navigation.PopToRootAsync();

    private async void OnSendersClicked(object? sender, EventArgs e)
    {
        MainPage.RequestSenders();
        await Navigation.PopToRootAsync();
    }
}

public sealed class AllMessageItem
{
    public AllMessageItem(Guid id, Guid senderId, string sender, string body, string? title,
        string receivedAt, DateTimeOffset receivedAtUtc, string amountText, SmsDeliveryStatus status)
    {
        Id = id;
        SenderId = senderId;
        Sender = sender;
        BodyPreview = body.Length > 110 ? body[..110] + "…" : body;
        Title = string.IsNullOrWhiteSpace(title) ? "بدون عنوان" : title;
        IsUntitled = string.IsNullOrWhiteSpace(title);
        ReceivedAt = receivedAt;
        ReceivedAtUtc = receivedAtUtc;
        AmountText = amountText;
        Status = status;
    }

    public Guid Id { get; }
    public Guid SenderId { get; }
    public string Sender { get; }
    public string Title { get; }
    public bool IsUntitled { get; }
    public string BodyPreview { get; }
    public string ReceivedAt { get; }
    public DateTimeOffset ReceivedAtUtc { get; }
    public string SenderAndDate => $"{Sender} · {ReceivedAt}";
    public string AmountText { get; }
    public SmsDeliveryStatus Status { get; }
    public string StatusText => Status switch
    {
        SmsDeliveryStatus.Sent => "ارسال‌شده",
        SmsDeliveryStatus.Rejected => "نیازمند بررسی",
        SmsDeliveryStatus.Excluded => "کنار گذاشته‌شده",
        _ when IsUntitled && SmsSyncSettings.RequireTitle => "عنوان لازم",
        _ => "در صف"
    };
    public Color BadgeBackground => Color.FromArgb(Application.Current?.RequestedTheme == AppTheme.Dark
        ? Status switch
        {
            SmsDeliveryStatus.Sent => "#123A34",
            SmsDeliveryStatus.Rejected => "#43291B",
            SmsDeliveryStatus.Excluded => "#263246",
            _ when IsUntitled && SmsSyncSettings.RequireTitle => "#40341C",
            _ => "#292C56"
        }
        : Status switch
        {
            SmsDeliveryStatus.Sent => "#DDF6EC",
            SmsDeliveryStatus.Rejected => "#FFF0E2",
            SmsDeliveryStatus.Excluded => "#E9EDF3",
            _ when IsUntitled && SmsSyncSettings.RequireTitle => "#FFF4D9",
            _ => "#EEEFFD"
        });
    public Color BadgeForeground => Color.FromArgb(Application.Current?.RequestedTheme == AppTheme.Dark
        ? Status switch
        {
            SmsDeliveryStatus.Sent => "#8FE0CD",
            SmsDeliveryStatus.Rejected => "#FFC18F",
            SmsDeliveryStatus.Excluded => "#C2CDDC",
            _ when IsUntitled && SmsSyncSettings.RequireTitle => "#FFD98A",
            _ => "#DDD9FF"
        }
        : Status switch
        {
            SmsDeliveryStatus.Sent => "#07695D",
            SmsDeliveryStatus.Rejected => "#A34816",
            SmsDeliveryStatus.Excluded => "#526176",
            _ when IsUntitled && SmsSyncSettings.RequireTitle => "#8A5B00",
            _ => "#453BB7"
        });
}
