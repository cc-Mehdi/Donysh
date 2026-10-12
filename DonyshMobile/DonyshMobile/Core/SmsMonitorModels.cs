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
    public string DeduplicationKey { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAtUtc { get; set; }
}

public sealed class SmsMonitorState
{
    public int Version { get; set; } = 1;
    public List<MonitoredSender> Senders { get; set; } = [];
    public List<CapturedSms> Messages { get; set; } = [];
    public List<SmsTemplate> Templates { get; set; } = [];
}

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
