namespace DonyshMobile;

public partial class SenderPickerPage : ContentPage
{
    private readonly IReadOnlyList<string> _senders;
    private readonly Func<string, Task> _selected;

    public SenderPickerPage(IReadOnlyList<string> senders, Func<string, Task> selected)
    {
        _senders = senders;
        _selected = selected;
        InitializeComponent();
        SenderList.ItemsSource = _senders;
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e) =>
        SenderList.ItemsSource = _senders.Where(x => x.Contains(e.NewTextValue ?? string.Empty,
            StringComparison.OrdinalIgnoreCase)).ToList();

    private async void OnSenderSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not string address) return;
        SenderList.SelectedItem = null;
        await _selected(address);
        await Navigation.PopAsync();
    }
}
