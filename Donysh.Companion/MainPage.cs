using System.Net.Http.Json;
using Donysh.Sms;

namespace Donysh.Companion;

public sealed class MainPage : ContentPage
{
    private readonly Entry server = new() { Text = CompanionSettings.Server, Placeholder = "https://donysh.ir", Keyboard = Keyboard.Url, FlowDirection = FlowDirection.LeftToRight };
    private readonly Entry code = new() { Placeholder = "کد اتصال یک‌بارمصرف", MaxLength = 32, IsSpellCheckEnabled = false, IsTextPredictionEnabled = false, FlowDirection = FlowDirection.LeftToRight };
    private readonly Entry mellat = new() { Text = CompanionSettings.MellatSenders, Placeholder = "فرستنده دقیق ملت (شماره یا نام)", MaxLength = 256, FlowDirection = FlowDirection.LeftToRight };
    private readonly Entry blu = new() { Text = CompanionSettings.BluSenders, Placeholder = "فرستنده دقیق بلو (شماره یا نام)", MaxLength = 256, FlowDirection = FlowDirection.LeftToRight };
    private readonly Label status = new() { FontSize = 14 };
    private readonly VerticalStackLayout messages = new() { Spacing = 12 };
    private readonly Button toggle = new();
    private bool pairing;

    public MainPage()
    {
        Title = "همراه دونیش"; FlowDirection = FlowDirection.RightToLeft; BackgroundColor = Color.FromArgb("F5F7FB");
        var pair = Button("اتصال به دونیش", PairAsync);
        toggle.Clicked += async (_, _) => await EnableAsync();
        Content = new ScrollView { Content = new VerticalStackLayout {
            Padding = 22, Spacing = 16, Children = {
                new Label { Text = "برداشت‌ها، بدون فراموشی", AutomationId = "MainHeading", FontSize = 26, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("202B46") },
                new Label { Text = "نسخه Windows · اتصال به سایت و آزمایش قالب پیامک. دریافت خودکار SMS فقط در Android فعال است.", IsVisible = !CompanionPlatform.SupportsSms, TextColor = Color.FromArgb("4338CA") },
                CreatePreview(),
                new Label { Text = "برداشت‌های ملت و بلو به تومان تبدیل و در دونیش به‌صورت «نیازمند بررسی» ثبت می‌شوند. پیامک‌های قبلی گوشی خوانده نمی‌شوند." },
                new Label { Text = "۱. در سایت، فضای مالی را انتخاب کنید؛ از منوی «همراه دونیش» کد اتصال بگیرید." },
                server, code, pair,
                new Label { Text = "۲. فرستنده را دقیقاً از جزئیات پیامک گوشی کپی کنید. چند فرستنده را با کامای انگلیسی جدا کنید. فیلد خالی یعنی آن بانک غیرفعال است. شماره حساب، فرستنده پیامک نیست.", IsVisible = CompanionPlatform.SupportsSms },
                mellat, blu, toggle,
                new Label { Text = "متن پیامک بانکی، مبلغ و تاریخ به فضای انتخابی ارسال می‌شود؛ در فضای مشترک اعضا آن را می‌بینند. مجوز پیامک فقط پس از تأیید شما درخواست می‌شود. پیامک رمز و واریز ارسال نمی‌شود." },
                status,
                Button("تازه‌سازی", async () => { CompanionPlatform.Schedule(); await RefreshAsync(); }),
                new Label { Text = "صف گوشی (حداکثر ۱۰۰۰ پیامک)", FontAttributes = FontAttributes.Bold, FontSize = 20 }, messages,
                new Label { Text = "ارسال: حداکثر ۵ پیامک در هر بسته، حداقل ۳۰ ثانیه فاصله. هنگام خطا فاصله بیشتر می‌شود. Android ممکن است در پس‌زمینه ارسال را عقب بیندازد؛ بستن اجباری اپ دریافت را تا بازکردن دوباره متوقف می‌کند." }
            }
        }};
        mellat.IsVisible = blu.IsVisible = toggle.IsVisible = CompanionPlatform.SupportsSms;
    }

    private static View CreatePreview()
    {
        var input = new Editor { Text = SmsPreview.MellatSample, AutoSize = EditorAutoSizeOption.TextChanges,
            MinimumHeightRequest = 125, MaxLength = 2048, AutomationId = "SmsPreviewInput", BackgroundColor = Colors.White };
        var result = new Label { Text = SmsPreview.Describe(input.Text), AutomationId = "SmsPreviewResult", TextColor = Color.FromArgb("202B46") };
        var parse = Button("بررسی پیامک — بدون ارسال", () => { result.Text = SmsPreview.Describe(input.Text); return Task.CompletedTask; });
        parse.AutomationId = "ParseSmsPreview";
        return new VerticalStackLayout { Spacing = 10, Children = {
            new Label { Text = "آزمایش محلی قالب پیامک", FontSize = 18, FontAttributes = FontAttributes.Bold }, input,
            new HorizontalStackLayout { Spacing = 8, Children = {
                Button("نمونه ملت", () => { input.Text = SmsPreview.MellatSample; result.Text = SmsPreview.Describe(input.Text); return Task.CompletedTask; }),
                Button("نمونه بلو", () => { input.Text = SmsPreview.BluSample; result.Text = SmsPreview.Describe(input.Text); return Task.CompletedTask; })
            } }, parse, result
        } };
    }

    private static Button Button(string text, Func<Task> action)
    {
        var b = new Button { Text = text, BackgroundColor = Color.FromArgb("4F46E5"), TextColor = Colors.White, CornerRadius = 12 };
        b.Clicked += async (_, _) => {
            b.IsEnabled = false;
            try { await action(); }
            catch (Exception) { if (Application.Current?.Windows.FirstOrDefault()?.Page is Page page) await page.DisplayAlert("عملیات کامل نشد", "اتصال شبکه، مجوزها و فضای ذخیره گوشی را بررسی کنید. هیچ اطلاعات صفی پاک نشده است.", "باشه"); }
            finally { b.IsEnabled = true; }
        };
        return b;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try { CompanionPlatform.Schedule(); await RefreshAsync(); }
        catch (Exception) { status.Text = "خواندن صف یا اطلاعات امن ممکن نیست. اطلاعات اپ را پاک نکنید؛ ابتدا پیامک‌های گوشی را بررسی کنید."; }
    }

    private async Task PairAsync()
    {
        pairing = true;
        toggle.IsEnabled = false;
        try { await PairCoreAsync(); }
        finally { pairing = false; toggle.IsEnabled = true; }
    }

    private async Task PairCoreAsync()
    {
        if (!Uri.TryCreate(server.Text?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0) {
            await DisplayAlert("آدرس نامعتبر", "فقط آدرس HTTPS سایت بدون مسیر اضافی وارد کنید.", "باشه"); return;
        }
        if (code.Text?.Trim().Length != 32) { await DisplayAlert("کد نامعتبر", "کد ۳۲ کاراکتری صفحه همراه دونیش را وارد کنید.", "باشه"); return; }
        // Stop reception before checking the queue; never move pending SMS to a new destination.
        CompanionSettings.Enabled = false;
        if (CompanionSettings.Queue.Snapshot().Items.Count > 0) {
            await DisplayAlert("صف خالی نیست", "ابتدا پیامک‌های صف را با اتصال قبلی ارسال یا پس از رسیدگی دستی حذف کنید؛ مقصد پیامک‌های معوق تغییر نمی‌کند.", "باشه"); await RefreshAsync(); return;
        }
        using var client = CompanionSettings.Http();
        var origin = uri.GetLeftPart(UriPartial.Authority);
        using var response = await client.PostAsJsonAsync(origin + "/api/mobile/pair", new PairRequest(code.Text.Trim()));
        if (!response.IsSuccessStatusCode) {
            await DisplayAlert("اتصال انجام نشد", "کد ممکن است منقضی/استفاده‌شده باشد یا سرور شلوغ باشد. از سایت کد جدید بگیرید و پس از یک دقیقه تلاش کنید.", "باشه"); await RefreshAsync(); return;
        }
        var result = await response.Content.ReadFromJsonAsync<PairResult>() ?? throw new InvalidDataException();
        if (result.Token.Length != 64) throw new InvalidDataException();
        await CompanionSettings.SaveConnectionAsync(new(origin, result.Token, result.WorkspaceName + " / " + result.CategoryName));
        CompanionSettings.Server = origin; CompanionSettings.Destination = result.WorkspaceName + " / " + result.CategoryName;
        code.Text = ""; Preferences.Remove("device-error"); await RefreshAsync();
        await DisplayAlert("متصل شد", "مقصد: " + CompanionSettings.Destination + "\nحالا فرستنده‌ها را وارد و دریافت را فعال کنید.", "باشه");
    }

    private async Task EnableAsync()
    {
        if (!CompanionPlatform.SupportsSms) return;
        if (pairing) return;
        toggle.IsEnabled = false;
        try {
            if (CompanionSettings.Enabled) { CompanionSettings.Enabled = false; await RefreshAsync(); return; }
            if (await CompanionSettings.ConnectionAsync() is null) { await DisplayAlert("اتصال لازم است", "ابتدا کد اتصال سایت را وارد کنید.", "باشه"); return; }
            if (string.IsNullOrWhiteSpace(mellat.Text) && string.IsNullOrWhiteSpace(blu.Text)) { await DisplayAlert("فرستنده لازم است", "حداقل فرستنده یکی از بانک‌ها را وارد کنید.", "باشه"); return; }
            if (!await DisplayAlert("اجازه ثبت خودکار برداشت‌ها", "فقط پیامک‌های جدید فرستنده‌های انتخابی بررسی می‌شوند. متن برداشت و مبلغ به " + CompanionSettings.Destination + " ارسال می‌شود. موافقید؟", "فعال شود", "انصراف")) return;
            if (await CompanionPlatform.RequestSmsPermissionAsync() != PermissionStatus.Granted) { await DisplayAlert("مجوز داده نشد", "بدون مجوز پیامک، دریافت خودکار فعال نمی‌شود.", "باشه"); return; }
            CompanionSettings.MellatSenders = mellat.Text?.Trim() ?? ""; CompanionSettings.BluSenders = blu.Text?.Trim() ?? "";
            CompanionSettings.Enabled = true; Preferences.Remove("device-error"); CompanionPlatform.Schedule(); await RefreshAsync();
        } catch (Exception) { await DisplayAlert("فعال‌سازی انجام نشد", "مجوزها و اطلاعات اتصال را بررسی کنید.", "باشه"); }
        finally { toggle.IsEnabled = CompanionPlatform.SupportsSms; }
    }

    private Task RefreshAsync()
    {
        var state = CompanionSettings.Queue.Snapshot();
        toggle.Text = CompanionSettings.Enabled ? "توقف دریافت و ارسال" : "ذخیره فرستنده‌ها و فعال‌سازی";
        mellat.IsEnabled = blu.IsEnabled = !CompanionSettings.Enabled;
        status.Text = $"مقصد: {CompanionSettings.Destination}\nدر صف: {state.Items.Count(x => !x.Held)} | مبهم: {state.Items.Count(x => x.Held)} | ارسال تأییدشده: {state.Delivered}\nذخیره‌نشده به علت پرشدن صف: {state.Overflow}\n{state.Status}\n{Preferences.Get("device-error", "")}";
        messages.Children.Clear();
        foreach (var item in state.Items.Take(20)) {
            messages.Children.Add(new Label { Text = (item.Held ? "نیازمند ثبت دستی؛ ارسال نمی‌شود" : "منتظر ارسال") + "\n" + item.Sms.Body, FontSize = 13 });
            messages.Children.Add(Button("حذف از صف پس از رسیدگی دستی", async () => {
                if (await DisplayAlert("حذف از صف", "این مورد از صف گوشی حذف شود؟ اگر قبلاً به سرور رسیده باشد، خرج سایت حذف نمی‌شود.", "حذف", "انصراف")) {
                    CompanionSettings.Queue.Discard(item.Sms.Key); await RefreshAsync();
                }
            }));
        }
        if (state.Items.Count > 20) messages.Children.Add(new Label { Text = "۲۰ مورد اول نمایش داده شده است؛ پس از رسیدگی موارد بعدی نمایش داده می‌شود." });
        return Task.CompletedTask;
    }
}
