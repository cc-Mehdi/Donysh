using DonyshMobile.Core;

namespace DonyshMobile;

public sealed class LifeActualPage : ContentPage
{
    private readonly LifeMobileClient _api = new();
    public LifeActualPage(IReadOnlyList<LifeActivity> activities, Func<Task> saved)
    {
        Title = "ثبت فعالیت";
        FlowDirection = FlowDirection.RightToLeft;
        BackgroundColor = Color.FromArgb("#F4F8F7");
        var stack = new VerticalStackLayout { Padding = 20, Spacing = 12 };
        stack.Children.Add(LifePage.Text("چه کاری انجام دادی؟", 22, Color.FromArgb("#102A3A"), true));
        var picker = new Picker { Title = "فعالیت" };
        foreach (var activity in activities) picker.Items.Add(activity.Title);
        if (activities.Count > 0) picker.SelectedIndex = 0;
        stack.Children.Add(picker);
        var minutes = new Entry { Placeholder = "مدت به دقیقه، مثلاً ۳۰", Keyboard = Keyboard.Numeric, Text = "30", MaxLength = 4 };
        stack.Children.Add(minutes);
        var note = new Editor { Placeholder = "یادداشت کوتاه (اختیاری)", AutoSize = EditorAutoSizeOption.TextChanges, MaxLength = 160 };
        stack.Children.Add(note);
        stack.Children.Add(LifePage.Button("ثبت فعالیت امروز", async () =>
        {
            if (picker.SelectedIndex < 0 || !int.TryParse(minutes.Text, out var duration) || duration is < 1 or > 1440)
            {
                await DisplayAlert("اطلاعات کامل نیست", "فعالیت و مدت معتبر را انتخاب کنید.", "باشه");
                return;
            }
            var connection = await LifeConnectionStore.LoadAsync();
            if (connection is null) { await DisplayAlert("اتصال ندارید", "دوباره به لایف متصل شوید.", "باشه"); return; }
            try
            {
                var today = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TehranZone()).ToString("yyyy-MM-dd");
                await _api.AddActualAsync(connection, activities[picker.SelectedIndex].Id, today, null, duration, note.Text);
                await Navigation.PopAsync();
                await saved();
            }
            catch (Exception) { await DisplayAlert("ثبت نشد", "فعالیت ذخیره نشد. دوباره تلاش کنید.", "باشه"); }
        }, Color.FromArgb("#087E78")));
        Content = new ScrollView { Content = stack };
    }
    private static TimeZoneInfo TehranZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran"); }
        catch { return TimeZoneInfo.CreateCustomTimeZone("Asia/Tehran", TimeSpan.FromHours(3.5), "Tehran", "Tehran"); }
    }
}
