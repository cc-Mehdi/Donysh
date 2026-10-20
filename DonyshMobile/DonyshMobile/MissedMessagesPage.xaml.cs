using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DonyshMobile.Core;
using DonyshMobile.UI;
#if ANDROID
using DonyshMobile.Platforms.Android;
#endif

namespace DonyshMobile;

public partial class MissedMessagesPage : DonyshPage
{
    private static readonly TimeSpan MaximumRange = TimeSpan.FromDays(31);
    private readonly SmsMonitorStore _store;
    private bool _busy, _loaded, _hasScanned, _changingSenderSelection, _changingMessageSelection;

    public ObservableCollection<RecoverySenderOption> Senders { get; } = [];
    public ObservableCollection<RecoveryMessageOption> Messages { get; } = [];

    public MissedMessagesPage(SmsMonitorStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        InitializeComponent();
        BindingContext = this;

        var now = DateTime.Now;
        var from = now.AddHours(-24);
        FromDatePicker.MinimumDate = ToDatePicker.MinimumDate = now.Date.AddDays(-31);
        FromDatePicker.MaximumDate = ToDatePicker.MaximumDate = now.Date;
        FromDatePicker.Date = from.Date;
        FromTimePicker.Time = from.TimeOfDay;
        ToDatePicker.Date = now.Date;
        ToTimePicker.Time = now.TimeOfDay;
        UpdateScanButtonText();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            if (!_loaded)
            {
                _loaded = true;
                foreach (var sender in await _store.GetSenderSummariesAsync())
                    Senders.Add(new RecoverySenderOption(sender.Id, sender.Address));
                AllSendersCheckBox.IsChecked = Senders.Count > 0;
                StatusLabel.Text = Senders.Count == 0
                    ? "ابتدا یک فرستنده را در صفحهٔ اصلی ثبت کنید."
                    : "فرستنده‌ها و بازه آماده‌اند؛ بررسی را شروع کنید.";
                ScanButton.IsEnabled = Senders.Count > 0;
                await UiMotion.EnterAsync(PageContent);
            }
        }
        catch (Exception exception)
        {
            UiSafety.Log(exception);
            StatusLabel.Text = "فهرست فرستنده‌ها آماده نشد؛ دوباره این صفحه را باز کنید.";
        }
        finally { await CompletePageLoadingAsync(); }
    }

    private async void OnScanClicked(object? sender, EventArgs e)
    {
        if (_busy) return;
        var selectedSenders = Senders.Where(x => x.IsSelected).Select(x => x.Address).ToList();
        if (selectedSenders.Count == 0)
        {
            await UiSafety.AlertAsync(this, "فرستنده‌ای انتخاب نشده", "حداقل یک فرستنده را برای بررسی انتخاب کنید.");
            return;
        }

        var from = ReadLocalDateTime(FromDatePicker, FromTimePicker);
        var to = ReadLocalDateTime(ToDatePicker, ToTimePicker);
        if (from >= to)
        {
            await UiSafety.AlertAsync(this, "بازه درست نیست", "زمان شروع باید پیش از زمان پایان باشد.");
            return;
        }
        if (to > DateTimeOffset.Now.AddMinutes(5))
        {
            await UiSafety.AlertAsync(this, "زمان پایان درست نیست", "زمان پایان نمی‌تواند در آینده باشد.");
            return;
        }
        if (to - from > MaximumRange)
        {
            await UiSafety.AlertAsync(this, "بازه خیلی طولانی است", "برای حفظ سرعت گوشی، هر بار حداکثر ۳۱ روز را بررسی کنید.");
            return;
        }

        await RunBusyAsync(async () =>
        {
#if ANDROID
            var permission = await Permissions.CheckStatusAsync<ReadSmsPermission>();
            if (permission != PermissionStatus.Granted)
                permission = await Permissions.RequestAsync<ReadSmsPermission>();
            if (permission != PermissionStatus.Granted)
            {
                StatusLabel.Text = "برای بررسی پیامک‌های قبلی، اجازهٔ خواندن پیامک را فعال کنید.";
                return;
            }

            var candidates = await SyncPlatform.FindMissingMessagesAsync(
                _store, from.ToUniversalTime(), to.ToUniversalTime(), selectedSenders);
            Messages.Clear();
            foreach (var candidate in candidates)
                Messages.Add(new RecoveryMessageOption(candidate));
            _hasScanned = true;
            ResultsHeading.IsVisible = AddBar.IsVisible = Messages.Count > 0;
            StatusLabel.Text = Messages.Count == 0
                ? "پیامک جامانده‌ای در این بازه پیدا نشد."
                : $"{UiText.Digits(Messages.Count)} پیامک پیدا شد؛ موارد ناخواسته را از انتخاب خارج کنید.";
            UpdateSelectedMessages();
#else
            await Task.CompletedTask;
            StatusLabel.Text = "بررسی پیامک‌های گوشی فقط در نسخهٔ اندروید انجام می‌شود.";
#endif
        });
    }

    private async void OnAddClicked(object? sender, EventArgs e)
    {
        if (_busy) return;
        var selected = Messages.Where(x => x.IsSelected).Select(x => x.Candidate).ToList();
        if (selected.Count == 0)
        {
            await UiSafety.AlertAsync(this, "پیامی انتخاب نشده", "پیامک‌هایی را که باید به فهرست افزوده شوند انتخاب کنید.");
            return;
        }

        await RunBusyAsync(async () =>
        {
            var added = await _store.CaptureBatchAsync(selected);
            if (added == 0)
            {
                StatusLabel.Text = "این پیامک‌ها از قبل در فهرست قرار داشتند.";
                return;
            }
            UiMotion.Feedback();
            SmsReceivedNotifier.NotifySyncChanged();
            SyncPlatform.Schedule();
            await UiSafety.AlertAsync(this, "به فهرست افزوده شد",
                $"{UiText.Digits(added)} پیامک در فهرست ارسال به دُنیش قرار گرفت.");
            await Navigation.PopAsync();
        });
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        _busy = true;
        BusyIndicator.IsVisible = BusyIndicator.IsRunning = true;
        ScanButton.IsEnabled = AddButton.IsEnabled = false;
        StatusLabel.Text = "در حال بررسی پیامک‌های گوشی…";
        try { await action(); }
        catch (UnauthorizedAccessException)
        {
            StatusLabel.Text = "اجازهٔ خواندن پیامک فعال نیست.";
        }
        catch (Exception exception)
        {
            UiSafety.Log(exception);
            StatusLabel.Text = "بررسی کامل نشد؛ هیچ پیامی به فهرست اضافه نشده است.";
        }
        finally
        {
            _busy = false;
            BusyIndicator.IsVisible = BusyIndicator.IsRunning = false;
            ScanButton.IsEnabled = Senders.Any(x => x.IsSelected);
            AddButton.IsEnabled = Messages.Any(x => x.IsSelected);
        }
    }

    private void OnAllSendersChanged(object? sender, CheckedChangedEventArgs e)
    {
        if (_changingSenderSelection) return;
        _changingSenderSelection = true;
        foreach (var option in Senders) option.IsSelected = e.Value;
        _changingSenderSelection = false;
        ScanButton.IsEnabled = !_busy && Senders.Any(x => x.IsSelected);
        InvalidateResults();
    }

    private void OnSenderSelectionChanged(object? sender, CheckedChangedEventArgs e)
    {
        if (_changingSenderSelection) return;
        _changingSenderSelection = true;
        AllSendersCheckBox.IsChecked = Senders.Count > 0 && Senders.All(x => x.IsSelected);
        _changingSenderSelection = false;
        ScanButton.IsEnabled = !_busy && Senders.Any(x => x.IsSelected);
        InvalidateResults();
    }

    private void OnToggleMessagesClicked(object? sender, EventArgs e)
    {
        var select = !Messages.All(x => x.IsSelected);
        _changingMessageSelection = true;
        foreach (var message in Messages) message.IsSelected = select;
        _changingMessageSelection = false;
        UpdateSelectedMessages();
    }

    private void OnMessageSelectionChanged(object? sender, CheckedChangedEventArgs e)
    {
        if (!_changingMessageSelection) UpdateSelectedMessages();
    }

    private void UpdateSelectedMessages()
    {
        var selected = Messages.Count(x => x.IsSelected);
        AddButton.IsEnabled = !_busy && selected > 0;
        AddButton.Text = selected > 0
            ? $"افزودن {UiText.Digits(selected)} پیام به فهرست"
            : "پیامی برای افزودن انتخاب نشده";
        ToggleMessagesButton.Text = Messages.Count > 0 && Messages.All(x => x.IsSelected) ? "لغو همه" : "انتخاب همه";
    }

    private void OnDateChanged(object? sender, DateChangedEventArgs e)
    {
        UpdateScanButtonText();
        InvalidateResults();
    }
    private void OnTimeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != TimePicker.TimeProperty.PropertyName) return;
        UpdateScanButtonText();
        InvalidateResults();
    }

    private void InvalidateResults()
    {
        if (!_hasScanned || _busy) return;
        _hasScanned = false;
        Messages.Clear();
        ResultsHeading.IsVisible = AddBar.IsVisible = false;
        StatusLabel.Text = "فیلتر تغییر کرد؛ برای دیدن نتیجهٔ تازه دوباره بررسی کنید.";
    }

    private void UpdateScanButtonText()
    {
        var span = ReadLocalDateTime(ToDatePicker, ToTimePicker) - ReadLocalDateTime(FromDatePicker, FromTimePicker);
        if (span <= TimeSpan.Zero)
        {
            ScanButton.Text = "بررسی بازهٔ انتخابی";
            return;
        }
        var roundedHours = Math.Max(1, (int)Math.Round(span.TotalHours));
        ScanButton.Text = roundedHours >= 48 && roundedHours % 24 == 0
            ? $"بررسی پیامک‌های {UiText.Digits(roundedHours / 24)} روز گذشته"
            : $"بررسی پیامک‌های {UiText.Digits(roundedHours)} ساعت گذشته";
    }

    private static DateTimeOffset ReadLocalDateTime(DatePicker datePicker, TimePicker timePicker)
    {
        var local = DateTime.SpecifyKind(datePicker.Date.Date + timePicker.Time, DateTimeKind.Local);
        return new DateTimeOffset(local);
    }
}

public sealed class RecoverySenderOption(Guid id, string address) : INotifyPropertyChanged
{
    private bool _isSelected = true;
    public Guid Id { get; } = id;
    public string Address { get; } = address;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class RecoveryMessageOption : INotifyPropertyChanged
{
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    private bool _isSelected = true;

    public RecoveryMessageOption(SmsCaptureCandidate candidate)
    {
        Candidate = candidate;
        SenderAddress = candidate.SenderAddress;
        ReceivedAtText = UiText.Digits(candidate.ReceivedAtUtc.ToLocalTime().ToString("yyyy/MM/dd  HH:mm"));
        BodyPreview = Whitespace.Replace(candidate.Body, " ").Trim();
    }

    public SmsCaptureCandidate Candidate { get; }
    public string SenderAddress { get; }
    public string ReceivedAtText { get; }
    public string BodyPreview { get; }
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
