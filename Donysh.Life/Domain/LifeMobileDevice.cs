namespace Donysh.Life.Domain;

public sealed class LifeMobileDevice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserSubject { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? PairCodeHash { get; set; }
    public string? TokenHash { get; set; }
    public DateTimeOffset PairExpiresAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSeenAtUtc { get; set; }
    public bool Revoked { get; set; }
}
