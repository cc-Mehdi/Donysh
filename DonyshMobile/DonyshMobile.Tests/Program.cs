using DonyshMobile.Core;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Cryptography;
using System.Xml.Linq;

var testDirectory = Path.Combine(Path.GetTempPath(), $"donysh-mobile-tests-{Guid.NewGuid():N}");

try
{
    SetupFlowRegressionTests.Run();
    await ManualRequeueRegressionTests.RunAsync(Path.Combine(testDirectory, "manual-requeue"));
    await TemplatePresetRegressionTests.RunAsync(Path.Combine(testDirectory, "template-retry"));
    using var manifestStream = typeof(Program).Assembly.GetManifestResourceStream("DonyshMobile.Tests.AndroidManifest.xml")
        ?? throw new InvalidOperationException("The Android manifest must be embedded for permission regression checks.");
    var manifest = XDocument.Load(manifestStream);
    XNamespace android = "http://schemas.android.com/apk/res/android";
    var permissions = manifest.Root!.Elements("uses-permission")
        .Select(element => (string?)element.Attribute(android + "name")).ToHashSet(StringComparer.Ordinal);
    Assert(permissions.Contains("android.permission.INTERNET"), "Android manifest permits API network requests");
    Assert(permissions.Contains("android.permission.ACCESS_NETWORK_STATE"), "Android manifest permits connectivity checks and network-constrained jobs");
    Assert(permissions.Contains("android.permission.RECEIVE_SMS"), "Android manifest permits live SMS reception");
    Assert(permissions.Contains("android.permission.READ_SMS"), "Android manifest permits opted-in missed-SMS recovery");

    Assert(SenderAddress.Normalize("۰۹۱۲ ۱۲۳ ۴۵۶۷") == "9121234567", "Persian digits");
    Assert(SenderAddress.Normalize("+98 (912) 123-4567") == "9121234567", "Iran phone variants");
    Assert(SenderAddress.Normalize("+989999987641") == SenderAddress.Normalize("0989999987641"),
        "International and 098 mobile addresses match");
    Assert(SenderAddress.Normalize("00989999987641") == SenderAddress.Normalize("09999987641"),
        "International and domestic mobile addresses match");
    Assert(SenderAddress.Normalize("0981234") == "0981234", "Do not rewrite short sender IDs");
    Assert(SenderAddress.Normalize(" بانک-ملت ") == "بانکملت", "Alphanumeric sender");

    var store = CreateStore(testDirectory);
    Assert(await store.AddSenderAsync("۰۹۱۲۱۲۳۴۵۶۷") == AddSenderResult.Added, "Add sender");
    Assert(await store.AddSenderAsync("+98 912 123 4567") == AddSenderResult.Duplicate, "Reject duplicate");
    var variantStore = CreateStore(Path.Combine(testDirectory, "variants"));
    Assert(await variantStore.AddSenderAsync("0989999987641") == AddSenderResult.Added, "Add 098 mobile sender");
    Assert(await variantStore.AddSenderAsync("+989999987641") == AddSenderResult.Duplicate, "Reject equivalent mobile sender");
    Assert(await variantStore.CaptureAsync("+989999987641", "پیامک از فرستنده دوم", DateTimeOffset.UtcNow),
        "Match 098 sender with international incoming SMS");
    Assert(!await store.CaptureAsync("30001234", "unmatched", DateTimeOffset.UtcNow), "Ignore unmatched sender");

    var receivedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
    Assert(await store.CaptureAsync("+989121234567", "پیام آزمایشی", receivedAt), "Capture matching SMS");
    Assert(!await store.CaptureAsync("+989121234567", "پیام آزمایشی", receivedAt), "Deduplicate SMS");

    var recoveryStore = CreateStore(Path.Combine(testDirectory, "recovery"));
    Assert(await recoveryStore.AddSenderAsync("Bank-Recovery") == AddSenderResult.Added, "Add recovery sender");
    var recoverySender = (await recoveryStore.GetSenderSummariesAsync()).Single();
    Assert(await recoveryStore.CaptureAsync("Bank-Recovery", "برداشت: 100,000", receivedAt),
        "Capture live SMS before recovery scan");
    var recovered = await recoveryStore.CaptureBatchAsync([
        new SmsCaptureCandidate("bank recovery", "برداشت: 100,000", receivedAt.AddSeconds(30), true),
        new SmsCaptureCandidate("Bank-Recovery", "برداشت: 200,000", receivedAt.AddMinutes(1), true),
        new SmsCaptureCandidate("Unknown", "برداشت: 300,000", receivedAt.AddMinutes(2), true)
    ]);
    Assert(recovered == 1, "Recovery scan ignores provider duplicates and unmonitored senders");
    var recoveryPreview = await recoveryStore.FindUnstoredCandidatesAsync([
        new SmsCaptureCandidate("bank recovery", "برداشت:\n100,000", receivedAt.AddSeconds(45), true),
        new SmsCaptureCandidate("Bank-Recovery", "برداشت: 400,000", receivedAt.AddMinutes(3), true),
        new SmsCaptureCandidate("Unknown", "برداشت: 500,000", receivedAt.AddMinutes(4), true)
    ]);
    Assert(recoveryPreview.Count == 1 && recoveryPreview[0].Body == "برداشت: 400,000",
        "Missed-message preview shows only unstored messages from monitored senders");
    Assert((await recoveryStore.GetMessagesAsync(recoverySender.Id)).All(x => x.Body != "برداشت: 400,000"),
        "Preview does not add a message to the delivery queue before confirmation");
    Assert(await recoveryStore.CaptureBatchAsync(recoveryPreview) == 1,
        "Confirmed preview messages enter the delivery queue once");

    var historyStore = CreateStore(Path.Combine(testDirectory, "delivery-history"));
    Assert(await historyStore.AddSenderAsync("Bank-History") == AddSenderResult.Added, "Add delivery-history sender");
    var oldDeliveredAt = DateTimeOffset.UtcNow.AddDays(-8);
    Assert(await historyStore.CaptureAsync("Bank-History", "برداشت 700,000 ریال", oldDeliveredAt),
        "Capture an old SMS before delivery");
    var oldBatch = await historyStore.TakePendingBatchAsync(DateTimeOffset.UtcNow);
    await historyStore.ConfirmBatchAsync(oldBatch,
        [new SmsDeliveryReceipt(oldBatch.Single().Key, "created")], DateTimeOffset.UtcNow);
    var historySender = (await historyStore.GetSenderSummariesAsync()).Single();
    Assert((await historyStore.GetMessagesAsync(historySender.Id)).Count == 0,
        "Old delivered SMS leaves the visible list after retention");
    historyStore = CreateStore(Path.Combine(testDirectory, "delivery-history"));
    Assert((await historyStore.FindUnstoredCandidatesAsync([
        new SmsCaptureCandidate("bank history", "برداشت 700,000 ریال", oldDeliveredAt.AddSeconds(20), true)
    ])).Count == 0, "Delivered fingerprint prevents a pruned SMS from reappearing in recovery");
    var recoveredMessage = (await recoveryStore.GetMessagesAsync(recoverySender.Id))
        .Single(item => item.Body == "برداشت: 200,000");
    Assert(recoveredMessage.RecoveredFromInbox, "Recovered SMS keeps its local source marker");
    Assert(await recoveryStore.ExcludeMessagesAsync(recoverySender.Id, [recoveredMessage.Id]) == 1,
        "User can exclude a recovered SMS from delivery");
    Assert((await recoveryStore.TakePendingBatchAsync(DateTimeOffset.UtcNow.AddMinutes(1)))
        .All(item => item.Id != recoveredMessage.Id), "Excluded SMS cannot enter an API batch");
    Assert(await recoveryStore.RequeueMessagesAsync(recoverySender.Id, [recoveredMessage.Id]) == 1,
        "User can restore an excluded SMS to the delivery queue");

    store = CreateStore(testDirectory);
    var summary = (await store.GetSenderSummariesAsync()).Single(x => x.Address == "۰۹۱۲۱۲۳۴۵۶۷");
    Assert(summary.MessageCount == 1, "Summary message count");
    var message = AssertSingle(await store.GetMessagesAsync(summary.Id), "Message list");
    Assert(message.Body == "پیام آزمایشی", "Message body");
    Assert(message.DeliveryStatus == SmsDeliveryStatus.Pending, "New SMS starts pending");
    Assert((await store.TakePendingBatchAsync(DateTimeOffset.UtcNow, requireTitle: true)).Count == 0,
        "Automatic batch waits for a title");
    Assert(await store.SetTitleAsync(message.Id, "هزینهٔ آزمایشی ویژه"), "Set a pending message title");
    var titleSuggestions = await store.GetTitleSuggestionsAsync(summary.Id, message.Body);
    Assert(titleSuggestions.Count > 0 && titleSuggestions[0] == "هزینهٔ آزمایشی ویژه",
        "Prefer the user's previous title for the same sender");
    var firstBatch = await store.TakePendingBatchAsync(DateTimeOffset.UtcNow, requireTitle: true);
    Assert(firstBatch.Count == 1 && firstBatch[0].Sender == "+989121234567"
        && firstBatch[0].Title == "هزینهٔ آزمایشی ویژه", "Title-gated batch includes title");
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
    store = CreateStore(testDirectory);
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

    var loginWorkspaceId = Guid.NewGuid();
    var loginApi = new MobileApiClient(() => new HttpClient(new StubHandler(request =>
    {
        Assert(request.RequestUri?.AbsolutePath == "/api/mobile/login", "Use mobile login endpoint");
        var payload = request.Content!.ReadFromJsonAsync<MobileLoginRequest>().GetAwaiter().GetResult();
        Assert(payload is { Email: "user@example.com", Password: "Secret123", DeviceName: "گوشی من" }
               && payload.InstallationId == new string('1', 64), "Send website credentials only to login endpoint");
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new MobileSessionResult(new string('A', 64), "کاربر",
                loginWorkspaceId, "شخصی", "سایر", [new(loginWorkspaceId, "شخصی", "personal")]))
        };
    })));
    var loginResult = await loginApi.LoginAsync("https://donysh.ir", " user@example.com ", "Secret123",
        new string('1', 64), "گوشی من");
    Assert(loginResult?.WorkspaceId == loginWorkspaceId, "Accept authenticated mobile session");

    var workspaceApi = new MobileApiClient(() => new HttpClient(new StubHandler(request =>
    {
        Assert(request.Headers.Authorization?.Scheme == "Bearer"
               && request.Headers.Authorization.Parameter == new string('A', 64), "Authorize workspace request with device token");
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new MobileWorkspaceList(loginWorkspaceId, "شخصی", "سایر",
                [new(loginWorkspaceId, "شخصی", "personal")]))
        };
    })));
    var workspaceResult = await workspaceApi.GetWorkspacesAsync(
        new MobileConnection("https://donysh.ir", new string('A', 64), "شخصی / سایر", loginWorkspaceId));
    Assert(workspaceResult.Workspaces.Single().Id == loginWorkspaceId, "Load accessible workspaces");

    var apiDirectory = Path.Combine(testDirectory, "api-client");
    var apiStore = CreateStore(apiDirectory);
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

    var manualStore = CreateStore(Path.Combine(testDirectory, "manual-send"));
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

    var titleRuleStore = CreateStore(Path.Combine(testDirectory, "title-rule-race"));
    await titleRuleStore.AddSenderAsync("Bank");
    await titleRuleStore.CaptureAsync("Bank", "برداشت: 90,000", DateTimeOffset.UtcNow);
    var allowTitleRuleChange = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var titleRuleChangeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var requireTitlesNow = false;
    var titleRuleChange = MobileApiClient.RunWithSyncLockAsync(async () =>
    {
        titleRuleChangeStarted.SetResult();
        await allowTitleRuleChange.Task;
        requireTitlesNow = true;
    });
    await titleRuleChangeStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
    var titleRuleApi = new MobileApiClient(() => throw new InvalidOperationException("Untitled SMS must not be posted"));
    var titleRuleSend = titleRuleApi.SendNextBatchAsync(titleRuleStore,
        () => Task.FromResult<MobileConnection?>(apiConnection), DateTimeOffset.UtcNow,
        requireTitleProvider: () => requireTitlesNow);
    allowTitleRuleChange.SetResult();
    await titleRuleChange.WaitAsync(TimeSpan.FromSeconds(10));
    Assert(await titleRuleSend.WaitAsync(TimeSpan.FromSeconds(10)) == SyncRunResult.NothingToSend,
        "A queued automatic sender obeys the latest title setting after acquiring the gate");

    var destinationStore = CreateStore(Path.Combine(testDirectory, "destination-race"));
    Assert(await destinationStore.AddSenderAsync("Bank") == AddSenderResult.Added, "Add destination race sender");
    Assert(await destinationStore.CaptureAsync("Bank", "برداشت: 95,000", DateTimeOffset.UtcNow), "Queue destination race SMS");
    var destinationKey = (await destinationStore.TakePendingBatchAsync(DateTimeOffset.UtcNow)).Single().Key;
    var newConnection = apiConnection with { Token = new string('B', 64), Destination = "New workspace" };
    MobileConnection? selectedConnection = apiConnection;
    var destinationChangeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var allowDestinationChange = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var destinationChange = MobileApiClient.RunWithSyncLockAsync(async () =>
    {
        destinationChangeStarted.SetResult();
        await allowDestinationChange.Task;
        selectedConnection = newConnection;
    });
    await destinationChangeStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
    var connectionProviderCalled = false;
    var destinationApi = new MobileApiClient(() => new HttpClient(new StubHandler(request =>
    {
        Assert(request.Headers.Authorization?.Parameter == newConnection.Token,
            "Queued sender uses the newly selected workspace credentials");
        return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = JsonContent.Create(new SmsBatchResult([new SmsResult(destinationKey, "created")])) };
    })));
    var waitingSend = destinationApi.SendNextBatchAsync(destinationStore, () =>
    {
        connectionProviderCalled = true;
        return Task.FromResult<MobileConnection?>(selectedConnection);
    }, DateTimeOffset.UtcNow);
    Assert(!connectionProviderCalled && !waitingSend.IsCompleted, "Load destination only after the change lock is released");
    allowDestinationChange.SetResult();
    await destinationChange.WaitAsync(TimeSpan.FromSeconds(10));
    Assert(await waitingSend.WaitAsync(TimeSpan.FromSeconds(10)) == SyncRunResult.Sent,
        "Send resumes after workspace change completes");

    var inflightStore = CreateStore(Path.Combine(testDirectory, "inflight-disconnect"));
    Assert(await inflightStore.AddSenderAsync("Bank") == AddSenderResult.Added, "Add inflight race sender");
    Assert(await inflightStore.CaptureAsync("Bank", "برداشت: 110,000", DateTimeOffset.UtcNow), "Queue inflight race SMS");
    var inflightKey = (await inflightStore.TakePendingBatchAsync(DateTimeOffset.UtcNow)).Single().Key;
    var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var allowResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var inflightApi = new MobileApiClient(() => new HttpClient(new AsyncStubHandler(async (_, cancellationToken) =>
    {
        requestStarted.SetResult();
        await allowResponse.Task.WaitAsync(cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = JsonContent.Create(new SmsBatchResult([new SmsResult(inflightKey, "created")])) };
    })));
    var inflightSend = inflightApi.SendNextBatchAsync(inflightStore, () => Task.FromResult<MobileConnection?>(selectedConnection), DateTimeOffset.UtcNow);
    await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
    var disconnected = false;
    var disconnect = MobileApiClient.RunWithSyncLockAsync(() =>
    {
        selectedConnection = null;
        disconnected = true;
        return Task.CompletedTask;
    });
    Assert(!disconnected && !disconnect.IsCompleted, "Disconnect waits until the current request and receipt complete");
    allowResponse.SetResult();
    Assert(await inflightSend.WaitAsync(TimeSpan.FromSeconds(10)) == SyncRunResult.Sent, "Current delivery is acknowledged before disconnect");
    await disconnect.WaitAsync(TimeSpan.FromSeconds(10));
    Assert(disconnected && selectedConnection is null, "Complete disconnect under the sending gate");
    Assert(await inflightApi.SendNextBatchAsync(inflightStore, () => Task.FromResult<MobileConnection?>(selectedConnection), DateTimeOffset.UtcNow.AddMinutes(2))
        == SyncRunResult.ConnectionRequired, "A later worker observes disconnect without sending");

    Assert(MobileApiClient.NormalizePairingCode(new string('a', 32)) == new string('A', 32), "Normalize pairing hex code");
    Assert(MobileApiClient.NormalizePairingCode("ABCD-EF۰۱-۲۳۴۵-۶۷۸۹-ABCD-EF01-2345-6789") == "ABCDEF0123456789ABCDEF0123456789",
        "Pairing accepts Persian digits and visual separators");
    Assert(MobileApiClient.NormalizePairingCode(new string('A', 32) + " غیرمجاز") is null,
        "Reject unexpected pairing text instead of silently deleting it");
    Assert(MobileApiClient.NormalizeServer("https://user:secret@donysh.ir") is null, "Reject credentials in server URLs");
    Assert(MobileApiClient.NormalizeServer("https://donysh.ir?token=value") is null, "Reject query-bearing API origins");

    var encryptedPath = Path.Combine(testDirectory, "sms-monitor-state.json");
    var encrypted = await File.ReadAllBytesAsync(encryptedPath);
    Assert(SmsStateProtection.IsEncrypted(encrypted), "Protect SMS data at rest");
    var wrongKeyStore = new SmsMonitorStore(testDirectory, _ => Task.FromResult(new byte[32]));
    await AssertThrowsAsync<InvalidDataException>(() => wrongKeyStore.GetSenderSummariesAsync(), "Wrong key cannot open SMS state");
    Assert(encrypted.SequenceEqual(await File.ReadAllBytesAsync(encryptedPath)), "Wrong key never resets original queue");
    var missingKeyStore = new SmsMonitorStore(testDirectory, _ => throw new InvalidDataException("Key unavailable"));
    await AssertThrowsAsync<InvalidDataException>(() => missingKeyStore.AddSenderAsync("AnotherBank"), "Unavailable key prevents writes");
    Assert(encrypted.SequenceEqual(await File.ReadAllBytesAsync(encryptedPath)), "Key failure preserves original queue");

    var corruptDirectory = Path.Combine(testDirectory, "corrupt-encrypted");
    Directory.CreateDirectory(corruptDirectory);
    var tampered = encrypted.ToArray();
    tampered[^1] ^= 1;
    var corruptPath = Path.Combine(corruptDirectory, "sms-monitor-state.json");
    await File.WriteAllBytesAsync(corruptPath, tampered);
    await AssertThrowsAsync<InvalidDataException>(() => CreateStore(corruptDirectory).GetSenderSummariesAsync(),
        "Reject altered encrypted data");
    Assert(tampered.SequenceEqual(await File.ReadAllBytesAsync(corruptPath)), "Corrupt state is preserved for recovery");

    var legacyDirectory = Path.Combine(testDirectory, "legacy");
    Directory.CreateDirectory(legacyDirectory);
    var legacySender = new MonitoredSender { Address = "Bank", NormalizedAddress = "BANK" };
    var legacyMessage = new CapturedSms { SenderId = legacySender.Id, SenderAddress = "Bank", Body = "private banking text",
        ReceivedAtUtc = DateTimeOffset.UtcNow, DeduplicationKey = "legacy-message-key" };
    var legacyPath = Path.Combine(legacyDirectory, "sms-monitor-state.json");
    await File.WriteAllTextAsync(legacyPath, JsonSerializer.Serialize(new SmsMonitorState
        { Senders = [legacySender], Messages = [legacyMessage] }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    var upgradedStore = CreateStore(legacyDirectory);
    Assert((await upgradedStore.GetSenderSummariesAsync()).Single().MessageCount == 1, "Migrate existing sender and queued SMS");
    var upgradedBytes = await File.ReadAllBytesAsync(legacyPath);
    Assert(SmsStateProtection.IsEncrypted(upgradedBytes), "Migrate plaintext legacy store to authenticated encryption");
    Assert(!System.Text.Encoding.UTF8.GetString(upgradedBytes).Contains("private banking text", StringComparison.Ordinal),
        "SMS text is no longer present in the saved file");
    Assert(!File.Exists(legacyPath + ".tmp"), "Migration does not leave a plaintext temporary file");
    Assert((await CreateStore(legacyDirectory).GetMessagesAsync(legacySender.Id)).Single().Body == legacyMessage.Body,
        "Reload encrypted state without losing legacy messages");

    var malformedApi = new MobileApiClient(() => new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("{\"results\":[null]}", System.Text.Encoding.UTF8, "application/json") })));
    Assert(await malformedApi.SendNextBatchAsync(upgradedStore, apiConnection, DateTimeOffset.UtcNow) == SyncRunResult.RetryScheduled,
        "Malformed acknowledgement schedules a safe retry");
    Assert((await upgradedStore.GetSyncSummaryAsync()).Pending == 1, "Malformed response cannot mark an SMS as delivered");

    Assert(await store.RemoveSenderAsync(summary.Id), "Remove sender");
    Assert((await store.GetSenderSummariesAsync()).Count == 0, "Remove sender messages");
    Assert((await store.GetTemplatesAsync(summary.Id)).Count == 0, "Remove sender templates");
    Assert(!(await store.GetTitleSuggestionsAsync(summary.Id)).Contains("هزینهٔ آزمایشی ویژه"),
        "Removing a sender also removes its private title history");

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

static SmsMonitorStore CreateStore(string directory) => new(directory,
    _ => Task.FromResult(Enumerable.Range(1, 32).Select(x => (byte)x).ToArray()));

static async Task AssertThrowsAsync<T>(Func<Task> action, string name) where T : Exception
{
    try { await action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"Test failed: {name}");
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

sealed class AsyncStubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        handler(request, cancellationToken);
}
