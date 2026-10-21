using Donysh.Auth.Domain;
using Donysh.Auth.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace Donysh.Auth.Pages.Account;

[Authorize]
public sealed class LogoutModel(SignInManager<AuthUser> signInManager, IConfiguration configuration) : PageModel
{
    public IActionResult OnPost()
    {
        var secret = configuration["CrossAppLogout:Secret"];
        var financeUrl = configuration["Products:FinanceUrl"];
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
            throw new InvalidOperationException("CrossAppLogout:Secret must contain at least 32 characters.");
        if (!Uri.TryCreate(financeUrl, UriKind.Absolute, out var finance) || finance.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Products:FinanceUrl must be an absolute HTTPS URI.");

        var endpoint = new Uri(finance, "/Account/FederatedLogout").AbsoluteUri;
        var proof = CrossAppLogoutToken.Create(secret, "finance-auth");
        Response.Headers.CacheControl = "no-store, no-cache";
        return Redirect(QueryHelpers.AddQueryString(endpoint, "proof", proof));
    }

    public async Task<IActionResult> OnGetFinalizeAsync(string? token)
    {
        var secret = configuration["CrossAppLogout:Secret"] ?? string.Empty;
        if (!CrossAppLogoutToken.TryValidate(token, secret, "auth-finalize")) return NotFound();
        await signInManager.SignOutAsync();
        Response.Headers.CacheControl = "no-store, no-cache";
        return RedirectToPage("/Account/SignedOut");
    }
}
