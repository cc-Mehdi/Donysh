using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DonyshMobile.Core;

public sealed class SmsMonitorStore
{
    private const int MaxSenders = 50;
    private const int MaxMessages = 5000;
    private const int MaxAddressLength = 64;
    private const int MaxBodyLength = 2048;
    private const int MaxTemplatesPerSender = 20;
    private const int MaxTitleHistory = 80;
    private const int MaxDeliveryHistory = 10_000;
    private const long MaxStateBytes = 128L * 1024 * 1024;
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private static readonly TimeSpan DeliveryHistoryRetention = TimeSpan.FromDays(35);
    private static readonly TimeSpan RecoveryDeduplicationWindow = TimeSpan.FromMinutes(5);
    private static readonly Regex DeduplicationWhitespace = new(@"\s+", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly string _statePath;
    private readonly Func<CancellationToken, Task<byte[]>>? _storageKeyProvider;
    private bool _requiresExistingKey;

    public SmsMonitorStore(string appDataDirectory, Func<CancellationToken, Task<byte[]>>? storageKeyProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataDirectory);
        Directory.CreateDirectory(appDataDirectory);
        _statePath = Path.Combine(appDataDirectory, "sms-monitor-state.json");
        _storageKeyProvider = storageKeyProvider;
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
                    var messages = state.Messages.Where(x => x.SenderId == sender.Id
                        && x.ReceivedAtUtc >= DateTimeOffset.UtcNow.Subtract(Retention)).ToList();
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
        if (trimmed.Length is 0 or > MaxAddressLength || normalized.Length == 0 || trimmed.Any(char.IsControl))
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
            state.TitleHistory.RemoveAll(x => x.SenderId == senderId);
            state.DeliveryHistory.RemoveAll(x => x.SenderId == senderId);
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
            var changed = !string.Equals(message.Title?.Trim(), title, StringComparison.OrdinalIgnoreCase);
            message.Title = title;
            if (changed) TrackTitleUsage(state, message.SenderId, title);
            await SaveUnsafeAsync(state, cancellationToken);
            return true;
        }
        finally { Gate.Release(); }
    }

    public async Task<IReadOnlyList<string>> GetTitleSuggestionsAsync(
        Guid senderId,
        string? messageBody = null,
        int maxCount = 5,
        CancellationToken cancellationToken = default)
    {
        if (maxCount is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(maxCount));
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadUnsafeAsync(cancellationToken);
            var candidates = new Dictionary<string, (string Title, int SameSender, int Uses, DateTimeOffset LastUsed)>(StringComparer.OrdinalIgnoreCase);

            void Add(string? value, Guid sourceSender, int uses, DateTimeOffset lastUsed)
            {
                var title = value?.Trim();
                if (string.IsNullOrWhiteSpace(title) || title.Length > 160 || title.Any(char.IsControl)) return;
                if (!candidates.TryGetValue(title, out var current))
                    current = (title, 0, 0, DateTimeOffset.MinValue);
                candidates[title] = (current.Title,
                    current.SameSender + (sourceSender == senderId ? Math.Max(1, uses) : 0),
                    current.Uses + Math.Max(1, uses),
                    lastUsed > current.LastUsed ? lastUsed : current.LastUsed);
            }

            foreach (var usage in state.TitleHistory)
                Add(usage.Title, usage.SenderId, usage.UseCount, usage.LastUsedAtUtc);
            foreach (var message in state.Messages.Where(x => !string.IsNullOrWhiteSpace(x.Title)))
                Add(message.Title, message.SenderId, 1, message.ReceivedAtUtc);

            var result = candidates.Values
                .OrderByDescending(x => x.SameSender > 0)
                .ThenByDescending(x => x.SameSender)
                .ThenByDescending(x => x.Uses)
                .ThenByDescending(x => x.LastUsed)
                .Select(x => x.Title)
                .Take(maxCount)
                .ToList();

            var kind = state.Templates
                .Where(x => x.SenderId == senderId)
                .Select(x => SmsTemplateParser.TryParse(x, messageBody ?? string.Empty)?.Kind)
                .FirstOrDefault(x => x.HasValue);
            var defaults = kind == TransactionKind.Deposit
                ? new[] { "حقوق و درآمد", "واریز شخصی", "بازگشت وجه", "پس‌انداز" }
                : new[] { "خرید روزانه", "خرید اینترنتی", "رفت‌وآمد", "قبض و خدمات", "سایر هزینه‌ها" };
            foreach (var fallback in defaults)
            {
                if (result.Count >= maxCount) break;
                if (!result.Contains(fallback, StringComparer.OrdinalIgnoreCase)) result.Add(fallback);
            }
            return result;
        }
        finally { Gate.Release(); }
    }

    public async Task<int> RequeueMessagesAsync(Guid senderId, IReadOnlyCollection<Guid> messageIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageIds);
        if (messageIds.Count == 0) return 0;
        var ids = messageIds.ToHashSet();
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadUnsafeAsync(cancellationToken);
            if (!state.Senders.Any(x => x.Id == senderId)) return 0;
            var changed = 0;
            foreach (var message in state.Messages.Where(x => x.SenderId == senderId && ids.Contains(x.Id)
                         && x.DeliveryStatus is SmsDeliveryStatus.Pending or SmsDeliveryStatus.Rejected or SmsDeliveryStatus.Excluded))
            {
                if (message.DeliveryStatus == SmsDeliveryStatus.Pending && message.LastDeliveryError is null
                    && message.ConfirmedAtUtc is null) continue;
                message.DeliveryStatus = SmsDeliveryStatus.Pending;
                message.DeliveryProtocolVersion = 2;
                message.ConfirmedAtUtc = null;
                message.LastDeliveryError = null;
                changed++;
            }
            if (changed == 0) return 0;
            // Preserve titles, idempotency keys, attempt history, cooldown and failure backoff.
            state.SyncStatus = "پیام‌های انتخاب‌شده دوباره در صف ارسال قرار گرفتند.";
            await SaveUnsafeAsync(state, cancellationToken);
            return changed;
        }
        finally { Gate.Release(); }
    }

    public async Task<int> ExcludeMessagesAsync(Guid senderId, IReadOnlyCollection<Guid> messageIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageIds);
        if (messageIds.Count == 0) return 0;
        var ids = messageIds.ToHashSet();
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadUnsafeAsync(cancellationToken);
            if (!state.Senders.Any(x => x.Id == senderId)) return 0;
            var changed = 0;
            foreach (var message in state.Messages.Where(x => x.SenderId == senderId && ids.Contains(x.Id)
                         && x.DeliveryStatus is SmsDeliveryStatus.Pending or SmsDeliveryStatus.Rejected))
            {
                message.DeliveryStatus = SmsDeliveryStatus.Excluded;
                message.ConfirmedAtUtc = null;
                message.LastDeliveryError = null;
                changed++;
            }
            if (changed == 0) return 0;
            state.SyncStatus = "پیام‌های انتخاب‌شده از ارسال کنار گذاشته شدند.";
            await SaveUnsafeAsync(state, cancellationToken);
            return changed;
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
                if (message.DeliveryStatus == SmsDeliveryStatus.Sent)
                    RememberDelivered(state, message, nowUtc);
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
            // A rejection is not a delivery receipt. Only retry this sender's messages that
            // the corrected template can now read as an expense; preserve keys/titles and pacing.
            var matcher = SmsTemplateParser.CreateMatcher(template)!;
            var requeued = 0;
            foreach (var message in state.Messages.Where(x => x.SenderId == template.SenderId
                         && x.DeliveryStatus == SmsDeliveryStatus.Rejected))
            {
                if (matcher(message.Body) is not { Kind: TransactionKind.Withdrawal }) continue;
                message.DeliveryStatus = SmsDeliveryStatus.Pending;
                message.DeliveryProtocolVersion = 2;
                message.ConfirmedAtUtc = null;
                message.LastDeliveryError = null;
                requeued++;
            }
            if (requeued > 0)
                state.SyncStatus = "پیام‌های ردشدهٔ سازگار با قالب ذخیره‌شده دوباره منتظر ارسال‌اند.";
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
        return await CaptureBatchAsync(
            [new SmsCaptureCandidate(senderAddress, body, receivedAtUtc)], cancellationToken) > 0;
    }

    public async Task<int> CaptureBatchAsync(
        IReadOnlyCollection<SmsCaptureCandidate> captures,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(captures);
        if (captures.Count == 0) return 0;
        if (captures.Count > 1000) throw new ArgumentOutOfRangeException(nameof(captures));

        await Gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadUnsafeAsync(cancellationToken);
            var stateChanged = Prune(state, DateTimeOffset.UtcNow);
            var senders = state.Senders
                .Select(sender => (Sender: sender, Address: SenderAddress.Normalize(sender.Address)))
                .Where(x => x.Address.Length > 0)
                .GroupBy(x => x.Address, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.First().Sender, StringComparer.Ordinal);
            var added = 0;

            foreach (var capture in captures.OrderBy(x => x.ReceivedAtUtc))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var address = capture.SenderAddress?.Trim() ?? string.Empty;
                var body = capture.Body?.Trim() ?? string.Empty;
                var normalized = SenderAddress.Normalize(address);
                if (normalized.Length == 0 || address.Length is 0 or > MaxAddressLength || address.Any(char.IsControl)
                    || body.Length is 0 or > MaxBodyLength || !senders.TryGetValue(normalized, out var sender)) continue;

                var receivedAtUtc = capture.ReceivedAtUtc.ToUniversalTime();
                var key = CreateKey(address, body, receivedAtUtc);
                var isDuplicate = state.Messages.Any(message => message.DeduplicationKey == key
                    || IsSameMessage(message.SenderId, message.Body, message.ReceivedAtUtc,
                        sender.Id, body, receivedAtUtc));
                if (isDuplicate) continue;

                if (state.Messages.Count >= MaxMessages)
                {
                    var oldestConfirmed = state.Messages
                        .Where(x => x.DeliveryStatus != SmsDeliveryStatus.Pending)
                        .MinBy(x => x.ReceivedAtUtc);
                    if (oldestConfirmed is not null)
                    {
                        state.Messages.Remove(oldestConfirmed);
                        stateChanged = true;
                    }
                }
                if (state.Messages.Count >= MaxMessages)
                {
                    state.SyncStatus = "صف آفلاین گوشی پر است؛ ابتدا اتصال و ارسال پیام‌های منتظر را بررسی کنید.";
                    stateChanged = true;
                    break;
                }

                state.Messages.Add(new CapturedSms
                {
                    SenderId = sender.Id,
                    SenderAddress = address,
                    DeduplicationKey = key,
                    Body = body,
                    ReceivedAtUtc = receivedAtUtc,
                    DeliveryProtocolVersion = 2,
                    RecoveredFromInbox = capture.RecoveredFromInbox
                });
                added++;
                stateChanged = true;
            }

            TrimConfirmedMessages(state);
            if (stateChanged) await SaveUnsafeAsync(state, cancellationToken);
            return added;
        }
        finally { Gate.Release(); }
    }

    public async Task<IReadOnlyList<SmsCaptureCandidate>> FindUnstoredCandidatesAsync(
        IReadOnlyCollection<SmsCaptureCandidate> captures,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(captures);
        if (captures.Count == 0) return [];
        if (captures.Count > 1000) throw new ArgumentOutOfRangeException(nameof(captures));

        await Gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadUnsafeAsync(cancellationToken);
            var senders = state.Senders
                .Select(sender => (Sender: sender, Address: SenderAddress.Normalize(sender.Address)))
                .Where(x => x.Address.Length > 0)
                .GroupBy(x => x.Address, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.First().Sender, StringComparer.Ordinal);
            var result = new List<SmsCaptureCandidate>();

            foreach (var capture in captures.OrderByDescending(x => x.ReceivedAtUtc))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var address = capture.SenderAddress?.Trim() ?? string.Empty;
                var body = capture.Body?.Trim() ?? string.Empty;
                var normalized = SenderAddress.Normalize(address);
                if (!senders.TryGetValue(normalized, out var sender) || body.Length is 0 or > MaxBodyLength) continue;

                var receivedAtUtc = capture.ReceivedAtUtc.ToUniversalTime();
                var key = CreateKey(address, body, receivedAtUtc);
                var alreadyStored = state.Messages.Any(message => message.DeduplicationKey == key
                    || IsSameMessage(message.SenderId, message.Body, message.ReceivedAtUtc,
                        sender.Id, body, receivedAtUtc));
                var bodyHash = CreateBodyHash(body);
                var alreadyDelivered = state.DeliveryHistory.Any(message => message.SenderId == sender.Id
                    && string.Equals(message.BodyHash, bodyHash, StringComparison.Ordinal)
                    && (message.ReceivedAtUtc - receivedAtUtc).Duration() <= RecoveryDeduplicationWindow);
                var alreadyListed = result.Any(message =>
                    SenderAddress.Normalize(message.SenderAddress) == normalized
                    && string.Equals(CreateBodyHash(message.Body), bodyHash, StringComparison.Ordinal)
                    && (message.ReceivedAtUtc - receivedAtUtc).Duration() <= RecoveryDeduplicationWindow);
                if (!alreadyStored && !alreadyDelivered && !alreadyListed)
                    result.Add(capture with { SenderAddress = address, Body = body, ReceivedAtUtc = receivedAtUtc });
            }

            return result;
        }
        finally { Gate.Release(); }
    }

    private async Task<SmsMonitorState> LoadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_statePath))
        {
            return new SmsMonitorState();
        }

        if (new FileInfo(_statePath).Length > MaxStateBytes)
            throw new InvalidDataException("حجم فایل پیامک‌ها از حد مجاز بیشتر است. داده‌های موجود حفظ شدند.");
        var data = await File.ReadAllBytesAsync(_statePath, cancellationToken);
        var isEncrypted = SmsStateProtection.IsEncrypted(data);
        _requiresExistingKey = isEncrypted;
        byte[]? key = null;
        SmsMonitorState state;
        try
        {
            if (isEncrypted)
            {
                key = await GetStorageKeyAsync(cancellationToken);
                data = SmsStateProtection.Decrypt(data, key);
            }
            state = JsonSerializer.Deserialize<SmsMonitorState>(data, JsonOptions)
                ?? throw new InvalidDataException("فایل اطلاعات پیامک قابل خواندن نیست.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(data);
            if (key is not null) CryptographicOperations.ZeroMemory(key);
        }
        if (state.Version != 1)
        {
            throw new InvalidDataException("نسخه فایل اطلاعات پیامک پشتیبانی نمی‌شود.");
        }

        state.Templates ??= [];
        state.TitleHistory ??= [];
        state.DeliveryHistory ??= [];
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

        // Upgrade existing installs only after the original state was read successfully.
        if (!isEncrypted) await SaveUnsafeAsync(state, cancellationToken);
        return state;
    }

    private static void TrackTitleUsage(SmsMonitorState state, Guid senderId, string title)
    {
        var usage = state.TitleHistory.FirstOrDefault(x => x.SenderId == senderId
            && string.Equals(x.Title, title, StringComparison.OrdinalIgnoreCase));
        if (usage is null)
        {
            state.TitleHistory.Add(new SmsTitleUsage
            {
                SenderId = senderId,
                Title = title,
                LastUsedAtUtc = DateTimeOffset.UtcNow
            });
        }
        else
        {
            usage.Title = title;
            usage.UseCount = Math.Min(10_000, usage.UseCount + 1);
            usage.LastUsedAtUtc = DateTimeOffset.UtcNow;
        }

        if (state.TitleHistory.Count <= MaxTitleHistory) return;
        var keep = state.TitleHistory
            .OrderByDescending(x => x.LastUsedAtUtc)
            .ThenByDescending(x => x.UseCount)
            .Take(MaxTitleHistory)
            .ToHashSet();
        state.TitleHistory.RemoveAll(x => !keep.Contains(x));
    }

    private async Task SaveUnsafeAsync(SmsMonitorState state, CancellationToken cancellationToken)
    {
        var temporaryPath = _statePath + ".tmp";
        var data = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
        byte[]? key = null;
        try
        {
            if (data.LongLength > MaxStateBytes - 64)
                throw new InvalidDataException("حجم داده‌های پیامک از حد مجاز بیشتر است. داده‌های قبلی حفظ شدند.");
            key = await GetStorageKeyAsync(cancellationToken);
            var encrypted = SmsStateProtection.Encrypt(data, key);
            await using (var stream = new FileStream(
                         temporaryPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         4096,
                         FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(encrypted, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporaryPath, _statePath, true);
            _requiresExistingKey = true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(data);
            if (key is not null) CryptographicOperations.ZeroMemory(key);
        }
    }

    private Task<byte[]> GetStorageKeyAsync(CancellationToken cancellationToken) =>
        _storageKeyProvider is not null
            ? _storageKeyProvider(cancellationToken)
            : SmsStateProtection.GetKeyAsync(cancellationToken, createIfMissing: !_requiresExistingKey);

    private static bool Prune(SmsMonitorState state, DateTimeOffset nowUtc)
    {
        var cutoff = nowUtc.Subtract(Retention);
        var removable = state.Messages.Where(x => x.ReceivedAtUtc < cutoff
            && x.DeliveryStatus != SmsDeliveryStatus.Pending).ToList();
        foreach (var message in removable.Where(x => x.DeliveryStatus == SmsDeliveryStatus.Sent))
            RememberDelivered(state, message, message.ConfirmedAtUtc ?? nowUtc);
        var changed = state.Messages.RemoveAll(x => removable.Contains(x)) > 0;
        changed |= PruneDeliveryHistory(state, nowUtc);
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
        foreach (var message in state.Messages.Where(x => removable.Contains(x.Id)
                     && x.DeliveryStatus == SmsDeliveryStatus.Sent))
            RememberDelivered(state, message, message.ConfirmedAtUtc ?? DateTimeOffset.UtcNow);
        state.Messages.RemoveAll(x => removable.Contains(x.Id));
    }

    private static bool IsSameMessage(
        Guid existingSenderId,
        string existingBody,
        DateTimeOffset existingReceivedAtUtc,
        Guid candidateSenderId,
        string candidateBody,
        DateTimeOffset candidateReceivedAtUtc) =>
        existingSenderId == candidateSenderId
        && string.Equals(CreateBodyHash(existingBody), CreateBodyHash(candidateBody), StringComparison.Ordinal)
        && (existingReceivedAtUtc - candidateReceivedAtUtc).Duration() <= RecoveryDeduplicationWindow;

    private static string CreateBodyHash(string body)
    {
        var normalized = DeduplicationWhitespace.Replace(
                body.Normalize(NormalizationForm.FormKC)
                    .Replace("\u200c", string.Empty, StringComparison.Ordinal)
                    .Replace("\u200f", string.Empty, StringComparison.Ordinal)
                    .Replace("\ufeff", string.Empty, StringComparison.Ordinal), " ")
            .Trim();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    private static void RememberDelivered(SmsMonitorState state, CapturedSms message, DateTimeOffset deliveredAtUtc)
    {
        var bodyHash = CreateBodyHash(message.Body);
        if (state.DeliveryHistory.Any(item => item.SenderId == message.SenderId
            && string.Equals(item.BodyHash, bodyHash, StringComparison.Ordinal)
            && (item.ReceivedAtUtc - message.ReceivedAtUtc).Duration() <= RecoveryDeduplicationWindow)) return;
        state.DeliveryHistory.Add(new DeliveredSmsFingerprint
        {
            SenderId = message.SenderId,
            BodyHash = bodyHash,
            ReceivedAtUtc = message.ReceivedAtUtc.ToUniversalTime(),
            DeliveredAtUtc = deliveredAtUtc.ToUniversalTime()
        });
        if (state.DeliveryHistory.Count <= MaxDeliveryHistory) return;
        state.DeliveryHistory = state.DeliveryHistory
            .OrderByDescending(x => x.DeliveredAtUtc)
            .Take(MaxDeliveryHistory)
            .ToList();
    }

    private static bool PruneDeliveryHistory(SmsMonitorState state, DateTimeOffset nowUtc)
    {
        var cutoff = nowUtc.Subtract(DeliveryHistoryRetention);
        return state.DeliveryHistory.RemoveAll(x => x.DeliveredAtUtc < cutoff) > 0;
    }

    private static string CreateKey(string senderAddress, string body, DateTimeOffset receivedAtUtc)
        => new SmsEnvelope(senderAddress, body, receivedAtUtc.ToUnixTimeMilliseconds()).Key;
}
