using Donysh.Life.Configuration;
using Donysh.Life.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace Donysh.Life.Pages.Account;

[Authorize]
public sealed class LogoutModel(IConfiguration configuration) : PageModel
{
    public IActionResult OnGet() => Redirect("/");

    public IActionResult OnPost()
    {
        var continuation = CrossAppLogoutToken.Create(GetLogoutSecret(), "life-continue");
        var location = Url.Page("/Account/Logout", "Complete", new { token = continuation })
            ?? "/Account/Logout?handler=Complete";
        Response.StatusCode = StatusCodes.Status303SeeOther;
        Response.Headers.Location = location;
        Response.Headers.CacheControl = "no-store, no-cache";
        return new EmptyResult();
    }

    public IActionResult OnGetComplete(string? token)
    {
        var secret = GetLogoutSecret();
        if (!CrossAppLogoutToken.TryValidate(token, secret, "life-continue")) return NotFound();

        var financeUrl = configuration["Products:FinanceUrl"];
        if (!TryCreateHttpsEndpoint(financeUrl, "/Account/FederatedLogout", out var endpoint))
            throw new InvalidOperationException("Products:FinanceUrl must be an absolute HTTPS URI.");

        var proof = CrossAppLogoutToken.Create(secret, "finance");
        var peerLogoutUrl = QueryHelpers.AddQueryString(endpoint, "proof", proof);
        return SignOut(new AuthenticationProperties
        {
            RedirectUri = peerLogoutUrl
        }, LifeAuthOptions.CookieScheme, LifeAuthOptions.OidcScheme);
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
