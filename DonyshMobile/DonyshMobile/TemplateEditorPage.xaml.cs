using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DonyshMobile.Core;
using DonyshMobile.UI;

namespace DonyshMobile;

public partial class TemplateEditorPage : ContentPage
{
    private readonly SmsMonitorStore _store;
    private readonly Guid _senderId;
    private readonly Guid _templateId;
    private readonly Action _scheduleSync;
    private bool _initializing = true;
    private bool _messagesLoaded;
    private bool _saving;
    private bool _entered;
    private bool _updatingPreset;
    private SmsTemplatePreset? _previousPreset;
    private IReadOnlyList<CapturedSms> _savedMessages = [];

    public ObservableCollection<TemplateFieldRow> Fields { get; } = [];

    public TemplateEditorPage(SmsMonitorStore store, Guid senderId, string senderAddress, SmsTemplate? template = null)
        : this(store, senderId, senderAddress, template, SyncPlatform.Schedule)
    {
    }

    internal TemplateEditorPage(SmsMonitorStore store, Guid senderId, string senderAddress, SmsTemplate? template, Action scheduleSync)
    {
        _store = store;
        _scheduleSync = scheduleSync;
        _senderId = senderId;
        _templateId = template?.Id ?? Guid.NewGuid();
        InitializeComponent();
        BindingContext = this;
        SenderLabel.Text = $"برای فرستنده: {senderAddress}";
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
            var preset = SmsTemplatePresets.All.FirstOrDefault(x => x.Id == template.PresetId);
            if (preset is not null)
            {
                _previousPreset = preset;
                SourcePicker.SelectedIndex = 1;
                PresetPicker.SelectedIndex = SmsTemplatePresets.All.ToList().IndexOf(preset);
                PresetDescriptionLabel.Text = preset.Description;
                BankSampleLabel.Text = preset.SampleMessage;
            }
            if (template.Fields.Count == 0)
            {
                PreviewCard.IsVisible = true;
                PreviewResultLabel.Text = "این قالب با نسخه قبلی ساخته شده است. مقادیر ردیف‌ها را یک بار تکمیل و دوباره ذخیره کنید.";
            }
        }
        _initializing = false;
        UpdateSourceMode();
        if (!IsPresetMode && Fields.Count == 0) LoadFields(DefaultFields());
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_entered)
        {
            _entered = true;
            await UiMotion.EnterAsync(EditorContent);
        }
        if (_messagesLoaded) return;
        try
        {
            _savedMessages = await _store.GetMessagesAsync(_senderId);
            SavedMessagePicker.Items.Clear();
            foreach (var message in _savedMessages)
            {
                var preview = message.Body.Replace('\r', ' ').Replace('\n', ' ').Trim();
                SavedMessagePicker.Items.Add($"{UiText.Date(message.ReceivedAtUtc)} · {UiText.Digits(preview[..Math.Min(40, preview.Length)])}");
            }
            SavedMessagePicker.IsVisible = _savedMessages.Count > 0;
            SavedMessageHint.Text = _savedMessages.Count > 0
                ? "می‌توانید یک پیام واقعی انتخاب کنید یا متن پیامک را در کادر زیر بچسبانید."
                : "هنوز پیام ثبت‌شده‌ای از این فرستنده ندارید؛ متن پیامک را از برنامه پیام‌ها کپی کنید.";
            _messagesLoaded = true;
        }
        catch (Exception)
        {
            SavedMessagePicker.IsVisible = false;
            SavedMessageHint.Text = "فهرست پیام‌ها خوانده نشد. می‌توانید متن پیام را در کادر زیر قرار دهید.";
        }
    }

    private void OnSourceChanged(object? sender, EventArgs e)
    {
        UpdateSourceMode();
        if (!IsPresetMode && Fields.Count == 0) LoadFields(DefaultFields());
        InvalidatePreview();
        if (!_initializing && IsPresetMode && PresetPicker.SelectedIndex >= 0) ShowPreview();
    }

    private bool IsPresetMode => SourcePicker.SelectedIndex == 1;

    private void UpdateSourceMode()
    {
        PresetSection.IsVisible = IsPresetMode;
        ManualFieldsPanel.IsVisible = !IsPresetMode;
        EditFieldsButton.IsVisible = IsPresetMode;
        FieldsTitleLabel.Text = IsPresetMode ? "۳ · بخش‌ها خودکار خوانده می‌شوند" : "۳ · بخش‌های پیام را مشخص کنید";
        FieldsHelpLabel.Text = IsPresetMode
            ? "مبلغ، تاریخ، ساعت و نوع تراکنش از پیام نمونه خوانده می‌شوند. نیازی به پرکردن دستی بخش‌ها نیست؛ نتیجهٔ پایین صفحه را بررسی کنید."
            : "سمت راست نام بخش و سمت چپ مقدار آن را بنویسید. مبلغ را با موجودی حساب اشتباه نگیرید.";
        IntroLabel.Text = IsPresetMode ? "بانک را انتخاب کنید و یک پیام واقعی را بررسی کنید؛ بخش‌ها خودکار تشخیص داده می‌شوند."
            : "فقط مبلغ را از پیام مشخص کنید. تاریخ، ساعت و اطلاعات دیگر را در صورت نیاز اضافه کنید.";
    }

    private void OnEditFieldsClicked(object? sender, EventArgs e) => SourcePicker.SelectedIndex = 0;

    private void OnPresetSelected(object? sender, EventArgs e)
    {
        if (_initializing || PresetPicker.SelectedIndex < 0 || PresetPicker.SelectedIndex >= SmsTemplatePresets.All.Count) return;
        var preset = SmsTemplatePresets.All[PresetPicker.SelectedIndex];
        _updatingPreset = true;
        try
        {
            PresetDescriptionLabel.Text = preset.Description;
            BankSampleLabel.Text = preset.SampleMessage;
            if (string.IsNullOrWhiteSpace(MessageEditor.Text) || MessageEditor.Text == _previousPreset?.SampleMessage)
                MessageEditor.Text = preset.SampleMessage;
            if (string.IsNullOrWhiteSpace(NameEntry.Text) || NameEntry.Text == _previousPreset?.BankName)
                NameEntry.Text = preset.BankName;
            _previousPreset = preset;
        }
        finally { _updatingPreset = false; }
        ShowPreview();
    }

    private void OnSavedMessageSelected(object? sender, EventArgs e)
    {
        var index = SavedMessagePicker.SelectedIndex;
        if (index >= 0 && index < _savedMessages.Count)
        {
            MessageEditor.Text = _savedMessages[index].Body;
            if (IsPresetMode) ShowPreview();
        }
    }

    private async void OnAddFieldClicked(object? sender, EventArgs e)
    {
        var used = Fields.Select(x => x.KeyLabel).ToHashSet(StringComparer.Ordinal);
        var next = TemplateFieldRow.AvailableKeys.FirstOrDefault(x => !used.Contains(x));
        if (next is null)
        {
            await DisplayAlert("همه بخش‌ها اضافه شده‌اند", "برای هر بخش از پیام فقط یک ردیف لازم است.", "باشه");
            return;
        }
        var choices = TemplateFieldRow.AvailableKeys.Where(x => !used.Contains(x)).ToArray();
        var selectedIndex = await DonyshSelectionSheet.ShowAsync(this, "چه بخشی اضافه شود؟", choices);
        if (!selectedIndex.HasValue || selectedIndex.Value < 0 || selectedIndex.Value >= choices.Length) return;
        var selected = choices[selectedIndex.Value];
        AddField(new TemplateFieldRow(selected, selected == "واحد" ? "ریال" : string.Empty));
        InvalidatePreview();
    }

    private void OnRemoveFieldClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: Guid id })
        {
            var row = Fields.FirstOrDefault(x => x.Id == id);
            if (row is not null)
            {
                row.PropertyChanged -= OnFieldChanged;
                Fields.Remove(row);
                InvalidatePreview();
            }
        }
    }

    private async void OnReviewClicked(object? sender, EventArgs e)
    {
        try
        {
            ShowPreview();
            await EditorScroll.ScrollToAsync(PreviewCard, ScrollToPosition.MakeVisible, true);
        }
        catch (Exception exception) { UiSafety.Log(exception); }
    }

    private void OnInputChanged(object? sender, TextChangedEventArgs e)
    {
        if (_initializing || _updatingPreset) return;
        InvalidatePreview();
        if (IsPresetMode && sender == MessageEditor) LoadFields([]);
    }

    private void OnFieldChanged(object? sender, PropertyChangedEventArgs e) => InvalidatePreview();

    private void InvalidatePreview()
    {
        if (_initializing || _updatingPreset || PreviewCard is null) return;
        PreviewCard.IsVisible = false;
        PatternPreviewLabel.IsVisible = false;
        DetailsButton.Text = "نمایش الگوی پیام";
    }

    private void OnToggleDetailsClicked(object? sender, EventArgs e)
    {
        PatternPreviewLabel.IsVisible = !PatternPreviewLabel.IsVisible;
        DetailsButton.Text = PatternPreviewLabel.IsVisible ? "بستن الگوی پیام" : "نمایش الگوی پیام";
    }

    private void OnToggleBankSampleClicked(object? sender, EventArgs e)
    {
        BankSampleLabel.IsVisible = !BankSampleLabel.IsVisible;
        BankSampleButton.Text = BankSampleLabel.IsVisible ? "بستن نمونهٔ بانک" : "نمایش نمونهٔ بانک";
    }

    private SmsTemplate? BuildTemplate(out string? error)
    {
        if (IsPresetMode)
        {
            var index = PresetPicker.SelectedIndex;
            if (index < 0 || index >= SmsTemplatePresets.All.Count)
            {
                error = "ابتدا بانک را از فهرست قالب‌های آماده انتخاب کنید.";
                return null;
            }
            var ready = SmsTemplatePresets.Build(SmsTemplatePresets.All[index], _templateId, _senderId,
                NameEntry.Text, MessageEditor.Text ?? string.Empty);
            LoadFields(ready.Template?.Fields ?? []);
            error = ready.Error;
            return ready.Template;
        }
        var fields = new List<SmsTemplateField>();
        foreach (var row in Fields)
        {
            if (string.IsNullOrWhiteSpace(row.Value)) continue;
            var key = SmsKeyValueTemplateBuilder.FromLabel(row.KeyLabel);
            if (key is null) { error = "نام بخش را در یکی از ردیف‌ها انتخاب کنید."; return null; }
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
        DetailsButton.IsVisible = false;
        PatternPreviewLabel.IsVisible = false;
        DetailsButton.Text = "نمایش الگوی پیام";
        var template = BuildTemplate(out var error);
        if (template is null)
        {
            SetPreviewState(false);
            PreviewResultLabel.Text = ReadablePattern(error ?? "مقدارهای پیام را دوباره بررسی کنید.");
            PatternPreviewLabel.Text = string.Empty;
            return false;
        }
        var parsed = SmsTemplateParser.TryParse(template, MessageEditor.Text ?? string.Empty);
        if (parsed is null)
        {
            SetPreviewState(false);
            PreviewResultLabel.Text = "این قالب پیام نمونه را نخواند. مبلغ و بخش‌های دیگر را دوباره بررسی کنید.";
            PatternPreviewLabel.Text = ReadablePattern(template.Pattern);
            return false;
        }
        SetPreviewState(true);
        PreviewResultLabel.Text = $"{(parsed.Kind == TransactionKind.Withdrawal ? "برداشت" : "واریز")} · {UiText.Digits(parsed.AmountRial.ToString("N0"))} ریال\nتاریخ: {UiText.Digits(parsed.DateText ?? "از زمان دریافت پیام")}\nساعت: {UiText.Digits(parsed.TimeText ?? "از زمان دریافت پیام")}\n\nنتیجه را بررسی کنید. پس از ذخیره، پیام‌های بعدی با همین قالب خوانده می‌شوند و پیام‌های ردشدهٔ سازگار هم دوباره منتظر ارسال قرار می‌گیرند.";
        PatternPreviewLabel.Text = "بخش‌های متغیر پیام:\n" + ReadablePattern(template.Pattern);
        DetailsButton.IsVisible = true;
        return true;
    }

    private static string ReadablePattern(string pattern) => UiText.Digits(pattern
        .Replace("{amount}", "«مبلغ»", StringComparison.OrdinalIgnoreCase)
        .Replace("{date}", "«تاریخ»", StringComparison.OrdinalIgnoreCase)
        .Replace("{time}", "«ساعت»", StringComparison.OrdinalIgnoreCase)
        .Replace("{type}", "«نوع تراکنش»", StringComparison.OrdinalIgnoreCase)
        .Replace("{ignore}", "«بخش متغیر»", StringComparison.OrdinalIgnoreCase));

    private void SetPreviewState(bool success)
    {
        PreviewTitleLabel.Text = success ? "✓ پیام نمونه خوانده شد" : "یک مورد نیاز به اصلاح دارد";
        PreviewResultLabel.SetAppThemeColor(Label.TextColorProperty,
            Color.FromArgb(success ? "#047857" : "#BE123C"),
            Color.FromArgb(success ? "#6EE7B7" : "#FDA4AF"));
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (_saving) return;
        var template = BuildTemplate(out var error);
        if (template is null || !ShowPreview())
        {
            await DisplayAlert("قالب کامل نیست", ReadablePattern(error ?? "مقدارهای ردیف‌ها را بررسی کنید."), "باشه");
            return;
        }
        _saving = true;
        SaveButton.IsEnabled = false;
        SaveButton.Text = "در حال ذخیره…";
        try
        {
            // Finish any in-flight receipt first so a late rejection cannot undo the retry reset.
            await MobileApiClient.RunWithSyncLockAsync(async () => error = await _store.SaveTemplateAsync(template));
            if (error is not null) { await DisplayAlert("ذخیره نشد", ReadablePattern(error), "باشه"); return; }
            SmsReceivedNotifier.NotifySyncChanged();
            _scheduleSync();
            UiMotion.Feedback();
            await Navigation.PopAsync();
        }
        catch (Exception exception)
        {
            UiSafety.Log(exception);
            await UiSafety.AlertAsync(this, "ذخیره نشد", "ذخیره قالب کامل نشد. دوباره تلاش کنید.");
        }
        finally
        {
            _saving = false;
            SaveButton.IsEnabled = true;
            SaveButton.Text = "بررسی و ذخیره قالب";
        }
    }

    private void LoadFields(IEnumerable<SmsTemplateField> fields)
    {
        var previous = _updatingPreset;
        _updatingPreset = true;
        try
        {
            foreach (var row in Fields) row.PropertyChanged -= OnFieldChanged;
            Fields.Clear();
            foreach (var field in fields)
                AddField(new TemplateFieldRow(SmsKeyValueTemplateBuilder.GetLabel(field.Key), field.Value));
        }
        finally { _updatingPreset = previous; }
    }

    private void AddField(TemplateFieldRow row)
    {
        row.PropertyChanged += OnFieldChanged;
        Fields.Add(row);
    }

    private static IEnumerable<SmsTemplateField> DefaultFields() =>
    [
        new() { Key = TemplateFieldKey.Amount },
        new() { Key = TemplateFieldKey.Unit, Value = "ریال" }
    ];
}

public sealed class TemplateFieldRow : INotifyPropertyChanged
{
    private string _keyLabel;
    private string _value;
    public static IList<string> AvailableKeys { get; } =
        Enum.GetValues<TemplateFieldKey>().Select(SmsKeyValueTemplateBuilder.GetLabel).ToList();
    public Guid Id { get; } = Guid.NewGuid();
    public string KeyLabel
    {
        get => _keyLabel;
        set
        {
            SetField(ref _keyLabel, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Hint)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Example)));
        }
    }
    public string Value { get => _value; set => SetField(ref _value, value); }
    public string Example => KeyLabel switch
    {
        "مبلغ" => "مثلاً ۱٬۴۰۰٬۰۰۰",
        "تاریخ" => "مثلاً ۱۴۰۵.۰۷.۱۹",
        "ساعت" => "مثلاً ۱۷:۵۵",
        "نوع تراکنش" => "مثلاً برداشت",
        "نام بانک" => "مثلاً بلو",
        "واحد" => "ریال یا تومان",
        _ => "مقدار داخل پیام"
    };
    public string Hint => KeyLabel switch
    {
        "مبلغ" => "لازم است · فقط مبلغ تراکنش را بنویسید، بدون واحد پول.",
        "تاریخ" => "اختیاری · همان تاریخ داخل پیام؛ ارقام فارسی هم پذیرفته می‌شوند.",
        "ساعت" => "اختیاری · همان ساعت داخل پیام، مانند ۱۷:۵۵.",
        "نوع تراکنش" => "اختیاری · عین عبارت پیام را بنویسید؛ مثلاً «برداشت» یا علامت «-».",
        "نام بانک" => "اختیاری · برای نام‌گذاری قالب استفاده می‌شود.",
        "واحد" => "ریال یا تومان؛ اگر در متن پیام نیست هم می‌توانید آن را مشخص کنید.",
        _ => string.Empty
    };

    public TemplateFieldRow(string keyLabel, string value) => (_keyLabel, _value) = (keyLabel, value);
    public event PropertyChangedEventHandler? PropertyChanged;
    private void SetField(ref string field, string value, [CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
