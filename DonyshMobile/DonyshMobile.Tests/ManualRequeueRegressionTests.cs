using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DonyshMobile.Core;

internal static class ManualRequeueRegressionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task RunAsync(string directory)
    {
        await CheckScopeAndPreservationAsync(Path.Combine(directory, "scope"));
        await CheckMinutePacingAsync(Path.Combine(directory, "pacing"));
        await CheckInflightReceiptAsync(Path.Combine(directory, "inflight"));
        Console.WriteLine("PASS Manual requeue scope, persistence, title gate, pacing and in-flight receipt regressions");
    }

    private static async Task CheckScopeAndPreservationAsync(string directory)
    {
        var now = DateTimeOffset.UtcNow;
        var sender = new MonitoredSender { Address = "BankA", NormalizedAddress = "BANKA" };
        var otherSender = new MonitoredSender { Address = "BankB", NormalizedAddress = "BANKB" };
        var failed = Message(sender, now, SmsDeliveryStatus.Pending, "خرید روزانه", "connection failed");
        var rejected = Message(sender, now.AddSeconds(1), SmsDeliveryStatus.Rejected, null, "template rejected");
        rejected.ConfirmedAtUtc = now;
        rejected.DeliveryProtocolVersion = 1;
        var alreadyQueued = Message(sender, now.AddSeconds(2), SmsDeliveryStatus.Pending, null, null);
        var sent = Message(sender, now.AddSeconds(3), SmsDeliveryStatus.Sent, "پرداخت تأییدشده", null);
        sent.ConfirmedAtUtc = now;
        var unrelated = Message(otherSender, now.AddSeconds(4), SmsDeliveryStatus.Rejected, "پیام بانک دیگر", "rejected");
        unrelated.ConfirmedAtUtc = now;
        var state = new SmsMonitorState
        {
            Senders = [sender, otherSender], Messages = [failed, rejected, alreadyQueued, sent, unrelated],
            NextSyncAtUtc = now.AddMinutes(10), ConsecutiveSyncFailures = 4, SyncStatus = "retry later"
        };
        await SaveSeedAsync(directory, state);
        var store = Store(directory);
        var original = JsonSerializer.Serialize(await ReadAsync(directory), JsonOptions);
        Check(await store.RequeueMessagesAsync(otherSender.Id, [failed.Id, rejected.Id]) == 0,
            "Wrong sender cannot requeue another sender's messages");
        Check(await store.RequeueMessagesAsync(Guid.NewGuid(), [unrelated.Id]) == 0,
            "Missing sender cannot requeue messages");
        Check(await store.RequeueMessagesAsync(sender.Id, [sent.Id, alreadyQueued.Id, Guid.NewGuid()]) == 0,
            "Sent, already queued and missing messages are no-ops");
        Check(await store.RequeueMessagesAsync(sender.Id, []) == 0, "Empty selection is a no-op");
        Check(JsonSerializer.Serialize(await ReadAsync(directory), JsonOptions) == original,
            "No-op requeue leaves every saved value unchanged");

        Check(await store.RequeueMessagesAsync(sender.Id,
            [failed.Id, rejected.Id, rejected.Id, alreadyQueued.Id, sent.Id, unrelated.Id, Guid.NewGuid()]) == 2,
            "Bulk requeue changes only eligible rows and counts repeated IDs once");
        var reloaded = await ReadAsync(directory);
        foreach (var expected in new[] { failed, rejected })
        {
            var actual = reloaded.Messages.Single(message => message.Id == expected.Id);
            Check(actual.DeliveryStatus == SmsDeliveryStatus.Pending && actual.ConfirmedAtUtc is null
                && actual.LastDeliveryError is null && actual.DeliveryProtocolVersion == 2,
                "Requeue clears rejection metadata and returns to the current pending protocol");
            Check(actual.DeduplicationKey == expected.DeduplicationKey && actual.Title == expected.Title
                && actual.DeliveryAttempts == expected.DeliveryAttempts && actual.Body == expected.Body
                && actual.SenderAddress == expected.SenderAddress && actual.ReceivedAtUtc == expected.ReceivedAtUtc,
                "Requeue preserves identity, title, body, sender, received time and attempt history");
        }
        Check(reloaded.NextSyncAtUtc == state.NextSyncAtUtc && reloaded.ConsecutiveSyncFailures == 4,
            "Manual requeue cannot bypass persisted exponential backoff");
        Check(reloaded.Messages.Single(message => message.Id == sent.Id).DeliveryStatus == SmsDeliveryStatus.Sent
            && reloaded.Messages.Single(message => message.Id == sent.Id).ConfirmedAtUtc == sent.ConfirmedAtUtc,
            "A delivered message keeps its receipt");
        Check(JsonSerializer.Serialize(reloaded.Messages.Single(message => message.Id == unrelated.Id), JsonOptions)
            == JsonSerializer.Serialize(unrelated, JsonOptions), "An unrelated sender's rejected message is unchanged");
        Check(await Store(directory).RequeueMessagesAsync(sender.Id, [failed.Id, rejected.Id]) == 0,
            "A repeated retry after reopening does not duplicate or count already queued messages");
        Check((await Store(directory).TakePendingBatchAsync(now)).Count == 0,
            "Requeued messages wait for the original backoff deadline");
        var titledBatch = await Store(directory).TakePendingBatchAsync(state.NextSyncAtUtc.AddSeconds(1), requireTitle: true);
        Check(titledBatch.Any(message => message.Id == failed.Id)
            && titledBatch.All(message => message.Id != rejected.Id && message.Id != alreadyQueued.Id),
            "Automatic title requirement still blocks untitled requeued messages");
        var manualBatch = await Store(directory).TakePendingBatchAsync(state.NextSyncAtUtc.AddSeconds(1));
        Check(manualBatch.Any(message => message.Id == rejected.Id && message.Key == rejected.DeliveryKey),
            "An explicit title-bypassing batch preserves the original receipt key");
    }

    private static async Task CheckMinutePacingAsync(string directory)
    {
        var now = DateTimeOffset.UtcNow;
        var sender = new MonitoredSender { Address = "Bank", NormalizedAddress = "BANK" };
        var message = Message(sender, now, SmsDeliveryStatus.Rejected, "خرید", "template rejected");
        var state = new SmsMonitorState { Senders = [sender], Messages = [message], NextSyncAtUtc = now.AddMinutes(1) };
        await SaveSeedAsync(directory, state);
        var store = Store(directory);
        Check(await store.RequeueMessagesAsync(sender.Id, [message.Id]) == 1, "Requeue a rejected message during pacing");
        Check((await store.TakePendingBatchAsync(now.AddSeconds(30))).Count == 0,
            "Retry button cannot bypass an existing one-minute interval");
        var attemptAt = now.AddMinutes(1).AddSeconds(1);
        var batch = await store.TakePendingBatchAsync(attemptAt);
        Check(batch.Count == 1, "Requeued message becomes eligible when its interval expires");
        await store.ConfirmBatchAsync(batch, [new SmsDeliveryReceipt(batch.Single().Key, "rejected")], attemptAt);
        Check(await store.RequeueMessagesAsync(sender.Id, [message.Id, message.Id]) == 1,
            "Retry after a new rejection counts the message once");
        Check((await store.TakePendingBatchAsync(attemptAt.AddSeconds(30))).Count == 0,
            "Immediate retry after rejection retains the new server-receipt cooldown");
        var next = await Store(directory).TakePendingBatchAsync(attemptAt.AddMinutes(1));
        Check(next.Count == 1 && next[0].Key == batch.Single().Key,
            "Next paced retry uses the same idempotency key after reopening");
    }

    private static async Task CheckInflightReceiptAsync(string directory)
    {
        var now = DateTimeOffset.UtcNow;
        var sender = new MonitoredSender { Address = "Bank", NormalizedAddress = "BANK" };
        var message = Message(sender, now, SmsDeliveryStatus.Pending, "خرید", "previous failure");
        await SaveSeedAsync(directory, new SmsMonitorState { Senders = [sender], Messages = [message] });
        var store = Store(directory);
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new MobileApiClient(() => new HttpClient(new DelayedReceiptHandler(async cancellationToken =>
        {
            requestStarted.TrySetResult();
            await releaseResponse.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new SmsBatchResult([new SmsResult(message.DeliveryKey, "created")]))
            };
        })));
        var sending = api.SendNextBatchAsync(store, new MobileConnection("https://donysh.ir", new string('A', 64), "test"), now);
        Task? requeue = null;
        var requeueEntered = false;
        var changed = -1;
        try
        {
            await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            requeue = MobileApiClient.RunWithSyncLockAsync(async () =>
            {
                requeueEntered = true;
                changed = await store.RequeueMessagesAsync(sender.Id, [message.Id]);
            });
            Check(!requeueEntered && !requeue.IsCompleted,
                "Manual requeue waits for the in-flight request and receipt");
            releaseResponse.TrySetResult();
            Check(await sending.WaitAsync(TimeSpan.FromSeconds(10)) == SyncRunResult.Sent,
                "A complete in-flight receipt marks the message sent");
            await requeue.WaitAsync(TimeSpan.FromSeconds(10));
            Check(changed == 0, "Stale manual retry cannot requeue a newly delivered message");
            var saved = await ReadAsync(directory);
            Check(saved.Messages.Single().DeliveryStatus == SmsDeliveryStatus.Sent
                && saved.Messages.Single().DeduplicationKey == message.DeduplicationKey,
                "Receipt wins over the stale retry without duplicating the message");
        }
        finally
        {
            releaseResponse.TrySetResult();
            await sending.WaitAsync(TimeSpan.FromSeconds(10));
            if (requeue is not null) await requeue.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static CapturedSms Message(MonitoredSender sender, DateTimeOffset receivedAt, SmsDeliveryStatus status,
        string? title, string? error) => new()
    {
        SenderId = sender.Id, SenderAddress = sender.Address, Body = "برداشت: 120,000", ReceivedAtUtc = receivedAt,
        DeduplicationKey = new SmsEnvelope(sender.Address, "برداشت: 120,000", receivedAt.ToUnixTimeMilliseconds()).Key,
        DeliveryKey = Convert.ToHexString(Guid.NewGuid().ToByteArray()) + Convert.ToHexString(Guid.NewGuid().ToByteArray()),
        DeliveryStatus = status, DeliveryProtocolVersion = 2, DeliveryAttempts = 3, Title = title, LastDeliveryError = error
    };

    private static SmsMonitorStore Store(string directory) => new(directory, _ => Task.FromResult(Key()));
    private static byte[] Key() => Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
    private static string StatePath(string directory) => Path.Combine(directory, "sms-monitor-state.json");

    private static async Task SaveSeedAsync(string directory, SmsMonitorState state)
    {
        Directory.CreateDirectory(directory);
        var data = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
        await File.WriteAllBytesAsync(StatePath(directory), SmsStateProtection.Encrypt(data, Key()));
    }

    private static async Task<SmsMonitorState> ReadAsync(string directory) =>
        JsonSerializer.Deserialize<SmsMonitorState>(SmsStateProtection.Decrypt(await File.ReadAllBytesAsync(StatePath(directory)), Key()), JsonOptions)!;

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("Manual requeue regression failed: " + name);
    }

    private sealed class DelayedReceiptHandler(Func<CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(cancellationToken);
    }
}
