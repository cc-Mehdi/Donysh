using DonyshMobile.Core;

namespace DonyshMobile;

public partial class TemplateEditorPage : ContentPage
{
    private readonly SmsMonitorStore _store;
    private readonly Guid _senderId;
    private readonly Guid _templateId;
    private IReadOnlyList<CapturedSms> _samples = [];

    public TemplateEditorPage(SmsMonitorStore store, Guid senderId, string senderAddress, SmsTemplate? template = null)
    {
        _store = store;
        _senderId = senderId;
        _templateId = template?.Id ?? Guid.NewGuid();
        InitializeComponent();
        SenderLabel.Text = senderAddress;
        Title = template is null ? "قالب جدید" : "ویرایش قالب";
        NameEntry.Text = template?.Name ?? string.Empty;
        PatternEditor.Text = template?.Pattern ?? string.Empty;
        UnitPicker.SelectedIndex = template?.Unit == MoneyUnit.Toman ? 1 : 0;
        KindPicker.SelectedIndex = template?.DefaultKind == TransactionKind.Deposit ? 1 : 0;
        MappingsEditor.Text = template is null ? string.Empty : string.Join("\n", template.Phrases.Select(p =>
            $"{p.Text}={(p.Kind == TransactionKind.Withdrawal ? "برداشت" : "واریز")}"));
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _samples = await _store.GetMessagesAsync(_senderId);
        SamplePicker.Items.Clear();
        foreach (var message in _samples)
            SamplePicker.Items.Add($"{message.ReceivedAtUtc.ToLocalTime():yyyy/MM/dd HH:mm} · {message.Body.Replace('\n', ' ')[..Math.Min(35, message.Body.Length)]}");
        if (_samples.Count > 0) SamplePicker.SelectedIndex = 0;
    }

    private void OnSampleSelected(object? sender, EventArgs e)
    {
        if (SamplePicker.SelectedIndex is var index && index >= 0 && index < _samples.Count)
            SampleEditor.Text = _samples[index].Body;
    }

    private SmsTemplate? ReadTemplate(out string? error)
    {
        var phrases = new List<TransactionPhrase>();
        foreach (var line in (MappingsEditor.Text ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var divider = line.LastIndexOf('=');
            if (divider < 1 || divider == line.Length - 1)
            {
                error = "هر نگاشت باید به شکل «عبارت=برداشت» یا «عبارت=واریز» باشد.";
                return null;
            }
            var kind = line[(divider + 1)..].Trim();
            if (kind is not ("برداشت" or "واریز"))
            {
                error = "نوع هر نگاشت فقط «برداشت» یا «واریز» است.";
                return null;
            }
            phrases.Add(new TransactionPhrase
            {
                Text = line[..divider].Trim(),
                Kind = kind == "برداشت" ? TransactionKind.Withdrawal : TransactionKind.Deposit
            });
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

    private void OnTestClicked(object? sender, EventArgs e)
    {
        var template = ReadTemplate(out var error);
        if (template is null) { ResultLabel.Text = error; return; }
        if (string.IsNullOrWhiteSpace(SampleEditor.Text)) { ResultLabel.Text = "یک پیام نمونه برای آزمون انتخاب یا وارد کنید."; return; }
        var parsed = SmsTemplateParser.TryParse(template, SampleEditor.Text);
        ResultLabel.Text = parsed is null
            ? "این پیام با قالب مطابقت ندارد یا عبارت نوع تراکنش آن نگاشت نشده است."
            : $"✓ {template.Name}\nنوع: {(parsed.Kind == TransactionKind.Withdrawal ? "برداشت" : "واریز")}\nمبلغ: {parsed.AmountRial:N0} ریال\nتاریخ: {parsed.DateText ?? "در پیام مشخص نشده"}\nساعت: {parsed.TimeText ?? "در پیام مشخص نشده"}";
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        var template = ReadTemplate(out var error);
        if (template is null) { await DisplayAlert("قالب نامعتبر", error, "باشه"); return; }
        error = await _store.SaveTemplateAsync(template);
        if (error is not null) { await DisplayAlert("ذخیره نشد", error, "باشه"); return; }
        await Navigation.PopAsync();
    }
}
