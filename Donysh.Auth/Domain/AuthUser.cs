using Microsoft.AspNetCore.Identity;

namespace Donysh.Auth.Domain;

public sealed class AuthUser : IdentityUser
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool NotificationsEnabled { get; set; }
    public bool IsDeactivated { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
