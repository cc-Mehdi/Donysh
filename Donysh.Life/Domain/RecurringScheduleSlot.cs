namespace Donysh.Life.Domain;

public sealed class RecurringScheduleSlot
{
    public Guid PatternId { get; set; }
    public string UserSubject { get; set; } = string.Empty;
    public DayOfWeek DayOfWeek { get; set; }
    public int SlotIndex { get; set; }

    public RecurringSchedulePattern Pattern { get; set; } = null!;
}
