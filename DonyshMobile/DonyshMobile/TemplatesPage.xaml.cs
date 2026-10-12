using System.Collections.ObjectModel;
using DonyshMobile.Core;

namespace DonyshMobile;

public partial class TemplatesPage : ContentPage
{
    private readonly SmsMonitorStore _store;
    private readonly Guid _senderId;
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
        var templates = await _store.GetTemplatesAsync(_senderId);
        Templates.Clear();
        foreach (var template in templates)
            Templates.Add(new TemplateItem(template.Id, template.Name,
                $"{(template.Unit == MoneyUnit.Rial ? "ریال" : "تومان")} · {(template.Pattern.Contains("{type}", StringComparison.OrdinalIgnoreCase) ? "نوع از متن پیام" : template.DefaultKind == TransactionKind.Withdrawal ? "برداشت" : "واریز")}"));
    }

    private async void OnAddClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(new TemplateEditorPage(_store, _senderId, SenderAddress));

    private async void OnEditClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: Guid id }) return;
        var template = (await _store.GetTemplatesAsync(_senderId)).FirstOrDefault(x => x.Id == id);
        if (template is not null)
            await Navigation.PushAsync(new TemplateEditorPage(_store, _senderId, SenderAddress, template));
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: Guid id }) return;
        var item = Templates.FirstOrDefault(x => x.Id == id);
        if (item is null || !await DisplayAlert("حذف قالب", $"قالب «{item.Name}» حذف شود؟", "حذف", "انصراف")) return;
        if (await _store.DeleteTemplateAsync(_senderId, id)) Templates.Remove(item);
    }
}

public sealed record TemplateItem(Guid Id, string Name, string Details);
