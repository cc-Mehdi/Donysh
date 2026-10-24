using DonyshMobile.Core;
using DonyshMobile.UI;

namespace DonyshMobile;

public sealed class LifeSettingsPage : ContentPage
{
    internal Picker ServicePicker { get; }

    public LifeSettingsPage()
    {
        Title = "تنظیمات زندگی";
        FlowDirection = FlowDirection.RightToLeft;
        LifeTheme.Set(this, BackgroundColorProperty, LifeTheme.Canvas);
        var stack = new VerticalStackLayout { Padding = 20, Spacing = 16 };
        stack.Children.Add(LifePage.Text("تنظیمات دُنیش زندگی", 23, LifeTheme.Ink, true));
        stack.Children.Add(LifePage.Text("ورود پیش‌فرض به اپ", 17, LifeTheme.Ink, true));
        stack.Children.Add(LifePage.Text("اگر انتخاب نکنید، صفحهٔ انتخاب سرویس هر بار نمایش داده می‌شود.", 13, LifeTheme.Muted));
        var picker = new Picker
        {
            Title = "سرویس پیش‌فرض",
            Items = { "هر بار بپرس", "دُنیش مالی", "دُنیش زندگی" }
        };
        LifeTheme.Set(picker, Picker.TextColorProperty, LifeTheme.Ink);
        LifeTheme.Set(picker, Picker.TitleColorProperty, LifeTheme.Muted);
        LifeTheme.Set(picker, BackgroundColorProperty, LifeTheme.Field);
        ServicePicker = picker;
        picker.SelectedIndex = ServicePreference.Default switch
        {
            DonyshService.Finance => 1, DonyshService.Life => 2, _ => 0
        };
        picker.SelectedIndexChanged += (_, _) => ServicePreference.Default = picker.SelectedIndex switch
        {
            1 => DonyshService.Finance, 2 => DonyshService.Life, _ => null
        };
        stack.Children.Add(LifePage.Card(new VerticalStackLayout { Padding = 14, Children = { picker } }));
        stack.Children.Add(LifePage.Button("رفتن به دُنیش مالی", () => App.OpenService(DonyshService.Finance), LifeTheme.Finance, LifeTheme.FinanceText));
        stack.Children.Add(LifePage.Button("انتخاب سرویس", () => App.OpenService(null), LifeTheme.SoftTeal, LifeTheme.AccentText));
        stack.Children.Add(LifePage.Button("قطع اتصال این گوشی از زندگی", async () =>
        {
            if (!await DisplayAlert("قطع اتصال", "اتصال لایف از این گوشی پاک شود؟", "قطع اتصال", "انصراف")) return;
            var connection = await LifeConnectionStore.LoadAsync();
            if (connection is not null)
            {
                try { await new LifeMobileClient().LogoutAsync(connection); }
                catch (Exception) { await DisplayAlert("قطع اتصال کامل نشد", "اتصال اینترنت را بررسی کنید و دوباره تلاش کنید.", "باشه"); return; }
            }
            LifeConnectionStore.Remove();
#if ANDROID
            Platforms.Android.LifeReminderScheduler.Clear();
#endif
            App.OpenService(DonyshService.Life);
        }, LifeTheme.Missed, LifeTheme.MissedText));
        Content = new ScrollView { Content = stack };
    }
}
