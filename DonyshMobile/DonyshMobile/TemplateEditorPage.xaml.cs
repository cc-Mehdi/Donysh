using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DonyshMobile.Core;

namespace DonyshMobile;

public partial class TemplateEditorPage : ContentPage
{
    private readonly SmsMonitorStore _store;
    private readonly Guid _senderId;
    private readonly Guid _templateId;
    private bool _initializing = true;
    private IReadOnlyList<CapturedSms> _savedMessages = [];

    public ObservableCollection<TemplateFieldRow> Fields { get; } = [];

    public TemplateEditorPage(SmsMonitorStore store, Guid senderId, string senderAddress, SmsTemplate? template = null)
    {
        _store = store;
        _senderId = senderId;
        _templateId = template?.Id ?? Guid.NewGuid();
        InitializeComponent();
        BindingContext = this;
        SenderLabel.Text = senderAddress;
        Title = template is null ? "قالب جدید" : "ویرایش قالب";
        foreach (var preset in SmsTemplatePresets.All) PresetPicker.Items.Add(preset.BankName);

        if (template is null)
        {
            LoadFields(DefaultFields());
            SourcePicker.SelectedIndex = 0;
        }
        else
        {
            NameEntry.Text = template.Name;
            MessageEditor.Text = string.IsNullOrWhiteSpace(template.SampleMessage) ? template.Pattern : template.SampleMessage;
            LoadFields(template.Fields.Count > 0 ? template.Fields : DefaultFields());
            SourcePicker.SelectedIndex = 0;
            if (template.Fields.Count == 0)
            {
                PreviewCard.IsVisible = true;
                PreviewResultLabel.Text = "این قالب با نسخه قبلی ساخته شده است. مقادیر ردیف‌ها را یک بار تکمیل و دوباره ذخیره کنید.";
            }
        }
        _initializing = false;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _savedMessages = await _store.GetMessagesAsync(_senderId);
        SavedMessagePicker.Items.Clear();
        foreach (var message in _savedMessages)
        {
            var preview = message.Body.Replace('\r', ' ').Replace('\n', ' ').Trim();
            SavedMessagePicker.Items.Add($"{message.ReceivedAtUtc.ToLocalTime():yyyy/MM/dd HH:mm} · {preview[..Math.Min(40, preview.Length)]}");
        }
    }

    private void OnSourceChanged(object? sender, EventArgs e)
    {
        PresetSection.IsVisible = SourcePicker.SelectedIndex == 1;
        if (!_initializing && SourcePicker.SelectedIndex == 1 && PresetPicker.SelectedIndex < 0)
            PresetPicker.SelectedIndex = 0;
    }

    private void OnPresetSelected(object? sender, EventArgs e)
    {
        if (_initializing || PresetPicker.SelectedIndex < 0 || PresetPicker.SelectedIndex >= SmsTemplatePresets.All.Count) return;
        var preset = SmsTemplatePresets.All[PresetPicker.SelectedIndex];
        PresetDescriptionLabel.Text = preset.Description;
        MessageEditor.Text = preset.SampleMessage;
        NameEntry.Text = preset.BankName;
        LoadFields(preset.Fields);
        ShowPreview();
    }

    private void OnSavedMessageSelected(object? sender, EventArgs e)
    {
        var index = SavedMessagePicker.SelectedIndex;
        if (index >= 0 && index < _savedMessages.Count)
            MessageEditor.Text = _savedMessages[index].Body;
    }

    private async void OnAddFieldClicked(object? sender, EventArgs e)
    {
        var used = Fields.Select(x => x.KeyLabel).ToHashSet(StringComparer.Ordinal);
        var next = TemplateFieldRow.AvailableKeys.FirstOrDefault(x => !used.Contains(x));
        if (next is null)
        {
            await DisplayAlert("همه کلیدها اضافه شده‌اند", "برای هر کلید فقط یک ردیف لازم است.", "باشه");
            return;
        }
        Fields.Add(new TemplateFieldRow(next, string.Empty));
    }

    private void OnRemoveFieldClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: Guid id })
        {
            var row = Fields.FirstOrDefault(x => x.Id == id);
            if (row is not null) Fields.Remove(row);
        }
    }

    private void OnReviewClicked(object? sender, EventArgs e) => ShowPreview();

    private SmsTemplate? BuildTemplate(out string? error)
    {
        var fields = new List<SmsTemplateField>();
        foreach (var row in Fields)
        {
            var key = SmsKeyValueTemplateBuilder.FromLabel(row.KeyLabel);
            if (key is null) { error = "کلید یکی از ردیف‌ها را انتخاب کنید."; return null; }
            fields.Add(new SmsTemplateField { Key = key.Value, Value = row.Value.Trim() });
        }
        var result = SmsKeyValueTemplateBuilder.Build(
            _templateId, _senderId, NameEntry.Text ?? string.Empty, MessageEditor.Text ?? string.Empty, fields);
        error = result.Error;
        return result.Template;
    }

    private bool ShowPreview()
    {
        PreviewCard.IsVisible = true;
        var template = BuildTemplate(out var error);
        if (template is null)
        {
            PreviewResultLabel.Text = error;
            PatternPreviewLabel.Text = string.Empty;
            return false;
        }
        var parsed = SmsTemplateParser.TryParse(template, MessageEditor.Text ?? string.Empty);
        if (parsed is null)
        {
            PreviewResultLabel.Text = "قالب ساخته شد اما با پیام نمونه تطبیق پیدا نکرد.";
            PatternPreviewLabel.Text = template.Pattern;
            return false;
        }
        PreviewResultLabel.Text = $"✓ نوع: {(parsed.Kind == TransactionKind.Withdrawal ? "برداشت" : "واریز")}   ·   مبلغ: {parsed.AmountRial:N0} ریال\nتاریخ: {parsed.DateText ?? "ثبت نشده"}   ·   ساعت: {parsed.TimeText ?? "ثبت نشده"}";
        PatternPreviewLabel.Text = "ساختار ذخیره‌شده:\n" + template.Pattern;
        return true;
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        var template = BuildTemplate(out var error);
        if (template is null || !ShowPreview())
        {
            await DisplayAlert("قالب کامل نیست", error ?? "مقادیر ردیف‌ها را بررسی کنید.", "باشه");
            return;
        }
        error = await _store.SaveTemplateAsync(template);
        if (error is not null) { await DisplayAlert("ذخیره نشد", error, "باشه"); return; }
        await Navigation.PopAsync();
    }

    private void LoadFields(IEnumerable<SmsTemplateField> fields)
    {
        Fields.Clear();
        foreach (var field in fields)
            Fields.Add(new TemplateFieldRow(SmsKeyValueTemplateBuilder.GetLabel(field.Key), field.Value));
    }

    private static IEnumerable<SmsTemplateField> DefaultFields() => Enum.GetValues<TemplateFieldKey>()
        .Select(key => new SmsTemplateField { Key = key });
}

public sealed class TemplateFieldRow : INotifyPropertyChanged
{
    private string _keyLabel;
    private string _value;
    public static IList<string> AvailableKeys { get; } =
        Enum.GetValues<TemplateFieldKey>().Select(SmsKeyValueTemplateBuilder.GetLabel).ToList();
    public Guid Id { get; } = Guid.NewGuid();
    public string KeyLabel { get => _keyLabel; set => SetField(ref _keyLabel, value); }
    public string Value { get => _value; set => SetField(ref _value, value); }

    public TemplateFieldRow(string keyLabel, string value) => (_keyLabel, _value) = (keyLabel, value);
    public event PropertyChangedEventHandler? PropertyChanged;
    private void SetField(ref string field, string value, [CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
