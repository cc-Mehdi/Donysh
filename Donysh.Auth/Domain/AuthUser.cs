using Microsoft.AspNetCore.Identity;

namespace Donysh.Auth.Domain;

public sealed class AuthUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
