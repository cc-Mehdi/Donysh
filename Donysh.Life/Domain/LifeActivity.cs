namespace Donysh.Life.Domain;

public sealed class LifeActivity
{
    public Guid Id { get; set; }
    public string UserSubject { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string NormalizedTitle { get; set; } = string.Empty;
    public string ColorHex { get; set; } = "#0D9488";
    public string IconKey { get; set; } = "circle";
    public string? Category { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public WeeklyGoal? WeeklyGoal { get; set; }
}
