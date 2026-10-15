using DonyshMobile.Core;
using DonyshMobile.UI;

namespace DonyshMobile;

public partial class PendingTitlesPage : ContentPage
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
        SmsReceivedNotifier.MessageStored += OnMessageStored;
        await RefreshAsync();
        if (!_entered) { _entered = true; await UiMotion.EnterAsync(TitlesContent); }
        if (_promptFirstMessage)
        {
            _promptFirstMessage = false;
            var first = (await _store.GetUntitledMessagesAsync()).FirstOrDefault();
            if (first is not null) Dispatcher.Dispatch(async () => await PromptForTitleAsync(first.Id));
        }
    }

    protected override void OnDisappearing()
    {
        SmsReceivedNotifier.MessageStored -= OnMessageStored;
        base.OnDisappearing();
    }

    private void OnMessageStored(object? sender, EventArgs e) => Dispatcher.Dispatch(async () => await RefreshAsync());

    private async void OnSetTitleClicked(object? sender, EventArgs e)
    {
        if (_saving || sender is not BindableObject { BindingContext: PendingTitleItem item }) return;
        await SaveTitleAsync(item.Id, item.DraftTitle);
    }

    private async Task PromptForTitleAsync(Guid id)
    {
        if (_saving || !_drafts.TryGetValue(id, out var message)) return;
        var excerpt = message.Body.Length > 110 ? message.Body[..110] + "…" : message.Body;
        var title = await DisplayPromptAsync("عنوان پیامک", $"{message.Sender}\n{excerpt}\n\nاین خرج برای چه بود؟",
            "ثبت", "انصراف", "مانند خرید روزانه", 160);
        if (title is null) return;
        await SaveTitleAsync(id, title);
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

    private async Task RefreshAsync()
    {
        try
        {
            var messages = await _store.GetUntitledMessagesAsync();
            var ids = messages.Select(x => x.Id).ToHashSet();
            foreach (var key in _drafts.Keys.Where(x => !ids.Contains(x)).ToArray()) _drafts.Remove(key);
            foreach (var message in messages)
                _drafts.TryAdd(message.Id, new PendingTitleItem(message.Id, message.SenderAddress, message.Body, UiText.Date(message.ReceivedAtUtc)));
            PendingList.ItemsSource = messages.Select(x => _drafts[x.Id]).ToList();
            RemainingLabel.Text = messages.Count == 0 ? "همهٔ عنوان‌ها ثبت شده‌اند" : $"{UiText.Digits(messages.Count)} پیام باقی مانده است";
        }
        catch (Exception) { RemainingLabel.Text = "خواندن پیام‌ها کامل نشد؛ دوباره وارد این صفحه شوید."; }
    }
}

public sealed record PendingTitleItem(Guid Id, string Sender, string Body, string ReceivedAt)
{
    public string DraftTitle { get; set; } = string.Empty;
}
