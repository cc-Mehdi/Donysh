using DonyshMobile.Core;
using System.Net;
using System.Net.Http.Json;

var testDirectory = Path.Combine(Path.GetTempPath(), $"donysh-mobile-tests-{Guid.NewGuid():N}");

try
{
    Assert(SenderAddress.Normalize("۰۹۱۲ ۱۲۳ ۴۵۶۷") == "9121234567", "Persian digits");
    Assert(SenderAddress.Normalize("+98 (912) 123-4567") == "9121234567", "Iran phone variants");
    Assert(SenderAddress.Normalize("+989999987641") == SenderAddress.Normalize("0989999987641"),
        "International and 098 mobile addresses match");
    Assert(SenderAddress.Normalize("00989999987641") == SenderAddress.Normalize("09999987641"),
        "International and domestic mobile addresses match");
    Assert(SenderAddress.Normalize("0981234") == "0981234", "Do not rewrite short sender IDs");
    Assert(SenderAddress.Normalize(" بانک-ملت ") == "بانکملت", "Alphanumeric sender");

    var store = new SmsMonitorStore(testDirectory);
    Assert(await store.AddSenderAsync("۰۹۱۲۱۲۳۴۵۶۷") == AddSenderResult.Added, "Add sender");
    Assert(await store.AddSenderAsync("+98 912 123 4567") == AddSenderResult.Duplicate, "Reject duplicate");
    var variantStore = new SmsMonitorStore(Path.Combine(testDirectory, "variants"));
    Assert(await variantStore.AddSenderAsync("0989999987641") == AddSenderResult.Added, "Add 098 mobile sender");
    Assert(await variantStore.AddSenderAsync("+989999987641") == AddSenderResult.Duplicate, "Reject equivalent mobile sender");
    Assert(await variantStore.CaptureAsync("+989999987641", "پیامک از فرستنده دوم", DateTimeOffset.UtcNow),
        "Match 098 sender with international incoming SMS");
    Assert(!await store.CaptureAsync("30001234", "unmatched", DateTimeOffset.UtcNow), "Ignore unmatched sender");

    var receivedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
    Assert(await store.CaptureAsync("+989121234567", "پیام آزمایشی", receivedAt), "Capture matching SMS");
    Assert(!await store.CaptureAsync("+989121234567", "پیام آزمایشی", receivedAt), "Deduplicate SMS");

    store = new SmsMonitorStore(testDirectory);
    var summary = (await store.GetSenderSummariesAsync()).Single(x => x.Address == "۰۹۱۲۱۲۳۴۵۶۷");
    Assert(summary.MessageCount == 1, "Summary message count");
    var message = AssertSingle(await store.GetMessagesAsync(summary.Id), "Message list");
    Assert(message.Body == "پیام آزمایشی", "Message body");
    Assert(message.DeliveryStatus == SmsDeliveryStatus.Pending, "New SMS starts pending");
    Assert((await store.TakePendingBatchAsync(DateTimeOffset.UtcNow, requireTitle: true)).Count == 0,
        "Automatic batch waits for a title");
    Assert(await store.SetTitleAsync(message.Id, "خرید روزانه"), "Set a pending message title");
    var firstBatch = await store.TakePendingBatchAsync(DateTimeOffset.UtcNow, requireTitle: true);
    Assert(firstBatch.Count == 1 && firstBatch[0].Sender == "+989121234567"
        && firstBatch[0].Title == "خرید روزانه", "Title-gated batch includes title");
    Assert(firstBatch[0].Key == new SmsEnvelope(firstBatch[0].Sender, firstBatch[0].Body,
        firstBatch[0].ReceivedAtUnixMs).Key, "Use the same idempotency key as the API contract");
    await store.ConfirmBatchAsync(firstBatch,
        [new SmsDeliveryReceipt(firstBatch[0].Key, "created")], DateTimeOffset.UtcNow);
    Assert(AssertSingle(await store.GetMessagesAsync(summary.Id), "Confirmed message").DeliveryStatus == SmsDeliveryStatus.Sent,
        "Mark sent only after API receipt");
    Assert((await store.TakePendingBatchAsync(DateTimeOffset.UtcNow.AddMinutes(2), requireTitle: true)).Count == 0,
        "Do not resend confirmed SMS or untitled SMS automatically");

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

    var resalatPreset = SmsTemplatePresets.All.Single(x => x.Id == "resalat-withdrawal");
    var resalatResult = SmsKeyValueTemplateBuilder.Build(Guid.NewGuid(), summary.Id, string.Empty,
        resalatPreset.SampleMessage, resalatPreset.Fields);
    Assert(resalatResult is { Error: null, Template: not null }, "Build Resalat preset");
    Assert(SmsTemplateParser.TryParse(resalatResult.Template!,
        "10.98765432.1\n-75,000\n07/21_18:06\nمانده: 269,842,300") is
        { AmountRial: 75_000, Kind: TransactionKind.Withdrawal, DateText: "07/21", TimeText: "18:06" },
        "Resalat preset parses a new withdrawal SMS");
    Assert(SmsTemplatePresets.All.Count >= 3, "Provide multiple bank presets");

    Assert(await store.CaptureAsync("+989121234567", "پیام قدیمی", DateTimeOffset.UtcNow.AddDays(-8)), "Accept receiver timestamp");
    var retained = await store.GetMessagesAsync(summary.Id);
    Assert(retained.Count == 1 && retained[0].Body == "پیام آزمایشی", "Prune messages older than seven days");
    var offlineBatch = await store.TakePendingBatchAsync(DateTimeOffset.UtcNow.AddMinutes(2));
    Assert(offlineBatch.Count == 1 && offlineBatch[0].Body == "پیام قدیمی", "Keep unsent SMS while offline");
    await store.RecordSyncFailureAsync(offlineBatch, DateTimeOffset.UtcNow.AddMinutes(2), "offline");
    var syncSummary = await store.GetSyncSummaryAsync();
    Assert(syncSummary.Pending == 1 && syncSummary.Sent == 1 && syncSummary.NextAttemptAtUtc > DateTimeOffset.UtcNow,
        "Back off without losing queued SMS");
    Assert(MobileApiClient.NormalizeServer("https://donysh.ir/path") == "https://donysh.ir", "Normalize API origin");
    Assert(MobileApiClient.NormalizeServer("http://donysh.ir") is null, "Require HTTPS API");

    var apiDirectory = Path.Combine(testDirectory, "api-client");
    var apiStore = new SmsMonitorStore(apiDirectory);
    Assert(await apiStore.AddSenderAsync("Bank") == AddSenderResult.Added, "Add API test sender");
    var apiSender = (await apiStore.GetSenderSummariesAsync()).Single();
    Assert(await apiStore.SaveTemplateAsync(new SmsTemplate
    {
        SenderId = apiSender.Id,
        Name = "قالب آزمایشی",
        Pattern = "برداشت: {amount}",
        Unit = MoneyUnit.Rial
    }) is null, "Save API test template");
    Assert(await apiStore.CaptureAsync("Bank", "برداشت: 120,000", DateTimeOffset.UtcNow), "Queue API test SMS");
    var apiPending = AssertSingle(await apiStore.TakePendingBatchAsync(DateTimeOffset.UtcNow), "API pending batch");
    Assert(apiPending.Parsed is { AmountRial: 120_000, Kind: "withdrawal", TemplateName: "قالب آزمایشی" },
        "Send locally parsed template data with SMS");
    var api = new MobileApiClient(() => new HttpClient(new StubHandler(request =>
    {
        var payload = request.Content!.ReadFromJsonAsync<SmsBatch>().GetAwaiter().GetResult();
        Assert(payload?.Messages.Single().Title == "خرید روزانه", "Send selected title to API");
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new SmsBatchResult([new SmsResult(apiPending.Key, "created")]))
        };
    })));
    var apiConnection = new MobileConnection("https://donysh.ir", new string('A', 64), "test");
    Assert(await api.SendNextBatchAsync(apiStore, apiConnection, DateTimeOffset.UtcNow, requireTitle: true)
        == SyncRunResult.NothingToSend, "Automatic sending blocks untitled messages");
    Assert(await apiStore.SetTitleAsync((await apiStore.GetMessagesAsync(apiSender.Id)).Single().Id, "خرید روزانه"),
        "Set API message title");
    Assert(await api.SendNextBatchAsync(apiStore,
        apiConnection, DateTimeOffset.UtcNow, requireTitle: true) == SyncRunResult.Sent,
        "Accept a complete API acknowledgement");
    Assert(AssertSingle(await apiStore.GetMessagesAsync((await apiStore.GetSenderSummariesAsync()).Single().Id),
        "API-confirmed SMS").DeliveryStatus == SmsDeliveryStatus.Sent, "API client persists confirmed status");

    var manualStore = new SmsMonitorStore(Path.Combine(testDirectory, "manual-send"));
    Assert(await manualStore.AddSenderAsync("Bank") == AddSenderResult.Added, "Add manual sender");
    Assert(await manualStore.CaptureAsync("Bank", "برداشت: 80,000", DateTimeOffset.UtcNow), "Queue manual SMS");
    var manualKey = (await manualStore.TakePendingBatchAsync(DateTimeOffset.UtcNow)).Single().Key;
    var manualApi = new MobileApiClient(() => new HttpClient(new StubHandler(request =>
    {
        var payload = request.Content!.ReadFromJsonAsync<SmsBatch>().GetAwaiter().GetResult();
        Assert(payload?.Messages.Single().Title is null, "Manual sending may omit title");
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new SmsBatchResult([new SmsResult(manualKey, "created")]))
        };
    })));
    Assert(await manualApi.SendNextBatchAsync(manualStore, apiConnection, DateTimeOffset.UtcNow,
        manualSend: true, requireTitle: true) == SyncRunResult.Sent, "Manual sending bypasses title requirement");

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

sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(handler(request));
}
