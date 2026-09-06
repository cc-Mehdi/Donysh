using System.Net.Http.Json;
using Donysh.Sms;
using Microsoft.Maui.Controls.Shapes;

namespace Donysh.Companion;

public sealed class MainPage : ContentPage
{
    private static readonly Color Ink = Color.FromArgb("17213B"), Muted = Color.FromArgb("667085"), Primary = Color.FromArgb("4F46E5"), Soft = Color.FromArgb("EEF2FF");
    private readonly Entry server = Input("https://donysh.ir", Keyboard.Url, 256, FlowDirection.LeftToRight);
    private readonly Entry code = Input("کد اتصال ۳۲ کاراکتری", Keyboard.Text, 32, FlowDirection.LeftToRight);
    private readonly Entry mellat = Input("مثلاً BankMellat", Keyboard.Text, 256, FlowDirection.LeftToRight);
    private readonly Entry blu = Input("مثلاً BluBank", Keyboard.Text, 256, FlowDirection.LeftToRight);
    private readonly Label connection = new() { FontSize = 13, FontAttributes = FontAttributes.Bold };
    private readonly Label status = new() { FontSize = 13, TextColor = Muted, LineHeight = 1.35 };
    private readonly VerticalStackLayout messages = new() { Spacing = 10 };
    private readonly Button toggle = PrimaryButton("فعال‌سازی دریافت خودکار");
    private bool pairing, loading;

    public MainPage()
    {
        Title = "همراه دونیش"; FlowDirection = FlowDirection.RightToLeft; BackgroundColor = Color.FromArgb("F6F7FB");
        server.TextChanged += (_, _) => PersistFields(); mellat.TextChanged += (_, _) => PersistFields(); blu.TextChanged += (_, _) => PersistFields();
        toggle.Clicked += async (_, _) => await RunAsync(toggle, EnableAsync);
        var pair = PrimaryButton("اتصال امن به دونیش"); pair.Clicked += async (_, _) => await RunAsync(pair, PairAsync);
        var refresh = SecondaryButton("تازه‌سازی وضعیت"); refresh.Clicked += async (_, _) => await RunAsync(refresh, async () => { CompanionPlatform.Schedule(); await RefreshAsync(); });

        Content = new ScrollView { Content = new VerticalStackLayout {
            Padding = new Thickness(20, 24, 20, 36), Spacing = 16, MaximumWidthRequest = 760, HorizontalOptions = LayoutOptions.Center,
            Children = {
                Hero(),
                Card(Header("اتصال به حساب", "۱", "کد یک‌بارمصرف را از بخش «همراه دونیش» سایت دریافت کنید."), Field("آدرس سایت", server), Field("کد اتصال", code), pair, connection),
                AutomationCard(),
                Card(Header("وضعیت همگام‌سازی", "۳", "صف به‌صورت امن روی همین دستگاه نگهداری می‌شود."), status, refresh),
                PreviewCard(),
                Card(Header("صف پیامک‌ها", "۴", "موارد نامطمئن برای بررسی دستی باقی می‌مانند."), messages),
                new Label { Text = "ارسال‌ها بسته‌ای و با فاصله زمانی انجام می‌شوند تا به سرور فشار وارد نشود.", FontSize = 12, TextColor = Muted, HorizontalTextAlignment = TextAlignment.Center }
            }
        }};
    }

    private View Hero()
    {
        var grid = new Grid { ColumnSpacing = 14, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
        grid.Add(new Image { Source = "donysh_logo.svg", HeightRequest = 66, WidthRequest = 66 }, 0, 0);
        grid.Add(new VerticalStackLayout { Spacing = 3, VerticalOptions = LayoutOptions.Center, Children = {
            new Label { Text = "همراه دونیش", FontSize = 27, FontAttributes = FontAttributes.Bold, TextColor = Ink },
            new Label { Text = "ثبت خودکار برداشت‌های بانکی", FontSize = 14, TextColor = Muted }
        }}, 1, 0);
        return Card(grid, new Label { IsVisible = !CompanionPlatform.SupportsSms, Text = "نسخه Windows برای تنظیم اتصال و آزمایش قالب پیامک است؛ دریافت خودکار SMS فقط در Android فعال است.", TextColor = Color.FromArgb("3730A3"), BackgroundColor = Soft, Padding = 10, FontSize = 12 });
    }

    private View AutomationCard()
    {
        var card = Card(Header("دریافت خودکار", "۲", "نام یا شماره فرستنده را دقیقاً از جزئیات پیامک کپی کنید."),
            Field("فرستنده بانک ملت", mellat), Field("فرستنده بلو بانک", blu),
            new Label { Text = "چند فرستنده را با کامای انگلیسی جدا کنید. فیلد خالی یعنی آن بانک غیرفعال است.", FontSize = 12, TextColor = Muted }, toggle);
        card.IsVisible = CompanionPlatform.SupportsSms; return card;
    }

    private View PreviewCard()
    {
        var input = new Editor { Text = SmsPreview.MellatSample, AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 125, MaxLength = 2048, AutomationId = "SmsPreviewInput", BackgroundColor = Colors.Transparent, TextColor = Ink };
        var result = new Label { Text = SmsPreview.Describe(input.Text), AutomationId = "SmsPreviewResult", TextColor = Ink, BackgroundColor = Color.FromArgb("ECFDF3"), Padding = 10 };
        var samples = new HorizontalStackLayout { Spacing = 8 };
        var sample1 = SecondaryButton("نمونه ملت"); sample1.Clicked += (_, _) => { input.Text = SmsPreview.MellatSample; result.Text = SmsPreview.Describe(input.Text); };
        var sample2 = SecondaryButton("نمونه بلو"); sample2.Clicked += (_, _) => { input.Text = SmsPreview.BluSample; result.Text = SmsPreview.Describe(input.Text); };
        samples.Children.Add(sample1); samples.Children.Add(sample2);
        var parse = PrimaryButton("بررسی پیامک بدون ارسال"); parse.AutomationId = "ParseSmsPreview"; parse.Clicked += (_, _) => result.Text = SmsPreview.Describe(input.Text);
        return Card(Header("آزمایش پیامک", "✓", "تشخیص مبلغ و تاریخ را بدون ارسال به سایت بررسی کنید."), samples, InputFrame(input), parse, result);
    }

    private static Border Card(params View[] views)
    {
        var content = new VerticalStackLayout { Spacing = 14 };
        foreach (var view in views) content.Children.Add(view);
        return new Border { BackgroundColor = Colors.White, Stroke = Color.FromArgb("E4E7EC"), StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = 20 }, Padding = 18, Content = content };
    }
    private static Border InputFrame(View view) => new() { BackgroundColor = Color.FromArgb("F9FAFB"), Stroke = Color.FromArgb("D0D5DD"), StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = 13 }, Padding = new Thickness(12, 3), Content = view };
    private static View Field(string label, View input) => new VerticalStackLayout { Spacing = 6, Children = { new Label { Text = label, FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = Ink }, InputFrame(input) } };
    private static View Header(string title, string number, string subtitle)
    {
        var grid = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
        grid.Add(new Border { WidthRequest = 34, HeightRequest = 34, BackgroundColor = Soft, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 10 }, Content = new Label { Text = number, TextColor = Primary, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center } }, 0, 0);
        grid.Add(new VerticalStackLayout { Spacing = 2, Children = { new Label { Text = title, FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = Ink }, new Label { Text = subtitle, FontSize = 12, TextColor = Muted } } }, 1, 0); return grid;
    }
    private static Entry Input(string placeholder, Keyboard keyboard, int length, FlowDirection flow) => new() { Placeholder = placeholder, Keyboard = keyboard, MaxLength = length, BackgroundColor = Colors.Transparent, TextColor = Ink, PlaceholderColor = Color.FromArgb("98A2B3"), FlowDirection = flow, IsSpellCheckEnabled = false, IsTextPredictionEnabled = false };
    private static Button PrimaryButton(string text) => new() { Text = text, BackgroundColor = Primary, TextColor = Colors.White, CornerRadius = 13, HeightRequest = 48, FontAttributes = FontAttributes.Bold };
    private static Button SecondaryButton(string text) => new() { Text = text, BackgroundColor = Soft, TextColor = Primary, CornerRadius = 11, HeightRequest = 42, FontAttributes = FontAttributes.Bold };

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try { LoadFields(); CompanionPlatform.Schedule(); await RefreshAsync(); }
        catch { status.Text = "خواندن اطلاعات امن ممکن نیست. اطلاعات برنامه را پاک نکنید."; }
    }
    private void LoadFields() { loading = true; server.Text = CompanionSettings.Server; mellat.Text = CompanionSettings.MellatSenders; blu.Text = CompanionSettings.BluSenders; loading = false; }
    private void PersistFields() { if (loading) return; CompanionSettings.Server = server.Text?.Trim() ?? ""; CompanionSettings.MellatSenders = mellat.Text?.Trim() ?? ""; CompanionSettings.BluSenders = blu.Text?.Trim() ?? ""; }
    private async Task RunAsync(Button button, Func<Task> action) { button.IsEnabled = false; try { await action(); } catch { await DisplayAlert("عملیات کامل نشد", "اتصال شبکه، مجوزها و فضای ذخیره گوشی را بررسی کنید. اطلاعات صف پاک نشده است.", "باشه"); } finally { button.IsEnabled = true; } }

    private async Task PairAsync()
    {
        pairing = true; toggle.IsEnabled = false;
        try
        {
            if (!Uri.TryCreate(server.Text?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0) { await DisplayAlert("آدرس نامعتبر", "فقط آدرس HTTPS سایت بدون مسیر اضافی وارد کنید.", "باشه"); return; }
            if (code.Text?.Trim().Length != 32) { await DisplayAlert("کد نامعتبر", "کد ۳۲ کاراکتری صفحه همراه دونیش را وارد کنید.", "باشه"); return; }
            CompanionSettings.Enabled = false;
            if (CompanionSettings.Queue.Snapshot().Items.Count > 0) { await DisplayAlert("صف خالی نیست", "ابتدا پیامک‌های اتصال قبلی را تعیین تکلیف کنید.", "باشه"); await RefreshAsync(); return; }
            using var client = CompanionSettings.Http(); var origin = uri.GetLeftPart(UriPartial.Authority);
            using var response = await client.PostAsJsonAsync(origin + "/api/mobile/pair", new PairRequest(code.Text.Trim()));
            if (!response.IsSuccessStatusCode) { await DisplayAlert("اتصال انجام نشد", "کد ممکن است منقضی یا استفاده‌شده باشد. از سایت کد جدید بگیرید.", "باشه"); return; }
            var result = await response.Content.ReadFromJsonAsync<PairResult>() ?? throw new InvalidDataException(); if (result.Token.Length != 64) throw new InvalidDataException();
            var destination = result.WorkspaceName + " / " + result.CategoryName; await CompanionSettings.SaveConnectionAsync(new(origin, result.Token, destination));
            CompanionSettings.Server = origin; CompanionSettings.Destination = destination; code.Text = ""; Preferences.Remove("device-error"); await RefreshAsync();
            await DisplayAlert("اتصال برقرار شد", "مقصد ثبت هزینه: " + destination, "عالیه");
        }
        finally { pairing = false; toggle.IsEnabled = true; }
    }

    private async Task EnableAsync()
    {
        if (!CompanionPlatform.SupportsSms || pairing) return;
        if (CompanionSettings.Enabled) { CompanionSettings.Enabled = false; await RefreshAsync(); return; }
        if (await CompanionSettings.ConnectionAsync() is null) { await DisplayAlert("اتصال لازم است", "ابتدا کد اتصال سایت را وارد کنید.", "باشه"); return; }
        PersistFields();
        if (string.IsNullOrWhiteSpace(CompanionSettings.MellatSenders) && string.IsNullOrWhiteSpace(CompanionSettings.BluSenders)) { await DisplayAlert("فرستنده لازم است", "حداقل فرستنده یکی از بانک‌ها را وارد کنید.", "باشه"); return; }
        if (!await DisplayAlert("اجازه ثبت خودکار برداشت‌ها", "فقط پیامک‌های جدید به " + CompanionSettings.Destination + " ارسال می‌شوند.", "فعال شود", "انصراف")) return;
        if (await CompanionPlatform.RequestSmsPermissionAsync() != PermissionStatus.Granted) { await DisplayAlert("مجوز داده نشد", "بدون مجوز پیامک دریافت خودکار فعال نمی‌شود.", "باشه"); return; }
        CompanionSettings.Enabled = true; Preferences.Remove("device-error"); CompanionPlatform.Schedule(); await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var saved = await CompanionSettings.ConnectionAsync(); connection.Text = saved is null ? "● هنوز به فضای مالی متصل نشده‌اید" : "● متصل به «" + saved.Destination + "»"; connection.TextColor = saved is null ? Color.FromArgb("B54708") : Color.FromArgb("067647");
        var state = CompanionSettings.Queue.Snapshot(); toggle.Text = CompanionSettings.Enabled ? "توقف دریافت خودکار" : "ذخیره و فعال‌سازی دریافت"; toggle.BackgroundColor = CompanionSettings.Enabled ? Color.FromArgb("B42318") : Primary; mellat.IsEnabled = blu.IsEnabled = !CompanionSettings.Enabled;
        status.Text = $"مقصد: {CompanionSettings.Destination}\nدر انتظار: {state.Items.Count(x => !x.Held)}  •  نیازمند بررسی: {state.Items.Count(x => x.Held)}\nارسال‌شده: {state.Delivered}  •  صف پر: {state.Overflow}\n{state.Status}\n{Preferences.Get("device-error", "")}".Trim();
        messages.Children.Clear();
        foreach (var item in state.Items.Take(20))
        {
            var discard = SecondaryButton("حذف پس از رسیدگی"); discard.Clicked += async (_, _) => await RunAsync(discard, async () => { if (await DisplayAlert("حذف از صف", "این مورد از صف گوشی حذف شود؟", "حذف", "انصراف")) { CompanionSettings.Queue.Discard(item.Sms.Key); await RefreshAsync(); } });
            messages.Children.Add(new Border { BackgroundColor = item.Held ? Color.FromArgb("FFFAEB") : Color.FromArgb("F9FAFB"), Stroke = item.Held ? Color.FromArgb("FEDF89") : Color.FromArgb("EAECF0"), StrokeShape = new RoundRectangle { CornerRadius = 14 }, Padding = 12, Content = new VerticalStackLayout { Spacing = 8, Children = { new Label { Text = item.Held ? "نیازمند ثبت دستی" : "منتظر ارسال", FontAttributes = FontAttributes.Bold, TextColor = item.Held ? Color.FromArgb("B54708") : Primary }, new Label { Text = item.Sms.Body, FontSize = 13, TextColor = Ink }, discard } } });
        }
        if (state.Items.Count == 0) messages.Children.Add(new Label { Text = "صف خالی است؛ پیامک معوقی وجود ندارد.", TextColor = Muted, HorizontalTextAlignment = TextAlignment.Center, Margin = new Thickness(0, 8) });
    }
}
