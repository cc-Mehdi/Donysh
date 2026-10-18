namespace DonyshMobile.Core;

public sealed class MonitoredSender
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Address { get; set; } = string.Empty;
    public string NormalizedAddress { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CapturedSms
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SenderId { get; set; }
    public string SenderAddress { get; set; } = string.Empty;
    public string DeduplicationKey { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? Title { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public SmsDeliveryStatus DeliveryStatus { get; set; } = SmsDeliveryStatus.Pending;
    public int DeliveryProtocolVersion { get; set; }
    public int DeliveryAttempts { get; set; }
    public DateTimeOffset? ConfirmedAtUtc { get; set; }
    public string? LastDeliveryError { get; set; }
    public bool RecoveredFromInbox { get; set; }
}

public sealed class SmsMonitorState
{
    public int Version { get; set; } = 1;
    public List<MonitoredSender> Senders { get; set; } = [];
    public List<CapturedSms> Messages { get; set; } = [];
    public List<SmsTemplate> Templates { get; set; } = [];
    public List<SmsTitleUsage> TitleHistory { get; set; } = [];
    public DateTimeOffset NextSyncAtUtc { get; set; }
    public int ConsecutiveSyncFailures { get; set; }
    public string SyncStatus { get; set; } = "هنوز پیامی به سامانه ارسال نشده است.";
}

public sealed class SmsTitleUsage
{
    public Guid SenderId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int UseCount { get; set; } = 1;
    public DateTimeOffset LastUsedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public enum SmsDeliveryStatus
{
    Pending,
    Sent,
    Rejected,
    Excluded
}

public sealed record SmsCaptureCandidate(
    string SenderAddress,
    string Body,
    DateTimeOffset ReceivedAtUtc,
    bool RecoveredFromInbox = false);

public sealed record SmsRecoveryResult(
    int Examined,
    int Added,
    bool PermissionRequired = false,
    bool Skipped = false);

public sealed record PendingSms(
    Guid Id,
    string Key,
    string Sender,
    string Body,
    long ReceivedAtUnixMs,
    ParsedTransaction? Parsed,
    string? Title);

public sealed record SmsDeliveryReceipt(string Key, string Status);

public sealed record SmsSyncSummary(
    int Pending,
    int Sent,
    int Rejected,
    DateTimeOffset NextAttemptAtUtc,
    string Status);

public sealed record SenderSummary(
    Guid Id,
    string Address,
    int MessageCount,
    DateTimeOffset? LastMessageAtUtc);

public enum AddSenderResult
{
    Added,
    Duplicate,
    Invalid,
    LimitReached
}
