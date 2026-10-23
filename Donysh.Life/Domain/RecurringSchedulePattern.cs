namespace Donysh.Life.Domain;

public sealed class RecurringSchedulePattern
{
    public Guid Id { get; set; }
    public string UserSubject { get; set; } = string.Empty;
    public Guid ActivityId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public int StartMinute { get; set; }
    public int DurationMinutes { get; set; }
    public string? Note { get; set; }
    public int? ReminderMinutesBefore { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public LifeActivity Activity { get; set; } = null!;
    public ICollection<RecurringScheduleSlot> Slots { get; set; } = [];
    public ICollection<ScheduleOccurrenceOverride> OccurrenceOverrides { get; set; } = [];
    public ICollection<ScheduleOccurrenceRecord> OccurrenceRecords { get; set; } = [];
}
