using DonyshMobile.Core;
using DonyshMobile.UI;
#if ANDROID
using DonyshMobile.Platforms.Android;
#endif

namespace DonyshMobile;

public partial class SetupWizardPage : ContentPage
{
    private const string FinishedKey = "setup-wizard-finished-v1";
    private readonly SmsMonitorStore _store;
    private readonly Func<Task<MobileConnection?>> _loadConnectionAsync;
    private readonly Func<Task<bool>> _hasReceivePermissionAsync;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private bool _busy;
    private bool _openingPage;
    private bool _entered;
#if ANDROID
    private bool _openPermissionSettings;
#endif
    private SetupStep _step = SetupStep.Connection;
    private SenderSummary? _selectedSender;

    public static bool WasFinished => Preferences.Default.Get(FinishedKey, false);
    public static void MarkFinished() => Preferences.Default.Set(FinishedKey, true);

    public bool IsConnectionStep => _step == SetupStep.Connection;
    public bool IsSenderStep => _step == SetupStep.Sender;
    public bool IsPermissionStep => _step == SetupStep.Permission;
    public bool IsTemplateStep => _step == SetupStep.Template;
    public bool IsCompleteStep => _step == SetupStep.Complete;
    public string ProgressText => _step switch
    {
        SetupStep.Connection => "اتصال به سامانه",
        SetupStep.Sender or SetupStep.Permission => "انتخاب فرستنده",
        SetupStep.Template => "ساخت قالب پیام",
        _ => "راه‌اندازی کامل شد"
    };
    public string ProgressFraction => $"مرحلهٔ {UiText.Digits(SetupFlow.Progress(_step))} از ۳";
    public double ProgressValue => SetupFlow.Progress(_step) / 3d;
    public string SelectedSenderText => _selectedSender is null
        ? "فرستندهٔ انتخاب‌شده"
        : $"فرستنده: {_selectedSender.Address}";

    public SetupWizardPage(SmsMonitorStore store)
        : this(store, MobileConnectionStore.LoadAsync, HasReceivePermissionAsync)
    {
    }

    internal SetupWizardPage(SmsMonitorStore store,
        Func<Task<MobileConnection?>> loadConnectionAsync,
        Func<Task<bool>> hasReceivePermissionAsync)
    {
        _store = store;
        _loadConnectionAsync = loadConnectionAsync;
        _hasReceivePermissionAsync = hasReceivePermissionAsync;
        InitializeComponent();
        BindingContext = this;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await RefreshAsync();
            if (!_entered)
            {
                _entered = true;
                await UiMotion.EnterAsync(WizardContent);
            }
        }
        catch (Exception exception)
        {
            UiSafety.Log(exception);
            ShowStatus("خواندن وضعیت راه‌اندازی کامل نشد. دوباره تلاش کنید.");
        }
    }

    private async Task RefreshAsync()
    {
        if (!await _refreshGate.WaitAsync(0)) return;
        try
        {
            var connection = await _loadConnectionAsync();
            var senders = await _store.GetSenderSummariesAsync();
            _selectedSender = senders.FirstOrDefault();
            var hasTemplate = false;
            foreach (var sender in senders)
            {
                if ((await _store.GetTemplatesAsync(sender.Id)).Count == 0) continue;
                _selectedSender = sender;
                hasTemplate = true;
                break;
            }
            var hasPermission = await _hasReceivePermissionAsync();
            if (hasPermission)
            {
#if ANDROID
                _openPermissionSettings = false;
#endif
                PermissionButton.Text = "فعال‌کردن دریافت پیامک";
            }
            _step = SetupFlow.Resolve(connection is not null, senders.Count, hasPermission, hasTemplate);
            UpdateProgressColors();
            OnPropertyChanged(string.Empty);
        }
        finally { _refreshGate.Release(); }
    }

    private static async Task<bool> HasReceivePermissionAsync()
    {
#if ANDROID
        try { return await Permissions.CheckStatusAsync<ReceiveSmsPermission>() == PermissionStatus.Granted; }
        catch (Exception) { return false; }
#else
        await Task.CompletedTask;
        return true;
#endif
    }

    private void UpdateProgressColors()
    {
        var active = Color.FromArgb("#4F46E5");
        var muted = Application.Current?.RequestedTheme == AppTheme.Dark
            ? Color.FromArgb("#94A3B8") : Color.FromArgb("#64748B");
        var progress = SetupFlow.Progress(_step);
        ConnectionStepLabel.TextColor = progress >= 1 ? active : muted;
        SenderStepLabel.TextColor = progress >= 2 ? active : muted;
        TemplateStepLabel.TextColor = progress >= 3 ? active : muted;
    }

    private async void OnConnectionClicked(object? sender, EventArgs e) =>
        await OpenPageAsync(() => new ConnectionPage(_store, setupMode: true));

    private async void OnPickSenderClicked(object? sender, EventArgs e)
    {
        if (_busy) return;
        _busy = true;
        SetButtonsEnabled(false);
        try
        {
#if ANDROID
            var permission = await Permissions.RequestAsync<ReadSmsPermission>();
            if (permission != PermissionStatus.Granted)
            {
                ShowStatus("اجازهٔ دیدن فهرست داده نشد؛ نام یا شماره را دستی وارد کنید.");
                ManualSenderPanel.IsVisible = true;
                return;
            }
            var addresses = await Task.Run(() => SmsConversationCatalog.RecentSenders(global::Android.App.Application.Context));
            await OpenPageAsync(() => new SenderPickerPage(addresses, AddSenderAsync));
#else
            ManualSenderPanel.IsVisible = true;
            ManualSenderEntry.Focus();
            await Task.CompletedTask;
#endif
        }
        catch (Exception exception)
        {
            UiSafety.Log(exception);
            ShowStatus("فهرست پیامک‌ها باز نشد؛ نام یا شماره را دستی وارد کنید.");
            ManualSenderPanel.IsVisible = true;
        }
        finally
        {
            _busy = false;
            SetButtonsEnabled(true);
        }
    }

    private void OnToggleManualClicked(object? sender, EventArgs e)
    {
        ManualSenderPanel.IsVisible = !ManualSenderPanel.IsVisible;
        if (ManualSenderPanel.IsVisible) ManualSenderEntry.Focus();
    }

    private async void OnAddManualSenderClicked(object? sender, EventArgs e)
    {
        if (_busy) return;
        _busy = true;
        SetButtonsEnabled(false);
        try { await AddSenderAsync(ManualSenderEntry.Text ?? string.Empty); }
        catch (Exception exception)
        {
            UiSafety.Log(exception);
            ShowStatus("ثبت فرستنده کامل نشد. دوباره تلاش کنید.");
        }
        finally
        {
            _busy = false;
            SetButtonsEnabled(true);
        }
    }

    private async Task AddSenderAsync(string address)
    {
        var result = await _store.AddSenderAsync(address);
        if (result == AddSenderResult.Added)
        {
            ManualSenderEntry.Text = string.Empty;
            ManualSenderPanel.IsVisible = false;
            StatusLabel.IsVisible = false;
            UiMotion.Feedback();
            await RefreshAsync();
            return;
        }
        ShowStatus(result switch
        {
            AddSenderResult.Duplicate => "این فرستنده قبلاً ثبت شده است.",
            AddSenderResult.LimitReached => "تعداد فرستنده‌ها به بیشترین مقدار رسیده است.",
            _ => "نام یا شمارهٔ فرستنده را بررسی کنید."
        });
    }

    private async void OnPermissionClicked(object? sender, EventArgs e)
    {
        if (_busy) return;
        _busy = true;
        SetButtonsEnabled(false);
        try
        {
#if ANDROID
            if (_openPermissionSettings)
            {
                AppInfo.ShowSettingsUI();
                return;
            }
            var status = await Permissions.RequestAsync<ReceiveSmsPermission>();
            if (status != PermissionStatus.Granted)
            {
                ShowStatus("برای نگهداری پیام‌های تازه، اجازهٔ دریافت پیامک را در تنظیمات گوشی فعال کنید.");
                _openPermissionSettings = true;
                PermissionButton.Text = "بازکردن تنظیمات گوشی";
                return;
            }
#endif
            StatusLabel.IsVisible = false;
            UiMotion.Feedback();
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            UiSafety.Log(exception);
            ShowStatus("فعال‌کردن دریافت پیامک کامل نشد. اجازه‌های برنامه را در تنظیمات گوشی بررسی کنید.");
        }
        finally
        {
            _busy = false;
            SetButtonsEnabled(true);
        }
    }

    private async void OnTemplateClicked(object? sender, EventArgs e)
    {
        if (_selectedSender is null)
        {
            await RefreshAsync();
            if (_selectedSender is null) return;
        }
        var selected = _selectedSender;
        await OpenPageAsync(() => new TemplateEditorPage(_store, selected.Id, selected.Address));
    }

    private async void OnFinishClicked(object? sender, EventArgs e)
    {
        MarkFinished();
        UiMotion.Feedback();
        try { await Navigation.PopAsync(); }
        catch (Exception exception)
        {
            UiSafety.Log(exception);
            ShowStatus("بازگشت به صفحهٔ اصلی کامل نشد؛ از دکمهٔ بازگشت گوشی استفاده کنید.");
        }
    }

    private async Task OpenPageAsync(Func<Page> pageFactory)
    {
        if (_openingPage) return;
        _openingPage = true;
        try { await Navigation.PushAsync(pageFactory()); }
        catch (Exception exception)
        {
            UiSafety.Log(exception);
            ShowStatus("صفحه باز نشد. دوباره تلاش کنید.");
        }
        finally { _openingPage = false; }
    }

    private void SetButtonsEnabled(bool enabled)
    {
        OpenConnectionButton.IsEnabled = enabled;
        PickSenderButton.IsEnabled = enabled;
        PermissionButton.IsEnabled = enabled;
        CreateTemplateButton.IsEnabled = enabled;
        FinishButton.IsEnabled = enabled;
    }

    private void ShowStatus(string message)
    {
        StatusLabel.Text = message;
        StatusLabel.IsVisible = true;
    }
}
