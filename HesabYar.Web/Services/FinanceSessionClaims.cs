using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace HesabYar.Web.Services;

public static class FinanceSessionClaims
{
    public static void Apply(ClaimsPrincipal principal, FinanceAccountLink account)
    {
        if (principal.Identity is not ClaimsIdentity identity)
            throw new InvalidOperationException("The Finance identity is unavailable.");

        Replace(identity, ClaimTypes.NameIdentifier, account.UserId);

        var securityStampType = new IdentityOptions().ClaimsIdentity.SecurityStampClaimType;
        Replace(identity, securityStampType, account.SecurityStamp);
    }

    private static void Replace(ClaimsIdentity identity, string type, string? value)
    {
        foreach (var claim in identity.FindAll(type).ToArray()) identity.RemoveClaim(claim);
        if (!string.IsNullOrWhiteSpace(value)) identity.AddClaim(new Claim(type, value));
    }
}
