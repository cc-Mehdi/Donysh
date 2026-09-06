using System.ComponentModel.DataAnnotations;
using HesabYar.Web.Domain;

namespace HesabYar.Web.Sms;

public sealed class MobileDevice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = "";
    public Guid WorkspaceId { get; set; }
    public Guid CategoryId { get; set; }
    [MaxLength(80)] public string Name { get; set; } = "";
    [MaxLength(64)] public string? PairCodeHash { get; set; }
    [MaxLength(64)] public string? TokenHash { get; set; }
    public DateTime PairExpiresAtUtc { get; set; }
    public DateTime NextAllowedAtUtc { get; set; } = DateTime.UnixEpoch;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool Revoked { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public Workspace Workspace { get; set; } = null!;
    public ExpenseCategory Category { get; set; } = null!;
}

// No expense FK: deleting a reviewed expense must not allow a retry to recreate it.
public sealed class SmsReceipt
{
    public string UserId { get; set; } = "";
    [MaxLength(64)] public string Key { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public ApplicationUser User { get; set; } = null!;
}
