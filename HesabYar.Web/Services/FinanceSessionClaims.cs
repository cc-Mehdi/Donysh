using System.Security.Claims;
using HesabYar.Web.Domain;
using Microsoft.AspNetCore.Identity;

namespace HesabYar.Web.Services;

public sealed class FinanceSessionPrincipalFactory(
    UserManager<ApplicationUser> users,
    IUserClaimsPrincipalFactory<ApplicationUser> principalFactory)
{
    public async Task<ClaimsPrincipal> CreateAsync(
        ClaimsPrincipal centralPrincipal,
        FinanceAccountLink account)
    {
        var user = await users.FindByIdAsync(account.UserId)
            ?? throw new InvalidOperationException("The linked Finance account no longer exists.");
        var principal = await principalFactory.CreateAsync(user);
        if (principal.Identity is not ClaimsIdentity identity)
            throw new InvalidOperationException("The Finance session identity could not be created.");

        // The Identity principal factory owns the local identifier, authentication
        // type, roles and security stamp. Preserve only the central subject needed
        // to correlate this product session with the central account.
        var subject = centralPrincipal.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(subject))
            throw new InvalidOperationException("The central account subject is unavailable.");
        Replace(identity, "sub", subject);

        return principal;
    }

    private static void Replace(ClaimsIdentity identity, string type, string? value)
    {
        foreach (var claim in identity.FindAll(type).ToArray()) identity.RemoveClaim(claim);
        if (!string.IsNullOrWhiteSpace(value)) identity.AddClaim(new Claim(type, value));
    }
}
