namespace Donysh.Life.Domain;

public enum OccurrenceOutcomeStatus
{
    Completed = 1,
    Missed = 2,
    Partial = 3,
    Moved = 4
}

public sealed class ScheduleOccurrenceRecord
{
    public Guid Id { get; set; }
    public Guid PatternId { get; set; }
    public string UserSubject { get; set; } = string.Empty;
    public DateOnly OriginalDate { get; set; }
    public DateOnly ScheduledDate { get; set; }
    public OccurrenceOutcomeStatus Status { get; set; }
    public int PlannedMinutes { get; set; }
    public int? ActualMinutes { get; set; }
    public DateOnly? MovedToDate { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public RecurringSchedulePattern Pattern { get; set; } = null!;
}
