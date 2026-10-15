using System.Collections.ObjectModel;
using DonyshMobile.Core;
using DonyshMobile.UI;

namespace DonyshMobile;

public partial class MessagesPage : ContentPage
{
    private readonly SmsMonitorStore _store;
    private readonly Guid _senderId;
    private readonly List<MessageItem> _allMessages = [];
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private bool _subscribed, _entered, _editing;
    private string _search = string.Empty;
    public string SenderAddress { get; }
    public ObservableCollection<MessageItem> Messages { get; } = [];
    public string MessageCountText => $"{UiText.Digits(Messages.Count)} پیام";

    public MessagesPage(SmsMonitorStore store, Guid senderId, string senderAddress)
    {
        _store = store;
        _senderId = senderId;
        SenderAddress = senderAddress;
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
        await RefreshAsync();
        if (!_entered) { _entered = true; await UiMotion.EnterAsync(MessagesContent); }
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
    private void OnFilterChanged(object? sender, EventArgs e) => FilterMessages();
    private void FilterMessages()
    {
        var filter = StatusPicker?.SelectedIndex ?? 0;
        Messages.Clear();
        foreach (var item in _allMessages.Where(x => (filter switch
                 {
                     1 => x.Status == SmsDeliveryStatus.Pending,
                     2 => x.Status == SmsDeliveryStatus.Sent,
                     3 => x.Status == SmsDeliveryStatus.Rejected,
                     4 => x.CanEditTitle && x.Untitled,
                     _ => true
                 }) && (string.IsNullOrWhiteSpace(_search) || x.Body.Contains(_search, StringComparison.OrdinalIgnoreCase)
                        || x.Title.Contains(_search, StringComparison.OrdinalIgnoreCase))))
            Messages.Add(item);
        OnPropertyChanged(nameof(MessageCountText));
    }

    private async Task RefreshAsync()
    {
        if (!await _refreshGate.WaitAsync(0)) return;
        try
        {
            var stored = await _store.GetMessagesAsync(_senderId);
            var templates = await _store.GetTemplatesAsync(_senderId);
            var items = await Task.Run(() =>
            {
                var matchers = templates.Select(SmsTemplateParser.CreateMatcher).Where(x => x is not null).ToArray();
                return stored.Select(message =>
                {
                    var match = matchers.Select(parse => parse!(message.Body)).FirstOrDefault(p => p is not null);
                    var summary = match is null ? "قالبی برای این پیام پیدا نشد" : UiText.Digits($"{(match.Kind == TransactionKind.Withdrawal ? "برداشت" : "واریز")} · {match.AmountRial:N0} ریال");
                    var untitled = string.IsNullOrWhiteSpace(message.Title);
                    var delivery = message.DeliveryStatus switch
                    {
                        SmsDeliveryStatus.Sent => "دریافت توسط سامانه تأیید شد",
                        SmsDeliveryStatus.Rejected => "قالب پذیرفته نشد؛ پیام روی گوشی باقی است",
                        _ when untitled && SmsSyncSettings.RequireTitle => "منتظر نوشتن عنوان شما",
                        _ when message.DeliveryAttempts > 0 => $"در صف تلاش دوباره · {UiText.Digits(message.DeliveryAttempts)} تلاش",
                        _ => "در صف ارسال به سامانه"
                    };
                    return new MessageItem(message.Id, untitled ? "بدون عنوان" : message.Title!, message.Body,
                        UiText.Date(message.ReceivedAtUtc), summary, delivery, message.DeliveryStatus,
                        message.DeliveryStatus == SmsDeliveryStatus.Pending, untitled);
                }).ToList();
            });
            _allMessages.Clear();
            _allMessages.AddRange(items);
            FilterMessages();
        }
        catch (Exception) { await DisplayAlert("پیام‌ها باز نشد", "خواندن اطلاعات کامل نشد. برنامه را دوباره باز کنید؛ پیام‌ها حذف نشده‌اند.", "باشه"); }
        finally { _refreshGate.Release(); }
    }

    private async void OnSetTitleClicked(object? sender, EventArgs e)
    {
        if (_editing || sender is not Button { CommandParameter: Guid id }) return;
        _editing = true;
        try
        {
            var item = _allMessages.Find(x => x.Id == id);
            if (item is null) return;
            var title = await DisplayPromptAsync("عنوان تراکنش", "این خرج برای چه بود؟", "ثبت عنوان", "انصراف",
                "مانند خرید روزانه", 160, initialValue: item.Untitled ? string.Empty : item.Title);
            if (title is null) return;
            if (!await _store.SetTitleAsync(id, title))
            {
                await DisplayAlert("عنوان ثبت نشد", "یک عنوان کوتاه، غیرخالی و تک‌خطی بنویسید. پیام باید هنوز منتظر ارسال باشد.", "باشه");
                return;
            }
            UiMotion.Feedback();
            await RefreshAsync();
            SyncPlatform.Schedule();
#if ANDROID
            await Platforms.Android.TitleReminderNotifier.RefreshAsync(global::Android.App.Application.Context);
#endif
        }
        catch (Exception) { await DisplayAlert("ثبت کامل نشد", "پیام روی گوشی باقی است. دوباره تلاش کنید.", "باشه"); }
        finally { _editing = false; }
    }

    private async void OnDetailsClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: Guid id } && _allMessages.Find(x => x.Id == id) is { } item)
            await DisplayAlert(item.Title, item.Body, "بستن");
    }

    private async void OnTemplatesClicked(object? sender, EventArgs e) => await Navigation.PushAsync(new TemplatesPage(_store, _senderId, SenderAddress));
}

public sealed record MessageItem(Guid Id, string Title, string Body, string ReceivedAt, string ParsedSummary,
    string DeliveryStatus, SmsDeliveryStatus Status, bool CanEditTitle, bool Untitled);
