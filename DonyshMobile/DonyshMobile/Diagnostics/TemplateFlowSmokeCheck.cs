#if DEBUG
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using DonyshMobile.Core;

namespace DonyshMobile.Diagnostics;

/// <summary>Opt-in regression checks; no real messages, credentials, scheduling or HTTP are used.</summary>
internal static class TemplateFlowSmokeCheck
{
    public static bool IsRequested { get; set; }
    private static int _started;

    public static Window CreateWindow()
    {
        var status = new Label { Text = "در حال بررسی قالب‌های پیامک…", Margin = 24 };
        var root = new ContentPage { Title = "آزمون جداگانهٔ دونیش", Content = status };
        root.Loaded += async (_, _) =>
        {
            if (Interlocked.Exchange(ref _started, 1) != 0) return;
            bool success;
            string? error = null;
            IReadOnlyList<string> checks = [];
            try { checks = await RunAsync(root.Navigation); success = true; }
            catch (Exception exception) { success = false; error = exception.ToString(); }
            await File.WriteAllTextAsync(Path.Combine(FileSystem.AppDataDirectory, "template-check-result.json"),
                JsonSerializer.Serialize(new { success, checks, error }));
            status.Text = success ? "بررسی قالب‌ها و صف پیام‌ها با موفقیت انجام شد. هیچ پیامی به سامانه ارسال نشد."
                : "بررسی کامل نشد؛ نتیجه برای رفع خطا ذخیره شد.";
        };
        return new Window(new NavigationPage(root));
    }

    public static async Task<IReadOnlyList<string>> RunAsync(INavigation navigation)
    {
        var checks = new List<string>();
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            checks.Add(name);
        }
        var directory = Path.Combine(FileSystem.CacheDirectory, "template-check-" + Guid.NewGuid().ToString("N"));
        var key = RandomNumberGenerator.GetBytes(32);
        var store = new SmsMonitorStore(directory, _ => Task.FromResult(key.ToArray()));
        const string address = "QA-Bank";
        const string actual = "بلو\nبرداشت پول\nعلی عزیز، ۲٬۵۰۰٬۰۰۰ ریال از حساب شما پرید.\nموجودی: ۹٬۸۰۰٬۰۰۰ ریال\n۱۹:۴۵\n۱۴۰۵.۰۷.۲۴";
        var preset = SmsTemplatePresets.All[0];
        await store.AddSenderAsync(address);
        var sender = (await store.GetSenderSummariesAsync()).Single();
        var now = DateTimeOffset.UtcNow;
        await store.CaptureAsync(address, preset.SampleMessage, now.AddSeconds(-2));
        await store.CaptureAsync(address, actual, now.AddSeconds(-1));
        var batch = await store.TakePendingBatchAsync(now);
        await store.ConfirmBatchAsync(batch, batch.Select(x => new SmsDeliveryReceipt(x.Key,
            x.Body == actual ? "rejected" : "created")).ToArray(), now);
        var messages = await store.GetMessagesAsync(sender.Id);
        var actualIndex = messages.ToList().FindIndex(x => x.Body == actual);
        var sampleIndex = messages.ToList().FindIndex(x => x.Body == preset.SampleMessage);
        var scheduled = 0;
        var editor = new TemplateEditorPage(store, sender.Id, address, null, () => scheduled++);
        var depth = navigation.NavigationStack.Count;
        await navigation.PushAsync(editor, animated: false);
        await WaitAsync(() => editor.IsLoaded && editor.FindByName<Picker>("SavedMessagePicker").Items.Count == 2);
        editor.FindByName<Picker>("SourcePicker").SelectedIndex = 1;
        editor.FindByName<Picker>("PresetPicker").SelectedIndex = 0;
        Check(editor.Fields.Count == 6 && !editor.FindByName<VerticalStackLayout>("ManualFieldsPanel").IsVisible,
            "Ready preset fills all fields and hides manual mapping");
        editor.FindByName<Picker>("SavedMessagePicker").SelectedIndex = actualIndex;
        editor.FindByName<Button>("ReviewButton").SendClicked();
        await WaitAsync(() => editor.FindByName<Border>("PreviewCard").IsVisible);
        Check(editor.Fields.Single(x => x.KeyLabel == "مبلغ").Value == "2٬500٬000"
            && editor.Fields.Single(x => x.KeyLabel == "تاریخ").Value == "1405.07.24"
            && editor.Fields.Single(x => x.KeyLabel == "ساعت").Value == "19:45",
            "Choosing a saved SMS reads its actual amount, date and time");
        Check(editor.FindByName<Label>("PreviewTitleLabel").Text.StartsWith("✓"), "Actual preset preview is successful");
        editor.FindByName<Picker>("SavedMessagePicker").SelectedIndex = sampleIndex;
        Check(editor.Fields.Single(x => x.KeyLabel == "مبلغ").Value == "1,400,000",
            "Changing the selected SMS replaces stale preset values");
        editor.FindByName<Editor>("MessageEditor").Text = "پیام ناسازگار";
        editor.FindByName<Button>("ReviewButton").SendClicked();
        Check(editor.Fields.Count == 0 && !editor.FindByName<Label>("PreviewTitleLabel").Text.StartsWith("✓"),
            "Incompatible bank messages do not retain a stale green preview or fields");
        editor.FindByName<Picker>("SavedMessagePicker").SelectedIndex = actualIndex;
        editor.FindByName<Button>("EditFieldsButton").SendClicked();
        Check(editor.FindByName<Picker>("SourcePicker").SelectedIndex == 0
            && editor.FindByName<VerticalStackLayout>("ManualFieldsPanel").IsVisible && editor.Fields.Count == 6,
            "Custom mapping remains available with the extracted fields");
        editor.FindByName<Picker>("SourcePicker").SelectedIndex = 1;
        editor.FindByName<Button>("SaveButton").SendClicked();
        await WaitAsync(() => navigation.NavigationStack.Count == depth);
        var saved = (await store.GetTemplatesAsync(sender.Id)).Single();
        var restored = await store.GetMessagesAsync(sender.Id);
        Check(saved.PresetId == preset.Id && scheduled == 1, "Save retains preset identity and requests one scheduled batch");
        Check(restored.Single(x => x.Body == actual).DeliveryStatus == SmsDeliveryStatus.Pending
            && restored.Single(x => x.Body == preset.SampleMessage).DeliveryStatus == SmsDeliveryStatus.Sent,
            "Saving through the actual button retries rejected SMS without replaying delivered SMS");
        var reopened = new TemplateEditorPage(store, sender.Id, address, saved, () => scheduled++);
        await navigation.PushAsync(reopened, animated: false);
        await WaitAsync(() => reopened.IsLoaded);
        Check(reopened.FindByName<Picker>("SourcePicker").SelectedIndex == 1
            && !reopened.FindByName<VerticalStackLayout>("ManualFieldsPanel").IsVisible,
            "Editing a saved ready template keeps the automatic flow");
        await navigation.PopAsync(animated: false);
        return checks;
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(8)) throw new InvalidOperationException("Template UI check timed out.");
            await Task.Delay(50);
        }
    }
}
#endif
