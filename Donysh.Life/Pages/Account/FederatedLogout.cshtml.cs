using Donysh.Life.Configuration;
using Donysh.Life.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace Donysh.Life.Pages.Account;

[AllowAnonymous]
public sealed class FederatedLogoutModel(IConfiguration configuration) : PageModel
{
    public async Task<IActionResult> OnGetAsync(string? proof)
    {
        var secret = configuration["CrossAppLogout:Secret"];
        var fromAuth = CrossAppLogoutToken.TryValidate(proof, secret ?? string.Empty, "life-auth");
        if (!fromAuth && !CrossAppLogoutToken.TryValidate(proof, secret ?? string.Empty, "life")) return NotFound();

        await HttpContext.SignOutAsync(LifeAuthOptions.CookieScheme);
        Response.Headers.CacheControl = "no-store, no-cache";
        var destination = configuration[fromAuth ? "Products:AuthUrl" : "Products:FinanceUrl"];
        if (!Uri.TryCreate(destination, UriKind.Absolute, out var origin)
            || origin.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException("The logout return URL must be an absolute HTTPS URI.");
        var endpoint = new Uri(origin, "/Account/Logout").AbsoluteUri;
        var token = CrossAppLogoutToken.Create(secret!, fromAuth ? "auth-finalize" : "finance-finalize");
        return Redirect(QueryHelpers.AddQueryString(endpoint, new Dictionary<string, string?>
        {
            ["handler"] = "Finalize",
            ["token"] = token
        }));
    }
}
