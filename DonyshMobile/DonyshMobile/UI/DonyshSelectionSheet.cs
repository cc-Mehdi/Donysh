namespace DonyshMobile.UI;

public sealed record DonyshSelectionOption(string Text, string? Glyph = null, bool IsDestructive = false);

public sealed class DonyshSelectionSheet : ContentPage
{
    private readonly TaskCompletionSource<int?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Border _panel;
    private int _closing;

    private DonyshSelectionSheet(string title, IReadOnlyList<DonyshSelectionOption> options, int selectedIndex)
    {
        FlowDirection = FlowDirection.RightToLeft;
        BackgroundColor = Colors.Transparent;
        Shell.SetNavBarIsVisible(this, false);

        var backdrop = new BoxView { Color = Color.FromArgb("#99081120") };
        var dismissTap = new TapGestureRecognizer();
        dismissTap.Tapped += async (_, _) => await CloseAsync(null);
        backdrop.GestureRecognizers.Add(dismissTap);

        var heading = new Label
        {
            Text = string.IsNullOrWhiteSpace(title) ? "انتخاب کنید" : title,
            FontFamily = "YekanBakhBold",
            FontSize = 19,
            VerticalOptions = LayoutOptions.Center
        };
        heading.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#15243C"), Color.FromArgb("#F1F5FF"));

        var close = new Button
        {
            Text = AppIcons.Close,
            FontFamily = "MaterialIconsRound",
            FontSize = 22,
            BackgroundColor = Colors.Transparent,
            Padding = 0,
            WidthRequest = 44,
            HeightRequest = 44,
            CornerRadius = 22
        };
        SemanticProperties.SetDescription(close, "بستن فهرست");
        close.SetAppThemeColor(Button.TextColorProperty, Color.FromArgb("#586B85"), Color.FromArgb("#ADBCD3"));
        close.Clicked += async (_, _) => await CloseAsync(null);

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 10
        };
        header.Add(heading);
        header.Add(close, 1);

        var rows = new VerticalStackLayout { Spacing = 7 };
        for (var index = 0; index < options.Count; index++)
        {
            var optionIndex = index;
            var option = options[index];
            rows.Add(CreateRow(option, optionIndex, optionIndex == selectedIndex));
        }

        var scroll = new ScrollView
        {
            Content = rows,
            MaximumHeightRequest = 470,
            VerticalScrollBarVisibility = ScrollBarVisibility.Default
        };

        var content = new VerticalStackLayout
        {
            Padding = new Thickness(18, 12, 18, 22),
            Spacing = 10,
            Children = { header, scroll }
        };

        _panel = new Border
        {
            Content = content,
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 28 },
            Margin = new Thickness(12, 24),
            MaximumWidthRequest = 680,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.End
        };
        _panel.SetAppThemeColor(Border.BackgroundColorProperty, Color.FromArgb("#FFFFFF"), Color.FromArgb("#162237"));
        _panel.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#E0E7F2"), Color.FromArgb("#2A3953"));

        var root = new Grid();
        root.Add(backdrop);
        root.Add(_panel);
        Content = root;
    }

    public static Task<int?> ShowAsync(Page host, string title, IReadOnlyList<string> items, int selectedIndex = -1) =>
        ShowAsync(host, title, items.Select(static item => new DonyshSelectionOption(item)).ToArray(), selectedIndex);

    public static async Task<int?> ShowAsync(Page host, string title, IReadOnlyList<DonyshSelectionOption> options, int selectedIndex = -1)
    {
        if (options.Count == 0) return null;
        var sheet = new DonyshSelectionSheet(title, options, selectedIndex);
        await host.Navigation.PushModalAsync(sheet, false);
        await sheet.AnimateInAsync();
        return await sheet._completion.Task;
    }

    protected override bool OnBackButtonPressed()
    {
        _ = CloseAsync(null);
        return true;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Complete callers even if the host navigation removes the sheet directly.
        // A normal selection owns completion in CloseAsync; do not let the modal
        // lifecycle replace that selected index with a cancellation result.
        if (Volatile.Read(ref _closing) == 0) _completion.TrySetResult(null);
    }

    internal Task CompleteSelectionForDiagnosticsAsync(int? result) => CloseAsync(result);

    private Border CreateRow(DonyshSelectionOption option, int index, bool selected)
    {
        var icon = new Label
        {
            Text = string.IsNullOrWhiteSpace(option.Glyph) ? AppIcons.CheckCircle : option.Glyph,
            FontFamily = "MaterialIconsRound",
            FontSize = 21,
            WidthRequest = 32,
            VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            Opacity = string.IsNullOrWhiteSpace(option.Glyph) && !selected ? 0 : 1
        };
        icon.SetAppThemeColor(Label.TextColorProperty,
            Color.FromArgb(option.IsDestructive ? "#A91D46" : "#4F46E5"),
            Color.FromArgb(option.IsDestructive ? "#FFC0CE" : "#C7D2FE"));

        var label = new Label
        {
            Text = option.Text,
            FontFamily = selected ? "YekanBakhBold" : "YekanBakh",
            FontSize = 16,
            VerticalOptions = LayoutOptions.Center
        };
        label.SetAppThemeColor(Label.TextColorProperty,
            Color.FromArgb(option.IsDestructive ? "#A91D46" : "#15243C"),
            Color.FromArgb(option.IsDestructive ? "#FFC0CE" : "#F1F5FF"));

        var selectedIcon = new Label
        {
            Text = AppIcons.Check,
            FontFamily = "MaterialIconsRound",
            FontSize = 20,
            WidthRequest = 30,
            IsVisible = selected,
            VerticalOptions = LayoutOptions.Center
        };
        selectedIcon.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#4F46E5"), Color.FromArgb("#C7D2FE"));

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 10
        };
        grid.Add(icon);
        grid.Add(label, 1);
        grid.Add(selectedIcon, 2);

        var row = new Border
        {
            Content = grid,
            Padding = new Thickness(13, 11),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 15 },
            MinimumHeightRequest = 50
        };
        row.SetAppThemeColor(Border.BackgroundColorProperty,
            Color.FromArgb(selected ? "#ECEFFE" : "#F8FAFD"),
            Color.FromArgb(selected ? "#202B4B" : "#0F1A2D"));
        row.SetAppThemeColor(Border.StrokeProperty,
            Color.FromArgb(selected ? "#C7D2FE" : "#E7ECF4"),
            Color.FromArgb(selected ? "#4B5790" : "#2A3953"));

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await CloseAsync(index);
        row.GestureRecognizers.Add(tap);
        return row;
    }

    private async Task AnimateInAsync()
    {
        if (!UiPreferences.Motion) return;
        _panel.Opacity = 0.72;
        _panel.TranslationY = 24;
        await Task.WhenAll(
            _panel.FadeTo(1, 115, Easing.CubicOut),
            _panel.TranslateTo(0, 0, 115, Easing.CubicOut));
    }

    private async Task CloseAsync(int? result)
    {
        if (Interlocked.Exchange(ref _closing, 1) != 0) return;
        try
        {
            if (UiPreferences.Motion)
                await Task.WhenAll(_panel.FadeTo(0.75, 75), _panel.TranslateTo(0, 18, 75, Easing.CubicIn));
            if (Navigation.ModalStack.LastOrDefault() == this)
                await Navigation.PopModalAsync(false);
        }
        catch (Exception exception) { UiSafety.Log(exception); }
        finally { _completion.TrySetResult(result); }
    }
}
