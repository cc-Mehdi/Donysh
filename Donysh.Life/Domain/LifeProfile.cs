namespace Donysh.Life.Domain;

public sealed class LifeProfile
{
    public Guid Id { get; set; }
    public string UserSubject { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = "Asia/Tehran";
    public DayOfWeek WeekStartsOn { get; set; } = DayOfWeek.Saturday;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
