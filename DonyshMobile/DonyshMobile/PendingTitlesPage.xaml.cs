using DonyshMobile.Core;

namespace DonyshMobile;

public partial class PendingTitlesPage : ContentPage
{
    private readonly SmsMonitorStore _store;
    private bool _promptFirstMessage;

    public PendingTitlesPage(SmsMonitorStore store, bool promptFirstMessage = false)
    {
        _store = store;
        _promptFirstMessage = promptFirstMessage;
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshAsync();
        if (_promptFirstMessage)
        {
            _promptFirstMessage = false;
            var first = (await _store.GetUntitledMessagesAsync()).FirstOrDefault();
            if (first is not null) Dispatcher.Dispatch(async () => await PromptForTitleAsync(first.Id));
        }
    }

    private async void OnSetTitleClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: Guid id }) return;
        await PromptForTitleAsync(id);
    }

    private async Task PromptForTitleAsync(Guid id)
    {
        var title = await DisplayPromptAsync("عنوان پیامک", "برای این تراکنش چه عنوانی ثبت شود؟",
            "ثبت", "انصراف", "مانند خرید روزانه", 160);
        if (title is null) return;
        if (!await _store.SetTitleAsync(id, title))
        {
            await DisplayAlert("عنوان ثبت نشد", "عنوان باید بین ۱ تا ۱۶۰ نویسه باشد و پیام هنوز منتظر ارسال باشد.", "باشه");
            return;
        }
        await RefreshAsync();
        SyncPlatform.Schedule();
#if ANDROID
        await DonyshMobile.Platforms.Android.TitleReminderNotifier.RefreshAsync(global::Android.App.Application.Context);
#endif
    }

    private async Task RefreshAsync()
    {
        var messages = await _store.GetUntitledMessagesAsync();
        PendingList.ItemsSource = messages.Select(x => new PendingTitleItem(x.Id, x.SenderAddress, x.Body)).ToList();
    }
}

public sealed record PendingTitleItem(Guid Id, string Sender, string Body);
