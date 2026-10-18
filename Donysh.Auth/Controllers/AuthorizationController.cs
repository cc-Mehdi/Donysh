using System.Security.Claims;
using Donysh.Auth.Domain;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Donysh.Auth.Controllers;

public sealed class AuthorizationController(UserManager<AuthUser> users, SignInManager<AuthUser> signInManager) : Controller
{
    [HttpGet("~/connect/authorize"), HttpPost("~/connect/authorize")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Authorize()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request is unavailable.");

        var authentication = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!authentication.Succeeded)
        {
            if ((request.Prompt ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains("none", StringComparer.Ordinal))
            {
                return Forbid(new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.LoginRequired,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "ورود کاربر لازم است."
                }), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            }

            return Challenge(new AuthenticationProperties
            {
                RedirectUri = Request.PathBase + Request.Path + Request.QueryString
            }, IdentityConstants.ApplicationScheme);
        }

        var user = await users.GetUserAsync(authentication.Principal!);
        if (user is null || !await signInManager.CanSignInAsync(user))
        {
            await signInManager.SignOutAsync();
            return Challenge(new AuthenticationProperties
            {
                RedirectUri = Request.PathBase + Request.Path + Request.QueryString
            }, IdentityConstants.ApplicationScheme);
        }

        var identity = new ClaimsIdentity(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            Claims.Name,
            Claims.Role);
        identity.SetClaim(Claims.Subject, user.Id)
            .SetClaim(Claims.Name, string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName)
            .SetClaim(Claims.Email, user.Email)
            .SetClaim(Claims.EmailVerified, user.EmailConfirmed.ToString().ToLowerInvariant());
        foreach (var role in await users.GetRolesAsync(user)) identity.AddClaim(Claims.Role, role);
        identity.SetDestinations(ResolveDestinations);

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(request.GetScopes());
        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpGet("~/connect/logout"), HttpPost("~/connect/logout")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return SignOut(new AuthenticationProperties { RedirectUri = "/Account/SignedOut" },
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpGet("~/connect/userinfo"), HttpPost("~/connect/userinfo")]
    [Authorize(AuthenticationSchemes = OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)]
    public async Task<IActionResult> UserInfo()
    {
        var subject = User.GetClaim(Claims.Subject);
        var user = string.IsNullOrWhiteSpace(subject) ? null : await users.FindByIdAsync(subject);
        if (user is null) return Challenge(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        return Ok(new Dictionary<string, object?>
        {
            [Claims.Subject] = user.Id,
            [Claims.Name] = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName,
            [Claims.Email] = user.Email,
            [Claims.EmailVerified] = user.EmailConfirmed
        });
    }

    private static IEnumerable<string> ResolveDestinations(Claim claim) => claim.Type switch
    {
        Claims.Subject => [Destinations.AccessToken, Destinations.IdentityToken],
        Claims.Name when claim.Subject?.HasScope(Scopes.Profile) == true
            => [Destinations.AccessToken, Destinations.IdentityToken],
        Claims.Role when claim.Subject?.HasScope(Scopes.Roles) == true
            => [Destinations.AccessToken, Destinations.IdentityToken],
        Claims.Email or Claims.EmailVerified when claim.Subject?.HasScope(Scopes.Email) == true
            => [Destinations.AccessToken, Destinations.IdentityToken],
        _ => [Destinations.AccessToken]
    };
}
