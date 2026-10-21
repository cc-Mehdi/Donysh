using HesabYar.Web.Configuration;
using HesabYar.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.WebUtilities;

namespace HesabYar.Web.Pages.Account;

[Authorize]
public sealed class LogoutModel(
    IOptions<CentralAuthOptions> centralAuthOptions,
    IConfiguration configuration) : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Index");

    public IActionResult OnPost()
    {
        if (centralAuthOptions.Value.Enabled)
        {
            var secret = GetLogoutSecret();
            var continuation = CrossAppLogoutToken.Create(secret, "finance-continue");
            var location = Url.Page("/Account/Logout", "Complete", new { token = continuation })
                ?? "/Account/Logout?handler=Complete";
            Response.StatusCode = StatusCodes.Status303SeeOther;
            Response.Headers.Location = location;
            Response.Headers.CacheControl = "no-store, no-cache";
            return new EmptyResult();
        }

        return SignOut(
            new AuthenticationProperties { RedirectUri = "/Account/Login" },
            IdentityConstants.ApplicationScheme);
    }

    public IActionResult OnGetComplete(string? token)
    {
        var secret = GetLogoutSecret();
        if (!CrossAppLogoutToken.TryValidate(token, secret, "finance-continue")) return NotFound();

        var lifeUrl = configuration["Products:LifeUrl"];
        if (!TryCreateHttpsEndpoint(lifeUrl, "/Account/FederatedLogout", out var endpoint))
            throw new InvalidOperationException("Products:LifeUrl must be an absolute HTTPS URI.");

        var proof = CrossAppLogoutToken.Create(secret, "life");
        var peerLogoutUrl = QueryHelpers.AddQueryString(endpoint, "proof", proof);
        return Redirect(peerLogoutUrl);
    }

    public IActionResult OnGetFinalize(string? token)
    {
        if (!CrossAppLogoutToken.TryValidate(token, GetLogoutSecret(), "finance-finalize")) return NotFound();

        return SignOut(
            new AuthenticationProperties { RedirectUri = "/Account/SignedOut" },
            IdentityConstants.ApplicationScheme,
            CentralAuthOptions.Scheme);
    }

    private string GetLogoutSecret()
    {
        var secret = configuration["CrossAppLogout:Secret"];
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
            throw new InvalidOperationException("CrossAppLogout:Secret must contain at least 32 characters.");
        return secret;
    }

    private static bool TryCreateHttpsEndpoint(string? value, string path, out string endpoint)
    {
        endpoint = string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttps) return false;
        endpoint = new Uri(baseUri, path).AbsoluteUri;
        return true;
    }
}
