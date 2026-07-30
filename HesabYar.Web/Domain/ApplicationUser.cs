using Microsoft.AspNetCore.Identity;

namespace HesabYar.Web.Domain;

public sealed class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<WorkspaceMember> WorkspaceMemberships { get; set; } = [];
}
