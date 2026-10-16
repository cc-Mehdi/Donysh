using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace DonyshMobile.UI;

[ContentProperty(nameof(Items))]
public sealed class DonyshPicker : ContentView
{
    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title), typeof(string), typeof(DonyshPicker), "انتخاب کنید",
        propertyChanged: static (bindable, _, _) => ((DonyshPicker)bindable).UpdateText());

    public static readonly BindableProperty SelectedIndexProperty = BindableProperty.Create(
        nameof(SelectedIndex), typeof(int), typeof(DonyshPicker), -1, BindingMode.TwoWay,
        propertyChanged: static (bindable, oldValue, newValue) =>
            ((DonyshPicker)bindable).OnSelectedIndexChanged((int)oldValue, (int)newValue));

    public static readonly BindableProperty SelectedItemProperty = BindableProperty.Create(
        nameof(SelectedItem), typeof(object), typeof(DonyshPicker), null, BindingMode.TwoWay,
        propertyChanged: static (bindable, _, newValue) => ((DonyshPicker)bindable).OnSelectedItemChanged(newValue));

    private readonly Label _valueLabel;
    private bool _synchronizing;
    private int _pendingSelectedIndex = -1;

    public DonyshPicker()
    {
        FlowDirection = FlowDirection.RightToLeft;
        MinimumHeightRequest = 52;
        HorizontalOptions = LayoutOptions.Fill;

        _valueLabel = new Label
        {
            FontFamily = "YekanBakh",
            FontSize = 15,
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        _valueLabel.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#15243C"), Color.FromArgb("#F1F5FF"));

        var chevron = new Label
        {
            Text = AppIcons.ChevronDown,
            FontFamily = "MaterialIconsRound",
            FontSize = 24,
            VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            WidthRequest = 32
        };
        chevron.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#4F46E5"), Color.FromArgb("#C7D2FE"));

        var grid = new Grid
        {
            Padding = new Thickness(13, 3),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        grid.Add(_valueLabel);
        grid.Add(chevron, 1);

        var border = new Border
        {
            Content = grid,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
            StrokeThickness = 1,
            MinimumHeightRequest = 52
        };
        border.SetAppThemeColor(Border.BackgroundColorProperty, Color.FromArgb("#F5F7FC"), Color.FromArgb("#0F1A2D"));
        border.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#E0E7F2"), Color.FromArgb("#2A3953"));

        var tap = new TapGestureRecognizer();
        tap.Tapped += OnTapped;
        border.GestureRecognizers.Add(tap);
        Content = border;

        Items.CollectionChanged += OnItemsChanged;
        UpdateText();
    }

    public ObservableCollection<string> Items { get; } = [];

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public event EventHandler? SelectedIndexChanged;

    private async void OnTapped(object? sender, TappedEventArgs e)
    {
        if (!IsEnabled || Items.Count == 0) return;
        var page = FindPage();
        if (page is null) return;
        try
        {
            var selected = await DonyshSelectionSheet.ShowAsync(page, Title, Items, SelectedIndex);
            if (selected.HasValue) SelectedIndex = selected.Value;
        }
        catch (Exception exception)
        {
            UiSafety.Log(exception);
        }
    }

    private Page? FindPage()
    {
        Element? current = this;
        while (current is not null)
        {
            if (current is Page page) return page;
            current = current.Parent;
        }
        return null;
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_pendingSelectedIndex >= 0 && _pendingSelectedIndex < Items.Count)
        {
            var pending = _pendingSelectedIndex;
            _pendingSelectedIndex = -1;
            SelectedIndex = pending;
        }
        else if (SelectedItem is not null)
        {
            var index = Items.IndexOf(SelectedItem.ToString() ?? string.Empty);
            if (index != SelectedIndex) SelectedIndex = index;
        }
        else if (SelectedIndex >= 0 && SelectedIndex < Items.Count)
        {
            _synchronizing = true;
            SelectedItem = Items[SelectedIndex];
            _synchronizing = false;
        }
        UpdateText();
    }

    private void OnSelectedIndexChanged(int oldIndex, int newIndex)
    {
        if (_synchronizing) return;
        if (newIndex >= 0 && Items.Count == 0)
        {
            _pendingSelectedIndex = newIndex;
            _synchronizing = true;
            SelectedIndex = -1;
            _synchronizing = false;
            UpdateText();
            return;
        }
        var normalized = newIndex >= 0 && newIndex < Items.Count ? newIndex : -1;
        if (normalized != newIndex)
        {
            SelectedIndex = normalized;
            return;
        }

        _synchronizing = true;
        SelectedItem = normalized >= 0 ? Items[normalized] : null;
        _synchronizing = false;
        UpdateText();
        if (oldIndex != normalized) SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSelectedItemChanged(object? newValue)
    {
        if (_synchronizing) return;
        _synchronizing = true;
        SelectedIndex = newValue is null ? -1 : Items.IndexOf(newValue.ToString() ?? string.Empty);
        _synchronizing = false;
        UpdateText();
    }

    private void UpdateText()
    {
        if (_valueLabel is null) return;
        var hasSelection = SelectedIndex >= 0 && SelectedIndex < Items.Count;
        _valueLabel.Text = hasSelection ? Items[SelectedIndex] : (string.IsNullOrWhiteSpace(Title) ? "انتخاب کنید" : Title);
        _valueLabel.Opacity = hasSelection ? 1 : 0.72;
    }
}
