using DonyshMobile.Core;

namespace DonyshMobile;

public partial class TemplateEditorPage : ContentPage
{
    private readonly SmsMonitorStore _store;
    private readonly Guid _senderId;
    private readonly Guid _templateId;
    private readonly bool _isExisting;
    private IReadOnlyList<CapturedSms> _samples = [];
    private IReadOnlyList<SmsFieldCandidate> _amounts = [];

    public TemplateEditorPage(SmsMonitorStore store, Guid senderId, string senderAddress, SmsTemplate? template = null)
    {
        _store = store;
        _senderId = senderId;
        _templateId = template?.Id ?? Guid.NewGuid();
        _isExisting = template is not null;
        InitializeComponent();
        SenderLabel.Text = senderAddress;
        Title = template is null ? "قالب جدید" : "ویرایش قالب";
        NameEntry.Text = template?.Name ?? string.Empty;
        PatternEditor.Text = template?.Pattern ?? string.Empty;
        UnitPicker.SelectedIndex = template?.Unit == MoneyUnit.Toman ? 1 : 0;
        KindPicker.SelectedIndex = template?.DefaultKind == TransactionKind.Deposit ? 1 : 0;
        MappingsEditor.Text = template is null ? string.Empty : string.Join("\n", template.Phrases.Select(p =>
            $"{p.Text}={(p.Kind == TransactionKind.Withdrawal ? "برداشت" : "واریز")}"));
        TypePhraseEntry.Text = template?.Phrases.FirstOrDefault()?.Text ?? string.Empty;
        if (template is not null)
        {
            ResultCard.IsVisible = true;
            ResultLabel.Text = "قالب ذخیره‌شده آماده است. برای بازسازی ساده، یک پیام نمونه انتخاب کنید؛ یا تنظیمات پیشرفته را باز کنید.";
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _samples = await _store.GetMessagesAsync(_senderId);
        SamplePicker.Items.Clear();
        foreach (var message in _samples)
        {
            var preview = message.Body.Replace('\r', ' ').Replace('\n', ' ').Trim();
            SamplePicker.Items.Add($"{message.ReceivedAtUtc.ToLocalTime():yyyy/MM/dd HH:mm} · {preview[..Math.Min(45, preview.Length)]}");
        }
        if (!_isExisting && _samples.Count > 0 && SamplePicker.SelectedIndex < 0) SamplePicker.SelectedIndex = 0;
    }

    private void OnSampleSelected(object? sender, EventArgs e)
    {
        var index = SamplePicker.SelectedIndex;
        if (index < 0 || index >= _samples.Count) return;
        SampleEditor.Text = _samples[index].Body;
        var analysis = SmsTemplateDraftBuilder.Analyze(SampleEditor.Text);
        _amounts = analysis.Amounts;
        AmountPicker.Items.Clear();
        foreach (var amount in _amounts) AmountPicker.Items.Add(amount.Display);
        AmountPicker.SelectedIndex = _amounts.Count > 0 ? 0 : -1;
        TypePhraseEntry.Text = analysis.SuggestedTypePhrase ?? string.Empty;
        KindPicker.SelectedIndex = analysis.SuggestedKind == TransactionKind.Deposit ? 1 : 0;
        UnitPicker.SelectedIndex = 0;
        DetectedFieldsLabel.Text = $"تاریخ: {analysis.Date?.Value ?? "پیدا نشد"}   ·   ساعت: {analysis.Time?.Value ?? "پیدا نشد"}";
        FieldsSection.IsVisible = true;
        ResultCard.IsVisible = false;
        if (_amounts.Count == 0)
        {
            ResultCard.IsVisible = true;
            ResultLabel.Text = "مبلغی در این پیام پیدا نشد. می‌توانید از تنظیمات پیشرفته قالب را دستی وارد کنید.";
        }
    }

    private void OnKindChanged(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameEntry.Text))
            NameEntry.Text = KindPicker.SelectedIndex == 1 ? "واریز خودکار" : "برداشت خودکار";
    }

    private void OnGenerateClicked(object? sender, EventArgs e) => GenerateAndTest();

    private bool GenerateAndTest()
    {
        var amountIndex = AmountPicker.SelectedIndex;
        if (string.IsNullOrWhiteSpace(SampleEditor.Text) || amountIndex < 0 || amountIndex >= _amounts.Count)
        {
            ResultCard.IsVisible = true;
            ResultLabel.Text = "یک پیام و مبلغ تراکنش را انتخاب کنید.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(NameEntry.Text))
            NameEntry.Text = KindPicker.SelectedIndex == 1 ? "واریز خودکار" : "برداشت خودکار";

        var template = SmsTemplateDraftBuilder.Build(
            _templateId, _senderId, NameEntry.Text, SampleEditor.Text, _amounts[amountIndex],
            UnitPicker.SelectedIndex == 1 ? MoneyUnit.Toman : MoneyUnit.Rial,
            KindPicker.SelectedIndex == 1 ? TransactionKind.Deposit : TransactionKind.Withdrawal,
            TypePhraseEntry.Text);
        PatternEditor.Text = template.Pattern;
        MappingsEditor.Text = string.Join("\n", template.Phrases.Select(p =>
            $"{p.Text}={(p.Kind == TransactionKind.Withdrawal ? "برداشت" : "واریز")}"));
        ShowTestResult(template);
        return SmsTemplateParser.TryParse(template, SampleEditor.Text) is not null;
    }

    private void ShowTestResult(SmsTemplate template)
    {
        ResultCard.IsVisible = true;
        var parsed = SmsTemplateParser.TryParse(template, SampleEditor.Text ?? string.Empty);
        ResultLabel.Text = parsed is null
            ? "قالب هنوز با پیام مطابقت ندارد. عبارت تشخیص نوع یا مبلغ انتخاب‌شده را بررسی کنید."
            : $"✓ آماده ذخیره\nنوع: {(parsed.Kind == TransactionKind.Withdrawal ? "برداشت" : "واریز")}\nمبلغ: {parsed.AmountRial:N0} ریال\nتاریخ: {parsed.DateText ?? "در پیام نیست"}\nساعت: {parsed.TimeText ?? "در پیام نیست"}";
    }

    private void OnAdvancedClicked(object? sender, EventArgs e)
    {
        AdvancedSection.IsVisible = !AdvancedSection.IsVisible;
        AdvancedButton.Text = AdvancedSection.IsVisible ? "بستن تنظیمات پیشرفته" : "تنظیمات پیشرفته";
    }

    private SmsTemplate? ReadAdvancedTemplate(out string? error)
    {
        var phrases = new List<TransactionPhrase>();
        foreach (var line in (MappingsEditor.Text ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var divider = line.LastIndexOf('=');
            if (divider < 1 || divider == line.Length - 1 || line[(divider + 1)..].Trim() is not ("برداشت" or "واریز"))
            {
                error = "نگاشت باید به شکل «عبارت=برداشت» یا «عبارت=واریز» باشد.";
                return null;
            }
            var kindText = line[(divider + 1)..].Trim();
            phrases.Add(new TransactionPhrase { Text = line[..divider].Trim(), Kind = kindText == "برداشت" ? TransactionKind.Withdrawal : TransactionKind.Deposit });
        }
        var template = new SmsTemplate
        {
            Id = _templateId, SenderId = _senderId,
            Name = NameEntry.Text?.Trim() ?? string.Empty,
            Pattern = PatternEditor.Text?.Trim() ?? string.Empty,
            Unit = UnitPicker.SelectedIndex == 1 ? MoneyUnit.Toman : MoneyUnit.Rial,
            DefaultKind = KindPicker.SelectedIndex == 1 ? TransactionKind.Deposit : TransactionKind.Withdrawal,
            Phrases = phrases
        };
        error = SmsTemplateParser.Validate(template);
        return error is null ? template : null;
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (!AdvancedSection.IsVisible && !string.IsNullOrWhiteSpace(SampleEditor.Text) && !GenerateAndTest()) return;
        if (string.IsNullOrWhiteSpace(PatternEditor.Text) && !GenerateAndTest()) return;
        var template = ReadAdvancedTemplate(out var error);
        if (template is null) { await DisplayAlert("قالب کامل نیست", error, "باشه"); return; }
        ShowTestResult(template);
        if (!string.IsNullOrWhiteSpace(SampleEditor.Text) && SmsTemplateParser.TryParse(template, SampleEditor.Text) is null)
        {
            await DisplayAlert("قالب با پیام هماهنگ نیست", "ابتدا مبلغ یا عبارت تشخیص نوع را اصلاح کنید.", "باشه");
            return;
        }
        error = await _store.SaveTemplateAsync(template);
        if (error is not null) { await DisplayAlert("ذخیره نشد", error, "باشه"); return; }
        await Navigation.PopAsync();
    }
}
