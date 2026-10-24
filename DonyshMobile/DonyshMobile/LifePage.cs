using DonyshMobile.Core;
using DonyshMobile.UI;

namespace DonyshMobile;

public sealed class LifePage : ContentPage
{
    private enum LifeSection { Home, Progress, Reminders }
    private readonly LifeMobileClient _api = new();
    private readonly VerticalStackLayout _body = new() { Spacing = 14, Padding = new Thickness(18, 4, 18, 26) };
    private readonly Grid _navigation = new() { ColumnSpacing = 0, Padding = new Thickness(10, 8, 10, 10), IsVisible = false };
    private LifeConnection? _connection;
    private LifeWeek? _week;
    private List<LifeReminder>? _reminders;
    private bool _busy;
    private bool _signingIn;
    private bool _showWeek;
#if DEBUG && WINDOWS
    private bool _smokePreview;
    internal VerticalStackLayout DashboardBody => _body;
    internal Grid DashboardNavigation => _navigation;
#endif
    private string? _selectedDate;
    private string? _expandedBlock;
    private LifeSection _section;

    public LifePage()
    {
        Title = "دُنیش زندگی";
        FlowDirection = FlowDirection.RightToLeft;
        Shell.SetNavBarIsVisible(this, false);
        LifeTheme.Set(this, BackgroundColorProperty, LifeTheme.Canvas);
        var scroll = new ScrollView { Content = _body };
        LifeTheme.Set(_navigation, BackgroundColorProperty, LifeTheme.Surface);
        var layout = new Grid { RowDefinitions = new RowDefinitionCollection
        {
            new(GridLength.Star), new(GridLength.Auto)
        } };
        layout.Children.Add(scroll);
        Grid.SetRow(_navigation, 1);
        layout.Children.Add(_navigation);
        Content = layout;
        RenderLoading();
    }

#if DEBUG && WINDOWS
    internal LifePage(LifeWeek preview) : this()
    {
        _smokePreview = true;
        _connection = new LifeConnection("https://life.donysh.ir", new string('A', 64), "preview", "حساب آزمایشی");
        _week = preview;
        RenderDashboard();
    }
#endif

    protected override async void OnAppearing()
    {
        base.OnAppearing();
#if DEBUG && WINDOWS
        if (_smokePreview) return;
#endif
        await RefreshAsync();
    }

    private void RenderLoading()
    {
        _navigation.IsVisible = false;
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
        }, ColumnSpacing = 8, Padding = new Thickness(0, 12, 0, 2) };
        var label = new VerticalStackLayout { Spacing = 0 };
        label.Children.Add(Text("دُنیش زندگی", 23, LifeTheme.Ink, true));
        var subtitle = Text(_connection?.AccountLabel is { Length: > 0 } account
            ? $"حساب: {account}" : "برنامه‌ات، در لحظهٔ اکنون", 11, LifeTheme.Muted);
        subtitle.MaxLines = 1;
        subtitle.LineBreakMode = LineBreakMode.TailTruncation;
        label.Children.Add(subtitle);
        row.Children.Add(label);
        var reminders = IconButton(AppIcons.Notification, "یادآورها", async () => await ShowRemindersAsync());
        reminders.IsVisible = _connection is not null;
        Grid.SetColumn(reminders, 1); row.Children.Add(reminders);
        var settings = IconButton(AppIcons.Settings, "تنظیمات و جابه‌جایی سرویس", async () => await Navigation.PushAsync(new LifeSettingsPage()));
        Grid.SetColumn(settings, 2); row.Children.Add(settings);
        return row;
    }

    private void RenderConnect(DonyshAccountStatus? accountStatus = null)
    {
        _navigation.IsVisible = false;
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
        _navigation.IsVisible = true;
        _body.Children.Clear();
        _body.Children.Add(Header());
        if (_section == LifeSection.Progress) RenderProgress();
        else if (_section == LifeSection.Reminders) RenderReminders();
        else RenderHome();
        RenderNavigation();
    }

    private void RenderHome()
    {
        if (_week is null) return;
        var selected = _week.Days.FirstOrDefault(day => day.Date == _selectedDate)
            ?? _week.Days.FirstOrDefault(day => day.Date == _week.Today)
            ?? _week.Days.FirstOrDefault();
        if (selected is null)
        {
            _body.Children.Add(Text("برای این هفته برنامه‌ای ثبت نشده است.", 15, LifeTheme.Muted));
            return;
        }
        _selectedDate = selected.Date;
        _body.Children.Add(ViewSwitch());
        _body.Children.Add(DayStrip(selected.Date));
        _body.Children.Add(Text($"{selected.Name} · {selected.PersianDate}", 15, LifeTheme.Ink, true));
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TehranZone());
        var minute = now.Hour * 60 + now.Minute;
        var current = selected.Date == _week.Today
            ? selected.Blocks.FirstOrDefault(block => block.StartMinute <= minute
                && block.StartMinute + block.DurationMinutes > minute && block.Status is null) : null;
        var next = current ?? selected.Blocks.FirstOrDefault(block => block.Status is null
            && (selected.Date != _week.Today || block.StartMinute > minute));
        var focus = new VerticalStackLayout { Spacing = 8, Padding = 18 };
        focus.Children.Add(Text(current is not null ? $"اکنون · {now:HH:mm}"
            : next is not null ? "فعالیت بعدی" : "برنامهٔ این روز", 13, LifeTheme.AccentText, true));
        focus.Children.Add(Text(next?.Title ?? "فعلاً فعالیت برنامه‌ریزی‌شده‌ای نداری", 20, LifeTheme.Ink, true));
        focus.Children.Add(Text(next is null ? "از فهرست پایین فعالیت تازه‌ای ثبت کن." :
            $"{Clock(next.StartMinute)} تا {Clock(next.StartMinute + next.DurationMinutes)} · {next.DurationMinutes} دقیقه", 13, LifeTheme.Muted));
        _body.Children.Add(Card(focus, LifeTheme.Focus));
        var summaryBlocks = _showWeek ? _week.Days.SelectMany(day => day.Blocks) : selected.Blocks;
        _body.Children.Add(Summary(summaryBlocks));
        _body.Children.Add(Text(_showWeek ? "برنامهٔ هفته" : selected.Date == _week.Today
            ? "برنامهٔ امروز" : "برنامهٔ این روز", 18, LifeTheme.Ink, true));
        var days = _showWeek ? _week.Days : [selected];
        foreach (var day in days)
        {
            if (_showWeek) _body.Children.Add(Text($"{day.Name} · {day.PersianDate}", 15, LifeTheme.Ink, true));
            if (day.Blocks.Count == 0) _body.Children.Add(Text("فعالیتی ثبت نشده است.", 13, LifeTheme.Muted));
            foreach (var block in day.Blocks.OrderBy(block => block.StartMinute))
                _body.Children.Add(BlockCard(block));
        }
    }

    private View ViewSwitch()
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitionCollection
        {
            new(GridLength.Star), new(GridLength.Star)
        }, ColumnSpacing = 6, Padding = 4 };
        var today = Button("امروز", () => { _showWeek = false; _selectedDate = _week?.Today; RenderDashboard(); },
            _showWeek ? LifeTheme.Quiet : LifeTheme.Surface, _showWeek ? LifeTheme.Muted : LifeTheme.AccentText);
        today.AutomationId = "LifeTodayTab";
        row.Children.Add(today);
        var week = Button("هفته", () => { _showWeek = true; RenderDashboard(); },
            _showWeek ? LifeTheme.Surface : LifeTheme.Quiet, _showWeek ? LifeTheme.AccentText : LifeTheme.Muted);
        week.AutomationId = "LifeWeekTab";
        Grid.SetColumn(week, 1); row.Children.Add(week);
        var frame = Card(row, LifeTheme.Quiet);
        frame.StrokeThickness = 0;
        return frame;
    }

    private View DayStrip(string selectedDate)
    {
        var strip = new Grid { ColumnSpacing = 3, Padding = new Thickness(4, 8) };
        if (_week is null) return strip;
        for (var index = 0; index < _week.Days.Count; index++)
        {
            var day = _week.Days[index];
            strip.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var selected = day.Date == selectedDate;
            var content = new VerticalStackLayout { Spacing = 2, Padding = new Thickness(0, 7),
                HorizontalOptions = LayoutOptions.Center };
            content.Children.Add(Text(ShortDay(day.Name), 11, selected ? LifeTheme.TodayText : LifeTheme.Muted, true));
            content.Children.Add(Text(day.PersianDate.Split('/').LastOrDefault() ?? "", 14,
                selected ? LifeTheme.TodayText : LifeTheme.Ink, true));
            var chip = Card(content, selected ? LifeTheme.Today : LifeTheme.Surface);
            chip.MinimumHeightRequest = 58;
            chip.AutomationId = $"LifeDay{index}";
            chip.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() =>
            {
                _selectedDate = day.Date;
                _showWeek = false;
                RenderDashboard();
            }) });
            Grid.SetColumn(chip, index);
            strip.Children.Add(chip);
        }
        return strip;
    }

    private static string ShortDay(string name) => name switch
    {
        "شنبه" => "ش", "یکشنبه" => "ی", "دوشنبه" => "د", "سه‌شنبه" => "س",
        "چهارشنبه" => "چ", "پنجشنبه" => "پ", "جمعه" => "ج", _ => name[..Math.Min(1, name.Length)]
    };

    private View Summary(IEnumerable<LifeBlock> blocks)
    {
        var items = blocks.Where(block => !block.IsContinuation).ToList();
        var row = new Grid { ColumnDefinitions = new ColumnDefinitionCollection
        {
            new(GridLength.Star), new(GridLength.Star), new(GridLength.Star)
        }, ColumnSpacing = 7 };
        row.Children.Add(StatTile(items.Count(block => block.Status is null), "مانده", LifeTheme.Missed, LifeTheme.MissedText));
        var partial = StatTile(items.Count(block => block.Status == "partial"), "نیمه‌تمام", LifeTheme.Partial, LifeTheme.PartialText);
        Grid.SetColumn(partial, 1); row.Children.Add(partial);
        var complete = StatTile(items.Count(block => block.Status == "completed"), "انجام‌شده", LifeTheme.Complete, LifeTheme.CompleteText);
        Grid.SetColumn(complete, 2); row.Children.Add(complete);
        return row;
    }

    private static View StatTile(int count, string caption, LifeColor background, LifeColor textColor)
    {
        var content = new VerticalStackLayout { Spacing = 1, Padding = new Thickness(7, 9),
            HorizontalOptions = LayoutOptions.Center };
        content.Children.Add(Text(UiText.Digits(count), 19, textColor, true));
        content.Children.Add(Text(caption, 10, textColor));
        return Card(content, background);
    }

    private View BlockCard(LifeBlock block)
    {
        var key = $"{block.PatternId:D}/{block.OriginalDate}";
        var stack = new VerticalStackLayout { Spacing = 8, Padding = 12 };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitionCollection
        {
            new(new GridLength(38)), new(GridLength.Star), new(GridLength.Auto)
        }, ColumnSpacing = 8, MinimumHeightRequest = 44 };
        var glyph = Text(ActivityIcon(block.Title, block.Status), 21, StatusColor(block.Status), true);
        glyph.FontFamily = "MaterialIconsRound";
        glyph.HorizontalTextAlignment = TextAlignment.Center;
        glyph.VerticalTextAlignment = TextAlignment.Center;
        row.Children.Add(Card(glyph, StatusBackground(block.Status)));
        var details = new VerticalStackLayout { Spacing = 0, VerticalOptions = LayoutOptions.Center };
        details.Children.Add(Text(block.Title, 15, LifeTheme.Ink, true));
        details.Children.Add(Text(block.Status is null ? $"{block.DurationMinutes} دقیقه" : StatusLabel(block.Status),
            11, block.Status is null ? LifeTheme.Muted : StatusColor(block.Status)));
        Grid.SetColumn(details, 1); row.Children.Add(details);
        var time = Text(Clock(block.StartMinute), 12, LifeTheme.Muted, true);
        time.VerticalTextAlignment = TextAlignment.Center;
        Grid.SetColumn(time, 2); row.Children.Add(time);
        stack.Children.Add(row);
        if (!block.IsContinuation)
        {
            var expand = Button(_expandedBlock == key ? "بستن اقدام‌ها" : "ثبت یا تغییر وضعیت  ⌄", () =>
            {
                _expandedBlock = _expandedBlock == key ? null : key;
                RenderDashboard();
            }, LifeTheme.Quiet, LifeTheme.AccentText);
            expand.FontSize = 11;
            expand.MinimumHeightRequest = 36;
            stack.Children.Add(expand);
        }
        if (!block.IsContinuation && _expandedBlock == key)
        {
            var actions = new Grid { ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(GridLength.Star), new(GridLength.Star), new(GridLength.Star)
            }, ColumnSpacing = 5 };
            actions.Children.Add(Button("✓ انجام شد", async () => await RecordAsync(block, "completed"), LifeTheme.Complete, LifeTheme.CompleteText));
            var partial = Button("نیمه‌تمام", async () => await RecordAsync(block, "partial"), LifeTheme.Partial, LifeTheme.PartialText);
            Grid.SetColumn(partial, 1); actions.Children.Add(partial);
            var missed = Button("انجام نشد", async () => await RecordAsync(block, "missed"), LifeTheme.Missed, LifeTheme.MissedText);
            Grid.SetColumn(missed, 2); actions.Children.Add(missed);
            stack.Children.Add(actions);
        }
        return Card(stack);
    }

    private static string ActivityIcon(string title, string? status)
    {
        if (status == "completed") return AppIcons.CheckCircle;
        if (title.Contains("کتاب", StringComparison.OrdinalIgnoreCase) || title.Contains("مطالعه", StringComparison.OrdinalIgnoreCase)) return AppIcons.Book;
        if (title.Contains("ورزش", StringComparison.OrdinalIgnoreCase) || title.Contains("پیاده", StringComparison.OrdinalIgnoreCase)) return AppIcons.Run;
        if (title.Contains("کار", StringComparison.OrdinalIgnoreCase) || title.Contains("پروژه", StringComparison.OrdinalIgnoreCase)) return AppIcons.Work;
        return AppIcons.Schedule;
    }

    private static LifeColor StatusBackground(string? status) => status switch
    {
        "completed" => LifeTheme.Complete, "partial" => LifeTheme.Partial,
        "missed" => LifeTheme.Missed, _ => LifeTheme.SoftTeal
    };
    private static LifeColor StatusColor(string? status) => status switch
    {
        "completed" => LifeTheme.CompleteText, "partial" => LifeTheme.PartialText,
        "missed" => LifeTheme.MissedText, _ => LifeTheme.AccentText
    };

    private void RenderProgress()
    {
        if (_week is null) return;
        _body.Children.Add(Text("پیشرفت این هفته", 22, LifeTheme.Ink, true));
        _body.Children.Add(Text("وضعیت فعالیت‌های برنامه‌ریزی‌شده را یکجا ببین.", 13, LifeTheme.Muted));
        _body.Children.Add(Summary(_week.Days.SelectMany(day => day.Blocks)));
        foreach (var day in _week.Days)
        {
            var blocks = day.Blocks.Where(block => !block.IsContinuation).ToList();
            var content = new VerticalStackLayout { Padding = 14, Spacing = 7 };
            content.Children.Add(Text($"{day.Name} · {day.PersianDate}", 15, LifeTheme.Ink, true));
            content.Children.Add(Text($"{UiText.Digits(blocks.Count(block => block.Status == "completed"))} از {UiText.Digits(blocks.Count)} فعالیت انجام شده",
                12, LifeTheme.Muted));
            var bar = new ProgressBar { Progress = blocks.Count == 0 ? 0 :
                (double)blocks.Count(block => block.Status == "completed") / blocks.Count };
            LifeTheme.Set(bar, ProgressBar.ProgressColorProperty, LifeTheme.AccentText);
            LifeTheme.Set(bar, BackgroundColorProperty, LifeTheme.Quiet);
            content.Children.Add(bar);
            _body.Children.Add(Card(content));
        }
    }

    private void RenderReminders()
    {
        _body.Children.Add(Text("یادآورهای پیش رو", 22, LifeTheme.Ink, true));
        _body.Children.Add(Text("یادآورها طبق تنظیمات دُنیش زندگی روی گوشی اعلان می‌شوند.", 13, LifeTheme.Muted));
        if (_reminders is null)
        {
            _body.Children.Add(Text("در حال دریافت یادآورها…", 14, LifeTheme.Muted));
            return;
        }
        if (_reminders.Count == 0)
        {
            _body.Children.Add(Card(new VerticalStackLayout { Padding = 18, Children =
            {
                Text("یادآوری برای روزهای پیش رو تنظیم نشده است.", 14, LifeTheme.Muted)
            } }));
            return;
        }
        foreach (var reminder in _reminders.OrderBy(item => item.RemindAtUtc))
        {
            var content = new VerticalStackLayout { Padding = 14, Spacing = 4 };
            content.Children.Add(Text(reminder.Title, 16, LifeTheme.Ink, true));
            content.Children.Add(Text($"{reminder.StartLabel} · {reminder.ScheduledDate}", 12, LifeTheme.Muted));
            _body.Children.Add(Card(content));
        }
    }

    private async Task ShowRemindersAsync()
    {
        if (_connection is null) return;
        _section = LifeSection.Reminders;
        _reminders = null;
        RenderDashboard();
        try
        {
            _reminders = await _api.RemindersAsync(_connection);
            RenderDashboard();
        }
        catch (Exception)
        {
            _body.Children.Add(Text("یادآورها دریافت نشدند. اتصال اینترنت را بررسی کنید.", 13, LifeTheme.WarningText));
        }
    }

    private void RenderNavigation()
    {
        _navigation.Children.Clear();
        _navigation.ColumnDefinitions.Clear();
        for (var index = 0; index < 5; index++)
            _navigation.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        AddNavItem(0, AppIcons.Home, "خانه", _section == LifeSection.Home, () =>
        {
            _section = LifeSection.Home; _showWeek = false; _selectedDate = _week?.Today; RenderDashboard();
        });
        AddNavItem(1, AppIcons.Chart, "پیشرفت", _section == LifeSection.Progress, () =>
        {
            _section = LifeSection.Progress; RenderDashboard();
        });
        var add = Button(AppIcons.Add, async () => await OpenManualAsync(), LifeTheme.Primary);
        add.FontFamily = "MaterialIconsRound";
        add.FontSize = 28;
        add.WidthRequest = 54;
        add.HeightRequest = 54;
        add.CornerRadius = 27;
        add.HorizontalOptions = LayoutOptions.Center;
        add.AutomationId = "LifeAddActivity";
        SemanticProperties.SetDescription(add, "ثبت فعالیت انجام‌شده");
        var addItem = new VerticalStackLayout { Spacing = 0, HorizontalOptions = LayoutOptions.Center };
        addItem.Children.Add(add);
        var addCaption = Text("ثبت فعالیت", 10, LifeTheme.AccentText, true);
        addCaption.HorizontalTextAlignment = TextAlignment.Center;
        addItem.Children.Add(addCaption);
        Grid.SetColumn(addItem, 2); _navigation.Children.Add(addItem);
        AddNavItem(3, AppIcons.Notification, "یادآورها", _section == LifeSection.Reminders,
            async () => await ShowRemindersAsync());
        AddNavItem(4, AppIcons.Settings, "بیشتر", false,
            async () => await Navigation.PushAsync(new LifeSettingsPage()));
    }

    private void AddNavItem(int column, string glyph, string title, bool selected, Action action)
    {
        var content = new VerticalStackLayout { Spacing = 0, MinimumHeightRequest = 52,
            HorizontalOptions = LayoutOptions.Center };
        var icon = Text(glyph, 23, selected ? LifeTheme.AccentText : LifeTheme.Muted);
        icon.FontFamily = "MaterialIconsRound";
        icon.HorizontalTextAlignment = TextAlignment.Center;
        content.Children.Add(icon);
        var caption = Text(title, 10, selected ? LifeTheme.AccentText : LifeTheme.Muted, selected);
        caption.HorizontalTextAlignment = TextAlignment.Center;
        content.Children.Add(caption);
        content.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(action) });
        Grid.SetColumn(content, column);
        _navigation.Children.Add(content);
    }

    private async Task OpenManualAsync()
    {
        if (_week is null) return;
        await Navigation.PushAsync(new LifeActualPage(_week.Activities, async () => await RefreshAsync()));
    }

    private static Microsoft.Maui.Controls.Button IconButton(string glyph, string description, Action action)
    {
        var button = Button(glyph, action, LifeTheme.SoftTeal, LifeTheme.AccentText);
        button.FontFamily = "MaterialIconsRound";
        button.FontSize = 21;
        button.WidthRequest = 44;
        button.MinimumWidthRequest = 44;
        SemanticProperties.SetDescription(button, description);
        return button;
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
