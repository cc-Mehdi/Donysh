using System.Collections.ObjectModel;
using System.Globalization;
using DonyshMobile.Core;

namespace DonyshMobile;

public partial class MessagesPage : ContentPage
{
    private readonly SmsMonitorStore _store;
    private readonly Guid _senderId;
    private bool _subscribed;

    public string SenderAddress { get; }
    public ObservableCollection<MessageItem> Messages { get; } = [];

    public MessagesPage(SmsMonitorStore store, Guid senderId, string senderAddress)
    {
        _store = store;
        _senderId = senderId;
        SenderAddress = senderAddress;
        Title = senderAddress;
        InitializeComponent();
        BindingContext = this;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_subscribed)
        {
            SmsReceivedNotifier.MessageStored += OnMessageStored;
            _subscribed = true;
        }
        await RefreshAsync();
    }

    protected override void OnDisappearing()
    {
        if (_subscribed)
        {
            SmsReceivedNotifier.MessageStored -= OnMessageStored;
            _subscribed = false;
        }
        base.OnDisappearing();
    }

    private void OnMessageStored(object? sender, EventArgs e) =>
        Dispatcher.Dispatch(async () => await RefreshAsync());

    private async Task RefreshAsync()
    {
        var stored = await _store.GetMessagesAsync(_senderId);
        var templates = await _store.GetTemplatesAsync(_senderId);
        var matchers = templates.Select(SmsTemplateParser.CreateMatcher).Where(x => x is not null).ToArray();
        Messages.Clear();
        foreach (var message in stored)
        {
            var match = matchers.Select(parse => parse!(message.Body)).FirstOrDefault(p => p is not null);
            var summary = match is null ? "" : $"{match.TemplateName} · {(match.Kind == TransactionKind.Withdrawal ? "برداشت" : "واریز")} · {match.AmountRial:N0} ریال";
            Messages.Add(new MessageItem(message.Body, FormatLocal(message.ReceivedAtUtc), summary));
        }
    }

    private async void OnTemplatesClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(new TemplatesPage(_store, _senderId, SenderAddress));

    private static string FormatLocal(DateTimeOffset value)
    {
        var local = value.ToLocalTime();
        var calendar = new PersianCalendar();
        return $"{calendar.GetYear(local.DateTime):0000}/{calendar.GetMonth(local.DateTime):00}/{calendar.GetDayOfMonth(local.DateTime):00} - {local:HH:mm}";
    }

}

public sealed record MessageItem(string Body, string ReceivedAt, string ParsedSummary);
