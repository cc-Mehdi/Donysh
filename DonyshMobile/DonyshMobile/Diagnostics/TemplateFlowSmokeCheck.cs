#if DEBUG
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using DonyshMobile.Core;
using Microsoft.Maui;

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
        checks.AddRange(await CheckManualRetryAsync(navigation, directory, key));
        return checks;
    }

    private static async Task<IReadOnlyList<string>> CheckManualRetryAsync(INavigation navigation, string directory, byte[] key)
    {
        var checks = new List<string>();
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            checks.Add(name);
        }
        SmsMonitorStore CreateStore(string name) =>
            new(Path.Combine(directory, name), _ => Task.FromResult(key.ToArray()));

        var store = CreateStore("manual-retry");
        const string address = "QA-Retry";
        const string rejectedBody = "برداشت: ۱۰۰۰ ریال - خرید روزانه";
        const string sentBody = "برداشت: ۲۰۰۰ ریال - پرداخت قبلی";
        await store.AddSenderAsync(address);
        var senderId = (await store.GetSenderSummariesAsync()).Single().Id;
        var now = DateTimeOffset.UtcNow;
        await store.CaptureAsync(address, sentBody, now.AddSeconds(-2));
        await store.CaptureAsync(address, rejectedBody, now.AddSeconds(-1));
        var batch = await store.TakePendingBatchAsync(now);
        await store.ConfirmBatchAsync(batch, batch.Select(message => new SmsDeliveryReceipt(message.Key,
            message.Body == rejectedBody ? "rejected" : "created")).ToArray(), now);
        var original = await store.GetMessagesAsync(senderId);
        var rejected = original.Single(message => message.Body == rejectedBody);
        var delivered = original.Single(message => message.Body == sentBody);
        var scheduled = 0;
        var confirmations = 0;
        var page = new MessagesPage(store, senderId, address, () => scheduled++, (_, _, _, _) =>
        {
            confirmations++;
            return Task.FromResult(false);
        });
        await navigation.PushAsync(page, animated: false);
        await WaitAsync(() => page.IsLoaded && page.Messages.Count == 2
            && page.Messages.Count(message => message.Status == SmsDeliveryStatus.Rejected) == 1);
        Check(page.Messages.Single(message => message.Id == rejected.Id).CanRetry
            && !page.Messages.Single(message => message.Id == delivered.Id).CanRetry,
            "Manual retry is offered for rejected SMS but never confirmed deliveries");

        var filter = page.FindByName<Picker>("StatusPicker");
        filter.SelectedIndex = 2;
        await WaitAsync(() => page.Messages.Count == 1 && page.Messages[0].Id == delivered.Id);
        Check(!page.HasRetryableMessages, "The sent-only filter hides bulk retry");
        filter.SelectedIndex = 0;
        await WaitAsync(() => page.Messages.Count == 2 && page.HasRetryableMessages);
        page.FindByName<Button>("RetryVisibleMessagesButton").SendClicked();
        await WaitAsync(() => confirmations == 1);
        var cancelled = await store.GetMessagesAsync(senderId);
        Check(scheduled == 0
            && cancelled.Single(message => message.Id == rejected.Id).DeliveryStatus == SmsDeliveryStatus.Rejected
            && cancelled.Single(message => message.Id == delivered.Id).DeliveryStatus == SmsDeliveryStatus.Sent,
            "Cancelling bulk retry leaves message states and background scheduling unchanged");

        var retry = FindRetryButton(page, page.Messages.Single(message => message.Id == rejected.Id));
        Check(retry.CommandParameter is Guid retryId && retryId == rejected.Id,
            "The actual message-card retry button is bound to its message identifier");
        retry.SendClicked();
        await WaitAsync(() => scheduled == 1
            && page.Messages.SingleOrDefault(message => message.Id == rejected.Id)?.Status == SmsDeliveryStatus.Pending
            && !string.IsNullOrWhiteSpace(page.FindByName<Label>("RetryStatusLabel").Text));
        var reopenedStore = CreateStore("manual-retry");
        var retried = await reopenedStore.GetMessagesAsync(senderId);
        Check(retried.Single(message => message.Id == rejected.Id).DeliveryStatus == SmsDeliveryStatus.Pending
            && retried.Single(message => message.Id == rejected.Id).DeduplicationKey == rejected.DeduplicationKey
            && retried.Single(message => message.Id == delivered.Id).DeliveryStatus == SmsDeliveryStatus.Sent
            && scheduled == 1,
            "The row retry button persists pending status and schedules once without replaying sent SMS");
        Check(!page.Messages.Single(message => message.Id == rejected.Id).CanRetry
            && page.Messages.Single(message => message.Id == rejected.Id).CanEditTitle,
            "A requeued SMS allows title editing and no longer shows a stale retry action");
        await navigation.PopAsync(animated: false);

        // A separate queue proves that bulk retry uses the current filter, not every
        // message from this sender or another sender on the phone.
        var bulkStore = CreateStore("filtered-bulk-retry");
        const string bulkAddress = "QA-Bulk";
        const string otherAddress = "QA-Other";
        const string matchingBody = "برداشت: ۳۰۰۰ ریال - خرید روزانه";
        const string excludedBody = "برداشت: ۴۰۰۰ ریال - اجاره";
        const string otherBody = "برداشت: ۵۰۰۰ ریال - فرستنده دیگر";
        await bulkStore.AddSenderAsync(bulkAddress);
        await bulkStore.AddSenderAsync(otherAddress);
        var senders = await bulkStore.GetSenderSummariesAsync();
        var bulkSenderId = senders.Single(sender => sender.Address == bulkAddress).Id;
        var otherSenderId = senders.Single(sender => sender.Address == otherAddress).Id;
        now = DateTimeOffset.UtcNow;
        await bulkStore.CaptureAsync(bulkAddress, matchingBody, now.AddSeconds(-1));
        await bulkStore.CaptureAsync(bulkAddress, excludedBody, now.AddSeconds(-2));
        await bulkStore.CaptureAsync(bulkAddress, sentBody, now.AddSeconds(-3));
        await bulkStore.CaptureAsync(otherAddress, otherBody, now.AddSeconds(-4));
        batch = await bulkStore.TakePendingBatchAsync(now);
        await bulkStore.ConfirmBatchAsync(batch, batch.Select(message => new SmsDeliveryReceipt(message.Key,
            message.Body == sentBody ? "created" : "rejected")).ToArray(), now);
        var bulkScheduled = 0;
        var bulkConfirmations = 0;
        var bulkPage = new MessagesPage(bulkStore, bulkSenderId, bulkAddress, () => bulkScheduled++, (_, _, _, _) =>
        {
            bulkConfirmations++;
            return Task.FromResult(true);
        });
        await navigation.PushAsync(bulkPage, animated: false);
        await WaitAsync(() => bulkPage.IsLoaded && bulkPage.Messages.Count == 3);
        bulkPage.FindByName<Picker>("StatusPicker").SelectedIndex = 3;
        await WaitAsync(() => bulkPage.Messages.Count == 2);
        var search = bulkPage.FindByName<SearchBar>("MessageSearchBar");
        search.Text = "خرید روزانه";
        await WaitAsync(() => bulkPage.Messages.Count == 1 && bulkPage.Messages[0].Body == matchingBody);
        bulkPage.FindByName<Button>("RetryVisibleMessagesButton").SendClicked();
        await WaitAsync(() => bulkConfirmations == 1 && bulkScheduled == 1 && bulkPage.Messages.Count == 0
            && !string.IsNullOrWhiteSpace(bulkPage.FindByName<Label>("RetryStatusLabel").Text));
        var bulkMessages = await bulkStore.GetMessagesAsync(bulkSenderId);
        Check(bulkMessages.Single(message => message.Body == matchingBody).DeliveryStatus == SmsDeliveryStatus.Pending
            && bulkMessages.Single(message => message.Body == excludedBody).DeliveryStatus == SmsDeliveryStatus.Rejected
            && bulkMessages.Single(message => message.Body == sentBody).DeliveryStatus == SmsDeliveryStatus.Sent
            && (await bulkStore.GetMessagesAsync(otherSenderId)).Single().DeliveryStatus == SmsDeliveryStatus.Rejected
            && !bulkPage.HasRetryableMessages && bulkScheduled == 1,
            "Confirmed bulk retry affects only retryable messages in the current status and search filter");
        await navigation.PopAsync(animated: false);
        return checks;
    }

    private static Button FindRetryButton(MessagesPage page, MessageItem message)
    {
        var realized = page.GetVisualTreeDescendants().OfType<Button>().FirstOrDefault(button =>
            button.AutomationId == "RetryMessageButton" && button.CommandParameter is Guid id && id == message.Id);
        if (realized is not null) return realized;

        // Some platforms do not expose virtualized CollectionView cards in the MAUI
        // visual tree. Materialize the real XAML template, retaining its actual event
        // binding and command parameter, rather than invoking a private handler.
        var list = page.GetVisualTreeDescendants().OfType<CollectionView>().Single();
        var template = list.ItemTemplate ?? throw new InvalidOperationException("The SMS list has no card template.");
        if (template.CreateContent() is not View card)
            throw new InvalidOperationException("The SMS card template did not create a view.");
        card.BindingContext = message;
        return card.FindByName<Button>("RetryMessageButton")
            ?? throw new InvalidOperationException("The message-card retry button was not found.");
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
