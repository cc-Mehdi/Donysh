using Microsoft.Maui.Layouts;

namespace DonyshMobile.UI;

public sealed class DonyshTitleSheet : ContentPage
{
    private readonly TaskCompletionSource<string?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Border _panel;
    private readonly Entry _entry;
    private int _closing;

    private DonyshTitleSheet(string heading, string messageExcerpt, string? initialValue, IReadOnlyList<string> suggestions)
    {
        FlowDirection = FlowDirection.RightToLeft;
        BackgroundColor = Colors.Transparent;
        Shell.SetNavBarIsVisible(this, false);

        var backdrop = new BoxView { Color = Color.FromArgb("#99081120") };
        var dismissTap = new TapGestureRecognizer();
        dismissTap.Tapped += async (_, _) => await CloseAsync(null);
        backdrop.GestureRecognizers.Add(dismissTap);

        var title = new Label
        {
            Text = heading,
            FontFamily = "YekanBakhBold",
            FontSize = 20,
            VerticalOptions = LayoutOptions.Center
        };

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
        SemanticProperties.SetDescription(close, "بستن عنوان‌گذاری");
        close.SetAppThemeColor(Button.TextColorProperty, Color.FromArgb("#586B85"), Color.FromArgb("#ADBCD3"));
        close.Clicked += async (_, _) => await CloseAsync(null);

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        header.Add(title);
        header.Add(close, 1);

        var excerpt = new Label
        {
            Text = messageExcerpt,
            FontFamily = "YekanBakh",
            FontSize = 13,
            MaxLines = 3,
            LineBreakMode = LineBreakMode.TailTruncation,
            LineHeight = 1.25
        };
        excerpt.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#586B85"), Color.FromArgb("#ADBCD3"));

        _entry = new Entry
        {
            Text = initialValue ?? string.Empty,
            Placeholder = "مثلاً خرید روزانه",
            MaxLength = 160,
            ReturnType = ReturnType.Done,
            FontFamily = "YekanBakh",
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing
        };
        _entry.Completed += async (_, _) => await SubmitAsync();

        var field = new Border
        {
            Content = _entry,
            Padding = new Thickness(12, 1),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 }
        };
        field.SetAppThemeColor(Border.BackgroundColorProperty, Color.FromArgb("#F5F7FC"), Color.FromArgb("#0F1A2D"));
        field.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#E0E7F2"), Color.FromArgb("#2A3953"));

        var suggestionsLabel = new Label
        {
            Text = "پیشنهادهای سریع",
            FontFamily = "YekanBakhMedium",
            FontSize = 12,
            IsVisible = suggestions.Count > 0
        };
        suggestionsLabel.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#586B85"), Color.FromArgb("#ADBCD3"));

        var chips = new FlexLayout
        {
            Direction = FlexDirection.Row,
            Wrap = FlexWrap.Wrap,
            JustifyContent = FlexJustify.Start,
            AlignItems = FlexAlignItems.Start,
            FlowDirection = FlowDirection.RightToLeft,
            IsVisible = suggestions.Count > 0
        };
        foreach (var suggestion in suggestions)
        {
            var chip = new Button
            {
                Text = suggestion,
                FontFamily = "YekanBakhMedium",
                FontSize = 12,
                Padding = new Thickness(12, 5),
                Margin = new Thickness(3),
                MinimumHeightRequest = 36,
                CornerRadius = 18,
                CommandParameter = suggestion
            };
            chip.SetAppThemeColor(Button.BackgroundColorProperty, Color.FromArgb("#ECEFFE"), Color.FromArgb("#283658"));
            chip.SetAppThemeColor(Button.TextColorProperty, Color.FromArgb("#38329B"), Color.FromArgb("#D6DEFF"));
            chip.Clicked += (_, _) =>
            {
                _entry.Text = suggestion;
                _entry.CursorPosition = _entry.Text.Length;
                _entry.Focus();
            };
            chips.Add(chip);
        }

        var save = new DonyshButton
        {
            Text = "ثبت عنوان",
            Glyph = AppIcons.CheckCircle,
            Style = (Style)Application.Current!.Resources["SuccessAction"]
        };
        save.Clicked += async (_, _) => await SubmitAsync();

        var content = new VerticalStackLayout
        {
            Padding = new Thickness(18, 12, 18, 22),
            Spacing = 11,
            Children = { header, excerpt, field, suggestionsLabel, chips, save }
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

    public static async Task<string?> ShowAsync(Page host, string heading, string messageExcerpt,
        string? initialValue, IReadOnlyList<string> suggestions)
    {
        var sheet = new DonyshTitleSheet(heading, messageExcerpt, initialValue, suggestions);
        await host.Navigation.PushModalAsync(sheet, false);
        await sheet.AnimateInAsync();
        sheet.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(80), () => sheet._entry.Focus());
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
        // SubmitAsync completes the request after the modal has closed. Only an
        // external navigation removal should be treated as cancellation here.
        if (Volatile.Read(ref _closing) == 0) _completion.TrySetResult(null);
    }

    private async Task SubmitAsync()
    {
        var title = _entry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            _entry.Text = string.Empty;
            _entry.Placeholder = "یک عنوان کوتاه بنویسید";
            _entry.Focus();
            return;
        }
        await CloseAsync(title);
    }

    private async Task AnimateInAsync()
    {
        if (!UiPreferences.Motion) return;
        _panel.Opacity = 0.72;
        _panel.TranslationY = 24;
        await Task.WhenAll(_panel.FadeTo(1, 115, Easing.CubicOut), _panel.TranslateTo(0, 0, 115, Easing.CubicOut));
    }

    private async Task CloseAsync(string? result)
    {
        if (Interlocked.Exchange(ref _closing, 1) != 0) return;
        try
        {
            _entry.Unfocus();
            if (UiPreferences.Motion)
                await Task.WhenAll(_panel.FadeTo(0.75, 75), _panel.TranslateTo(0, 18, 75, Easing.CubicIn));
            if (Navigation.ModalStack.LastOrDefault() == this) await Navigation.PopModalAsync(false);
        }
        catch (Exception exception) { UiSafety.Log(exception); }
        finally { _completion.TrySetResult(result); }
    }
}
