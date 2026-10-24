using Donysh.Auth.Domain;
using Donysh.Shared;
using Microsoft.AspNetCore.Identity;

namespace Donysh.Auth.Security;

public static class MobileSignInApi
{
    public static void MapMobileSignIn(this WebApplication app) => app.MapPost("/api/mobile/sign-in",
        async (HttpContext http, MobileCredentials? input, UserManager<AuthUser> users,
            SignInManager<AuthUser> signIn, IConfiguration configuration) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            if (!http.Request.IsHttps) return Results.BadRequest();
            if (input is null) return Results.BadRequest();
            var identifier = input.Identifier?.Trim();
            if (string.IsNullOrWhiteSpace(identifier) || identifier.Length > 254 || identifier.Any(char.IsControl)
                || string.IsNullOrEmpty(input.Password) || input.Password.Length > 256
                || input.InstallationId is not { Length: 64 } || !input.InstallationId.All(Uri.IsHexDigit))
                return Results.BadRequest();
            var user = identifier.Contains('@')
                ? await users.FindByEmailAsync(identifier) : await users.FindByNameAsync(identifier);
            if (user is null || user.IsDeactivated
                || !(await signIn.CheckPasswordSignInAsync(user, input.Password, lockoutOnFailure: true)).Succeeded)
                return Results.Unauthorized();
            var secret = configuration["CrossAppLogout:Secret"] ?? string.Empty;
            var ticket = MobileSignInTicket.Create(secret, user.Id, user.Email ?? string.Empty,
                user.DisplayName, input.InstallationId, user.EmailConfirmed);
            return Results.Ok(new MobileSignInResponse(ticket, user.Id, user.DisplayName, user.Email ?? identifier,
                LifeEnabled: true));
        }).AllowAnonymous().RequireRateLimiting("account");
}

public sealed record MobileCredentials(string Identifier, string Password, string InstallationId);
public sealed record MobileSignInResponse(string Ticket, string Subject, string DisplayName, string Email,
    bool LifeEnabled);
