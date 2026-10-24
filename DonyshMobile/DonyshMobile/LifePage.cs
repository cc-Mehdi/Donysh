using DonyshMobile.Core;
using DonyshMobile.UI;

namespace DonyshMobile;

public sealed class LifePage : ContentPage
{
    private readonly LifeMobileClient _api = new();
    private readonly VerticalStackLayout _body = new() { Spacing = 14, Padding = new Thickness(18, 4, 18, 26) };
    private LifeConnection? _connection;
    private LifeWeek? _week;
    private bool _busy;
    private bool _signingIn;
    private bool _showWeek;

    public LifePage()
    {
        Title = "دُنیش زندگی";
        FlowDirection = FlowDirection.RightToLeft;
        Shell.SetNavBarIsVisible(this, false);
        LifeTheme.Set(this, BackgroundColorProperty, LifeTheme.Canvas);
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
        _body.Children.Add(Text("در حال دریافت برنامه…", 15, LifeTheme.Muted));
    }

    private async Task RefreshAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            _connection = await LifeConnectionStore.LoadAsync();
            if (_connection is null) { RenderConnect(await DonyshAccountStatusStore.LoadAsync()); return; }
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
            RenderConnect(await DonyshAccountStatusStore.LoadAsync());
            await DisplayAlert("ورود دوباره لازم است", "نشست این گوشی در دُنیش زندگی پایان یافته است. دوباره با حساب خود وارد شوید.", "باشه");
        }
        catch (Exception)
        {
            _body.Children.Clear();
            _body.Children.Add(Header());
            _body.Children.Add(Text("برنامه دریافت نشد. اتصال اینترنت را بررسی کنید.", 15, LifeTheme.Muted));
            _body.Children.Add(Button("تلاش دوباره", async () => await RefreshAsync(), LifeTheme.Primary));
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
        label.Children.Add(Text("دُنیش زندگی", 24, LifeTheme.Ink, true));
        label.Children.Add(Text(_connection?.AccountLabel is { Length: > 0 } account
            ? $"حساب: {account}" : "زندگی، در لحظهٔ اکنون", 12, LifeTheme.Muted));
        row.Children.Add(label);
        var finance = Button("مالی", () => App.OpenService(DonyshService.Finance), LifeTheme.Finance, LifeTheme.FinanceText);
        Grid.SetColumn(finance, 1); row.Children.Add(finance);
        var settings = Button("تنظیمات", async () => await Navigation.PushAsync(new LifeSettingsPage()), LifeTheme.SoftTeal, LifeTheme.AccentText);
        Grid.SetColumn(settings, 2); row.Children.Add(settings);
        return row;
    }

    private void RenderConnect(DonyshAccountStatus? accountStatus = null)
    {
        _body.Children.Clear();
        _body.Children.Add(Header());
        var card = new VerticalStackLayout { Spacing = 14, Padding = 20 };
        card.Children.Add(Text("برنامه‌ات را به گوشی بیاور", 22, LifeTheme.Ink, true));
        if (accountStatus is { LifeAvailable: false, FinanceAvailable: true })
            card.Children.Add(Text($"حساب «{accountStatus.Label}» فقط دُنیش مالی دارد. اگر دُنیش زندگی شما روی حساب دیگری است، با آن حساب وارد شوید.", 14, LifeTheme.WarningText));
        else
            card.Children.Add(Text("با ایمیل یا نام کاربری و گذرواژهٔ حساب دُنیش وارد شوید. دسترسی مالی و زندگی جداگانه بررسی می‌شود.", 14, LifeTheme.Muted));
        card.Children.Add(Text("ایمیل یا نام کاربری", 14, LifeTheme.Ink, true));
        var identifier = new Entry { Placeholder = "ایمیل یا نام کاربری", MaxLength = 254,
            FlowDirection = FlowDirection.LeftToRight };
        ApplyField(identifier);
        card.Children.Add(identifier);
        card.Children.Add(Text("گذرواژه", 14, LifeTheme.Ink, true));
        var password = new Entry { Placeholder = "گذرواژهٔ حساب دُنیش", MaxLength = 256, IsPassword = true,
            FlowDirection = FlowDirection.LeftToRight };
        ApplyField(password);
        var passwordRow = new Grid { ColumnDefinitions = new ColumnDefinitionCollection
        {
            new(GridLength.Star), new(GridLength.Auto)
        }, ColumnSpacing = 8 };
        passwordRow.Children.Add(password);
        Button eye = null!;
        eye = Button(AppIcons.Visibility, () =>
        {
            password.IsPassword = !password.IsPassword;
            eye.Text = password.IsPassword ? AppIcons.Visibility : AppIcons.VisibilityOff;
            SemanticProperties.SetDescription(eye, password.IsPassword ? "نمایش گذرواژه" : "پنهان‌کردن گذرواژه");
        }, LifeTheme.SoftTeal, LifeTheme.AccentText);
        eye.FontFamily = "MaterialIconsRound";
        eye.MinimumWidthRequest = 48;
        SemanticProperties.SetDescription(eye, "نمایش گذرواژه");
        Grid.SetColumn(eye, 1); passwordRow.Children.Add(eye);
        card.Children.Add(passwordRow);
        card.Children.Add(Button("ورود به دُنیش", async () =>
        {
            if (_signingIn) return;
            _signingIn = true;
            try
            {
                var installationId = await MobileConnectionStore.GetInstallationIdAsync();
                var account = await new DonyshAccountClient().SignInAsync(identifier.Text ?? "", password.Text ?? "", installationId);
                password.Text = string.Empty;
                if (account is null)
                {
                    await DisplayAlert("ورود انجام نشد", "ایمیل، نام کاربری یا گذرواژه درست نیست.", "باشه");
                    return;
                }
                var deviceName = string.IsNullOrWhiteSpace(DeviceInfo.Name) ? "گوشی همراه" : DeviceInfo.Name;
                LifeConnection? life = null;
                Exception? lifeFailure = null;
                if (account.LifeEnabled)
                {
                    try
                    {
                        var previous = await LifeConnectionStore.LoadAsync();
                        life = await _api.SignInAsync(account.Ticket, installationId, deviceName,
                            previous?.UserSubject == account.Subject ? previous.Token : null);
                        if (life is not null) await LifeConnectionStore.SaveAsync(life with { AccountLabel = account.Email });
                    }
                    catch (Exception exception) { lifeFailure = exception; UiSafety.Log(exception); }
                }
                MobileSessionResult? finance = null;
                Exception? financeFailure = null;
                try
                {
                    var previousFinance = await MobileConnectionStore.LoadAsync();
                    finance = await new MobileApiClient().LoginAsync(MobileConnectionStore.Server,
                        account.Ticket, installationId, deviceName,
                        previousFinance?.UserId == account.Subject ? previousFinance.Token : null);
                    if (finance is not null)
                    {
                        var queue = await new SmsMonitorStore(FileSystem.AppDataDirectory).GetSyncSummaryAsync();
                        var acceptFinance = queue.Pending == 0 || previousFinance?.UserId == account.Subject
                            || await DisplayAlert("مقصد پیام‌های منتظر",
                                $"{UiText.Digits(queue.Pending)} پیام منتظر پس از ورود به فضای مالی این حساب ارسال می‌شود. ادامه می‌دهید؟",
                                "ادامه", "انصراف");
                        if (acceptFinance)
                            await MobileConnectionStore.SaveAsync(new MobileConnection(MobileConnectionStore.Server,
                                finance.Token, $"{finance.WorkspaceName} / {finance.CategoryName}",
                                finance.WorkspaceId, finance.DisplayName, finance.UserId));
                        else
                        {
                            await new MobileApiClient().LogoutAsync(new MobileConnection(MobileConnectionStore.Server,
                                finance.Token, string.Empty, finance.WorkspaceId, finance.DisplayName, finance.UserId));
                            finance = null;
                        }
                    }
                }
                catch (Exception exception) { financeFailure = exception; UiSafety.Log(exception); }
                await DonyshAccountStatusStore.SaveAsync(new DonyshAccountStatus(account.Subject, account.Email,
                    financeFailure is null ? finance is not null : null, account.LifeEnabled));
                if (life is null)
                {
                    RenderConnect(new DonyshAccountStatus(account.Subject, account.Email,
                        financeFailure is null ? finance is not null : null, account.LifeEnabled));
                    await DisplayAlert(account.LifeEnabled ? "ورود زندگی کامل نشد" : "دسترسی زندگی فعال نیست",
                        lifeFailure is not null
                            ? "ارتباط با دُنیش زندگی برقرار نشد. دسترسی مالی جداگانه بررسی شد؛ دوباره تلاش کنید."
                            : account.LifeEnabled
                                ? "دسترسی زندگی برای این حساب فعال است، ولی نشست گوشی ساخته نشد. دوباره تلاش کنید."
                            : $"حساب «{account.Email}» فقط دُنیش مالی دارد. اگر دُنیش زندگی شما روی حساب دیگری است، با آن حساب وارد شوید.",
                        "باشه");
                    return;
                }
                await DisplayAlert("ورود انجام شد", finance is null
                    ? financeFailure is null
                        ? "دُنیش زندگی آماده است. دسترسی مالی برای این حساب پیدا نشد."
                        : "دُنیش زندگی آماده است. بررسی مالی به دلیل مشکل ارتباطی کامل نشد."
                    : "دُنیش زندگی و مالی برای این حساب بررسی شدند.", "باشه");
            }
            catch (ArgumentException ex) { await DisplayAlert("اطلاعات معتبر نیست", ex.Message, "باشه"); }
            catch (Exception) { await DisplayAlert("ورود برقرار نشد", "اتصال اینترنت و اطلاعات ورود را بررسی کنید.", "باشه"); }
            finally { _signingIn = false; password.Text = string.Empty; await RefreshAsync(); }
        }, LifeTheme.Primary));
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
        focus.Children.Add(Text("اکنون · " + now.ToString("HH:mm"), 13, LifeTheme.AccentText, true));
        focus.Children.Add(Text(current?.Title ?? "فعلاً فعالیت برنامه‌ریزی‌شده‌ای نداری", 21, LifeTheme.Ink, true));
        focus.Children.Add(Text(current is null ? "برای برنامهٔ بعدی، فهرست امروز را ببین." :
            $"از {Clock(current.StartMinute)} تا {Clock(current.StartMinute + current.DurationMinutes)}", 13, LifeTheme.Muted));
        _body.Children.Add(Card(focus, LifeTheme.Focus));
        var actions = new Grid { ColumnDefinitions = new ColumnDefinitionCollection
        {
            new(GridLength.Star), new(GridLength.Star)
        }, ColumnSpacing = 8 };
        actions.Children.Add(Button(_showWeek ? "امروز" : "امروز ✓", () => { _showWeek = false; RenderDashboard(); }, _showWeek ? LifeTheme.Surface : LifeTheme.Primary, _showWeek ? LifeTheme.AccentText : LifeTheme.OnPrimary));
        var weekButton = Button(_showWeek ? "هفته ✓" : "هفته", () => { _showWeek = true; RenderDashboard(); }, _showWeek ? LifeTheme.Primary : LifeTheme.Surface, _showWeek ? LifeTheme.OnPrimary : LifeTheme.AccentText);
        Grid.SetColumn(weekButton, 1); actions.Children.Add(weekButton);
        _body.Children.Add(actions);
        var days = _showWeek ? _week.Days : today is null ? [] : new List<LifeDay> { today };
        foreach (var day in days)
        {
            _body.Children.Add(Text($"{day.Name} · {day.PersianDate}", 17, LifeTheme.Ink, true));
            if (day.Blocks.Count == 0) _body.Children.Add(Text("برنامه‌ای برای این روز ثبت نشده است.", 13, LifeTheme.Muted));
            foreach (var block in day.Blocks) _body.Children.Add(BlockCard(block));
        }
        _body.Children.Add(Button("+ ثبت فعالیت انجام‌شده", async () => await Navigation.PushAsync(new LifeActualPage(_week.Activities,
            async () => await RefreshAsync())), LifeTheme.Primary));
    }

    private View BlockCard(LifeBlock block)
    {
        var stack = new VerticalStackLayout { Spacing = 6, Padding = 14 };
        stack.Children.Add(Text($"{Clock(block.StartMinute)}  ·  {block.DurationMinutes} دقیقه", 12, LifeTheme.AccentText, true));
        stack.Children.Add(Text(block.Title, 17, LifeTheme.Ink, true));
        if (block.Status is not null) stack.Children.Add(Text(StatusLabel(block.Status), 12, LifeTheme.Muted));
        if (!block.IsContinuation)
        {
            var buttons = new Grid { ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(GridLength.Star), new(GridLength.Star), new(GridLength.Star)
            }, ColumnSpacing = 5 };
            buttons.Children.Add(Button("✓ انجام شد", async () => await RecordAsync(block, "completed"), LifeTheme.Complete, LifeTheme.CompleteText));
            var partial = Button("نیمه‌تمام", async () => await RecordAsync(block, "partial"), LifeTheme.Partial, LifeTheme.PartialText);
            Grid.SetColumn(partial, 1); buttons.Children.Add(partial);
            var missed = Button("انجام نشد", async () => await RecordAsync(block, "missed"), LifeTheme.Missed, LifeTheme.MissedText);
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
    internal static Label Text(string value, double size, LifeColor color, bool bold = false)
    {
        var label = new Label { Text = value, FontSize = size,
            FontFamily = bold ? "YekanBakhBold" : "YekanBakh", LineBreakMode = LineBreakMode.WordWrap };
        LifeTheme.Set(label, Label.TextColorProperty, color);
        return label;
    }
    internal static Border Card(View content, LifeColor? background = null)
    {
        var card = new Border { Content = content, StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(18) } };
        LifeTheme.Set(card, BackgroundColorProperty, background ?? LifeTheme.Surface);
        LifeTheme.Set(card, Border.StrokeProperty, LifeTheme.SurfaceBorder);
        return card;
    }
    internal static Button Button(string title, Action action, LifeColor background, LifeColor? foreground = null)
    {
        var button = new Button { Text = title,
            FontFamily = "YekanBakhBold", FontSize = 13, CornerRadius = 12, MinimumHeightRequest = 44 };
        LifeTheme.Set(button, Microsoft.Maui.Controls.Button.BackgroundColorProperty, background);
        LifeTheme.Set(button, Microsoft.Maui.Controls.Button.TextColorProperty, foreground ?? LifeTheme.OnPrimary);
        button.Clicked += (_, _) => action();
        return button;
    }
    internal static void ApplyField(InputView field)
    {
        LifeTheme.Set(field, InputView.TextColorProperty, LifeTheme.Ink);
        LifeTheme.Set(field, InputView.PlaceholderColorProperty, LifeTheme.Muted);
        LifeTheme.Set(field, VisualElement.BackgroundColorProperty, LifeTheme.Field);
    }
}
