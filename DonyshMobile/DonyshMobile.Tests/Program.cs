using DonyshMobile.Core;

var testDirectory = Path.Combine(Path.GetTempPath(), $"donysh-mobile-tests-{Guid.NewGuid():N}");

try
{
    Assert(SenderAddress.Normalize("۰۹۱۲ ۱۲۳ ۴۵۶۷") == "9121234567", "Persian digits");
    Assert(SenderAddress.Normalize("+98 (912) 123-4567") == "9121234567", "Iran phone variants");
    Assert(SenderAddress.Normalize(" بانک-ملت ") == "بانکملت", "Alphanumeric sender");

    var store = new SmsMonitorStore(testDirectory);
    Assert(await store.AddSenderAsync("۰۹۱۲۱۲۳۴۵۶۷") == AddSenderResult.Added, "Add sender");
    Assert(await store.AddSenderAsync("+98 912 123 4567") == AddSenderResult.Duplicate, "Reject duplicate");
    Assert(!await store.CaptureAsync("30001234", "unmatched", DateTimeOffset.UtcNow), "Ignore unmatched sender");

    var receivedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
    Assert(await store.CaptureAsync("+989121234567", "پیام آزمایشی", receivedAt), "Capture matching SMS");
    Assert(!await store.CaptureAsync("+989121234567", "پیام آزمایشی", receivedAt), "Deduplicate SMS");

    store = new SmsMonitorStore(testDirectory);
    var summary = AssertSingle(await store.GetSenderSummariesAsync(), "Persist sender after store reload");
    Assert(summary.MessageCount == 1, "Summary message count");
    var message = AssertSingle(await store.GetMessagesAsync(summary.Id), "Message list");
    Assert(message.Body == "پیام آزمایشی", "Message body");

    var mellat = new SmsTemplate
    {
        SenderId = summary.Id, Name = "ملت برداشت", Unit = MoneyUnit.Rial,
        Pattern = "حساب{ignore}\n{type}{amount}\nمانده{ignore}\n{date}-{time}",
        Phrases = [new TransactionPhrase { Text = "برداشت", Kind = TransactionKind.Withdrawal }]
    };
    Assert(await store.SaveTemplateAsync(mellat) is null, "Save template");
    store = new SmsMonitorStore(testDirectory);
    Assert((await store.GetTemplatesAsync(summary.Id)).Count == 1, "Persist template");
    var mellatResult = SmsTemplateParser.TryParse(mellat, "حساب5555555555\nبرداشت۵۰,۰۰۰,۰۰۰\nمانده111,111,111\n05/06/15-12:14");
    Assert(mellatResult is { AmountRial: 50_000_000, Kind: TransactionKind.Withdrawal, DateText: "05/06/15", TimeText: "12:14" }, "Mellat SMS extraction");

    var blu = new SmsTemplate
    {
        SenderId = summary.Id, Name = "بلو", Unit = MoneyUnit.Rial,
        Pattern = "بلو\n{type}\n{ignore}، {amount} ریال از حساب شما پرید.\nموجودی: {ignore} ریال\n{time}\n{date}",
        Phrases = [new TransactionPhrase { Text = "برداشت پول", Kind = TransactionKind.Withdrawal }]
    };
    var bluResult = SmsTemplateParser.TryParse(blu, "بلو\nبرداشت پول\nمهدی عزیز، 10,000,000 ریال از حساب شما پرید.\nموجودی: 11,111,111 ریال\n12:18\n1405.06.15");
    Assert(bluResult is { AmountRial: 10_000_000, Kind: TransactionKind.Withdrawal, DateText: "1405.06.15" }, "Blu SMS extraction");
    Assert(SmsTemplateParser.TryParse(blu, "بلو\nواریز پول\nمهدی عزیز، 10,000,000 ریال از حساب شما پرید.\nموجودی: 11,111,111 ریال\n12:18\n1405.06.15") is null, "Reject unmapped transaction type");
    Assert(await store.SaveTemplateAsync(blu) is null, "Multiple templates per sender");
    Assert((await store.GetTemplatesAsync(summary.Id)).Count == 2, "Load multiple templates");
    blu.Name = "بلو - اصلاح‌شده";
    Assert(await store.SaveTemplateAsync(blu) is null, "Update template");
    Assert((await store.GetTemplatesAsync(summary.Id)).Count == 2, "Update does not duplicate template");
    Assert(await store.DeleteTemplateAsync(summary.Id, blu.Id), "Delete template");
    Assert((await store.GetTemplatesAsync(summary.Id)).Count == 1, "Only selected template deleted");

    var toman = new SmsTemplate { SenderId = summary.Id, Name = "تومان", Pattern = "برداشت: {amount} تومان", Unit = MoneyUnit.Toman };
    Assert(SmsTemplateParser.TryParse(toman, "برداشت: ۲۵۰,۰۰۰ تومان") is { AmountRial: 2_500_000, Kind: TransactionKind.Withdrawal }, "Toman to rial conversion");

    var bluPreset = SmsTemplatePresets.All.Single(x => x.Id == "blu-withdrawal");
    var generatedResult = SmsKeyValueTemplateBuilder.Build(Guid.NewGuid(), summary.Id, string.Empty,
        bluPreset.SampleMessage, bluPreset.Fields);
    Assert(generatedResult is { Error: null, Template: not null }, "Build key/value template");
    Assert(generatedResult.Template!.Name == "بلو - برداشت", "Generate template name from key/value fields");
    Assert(SmsTemplateParser.TryParse(generatedResult.Template,
        "بلو\nبرداشت پول\nمهدی عزیز، 2,900,000ریال از حساب شما پرید.\nموجودی: 5,000,000 ریال\n۱۹:۰۵\n۱۴۰۵.۰۷.۲۰") is { AmountRial: 2_900_000, Kind: TransactionKind.Withdrawal }, "Key/value template parses next SMS");
    Assert(SmsKeyValueTemplateBuilder.Build(Guid.NewGuid(), summary.Id, "", bluPreset.SampleMessage,
        bluPreset.Fields.Where(x => x.Key != TemplateFieldKey.Amount).ToList()).Error is not null, "Amount key is required");
    Assert(SmsTemplatePresets.All.Count >= 2, "Provide multiple bank presets");

    Assert(await store.CaptureAsync("+989121234567", "پیام قدیمی", DateTimeOffset.UtcNow.AddDays(-8)), "Accept receiver timestamp");
    var retained = await store.GetMessagesAsync(summary.Id);
    Assert(retained.Count == 1 && retained[0].Body == "پیام آزمایشی", "Prune messages older than seven days");

    Assert(await store.RemoveSenderAsync(summary.Id), "Remove sender");
    Assert((await store.GetSenderSummariesAsync()).Count == 0, "Remove sender messages");
    Assert((await store.GetTemplatesAsync(summary.Id)).Count == 0, "Remove sender templates");

    Console.WriteLine("All DonyshMobile core tests passed.");
    return 0;
}
finally
{
    if (Directory.Exists(testDirectory))
    {
        Directory.Delete(testDirectory, true);
    }
}

static T AssertSingle<T>(IReadOnlyList<T> values, string name)
{
    Assert(values.Count == 1, name);
    return values[0];
}

static void Assert(bool condition, string name)
{
    if (!condition)
    {
        throw new InvalidOperationException($"Test failed: {name}");
    }
}
