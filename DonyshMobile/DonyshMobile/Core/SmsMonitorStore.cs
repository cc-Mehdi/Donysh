using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DonyshMobile.Core;

public sealed class SmsMonitorStore
{
    private const int MaxSenders = 50;
    private const int MaxMessages = 2000;
    private const int MaxAddressLength = 160;
    private const int MaxBodyLength = 10_000;
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
            if (state.Senders.Any(x => x.NormalizedAddress == normalized))
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
                .Where(x => x.SenderId == senderId)
                .OrderByDescending(x => x.ReceivedAtUtc)
                .ToList();
        }
        finally
        {
            Gate.Release();
        }
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
            var sender = state.Senders.SingleOrDefault(x => x.NormalizedAddress == normalized);
            if (sender is null)
            {
                return false;
            }

            var key = CreateKey(normalized, body, receivedAtUtc);
            if (state.Messages.Any(x => x.DeduplicationKey == key))
            {
                return false;
            }

            state.Messages.Add(new CapturedSms
            {
                SenderId = sender.Id,
                DeduplicationKey = key,
                Body = body.Trim(),
                ReceivedAtUtc = receivedAtUtc.ToUniversalTime()
            });

            if (state.Messages.Count > MaxMessages)
            {
                state.Messages = state.Messages
                    .OrderByDescending(x => x.ReceivedAtUtc)
                    .Take(MaxMessages)
                    .ToList();
            }

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
        return state.Messages.RemoveAll(x => x.ReceivedAtUtc < cutoff) > 0;
    }

    private static string CreateKey(string normalizedAddress, string body, DateTimeOffset receivedAtUtc)
    {
        var value = $"{normalizedAddress}\n{receivedAtUtc.ToUnixTimeMilliseconds()}\n{body}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
