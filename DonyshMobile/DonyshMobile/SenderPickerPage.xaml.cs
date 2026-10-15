using DonyshMobile.Core;
using DonyshMobile.UI;

namespace DonyshMobile;

public partial class SenderPickerPage : ContentPage
{
    private readonly IReadOnlyList<string> _senders;
    private readonly Func<string, Task> _selected;
    private string? _selectedAddress;
    private bool _confirming;
    private bool _entered;

    public SenderPickerPage(IReadOnlyList<string> senders, Func<string, Task> selected)
    {
        _senders = senders;
        _selected = selected;
        InitializeComponent();
        SenderList.ItemsSource = _senders;
        UpdateCount(_senders.Count);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_entered) return;
        _entered = true;
        await UiMotion.EnterAsync(PickerContent);
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        var query = e.NewTextValue?.Trim() ?? string.Empty;
        var normalized = SenderAddress.Normalize(query);
        var results = _senders.Where(address => address.Contains(query, StringComparison.OrdinalIgnoreCase)
            || normalized.Length > 0 && SenderAddress.Normalize(address).Contains(normalized, StringComparison.Ordinal)).ToList();
        SenderList.ItemsSource = results;
        _selectedAddress = null;
        ConfirmButton.IsEnabled = false;
        SelectionLabel.Text = "یک فرستنده از فهرست انتخاب کنید.";
        UpdateCount(results.Count);
    }

    private void UpdateCount(int count) => ResultCountLabel.Text = $"{UiText.Digits(count)} فرستنده در پیامک‌های اخیر";

    private void OnSenderSelected(object? sender, SelectionChangedEventArgs e)
    {
        _selectedAddress = e.CurrentSelection.FirstOrDefault() as string;
        ConfirmButton.IsEnabled = _selectedAddress is not null && !_confirming;
        SelectionLabel.Text = _selectedAddress is null ? "یک فرستنده از فهرست انتخاب کنید." : $"انتخاب شما: {_selectedAddress}";
    }

    private async void OnConfirmClicked(object? sender, EventArgs e)
    {
        if (_confirming || _selectedAddress is not { } address) return;
        _confirming = true;
        ConfirmButton.IsEnabled = false;
        ConfirmButton.Text = "در حال افزودن…";
        try
        {
            await _selected(address);
            UiMotion.Feedback();
            await Navigation.PopAsync();
        }
        catch (Exception)
        {
            await DisplayAlert("فرستنده اضافه نشد", "افزودن فرستنده کامل نشد. دوباره تلاش کنید.", "باشه");
        }
        finally
        {
            _confirming = false;
            ConfirmButton.IsEnabled = _selectedAddress is not null;
            ConfirmButton.Text = "افزودن به فرستنده‌های تحت نظر";
        }
    }
}
