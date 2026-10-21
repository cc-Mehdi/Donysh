using HesabYar.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace HesabYar.Web.Pages.Account;

[AllowAnonymous]
public sealed class FederatedLogoutModel(IConfiguration configuration) : PageModel
{
    public async Task<IActionResult> OnGetAsync(string? proof)
    {
        var secret = configuration["CrossAppLogout:Secret"];
        var fromAuth = CrossAppLogoutToken.TryValidate(proof, secret ?? string.Empty, "finance-auth");
        if (!fromAuth && !CrossAppLogoutToken.TryValidate(proof, secret ?? string.Empty, "finance")) return NotFound();

        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        Response.Headers.CacheControl = "no-store, no-cache";
        var lifeUrl = configuration["Products:LifeUrl"];
        if (!Uri.TryCreate(lifeUrl, UriKind.Absolute, out var life)
            || life.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException("Products:LifeUrl must be an absolute HTTPS URI.");
        var endpoint = new Uri(life, "/Account/Logout").AbsoluteUri;
        var token = CrossAppLogoutToken.Create(secret!, fromAuth ? "life-auth" : "life-finalize");
        return fromAuth
            ? Redirect(QueryHelpers.AddQueryString(new Uri(life, "/Account/FederatedLogout").AbsoluteUri, "proof", token))
            : Redirect(QueryHelpers.AddQueryString(endpoint, new Dictionary<string, string?>
            {
                ["handler"] = "Finalize",
                ["token"] = token
            }));
    }
}
