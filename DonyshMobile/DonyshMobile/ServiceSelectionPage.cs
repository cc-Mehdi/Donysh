using DonyshMobile.Core;

namespace DonyshMobile;

public sealed class ServiceSelectionPage : ContentPage
{
    public ServiceSelectionPage()
    {
        Title = "انتخاب سرویس";
        Shell.SetNavBarIsVisible(this, false);
        FlowDirection = FlowDirection.RightToLeft;
        BackgroundColor = Color.FromArgb("#F5F8F8");
        var content = new Grid { RowDefinitions = new RowDefinitionCollection
        {
            new(GridLength.Auto), new(GridLength.Star), new(GridLength.Star), new(GridLength.Auto)
        }, Padding = new Thickness(18, 32, 18, 24), RowSpacing = 12 };
        var heading = new VerticalStackLayout { Spacing = 5 };
        heading.Children.Add(new Label { Text = "دُنیش همراه", FontFamily = "YekanBakhBold", FontSize = 28, TextColor = Color.FromArgb("#14263C") });
        heading.Children.Add(new Label { Text = "امروز روی کدام بخش تمرکز می‌کنید؟", FontSize = 15, TextColor = Color.FromArgb("#547076") });
        content.Children.Add(heading);
        var finance = Card("مالی", "خرج‌هایت را روشن ببین", "پیامک‌ها، دسته‌بندی هزینه و تصویر دقیق از پولت", "donysh_logo.png", "#38257D", "#7764DB", DonyshService.Finance);
        Grid.SetRow(finance, 1); content.Children.Add(finance);
        var life = Card("زندگی", "زمانت را آگاهانه زندگی کن", "برنامهٔ امروز، پیگیری فعالیت‌ها و یادآوری‌های به‌موقع", "donysh_life_logo.png", "#075F5C", "#08A99D", DonyshService.Life);
        Grid.SetRow(life, 2); content.Children.Add(life);
        var foot = new Label
        {
            Text = "می‌توانید از تنظیمات هر سرویس، بخش پیش‌فرض ورود را تعیین کنید.",
            FontSize = 12, TextColor = Color.FromArgb("#536D73"), HorizontalTextAlignment = TextAlignment.Center
        };
        Grid.SetRow(foot, 3); content.Children.Add(foot);
        Content = content;
    }

    private static Border Card(string title, string headline, string detail, string image,
        string dark, string accent, DonyshService service)
    {
        var stack = new VerticalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center, Padding = 22 };
        stack.Children.Add(new Image { Source = image, HeightRequest = 72, WidthRequest = 110, HorizontalOptions = LayoutOptions.Start, Aspect = Aspect.AspectFit });
        stack.Children.Add(new Label { Text = "دُنیش " + title, TextColor = Colors.White, FontFamily = "YekanBakhBold", FontSize = 25 });
        stack.Children.Add(new Label { Text = headline, TextColor = Color.FromArgb("#FFFFFF"), FontSize = 17, FontAttributes = FontAttributes.Bold });
        stack.Children.Add(new Label { Text = detail, TextColor = Color.FromArgb("#E5F5F3"), FontSize = 13 });
        stack.Children.Add(new Label { Text = "ورود به " + title + "  ←", TextColor = Colors.White, FontFamily = "YekanBakhBold", FontSize = 14, Margin = new Thickness(0, 10, 0, 0) });
        var border = new Border
        {
            StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(24) },
            Background = new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(dark), 0), new(Color.FromArgb(accent), 1)
            }, new Point(0, 0), new Point(1, 1)),
            Content = stack
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => App.OpenService(service);
        border.GestureRecognizers.Add(tap);
        SemanticProperties.SetDescription(border, "ورود به دُنیش " + title);
        return border;
    }
}
