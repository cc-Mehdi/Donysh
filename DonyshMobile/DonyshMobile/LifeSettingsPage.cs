using DonyshMobile.Core;

namespace DonyshMobile;

public sealed class LifeSettingsPage : ContentPage
{
    internal Picker ServicePicker { get; }

    public LifeSettingsPage()
    {
        Title = "تنظیمات زندگی";
        FlowDirection = FlowDirection.RightToLeft;
        BackgroundColor = Color.FromArgb("#F4F8F7");
        var stack = new VerticalStackLayout { Padding = 20, Spacing = 16 };
        stack.Children.Add(LifePage.Text("تنظیمات دُنیش زندگی", 23, Color.FromArgb("#102A3A"), true));
        stack.Children.Add(LifePage.Text("ورود پیش‌فرض به اپ", 17, Color.FromArgb("#102A3A"), true));
        stack.Children.Add(LifePage.Text("اگر انتخاب نکنید، صفحهٔ انتخاب سرویس هر بار نمایش داده می‌شود.", 13, Color.FromArgb("#527478")));
        var picker = new Picker
        {
            Title = "سرویس پیش‌فرض", TitleColor = Color.FromArgb("#527478"),
            TextColor = Color.FromArgb("#102A3A"), BackgroundColor = Colors.White,
            Items = { "هر بار بپرس", "دُنیش مالی", "دُنیش زندگی" }
        };
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
        stack.Children.Add(LifePage.Button("رفتن به دُنیش مالی", () => App.OpenService(DonyshService.Finance), Color.FromArgb("#473592")));
        stack.Children.Add(LifePage.Button("انتخاب سرویس", () => App.OpenService(null), Color.FromArgb("#E4F4F0"), Color.FromArgb("#087E78")));
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
        }, Color.FromArgb("#FDEBE8"), Color.FromArgb("#9A4136")));
        Content = new ScrollView { Content = stack };
    }
}
