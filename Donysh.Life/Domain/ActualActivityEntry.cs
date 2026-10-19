namespace Donysh.Life.Domain;

public sealed class ActualActivityEntry
{
    public Guid Id { get; set; }
    public string UserSubject { get; set; } = string.Empty;
    public Guid ActivityId { get; set; }
    public DateOnly OccurredDate { get; set; }
    public int? StartMinute { get; set; }
    public int ActualMinutes { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public LifeActivity Activity { get; set; } = null!;
}
