namespace Donysh.Life.Domain;

public sealed class ScheduleOccurrenceOverride
{
    public Guid Id { get; set; }
    public Guid PatternId { get; set; }
    public string UserSubject { get; set; } = string.Empty;
    public DateOnly OriginalDate { get; set; }
    public DateOnly ScheduledDate { get; set; }
    public int StartMinute { get; set; }
    public int DurationMinutes { get; set; }
    public bool IsCancelled { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public RecurringSchedulePattern Pattern { get; set; } = null!;
}
