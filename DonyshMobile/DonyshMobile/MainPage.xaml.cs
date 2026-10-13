using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using DonyshMobile.Core;
using Microsoft.Maui.ApplicationModel;

#if ANDROID
using DonyshMobile.Platforms.Android;
#endif

namespace DonyshMobile;

public partial class MainPage : ContentPage
{
    private static int _pendingTitlesRequested;
    public static void RequestPendingTitles() => Interlocked.Exchange(ref _pendingTitlesRequested, 1);
    public static bool ConsumePendingTitlesRequest() => Interlocked.Exchange(ref _pendingTitlesRequested, 0) == 1;
    private readonly SmsMonitorStore _store = new(FileSystem.AppDataDirectory);
    private bool _subscribed;
    private string _permissionStatusText = "در حال بررسی…";
    private string _permissionButtonText = "فعال‌سازی";

    public ObservableCollection<SenderItem> Senders { get; } = [];
    public string PermissionStatusText { get => _permissionStatusText; private set => SetField(ref _permissionStatusText, value); }
    public string PermissionButtonText { get => _permissionButtonText; private set => SetField(ref _permissionButtonText, value); }
    public string SenderCountText => $"{Senders.Count} فرستنده";

    public MainPage()
    {
        InitializeComponent();
        BindingContext = this;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_subscribed)
        {
            SmsReceivedNotifier.MessageStored += OnMessageStored;
            _subscribed = true;
        }
        await RefreshAsync();
        await RefreshPermissionAsync();
        SyncPlatform.Schedule();
        if (ConsumePendingTitlesRequest()) await Navigation.PushAsync(new PendingTitlesPage(_store, promptFirstMessage: true));
#if ANDROID
        if (SmsSyncSettings.RequireTitle && OperatingSystem.IsAndroidVersionAtLeast(33)
            && !Preferences.Default.Get("title-notification-permission-asked", false))
        {
            Preferences.Default.Set("title-notification-permission-asked", true);
            try { await Permissions.RequestAsync<PostNotificationsPermission>(); }
            catch (PermissionException) { /* Title entry remains available inside the app. */ }
        }
        await TitleReminderNotifier.RefreshAsync(global::Android.App.Application.Context);
#endif
    }

    protected override void OnDisappearing()
    {
        if (_subscribed)
        {
            SmsReceivedNotifier.MessageStored -= OnMessageStored;
            _subscribed = false;
        }
        base.OnDisappearing();
    }

    private async void OnPermissionClicked(object? sender, EventArgs e)
    {
#if ANDROID
        try
        {
            var status = await Permissions.CheckStatusAsync<ReceiveSmsPermission>();
            if (status == PermissionStatus.Granted)
            {
                AppInfo.ShowSettingsUI();
            }
            else
            {
                status = await Permissions.RequestAsync<ReceiveSmsPermission>();
            }

            if (status != PermissionStatus.Granted
                && await DisplayAlert("دسترسی داده نشد",
                    "برای دریافت پیامک‌های جدید باید دسترسی SMS را از تنظیمات برنامه فعال کنید.",
                    "بازکردن تنظیمات", "بعداً"))
            {
                AppInfo.ShowSettingsUI();
            }
        }
        catch (PermissionException)
        {
            await DisplayAlert("خطای دسترسی", "مجوز دریافت پیامک در تنظیمات اندروید برنامه ثبت نشده است.", "باشه");
        }
#else
        await DisplayAlert("فقط اندروید", "دریافت پیامک در این مرحله فقط روی دستگاه اندرویدی فعال است.", "باشه");
#endif
        await RefreshPermissionAsync();
    }

    private async void OnAddSenderClicked(object? sender, EventArgs e)
    {
        var result = await _store.AddSenderAsync(SenderEntry.Text ?? string.Empty);
        switch (result)
        {
            case AddSenderResult.Added:
                SenderEntry.Text = string.Empty;
                await RefreshAsync();
                break;
            case AddSenderResult.Duplicate:
                await DisplayAlert("تکراری", "این فرستنده قبلاً در فهرست مانیتور قرار دارد.", "باشه");
                break;
            case AddSenderResult.LimitReached:
                await DisplayAlert("محدودیت فهرست", "در این نسخه حداکثر ۵۰ فرستنده قابل مانیتور است.", "باشه");
                break;
            default:
                await DisplayAlert("فرستنده نامعتبر", "شماره یا نام فرستنده را به‌درستی وارد کنید.", "باشه");
                break;
        }
    }

    private async void OnPickSenderClicked(object? sender, EventArgs e)
    {
#if ANDROID
        try
        {
            var permission = await Permissions.RequestAsync<ReadSmsPermission>();
            if (permission != PermissionStatus.Granted)
            {
                await DisplayAlert("اجازه لازم است", "برای نمایش فرستنده‌های پیامک، اجازه خواندن پیامک‌ها را بدهید.", "باشه");
                return;
            }
            var addresses = SmsConversationCatalog.RecentSenders(global::Android.App.Application.Context);
            await Navigation.PushAsync(new SenderPickerPage(addresses, async address =>
            {
                var result = await _store.AddSenderAsync(address);
                if (result == AddSenderResult.Added) await RefreshAsync();
                else if (result == AddSenderResult.Duplicate)
                    await DisplayAlert("تکراری", "این فرستنده قبلاً ثبت شده است.", "باشه");
                else
                    await DisplayAlert("ثبت نشد", "افزودن فرستنده ممکن نشد؛ فهرست را بررسی کنید.", "باشه");
            }));
        }
        catch (Exception)
        {
            await DisplayAlert("انتخاب فرستنده", "خواندن فهرست پیامک‌ها ممکن نشد. شماره را دستی وارد کنید.", "باشه");
        }
#else
        await DisplayAlert("فقط اندروید", "انتخاب از پیامک‌ها فقط در اندروید در دسترس است.", "باشه");
#endif
    }

    private async void OnRemoveSenderClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: Guid senderId }) return;
        var item = Senders.SingleOrDefault(x => x.Id == senderId);
        if (item is null || !await DisplayAlert("حذف فرستنده",
                $"«{item.Address}» و تمام پیامک‌های ذخیره‌شده آن از این گوشی حذف شوند؟",
                "حذف", "انصراف")) return;

        await _store.RemoveSenderAsync(senderId);
        await RefreshAsync();
    }

    private async void OnSenderSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is CollectionView collectionView) collectionView.SelectedItem = null;
        if (e.CurrentSelection.FirstOrDefault() is SenderItem item)
        {
            await Navigation.PushAsync(new MessagesPage(_store, item.Id, item.Address));
        }
    }

    private async void OnConnectionClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(new ConnectionPage(_store));

    private async void OnPendingTitlesClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(new PendingTitlesPage(_store));

    private void OnMessageStored(object? sender, EventArgs e) =>
        Dispatcher.Dispatch(async () => await RefreshAsync());

    private async Task RefreshAsync()
    {
        try
        {
            var summaries = await _store.GetSenderSummariesAsync();
            Senders.Clear();
            foreach (var summary in summaries)
            {
                Senders.Add(new SenderItem(summary.Id, summary.Address,
                    summary.MessageCount == 0
                        ? "هنوز پیام جدیدی ثبت نشده"
                        : $"{summary.MessageCount} پیام · آخرین: {FormatLocal(summary.LastMessageAtUtc!.Value)}"));
            }
            OnPropertyChanged(nameof(SenderCountText));
        }
        catch (Exception exception)
        {
            await DisplayAlert("خطای ذخیره‌سازی", exception.Message, "باشه");
        }
    }

    private async Task RefreshPermissionAsync()
    {
#if ANDROID
        var status = await Permissions.CheckStatusAsync<ReceiveSmsPermission>();
        PermissionStatusText = status == PermissionStatus.Granted
            ? "فعال است؛ پیامک‌های جدید فرستنده‌های فهرست ذخیره می‌شوند."
            : "غیرفعال است؛ برای شروع مانیتور، دسترسی را فعال کنید.";
        PermissionButtonText = status == PermissionStatus.Granted ? "تنظیمات" : "فعال‌سازی";
#else
        PermissionStatusText = "دریافت پیامک فقط در نسخه اندروید در دسترس است.";
        PermissionButtonText = "راهنما";
        await Task.CompletedTask;
#endif
    }

    private static string FormatLocal(DateTimeOffset value)
    {
        var local = value.ToLocalTime();
        var calendar = new PersianCalendar();
        return $"{calendar.GetYear(local.DateTime):0000}/{calendar.GetMonth(local.DateTime):00}/{calendar.GetDayOfMonth(local.DateTime):00} {local:HH:mm}";
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(propertyName);
    }

}

public sealed record SenderItem(Guid Id, string Address, string Details);
