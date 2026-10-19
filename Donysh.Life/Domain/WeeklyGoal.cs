namespace Donysh.Life.Domain;

public sealed class WeeklyGoal
{
    public Guid Id { get; set; }
    public Guid ActivityId { get; set; }
    public string UserSubject { get; set; } = string.Empty;
    public int TargetMinutes { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public LifeActivity Activity { get; set; } = null!;
}
