using System.Collections.ObjectModel;
using DonyshMobile.Core;
using DonyshMobile.UI;

namespace DonyshMobile;

public partial class TemplatesPage : DonyshPage
{
    private readonly SmsMonitorStore _store;
    private readonly Guid _senderId;
    private bool _navigating;
    private bool _entered;
    public string SenderAddress { get; }
    public ObservableCollection<TemplateItem> Templates { get; } = [];

    public TemplatesPage(SmsMonitorStore store, Guid senderId, string senderAddress)
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
        try
        {
            var templates = await _store.GetTemplatesAsync(_senderId);
            Templates.Clear();
            foreach (var template in templates)
                Templates.Add(new TemplateItem(template.Id, template.Name,
                    $"{(template.Unit == MoneyUnit.Rial ? "ریال" : "تومان")} · {(template.Pattern.Contains("{type}", StringComparison.OrdinalIgnoreCase) ? "نوع از متن پیام" : template.DefaultKind == TransactionKind.Withdrawal ? "برداشت" : "واریز")}",
                    template.SampleMessage));
            TemplateCountLabel.Text = Templates.Count == 0 ? "هنوز قالبی اضافه نشده است." : $"{UiText.Digits(Templates.Count)} قالب برای این فرستنده";
            if (!_entered)
            {
                _entered = true;
                await UiMotion.EnterAsync(TemplateContent);
            }
        }
        catch (Exception)
        {
            TemplateCountLabel.Text = "فهرست قالب‌ها خوانده نشد. دوباره وارد این صفحه شوید.";
        }
        finally { await CompletePageLoadingAsync(); }
    }

    private async void OnAddClicked(object? sender, EventArgs e)
    {
        if (_navigating) return;
        _navigating = true;
        try { await Navigation.PushAsync(new TemplateEditorPage(_store, _senderId, SenderAddress)); }
        catch (Exception)
        {
            await DisplayAlert("صفحه باز نشد", "باز کردن صفحه ساخت قالب کامل نشد. دوباره تلاش کنید.", "باشه");
        }
        finally { _navigating = false; }
    }

    private async void OnEditClicked(object? sender, EventArgs e)
    {
        if (_navigating || sender is not Button { CommandParameter: Guid id }) return;
        _navigating = true;
        try
        {
            var template = (await _store.GetTemplatesAsync(_senderId)).FirstOrDefault(x => x.Id == id);
            if (template is not null)
                await Navigation.PushAsync(new TemplateEditorPage(_store, _senderId, SenderAddress, template));
        }
        catch (Exception)
        {
            await DisplayAlert("قالب باز نشد", "خواندن قالب کامل نشد. دوباره تلاش کنید.", "باشه");
        }
        finally { _navigating = false; }
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: Guid id }) return;
        var item = Templates.FirstOrDefault(x => x.Id == id);
        if (item is null || !await DisplayAlert("حذف قالب", $"قالب «{item.Name}» حذف شود؟", "حذف", "انصراف")) return;
        try
        {
            if (await _store.DeleteTemplateAsync(_senderId, id))
            {
                SmsConfigurationSync.MarkChanged();
                SmsConfigurationSync.Queue(_store);
                Templates.Remove(item);
                TemplateCountLabel.Text = Templates.Count == 0 ? "هنوز قالبی اضافه نشده است." : $"{UiText.Digits(Templates.Count)} قالب برای این فرستنده";
            }
        }
        catch (Exception)
        {
            await DisplayAlert("حذف نشد", "حذف قالب کامل نشد. دوباره تلاش کنید.", "باشه");
        }
    }
}

public sealed record TemplateItem(Guid Id, string Name, string Details, string Sample)
{
    public bool HasSample => !string.IsNullOrWhiteSpace(Sample);
}
