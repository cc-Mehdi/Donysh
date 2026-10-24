using DonyshMobile.Core;

namespace DonyshMobile;

public sealed class LifePage : ContentPage
{
    private static readonly Color Ink = Color.FromArgb("#102A3A");
    private static readonly Color Teal = Color.FromArgb("#087E78");
    private static readonly Color Muted = Color.FromArgb("#527478");
    private readonly LifeMobileClient _api = new();
    private readonly VerticalStackLayout _body = new() { Spacing = 14, Padding = new Thickness(18, 4, 18, 26) };
    private LifeConnection? _connection;
    private LifeWeek? _week;
    private bool _busy;
    private bool _showWeek;

    public LifePage()
    {
        Title = "دُنیش زندگی";
        FlowDirection = FlowDirection.RightToLeft;
        Shell.SetNavBarIsVisible(this, false);
        BackgroundColor = Color.FromArgb("#F4F8F7");
        var scroll = new ScrollView { Content = _body };
        Content = new Grid { Children = { scroll } };
        RenderLoading();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshAsync();
    }

    private void RenderLoading()
    {
        _body.Children.Clear();
        _body.Children.Add(Header());
        _body.Children.Add(Text("در حال دریافت برنامه…", 15, Muted));
    }

    private async Task RefreshAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            _connection = await LifeConnectionStore.LoadAsync();
            if (_connection is null) { RenderConnect(); return; }
            _week = await _api.WeekAsync(_connection);
            RenderDashboard();
#if ANDROID
            try
            {
                if (OperatingSystem.IsAndroidVersionAtLeast(33)
                    && await Permissions.CheckStatusAsync<Platforms.Android.PostNotificationsPermission>() != PermissionStatus.Granted)
                    await Permissions.RequestAsync<Platforms.Android.PostNotificationsPermission>();
                await Platforms.Android.LifeReminderScheduler.RefreshAsync(_connection);
            }
            catch (Exception) { /* The agenda stays available while notification refresh is offline. */ }
#endif
        }
        catch (UnauthorizedAccessException)
        {
            LifeConnectionStore.Remove();
            _connection = null;
            RenderConnect();
            await DisplayAlert("اتصال پایان یافت", "دسترسی این گوشی در دنیش لایف لغو شده است. کد تازه‌ای بسازید.", "باشه");
        }
        catch (Exception)
        {
            _body.Children.Clear();
            _body.Children.Add(Header());
            _body.Children.Add(Text("برنامه دریافت نشد. اتصال اینترنت را بررسی کنید.", 15, Muted));
            _body.Children.Add(Button("تلاش دوباره", async () => await RefreshAsync(), Teal));
        }
        finally { _busy = false; }
    }

    private View Header()
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitionCollection
        {
            new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto)
        }, ColumnSpacing = 8, Padding = new Thickness(0, 14, 0, 6) };
        var label = new VerticalStackLayout { Spacing = 0 };
        label.Children.Add(Text("دُنیش زندگی", 24, Ink, true));
        label.Children.Add(Text("زندگی، در لحظهٔ اکنون", 12, Muted));
        row.Children.Add(label);
        var finance = Button("مالی", () => App.OpenService(DonyshService.Finance), Color.FromArgb("#E9E7F9"), Color.FromArgb("#473592"));
        Grid.SetColumn(finance, 1); row.Children.Add(finance);
        var settings = Button("تنظیمات", async () => await Navigation.PushAsync(new LifeSettingsPage()), Color.FromArgb("#DFF5ED"), Teal);
        Grid.SetColumn(settings, 2); row.Children.Add(settings);
        return row;
    }

    private void RenderConnect()
    {
        _body.Children.Clear();
        _body.Children.Add(Header());
        var card = new VerticalStackLayout { Spacing = 14, Padding = 20 };
        card.Children.Add(Text("برنامه‌ات را به گوشی بیاور", 22, Ink, true));
        card.Children.Add(Text("در سایت life.donysh.ir، از «همراه دُنیش» یک کد اتصال بسازید. ورود لایف از سرویس مالی مستقل است.", 14, Muted));
        var code = new Entry { Placeholder = "کد اتصال ۳۲ نویسه‌ای", MaxLength = 32, IsPassword = true,
            FlowDirection = FlowDirection.LeftToRight, BackgroundColor = Colors.White };
        card.Children.Add(code);
        card.Children.Add(Button("اتصال به دنیش زندگی", async () =>
        {
            if (_busy) return;
            try
            {
                var connection = await _api.PairAsync(code.Text ?? "");
                if (connection is null) { await DisplayAlert("کد پذیرفته نشد", "کد نامعتبر یا منقضی شده است.", "باشه"); return; }
                await LifeConnectionStore.SaveAsync(connection);
                code.Text = string.Empty;
                await RefreshAsync();
            }
            catch (ArgumentException ex) { await DisplayAlert("کد معتبر نیست", ex.Message, "باشه"); }
            catch (Exception) { await DisplayAlert("اتصال برقرار نشد", "اینترنت و کد اتصال را بررسی کنید.", "باشه"); }
        }, Teal));
        _body.Children.Add(Card(card));
    }

    private void RenderDashboard()
    {
        if (_week is null) return;
        _body.Children.Clear();
        _body.Children.Add(Header());
        var today = _week.Days.FirstOrDefault(day => day.Date == _week.Today);
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TehranZone());
        var minute = now.Hour * 60 + now.Minute;
        var current = today?.Blocks.FirstOrDefault(block => block.StartMinute <= minute
            && block.StartMinute + block.DurationMinutes > minute && block.Status is null);
        var focus = new VerticalStackLayout { Spacing = 7, Padding = 20 };
        focus.Children.Add(Text("اکنون · " + now.ToString("HH:mm"), 13, Teal, true));
        focus.Children.Add(Text(current?.Title ?? "فعلاً فعالیت برنامه‌ریزی‌شده‌ای نداری", 21, Ink, true));
        focus.Children.Add(Text(current is null ? "برای برنامهٔ بعدی، فهرست امروز را ببین." :
            $"از {Clock(current.StartMinute)} تا {Clock(current.StartMinute + current.DurationMinutes)}", 13, Muted));
        _body.Children.Add(Card(focus, Color.FromArgb("#E4FAF2")));
        var actions = new Grid { ColumnDefinitions = new ColumnDefinitionCollection
        {
            new(GridLength.Star), new(GridLength.Star)
        }, ColumnSpacing = 8 };
        actions.Children.Add(Button(_showWeek ? "امروز" : "امروز ✓", () => { _showWeek = false; RenderDashboard(); }, _showWeek ? Colors.White : Teal, _showWeek ? Teal : Colors.White));
        var weekButton = Button(_showWeek ? "هفته ✓" : "هفته", () => { _showWeek = true; RenderDashboard(); }, _showWeek ? Teal : Colors.White, _showWeek ? Colors.White : Teal);
        Grid.SetColumn(weekButton, 1); actions.Children.Add(weekButton);
        _body.Children.Add(actions);
        var days = _showWeek ? _week.Days : today is null ? [] : new List<LifeDay> { today };
        foreach (var day in days)
        {
            _body.Children.Add(Text($"{day.Name} · {day.PersianDate}", 17, Ink, true));
            if (day.Blocks.Count == 0) _body.Children.Add(Text("برنامه‌ای برای این روز ثبت نشده است.", 13, Muted));
            foreach (var block in day.Blocks) _body.Children.Add(BlockCard(block));
        }
        _body.Children.Add(Button("+ ثبت فعالیت انجام‌شده", async () => await Navigation.PushAsync(new LifeActualPage(_week.Activities,
            async () => await RefreshAsync())), Teal));
    }

    private View BlockCard(LifeBlock block)
    {
        var stack = new VerticalStackLayout { Spacing = 6, Padding = 14 };
        stack.Children.Add(Text($"{Clock(block.StartMinute)}  ·  {block.DurationMinutes} دقیقه", 12, Teal, true));
        stack.Children.Add(Text(block.Title, 17, Ink, true));
        if (block.Status is not null) stack.Children.Add(Text(StatusLabel(block.Status), 12, Muted));
        if (!block.IsContinuation)
        {
            var buttons = new Grid { ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(GridLength.Star), new(GridLength.Star), new(GridLength.Star)
            }, ColumnSpacing = 5 };
            buttons.Children.Add(Button("✓ انجام شد", async () => await RecordAsync(block, "completed"), Color.FromArgb("#DDF8EA"), Color.FromArgb("#116C4E")));
            var partial = Button("نیمه‌تمام", async () => await RecordAsync(block, "partial"), Color.FromArgb("#FFF1D9"), Color.FromArgb("#8F6108"));
            Grid.SetColumn(partial, 1); buttons.Children.Add(partial);
            var missed = Button("انجام نشد", async () => await RecordAsync(block, "missed"), Color.FromArgb("#FDEBE8"), Color.FromArgb("#9A4136"));
            Grid.SetColumn(missed, 2); buttons.Children.Add(missed);
            stack.Children.Add(buttons);
        }
        return Card(stack);
    }

    private async Task RecordAsync(LifeBlock block, string status)
    {
        if (_connection is null) return;
        try
        {
            await _api.RecordAsync(_connection, block, status, status == "partial" ? Math.Max(1, block.DurationMinutes / 2) : null);
            await RefreshAsync();
        }
        catch (Exception) { await DisplayAlert("ثبت نشد", "وضعیت فعالیت ذخیره نشد. دوباره تلاش کنید.", "باشه"); }
    }

    private static string StatusLabel(string status) => status switch
    {
        "completed" => "انجام‌شده", "partial" => "نیمه‌تمام", "missed" => "انجام‌نشده", "moved" => "جابه‌جاشده", _ => ""
    };
    private static string Clock(int minute) => $"{minute / 60 % 24:00}:{minute % 60:00}";
    private static TimeZoneInfo TehranZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran"); }
        catch { return TimeZoneInfo.CreateCustomTimeZone("Asia/Tehran", TimeSpan.FromHours(3.5), "Tehran", "Tehran"); }
    }
    internal static Label Text(string value, double size, Color color, bool bold = false) =>
        new() { Text = value, FontSize = size, TextColor = color, FontFamily = bold ? "YekanBakhBold" : "YekanBakh", LineBreakMode = LineBreakMode.WordWrap };
    internal static Border Card(View content, Color? background = null) => new()
    {
        Content = content, BackgroundColor = background ?? Colors.White,
        Stroke = Color.FromArgb("#D8E9E5"), StrokeThickness = 1,
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(18) }
    };
    internal static Button Button(string title, Action action, Color background, Color? foreground = null)
    {
        var button = new Button { Text = title, BackgroundColor = background, TextColor = foreground ?? Colors.White,
            FontFamily = "YekanBakhBold", FontSize = 13, CornerRadius = 12, MinimumHeightRequest = 44 };
        button.Clicked += (_, _) => action();
        return button;
    }
}
