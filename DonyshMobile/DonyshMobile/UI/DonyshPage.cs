namespace DonyshMobile.UI;

/// <summary>
/// A page shell that keeps navigation responsive while the page prepares its data.
/// The real page is created behind a lightweight skeleton and revealed only after
/// the derived page confirms that its initial load has completed.
/// </summary>
public class DonyshPage : ContentPage
{
    private Grid? _loadingRoot;
    private DonyshLoadingView? _loadingView;
    private bool _initialLoadCompleted;

    public bool IsPageLoading { get; private set; }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!_initialLoadCompleted) ShowPageLoading();
    }

    protected async Task CompletePageLoadingAsync()
    {
        if (_initialLoadCompleted) return;

        _initialLoadCompleted = true;
        IsPageLoading = false;
        if (_loadingView is null) return;

        _loadingView.Stop();
        try
        {
            if (UiPreferences.Motion)
                await _loadingView.FadeTo(0, 140, Easing.CubicOut);
        }
        catch (Exception)
        {
            // A page can be detached while its initial operation completes.
        }
        finally
        {
            _loadingView.IsVisible = false;
            _loadingView.Opacity = 1;
        }
    }

    private void ShowPageLoading()
    {
        EnsureLoadingLayer();
        if (_loadingView is null) return;

        IsPageLoading = true;
        _loadingView.Opacity = 1;
        _loadingView.IsVisible = true;
        _loadingView.Start();
    }

    private void EnsureLoadingLayer()
    {
        if (_loadingRoot is not null || Content is not View pageContent) return;

        Content = null;
        _loadingView = new DonyshLoadingView();
        _loadingRoot = new Grid();
        _loadingRoot.Children.Add(pageContent);
        _loadingRoot.Children.Add(_loadingView);
        Content = _loadingRoot;
    }
}

internal sealed class DonyshLoadingView : ContentView
{
    private readonly List<VisualElement> _pulseTargets = [];
    private readonly ActivityIndicator _indicator;
    private CancellationTokenSource? _animationCancellation;

    public DonyshLoadingView()
    {
        InputTransparent = false;
        FlowDirection = FlowDirection.RightToLeft;
        SemanticProperties.SetDescription(this, "در حال آماده‌سازی صفحه");
        this.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#F5F7FB"), Color.FromArgb("#09111F"));

        _indicator = new ActivityIndicator
        {
            IsRunning = true,
            WidthRequest = 24,
            HeightRequest = 24,
            Color = Color.FromArgb("#5B4CF0")
        };

        var heading = new Label
        {
            Text = "در حال آماده‌سازی…",
            FontFamily = "YekanBakhBold",
            FontSize = 16,
            VerticalTextAlignment = TextAlignment.Center
        };

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 10,
            Children = { _indicator, heading }
        };
        Grid.SetColumn(heading, 1);

        var skeleton = new VerticalStackLayout { Spacing = 14 };
        skeleton.Children.Add(header);
        skeleton.Children.Add(CreateLine(0.58, 16));
        skeleton.Children.Add(CreateCard(112));

        var stats = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 9
        };
        for (var column = 0; column < 3; column++)
        {
            var card = CreateCard(76);
            Grid.SetColumn(card, column);
            stats.Children.Add(card);
        }
        skeleton.Children.Add(stats);
        skeleton.Children.Add(CreateLine(0.34, 16));
        skeleton.Children.Add(CreateCard(82));
        skeleton.Children.Add(CreateCard(82));

        Content = new Grid
        {
            Padding = new Thickness(18, 20),
            Children = { skeleton }
        };
    }

    public void Start()
    {
        Stop();
        _indicator.IsRunning = true;
        if (!UiPreferences.Motion) return;

        _animationCancellation = new CancellationTokenSource();
        _ = PulseAsync(_animationCancellation.Token);
    }

    public void Stop()
    {
        _indicator.IsRunning = false;
        _animationCancellation?.Cancel();
        _animationCancellation?.Dispose();
        _animationCancellation = null;
        foreach (var target in _pulseTargets)
        {
            target.CancelAnimations();
            target.Opacity = 1;
        }
    }

    private Border CreateCard(double height)
    {
        var card = new Border
        {
            HeightRequest = height,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 20 }
        };
        card.SetAppThemeColor(Border.BackgroundColorProperty, Color.FromArgb("#E4EAF4"), Color.FromArgb("#1A2940"));
        _pulseTargets.Add(card);
        return card;
    }

    private Border CreateLine(double widthFactor, double height)
    {
        var line = new Border
        {
            HeightRequest = height,
            WidthRequest = 320 * widthFactor,
            HorizontalOptions = LayoutOptions.Start,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = height / 2 }
        };
        line.SetAppThemeColor(Border.BackgroundColorProperty, Color.FromArgb("#D9E1EF"), Color.FromArgb("#21324B"));
        _pulseTargets.Add(line);
        return line;
    }

    private async Task PulseAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                foreach (var target in _pulseTargets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await target.FadeTo(0.48, 360, Easing.SinInOut);
                }
                foreach (var target in _pulseTargets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await target.FadeTo(1, 440, Easing.SinInOut);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            // Animation is optional and is cancelled when navigation completes.
        }
    }
}
