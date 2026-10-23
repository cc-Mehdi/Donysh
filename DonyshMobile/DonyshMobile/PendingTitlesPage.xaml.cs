using System.Collections.ObjectModel;
using System.ComponentModel;
using DonyshMobile.Core;
using DonyshMobile.UI;

namespace DonyshMobile;

public partial class PendingTitlesPage : DonyshPage
{
    private readonly SmsMonitorStore _store;
    private bool _promptFirstMessage;
    private bool _saving, _entered;
    private readonly Dictionary<Guid, PendingTitleItem> _drafts = [];

    public PendingTitlesPage(SmsMonitorStore store, bool promptFirstMessage = false)
    {
        _store = store;
        _promptFirstMessage = promptFirstMessage;
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            SmsReceivedNotifier.MessageStored += OnMessageStored;
            var refreshed = await RefreshAsync();
            if (!_entered) { _entered = true; await UiMotion.EnterAsync(TitlesContent); }
            if (_promptFirstMessage)
            {
                _promptFirstMessage = false;
                // Refresh already handles an unreadable encrypted queue. Do not read it
                // again outside that error boundary when opened from a notification.
                var first = refreshed ? _drafts.Values.FirstOrDefault() : null;
                if (first is not null) Dispatcher.Dispatch(async () => await PromptForTitleAsync(first.Id));
            }
        }
        catch (Exception exception) { UiSafety.Log(exception); }
        finally { await CompletePageLoadingAsync(); }
    }

    protected override void OnDisappearing()
    {
        SmsReceivedNotifier.MessageStored -= OnMessageStored;
        base.OnDisappearing();
    }

    private void OnMessageStored(object? sender, EventArgs e) => Dispatcher.Dispatch(async () => await RefreshAsync());

    private async void OnHomeClicked(object? sender, EventArgs e) => await Navigation.PopToRootAsync();
    private async void OnAllMessagesClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(new AllMessagesPage(_store));
    private async void OnReviewClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(new AllMessagesPage(_store, reviewOnly: true));
    private async void OnSendersClicked(object? sender, EventArgs e)
    {
        MainPage.RequestSenders();
        await Navigation.PopToRootAsync();
    }

    private async void OnSetTitleClicked(object? sender, EventArgs e)
    {
        if (_saving || sender is not BindableObject { BindingContext: PendingTitleItem item }) return;
        await SaveTitleAsync(item.Id, item.DraftTitle);
    }

    private async Task PromptForTitleAsync(Guid id)
    {
        if (_saving || !_drafts.TryGetValue(id, out var message)) return;
        var excerpt = message.Body.Length > 110 ? message.Body[..110] + "…" : message.Body;
        var title = await DonyshTitleSheet.ShowAsync(this, "عنوان پیامک",
            $"{message.Sender}\n{excerpt}", message.DraftTitle, message.Suggestions);
        if (title is null) return;
        await SaveTitleAsync(id, title);
    }

    private void OnSuggestionClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: string suggestion } button) return;
        Element? current = button.Parent;
        while (current is not null && current.BindingContext is not PendingTitleItem) current = current.Parent;
        if (current?.BindingContext is PendingTitleItem item) item.DraftTitle = suggestion;
    }

    private async Task SaveTitleAsync(Guid id, string? title)
    {
        if (_saving) return;
        _saving = true;
        try
        {
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
        catch (Exception) { await DisplayAlert("ذخیره کامل نشد", "پیام روی گوشی باقی است. دوباره تلاش کنید.", "باشه"); }
        finally { _saving = false; }
    }

    private async Task<bool> RefreshAsync()
    {
        try
        {
            var messages = await _store.GetUntitledMessagesAsync();
            var ids = messages.Select(x => x.Id).ToHashSet();
            var matchersBySender = new Dictionary<Guid, Func<string, ParsedSms?>[]>();
            foreach (var key in _drafts.Keys.Where(x => !ids.Contains(x)).ToArray()) _drafts.Remove(key);
            foreach (var message in messages)
            {
                var suggestions = await _store.GetTitleSuggestionsAsync(message.SenderId, message.Body);
                if (!_drafts.TryGetValue(message.Id, out var item))
                {
                    if (!matchersBySender.TryGetValue(message.SenderId, out var matchers))
                    {
                        var templates = await _store.GetTemplatesAsync(message.SenderId);
                        matchers = templates.Select(SmsTemplateParser.CreateMatcher).Where(x => x is not null)
                            .Select(x => x!).ToArray();
                        matchersBySender[message.SenderId] = matchers;
                    }
                    var parsed = matchers.Select(parse => parse(message.Body)).FirstOrDefault(x => x is not null);
                    var amount = parsed is null ? "پیام بانکی" : UiText.Digits($"{parsed.AmountRial:N0} ریال");
                    item = new PendingTitleItem(message.Id, message.SenderId, message.SenderAddress, message.Body,
                        UiText.Date(message.ReceivedAtUtc), amount);
                    _drafts.Add(message.Id, item);
                }
                item.SetSuggestions(suggestions);
            }
            PendingList.ItemsSource = messages.Select(x => _drafts[x.Id]).ToList();
            RemainingLabel.Text = messages.Count == 0 ? "همهٔ عنوان‌ها ثبت شده‌اند" : $"{UiText.Digits(messages.Count)} پیام باقی مانده است";
            return true;
        }
        catch (Exception)
        {
            RemainingLabel.Text = "خواندن پیام‌ها کامل نشد؛ دوباره وارد این صفحه شوید.";
            return false;
        }
    }
}

public sealed class PendingTitleItem : INotifyPropertyChanged
{
    private string _draftTitle = string.Empty;
    public PendingTitleItem(Guid id, Guid senderId, string sender, string body, string receivedAt, string amountText) =>
        (Id, SenderId, Sender, Body, ReceivedAt, AmountText) = (id, senderId, sender, body, receivedAt, amountText);

    public Guid Id { get; }
    public Guid SenderId { get; }
    public string Sender { get; }
    public string Body { get; }
    public string ReceivedAt { get; }
    public string SenderAndDate => $"{Sender} · {ReceivedAt}";
    public string AmountText { get; }
    public ObservableCollection<string> Suggestions { get; } = [];
    public bool HasSuggestions => Suggestions.Count > 0;
    public string DraftTitle
    {
        get => _draftTitle;
        set
        {
            if (_draftTitle == value) return;
            _draftTitle = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DraftTitle)));
        }
    }

    public void SetSuggestions(IEnumerable<string> suggestions)
    {
        var desired = suggestions.ToArray();
        if (Suggestions.SequenceEqual(desired, StringComparer.Ordinal)) return;
        Suggestions.Clear();
        foreach (var suggestion in desired) Suggestions.Add(suggestion);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSuggestions)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
