using DonyshMobile.Core;

internal static class TemplatePresetRegressionTests
{
    public static async Task RunAsync(string directory)
    {
        await MatchingMessagesOnlyAsync(Path.Combine(directory, "matching-only"));
        var senderId = Guid.NewGuid();
        foreach (var preset in SmsTemplatePresets.All)
        {
            var ready = SmsTemplatePresets.Build(preset, Guid.NewGuid(), senderId, null, preset.SampleMessage);
            Check(ready.Template?.PresetId == preset.Id && ready.Error is null, "Each ready bank preset reads its example");
            Check(ready.Template!.Fields.Count == 6, "Ready presets supply all six fields without manual input");
        }
        var blue = SmsTemplatePresets.All.Single(x => x.Id == "blu-withdrawal");
        const string blueMessage = "بلو\nبرداشت پول\nعلی عزیز، ۲٬۵۰۰٬۰۰۰ ریال از حساب شما پرید.\nموجودی: ۹٬۸۰۰٬۰۰۰ ریال\n۱۹:۴۵\n۱۴۰۵.۰۷.۲۴";
        var filled = SmsTemplatePresets.Build(blue, Guid.NewGuid(), senderId, "برداشت من", blueMessage).Template!;
        Check(filled is not null && SmsTemplateParser.TryParse(filled, blueMessage) is
            { AmountRial: 2_500_000, DateText: "1405.07.24", TimeText: "19:45" }, "Read actual Persian amounts/date/time, not preset examples or balance");
        Check(filled!.Fields.Single(x => x.Key == TemplateFieldKey.Amount).Value == "2٬500٬000",
            "Populate the actual amount field");
        Check(SmsTemplateParser.TryParse(filled, blue.SampleMessage) is { AmountRial: 1_400_000 },
            "Saved ready bank pattern works across customer names and balances");
        Check(SmsTemplatePresets.Build(blue, Guid.NewGuid(), senderId, null, "قیمت: 9000").Template is null,
            "Never guess fields from an incompatible message");
        var mellat = SmsTemplatePresets.All.Single(x => x.Id == "mellat-withdrawal");
        const string mellatMessage = "حساب۱۲۳۴۵۶\nبرداشت۷۵۰٬۰۰۰\nمانده۹٬۰۰۰٬۰۰۰\n۰۵/۰۷/۲۴-۱۸:۳۱";
        Check(SmsTemplateParser.TryParse(SmsTemplatePresets.Build(mellat, Guid.NewGuid(), senderId, null, mellatMessage).Template!, mellatMessage)
            is { AmountRial: 750_000, DateText: "05/07/24", TimeText: "18:31" }, "Read actual Mellat fields");
        var resalat = SmsTemplatePresets.All.Single(x => x.Id == "resalat-withdrawal");
        const string resalatMessage = "10.99999999.1\n-۸۵٬۰۰۰\n۰۷/۲۴_۲۰:۳۱\nمانده: ۱٬۹۰۰٬۰۰۰";
        Check(SmsTemplateParser.TryParse(SmsTemplatePresets.Build(resalat, Guid.NewGuid(), senderId, null, resalatMessage).Template!, resalatMessage)
            is { AmountRial: 85_000, DateText: "07/24", TimeText: "20:31" }, "Read actual Resalat fields");
        Check(SmsTemplatePresets.Build(resalat, Guid.NewGuid(), senderId, null, resalatMessage.Replace("-", "+")).Template is null,
            "Do not interpret a Resalat deposit as a withdrawal");
        Check(SmsKeyValueTemplateBuilder.Build(Guid.NewGuid(), senderId, "دستی", "مبلغ: 1200", []).Template is null,
            "Custom templates still require manual field selection");

        var key = Enumerable.Range(1, 32).Select(x => (byte)x).ToArray();
        var store = new SmsMonitorStore(directory, _ => Task.FromResult(key.ToArray()));
        await store.AddSenderAsync("FirstBank");
        await store.AddSenderAsync("OtherBank");
        var senders = await store.GetSenderSummariesAsync();
        senderId = senders.Single(x => x.Address == "FirstBank").Id;
        var otherId = senders.Single(x => x.Address == "OtherBank").Id;
        var now = DateTimeOffset.UtcNow;
        await store.CaptureAsync("FirstBank", blueMessage, now.AddSeconds(-6));
        await store.CaptureAsync("FirstBank", blue.SampleMessage, now.AddSeconds(-5));
        await store.CaptureAsync("FirstBank", "پیام ناسازگار", now.AddSeconds(-4));
        await store.CaptureAsync("OtherBank", blueMessage, now.AddSeconds(-3));
        await store.CaptureAsync("FirstBank", "واریز: 8000", now.AddSeconds(-2));
        var batch = await store.TakePendingBatchAsync(now);
        var confirmedKey = batch.Single(x => x.Body == blue.SampleMessage).Key;
        var retried = batch.Single(x => x.Body == blueMessage && x.Sender == "FirstBank");
        await store.SetTitleAsync(retried.Id, "خرید آزمایشی");
        await store.RecordSyncFailureAsync(batch, now, "خطای آزمایشی");
        await store.ConfirmBatchAsync(batch, batch.Select(x => new SmsDeliveryReceipt(x.Key,
            x.Key == confirmedKey ? "created" : "rejected")).ToArray(), now);
        var before = await store.GetSyncSummaryAsync();
        var correct = SmsTemplatePresets.Build(blue, Guid.NewGuid(), senderId, "قالب اصلاح‌شده", blueMessage).Template!;
        Check(await store.SaveTemplateAsync(correct) is null, "Save corrected ready template");
        var messages = await store.GetMessagesAsync(senderId);
        var restored = messages.Single(x => x.Id == retried.Id);
        Check(restored.DeliveryStatus == SmsDeliveryStatus.Pending && restored.ConfirmedAtUtc is null && restored.LastDeliveryError is null,
            "Matching rejected messages become pending after template save");
        Check(restored.DeliveryKey == retried.Key && restored.Title == "خرید آزمایشی" && restored.DeliveryAttempts == 1,
            "Keep the same idempotency key, title and attempt history");
        Check(messages.Single(x => x.DeliveryKey == confirmedKey).DeliveryStatus == SmsDeliveryStatus.Sent,
            "Never requeue delivered messages");
        Check(messages.Where(x => x.Body is "پیام ناسازگار" or "واریز: 8000").All(x => x.DeliveryStatus == SmsDeliveryStatus.Rejected)
            && (await store.GetMessagesAsync(otherId)).Single().DeliveryStatus == SmsDeliveryStatus.Rejected,
            "Keep unsupported, deposit and other-sender rejections unchanged");
        Check((await store.GetSyncSummaryAsync()).NextAttemptAtUtc == before.NextAttemptAtUtc
            && (await store.TakePendingBatchAsync(now)).Count == 0, "Preserve one-minute throttling after template edits");
        var retryBatch = await store.TakePendingBatchAsync(now.AddMinutes(2));
        Check(retryBatch.Count == 1 && retryBatch.Single().Parsed is { AmountRial: 2_500_000, Kind: "withdrawal" },
            "Send the updated parsed transaction on the next allowed batch");
        Check((await store.GetTemplatesAsync(senderId)).Single().PresetId == blue.Id, "Keep ready-preset selection after reload");
        correct.Pattern = "بدون بخش مبلغ";
        Check(await store.SaveTemplateAsync(correct) is not null, "Reject invalid template changes before queue mutation");
        Console.WriteLine("Template autofill and safe rejected-message retry checks passed.");
    }

    private static async Task MatchingMessagesOnlyAsync(string directory)
    {
        var key = Enumerable.Range(1, 32).Select(x => (byte)x).ToArray();
        var store = new SmsMonitorStore(directory, _ => Task.FromResult(key.ToArray()));
        Check(await store.AddSenderAsync("Bank-Test") == AddSenderResult.Added, "Add monitored bank");
        var senderId = (await store.GetSenderSummariesAsync()).Single().Id;
        var now = DateTimeOffset.UtcNow;
        const string withdrawal = "حساب123456\nبرداشت750,000\nمانده9,000,000\n05/07/24-18:31";
        const string deposit = "حساب123456\nواریز750,000\nمانده9,000,000\n05/07/24-18:31";
        const string otp = "رمز دوم شما 123456 است";
        Check(await store.CaptureAsync("Bank-Test", deposit, now.AddSeconds(-3)), "Capture a legacy message before setup");
        var legacyId = (await store.GetMessagesAsync(senderId)).Single().Id;
        var preset = SmsTemplatePresets.All.Single(x => x.Id == "mellat-withdrawal");
        var template = SmsTemplatePresets.Build(preset, Guid.NewGuid(), senderId, null, withdrawal).Template!;
        Check(await store.SaveTemplateAsync(template) is null, "Save withdrawal template");
        Check(!await store.CaptureAsync("Bank-Test", deposit, now.AddSeconds(-2)), "Ignore deposit without a matching template");
        Check(!await store.CaptureAsync("Bank-Test", otp, now.AddSeconds(-1)), "Ignore second password");
        Check(await store.CaptureAsync("Bank-Test", withdrawal, now), "Capture matching withdrawal");
        Check((await store.GetMessagesAsync(senderId)).Single().Body == withdrawal, "Show only matching messages");
        var inbox = await store.GetVisibleInboxAsync();
        Check(inbox.Messages.Count == 1 && inbox.Messages[0].Body == withdrawal
            && inbox.Templates.Count == 1 && inbox.Templates[0].SenderId == senderId,
            "The all-messages inbox includes only SMS matching a configured template");
        Check((await store.GetUntitledMessagesAsync()).Single().Body == withdrawal, "Request a title only for matching messages");
        Check(!await store.SetTitleAsync(legacyId, "واریز"), "Do not title a legacy nonmatching message");
        Check((await store.GetSenderSummariesAsync()).Single().MessageCount == 1, "Count only matching messages");
        var batch = await store.TakePendingBatchAsync(now.AddMinutes(1));
        Check(batch.Count == 1 && batch.Single().Body == withdrawal, "Send only matching messages");
        Check((await store.FindUnstoredCandidatesAsync([
            new SmsCaptureCandidate("Bank-Test", deposit, now.AddMinutes(2), true),
            new SmsCaptureCandidate("Bank-Test", otp, now.AddMinutes(3), true)
        ])).Count == 0, "Do not offer deposits or passwords for inbox recovery");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("Template regression failed: " + description);
    }
}
