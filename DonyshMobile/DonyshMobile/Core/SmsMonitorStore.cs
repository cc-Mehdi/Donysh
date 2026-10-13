using System.Text.Json;

namespace DonyshMobile.Core;

public sealed class SmsMonitorStore
{
    private const int MaxSenders = 50;
    private const int MaxMessages = 5000;
    private const int MaxAddressLength = 160;
    private const int MaxBodyLength = 2048;
    private const int MaxTemplatesPerSender = 20;
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly string _statePath;

    public SmsMonitorStore(string appDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataDirectory);
        Directory.CreateDirectory(appDataDirectory);
        _statePath = Path.Combine(appDataDirectory, "sms-monitor-state.json");
    }

    public async Task<IReadOnlyList<SenderSummary>> GetSenderSummariesAsync(
        CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadUnsafeAsync(cancellationToken);
            var changed = Prune(state, DateTimeOffset.UtcNow);
            if (changed)
            {
                await SaveUnsafeAsync(state, cancellationToken);
            }

            return state.Senders
                .Select(sender =>
                {
                    var messages = state.Messages.Where(x => x.SenderId == sender.Id).ToList();
                    return new SenderSummary(
                        sender.Id,
                        sender.Address,
                        messages.Count,
                        messages.Count == 0 ? null : messages.Max(x => x.ReceivedAtUtc));
                })
                .OrderByDescending(x => x.LastMessageAtUtc)
                .ThenBy(x => x.Address, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<AddSenderResult> AddSenderAsync(
        string address,
        CancellationToken cancellationToken = default)
    {
        var trimmed = address?.Trim() ?? string.Empty;
        var normalized = SenderAddress.Normalize(trimmed);
        if (trimmed.Length is 0 or > MaxAddressLength || normalized.Length == 0)
        {
            return AddSenderResult.Invalid;
        }

        await Gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadUnsafeAsync(cancellationToken);
            if (state.Senders.Any(x => SenderAddress.Normalize(x.Address) == normalized))
            {
                return AddSenderResult.Duplicate;
            }

            if (state.Senders.Count >= MaxSenders)
            {
                return AddSenderResult.LimitReached;
            }

            state.Senders.Add(new MonitoredSender
            {
                Address = trimmed,
                NormalizedAddress = normalized
            });
            await SaveUnsafeAsync(state, cancellationToken);
            return AddSenderResult.Added;
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<bool> RemoveSenderAsync(Guid senderId, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadUnsafeAsync(cancellationToken);
            var removed = state.Senders.RemoveAll(x => x.Id == senderId) > 0;
            if (!removed)
            {
                return false;
            }

            state.Messages.RemoveAll(x => x.SenderId == senderId);
            state.Templates.RemoveAll(x => x.SenderId == senderId);
            await SaveUnsafeAsync(state, cancellationToken);
            return true;
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<IReadOnlyList<CapturedSms>> GetMessagesAsync(
        Guid senderId,
        CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadUnsafeAsync(cancellationToken);
            var changed = Prune(state, DateTimeOffset.UtcNow);
            if (changed)
            {
                await SaveUnsafeAsync(state, cancellationToken);
            }

            return state.Messages
                .Where(x => x.SenderId == senderId && x.ReceivedAtUtc >= DateTimeOffset.UtcNow.Subtract(Retention))
                .OrderByDescending(x => x.ReceivedAtUtc)
                .ToList();
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<IReadOnlyList<CapturedSms>> GetUntitledMessagesAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadUnsafeAsync(cancellationToken);
            return state.Messages
                .Where(x => x.DeliveryStatus == SmsDeliveryStatus.Pending && string.IsNullOrWhiteSpace(x.Title))
                .OrderBy(x => x.ReceivedAtUtc)
                .ToList();
        }
        finally { Gate.Release(); }
    }

    public async Task<bool> SetTitleAsync(Guid messageId, string? title, CancellationToken cancellationToken = default)
    {
        title = title?.Trim();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 160 || title.Any(char.IsControl)) return false;
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadUnsafeAsync(cancellationToken);
            var message = state.Messages.SingleOrDefault(x => x.Id == messageId && x.DeliveryStatus == SmsDeliveryStatus.Pending);
            if (message is null) return false;
            message.Title = title;
            await SaveUnsafeAsync(state, cancellationToken);
            return true;
        }
        finally { Gate.Release(); }
    }

    public async Task<SmsSyncSummary> GetSyncSummaryAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadUnsafeAsync(cancellationToken);
            return new SmsSyncSummary(
                state.Messages.Count(x => x.DeliveryStatus == SmsDeliveryStatus.Pending),
                state.Messages.Count(x => x.DeliveryStatus == SmsDeliveryStatus.Sent),
                state.Messages.Count(x => x.DeliveryStatus == SmsDeliveryStatus.Rejected),
                state.NextSyncAtUtc,
                state.SyncStatus);
        }
        finally { Gate.Release(); }
    }

    public async Task<IReadOnlyList<PendingSms>> TakePendingBatchAsync(
        DateTimeOffset nowUtc,
        int batchSize = 5,
        bool requireTitle = false,
        CancellationToken cancellationToken = default)
    {
        if (batchSize is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(batchSize));
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadUnsafeAsync(cancellationToken);
            var migrated = false;
            foreach (var message in state.Messages.Where(x =>
                         x.DeliveryStatus == SmsDeliveryStatus.Rejected && x.DeliveryProtocolVersion < 2))
            {
                message.DeliveryStatus = SmsDeliveryStatus.Pending;
                message.DeliveryProtocolVersion = 2;
                message.ConfirmedAtUtc = null;
                message.LastDeliveryError = null;
                migrated = true;
            }
            if (migrated)
            {
                state.NextSyncAtUtc = nowUtc;
                state.SyncStatus = "پیام‌های ردشده نسخه قبلی برای بررسی دوباره آماده شدند.";
                await SaveUnsafeAsync(state, cancellationToken);
            }
            if (state.NextSyncAtUtc > nowUtc) return [];
            var senders = state.Senders.ToDictionary(x => x.Id);
            return state.Messages
                .Where(x => x.DeliveryStatus == SmsDeliveryStatus.Pending && senders.ContainsKey(x.SenderId)
                    && (!requireTitle || !string.IsNullOrWhiteSpace(x.Title)))
                .OrderBy(x => x.ReceivedAtUtc)
                .Take(batchSize)
                .Select(x =>
                {
                    var parsed = state.Templates
                        .Where(template => template.SenderId == x.SenderId)
                        .Select(template => SmsTemplateParser.TryParse(template, x.Body))
                        .FirstOrDefault(result => result is not null);
                    return new PendingSms(
                        x.Id,
                        x.DeduplicationKey,
                        string.IsNullOrWhiteSpace(x.SenderAddress) ? senders[x.SenderId].Address : x.SenderAddress,
                        x.Body,
                        x.ReceivedAtUtc.ToUnixTimeMilliseconds(),
                        parsed is null ? null : new ParsedTransaction(
                            parsed.AmountRial,
                            parsed.Kind == TransactionKind.Withdrawal ? "withdrawal" : "deposit",
                            parsed.TemplateName),
                        x.Title);
                })
                .ToList();
        }
        finally { Gate.Release(); }
    }

    public async Task ConfirmBatchAsync(
        IReadOnlyCollection<PendingSms> batch,
        IReadOnlyCollection<SmsDeliveryReceipt> receipts,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (batch.Count == 0 || receipts.Count != batch.Count
            || receipts.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != batch.Count
            || receipts.Any(x => x.Status is not ("created" or "duplicate" or "rejected"))
            || receipts.Any(x => batch.All(item => item.Key != x.Key)))
            throw new InvalidDataException("پاسخ سامانه کامل یا معتبر نیست.");

        await Gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadUnsafeAsync(cancellationToken);
            foreach (var receipt in receipts)
            {
                var message = state.Messages.SingleOrDefault(x => x.DeduplicationKey == receipt.Key);
                if (message is null || message.DeliveryStatus != SmsDeliveryStatus.Pending) continue;
                message.DeliveryStatus = receipt.Status == "rejected"
                    ? SmsDeliveryStatus.Rejected
                    : SmsDeliveryStatus.Sent;
                message.DeliveryProtocolVersion = 2;
                message.ConfirmedAtUtc = nowUtc;
                message.LastDeliveryError = receipt.Status == "rejected"
                    ? "سامانه پیام را دریافت کرد، اما قالب آن را نپذیرفت."
                    : null;
            }
            state.ConsecutiveSyncFailures = 0;
            state.NextSyncAtUtc = nowUtc.AddMinutes(1);
            state.SyncStatus = receipts.Any(x => x.Status == "rejected")
                ? "پاسخ سامانه دریافت شد؛ بعضی پیام‌ها برای بررسی روی گوشی نگه داشته شدند."
                : "دریافت آخرین بسته توسط سامانه تأیید شد.";
            await SaveUnsafeAsync(state, cancellationToken);
        }
        finally { Gate.Release(); }
    }

    public async Task RecordSyncFailureAsync(
        IReadOnlyCollection<PendingSms> batch,
        DateTimeOffset nowUtc,
        string error,
        TimeSpan? retryAfter = null,
        CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadUnsafeAsync(cancellationToken);
            state.ConsecutiveSyncFailures = Math.Min(10, state.ConsecutiveSyncFailures + 1);
            var exponentialSeconds = Math.Min(1800, 60 * Math.Pow(2, state.ConsecutiveSyncFailures - 1));
            var requestedSeconds = Math.Clamp(retryAfter?.TotalSeconds ?? 0, 0, 86400);
            state.NextSyncAtUtc = nowUtc.AddSeconds(Math.Max(exponentialSeconds, requestedSeconds));
            state.SyncStatus = error;
            var keys = batch.Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var message in state.Messages.Where(x => keys.Contains(x.DeduplicationKey)
                                                               && x.DeliveryStatus == SmsDeliveryStatus.Pending))
            {
                message.DeliveryAttempts++;
                message.LastDeliveryError = error;
            }
            await SaveUnsafeAsync(state, cancellationToken);
        }
        finally { Gate.Release(); }
    }

    public async Task<IReadOnlyList<SmsTemplate>> GetTemplatesAsync(Guid senderId, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadUnsafeAsync(cancellationToken);
            return state.Templates.Where(x => x.SenderId == senderId).ToList();
        }
        finally { Gate.Release(); }
    }

    public async Task<string?> SaveTemplateAsync(SmsTemplate template, CancellationToken cancellationToken = default)
    {
        var error = SmsTemplateParser.Validate(template);
        if (error is not null) return error;
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadUnsafeAsync(cancellationToken);
            if (!state.Senders.Any(x => x.Id == template.SenderId)) return "فرستنده پیدا نشد.";
            var existing = state.Templates.FindIndex(x => x.Id == template.Id);
            if (existing >= 0 && state.Templates[existing].SenderId != template.SenderId) return "این قالب متعلق به فرستنده دیگری است.";
            if (existing < 0)
            {
                if (state.Templates.Count(x => x.SenderId == template.SenderId) >= MaxTemplatesPerSender)
                    return "برای هر فرستنده حداکثر ۲۰ قالب مجاز است.";
                state.Templates.Add(template);
            }
            else state.Templates[existing] = template;
            await SaveUnsafeAsync(state, cancellationToken);
            return null;
        }
        finally { Gate.Release(); }
    }

    public async Task<bool> DeleteTemplateAsync(Guid senderId, Guid templateId, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadUnsafeAsync(cancellationToken);
            if (state.Templates.RemoveAll(x => x.Id == templateId && x.SenderId == senderId) == 0) return false;
            await SaveUnsafeAsync(state, cancellationToken);
            return true;
        }
        finally { Gate.Release(); }
    }

    public async Task<bool> CaptureAsync(
        string senderAddress,
        string body,
        DateTimeOffset receivedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var normalized = SenderAddress.Normalize(senderAddress);
        if (normalized.Length == 0 || string.IsNullOrWhiteSpace(body) || body.Length > MaxBodyLength)
        {
            return false;
        }

        await Gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadUnsafeAsync(cancellationToken);
            Prune(state, DateTimeOffset.UtcNow);
            var sender = state.Senders.FirstOrDefault(x => SenderAddress.Normalize(x.Address) == normalized);
            if (sender is null)
            {
                return false;
            }

            var key = CreateKey(senderAddress, body, receivedAtUtc);
            if (state.Messages.Any(x => x.DeduplicationKey == key))
            {
                return false;
            }

            if (state.Messages.Count >= MaxMessages)
            {
                var oldestConfirmed = state.Messages
                    .Where(x => x.DeliveryStatus != SmsDeliveryStatus.Pending)
                    .MinBy(x => x.ReceivedAtUtc);
                if (oldestConfirmed is not null) state.Messages.Remove(oldestConfirmed);
            }
            if (state.Messages.Count >= MaxMessages)
            {
                state.SyncStatus = "صف آفلاین گوشی پر است؛ ابتدا اتصال و ارسال پیام‌های منتظر را بررسی کنید.";
                await SaveUnsafeAsync(state, cancellationToken);
                return false;
            }

            state.Messages.Add(new CapturedSms
            {
                SenderId = sender.Id,
                SenderAddress = senderAddress.Trim(),
                DeduplicationKey = key,
                Body = body.Trim(),
                ReceivedAtUtc = receivedAtUtc.ToUniversalTime(),
                DeliveryProtocolVersion = 2
            });

            TrimConfirmedMessages(state);

            await SaveUnsafeAsync(state, cancellationToken);
            return true;
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<SmsMonitorState> LoadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_statePath))
        {
            return new SmsMonitorState();
        }

        await using var stream = new FileStream(
            _statePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var state = await JsonSerializer.DeserializeAsync<SmsMonitorState>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("فایل اطلاعات پیامک قابل خواندن نیست.");
        if (state.Version != 1)
        {
            throw new InvalidDataException("نسخه فایل اطلاعات پیامک پشتیبانی نمی‌شود.");
        }

        state.Templates ??= [];
        state.Senders ??= [];
        state.Messages ??= [];
        var senders = state.Senders.ToDictionary(x => x.Id);
        foreach (var message in state.Messages.Where(x => string.IsNullOrWhiteSpace(x.SenderAddress)))
        {
            if (!senders.TryGetValue(message.SenderId, out var sender)) continue;
            message.SenderAddress = sender.Address;
            message.DeduplicationKey = CreateKey(message.SenderAddress, message.Body, message.ReceivedAtUtc);
        }
        foreach (var template in state.Templates)
        {
            template.Phrases ??= [];
            template.Fields ??= [];
        }

        return state;
    }

    private async Task SaveUnsafeAsync(SmsMonitorState state, CancellationToken cancellationToken)
    {
        var temporaryPath = _statePath + ".tmp";
        await using (var stream = new FileStream(
                         temporaryPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         4096,
                         FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, _statePath, true);
    }

    private static bool Prune(SmsMonitorState state, DateTimeOffset nowUtc)
    {
        var cutoff = nowUtc.Subtract(Retention);
        var changed = state.Messages.RemoveAll(x => x.ReceivedAtUtc < cutoff
            && x.DeliveryStatus != SmsDeliveryStatus.Pending) > 0;
        var count = state.Messages.Count;
        TrimConfirmedMessages(state);
        return changed || state.Messages.Count != count;
    }

    private static void TrimConfirmedMessages(SmsMonitorState state)
    {
        if (state.Messages.Count <= MaxMessages) return;
        var removable = state.Messages
            .Where(x => x.DeliveryStatus != SmsDeliveryStatus.Pending)
            .OrderBy(x => x.ReceivedAtUtc)
            .Take(state.Messages.Count - MaxMessages)
            .Select(x => x.Id)
            .ToHashSet();
        state.Messages.RemoveAll(x => removable.Contains(x.Id));
    }

    private static string CreateKey(string senderAddress, string body, DateTimeOffset receivedAtUtc)
        => new SmsEnvelope(senderAddress, body, receivedAtUtc.ToUnixTimeMilliseconds()).Key;
}
