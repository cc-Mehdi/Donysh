using Donysh.Life.Configuration;
using Donysh.Life.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Donysh.Life.Pages.Account;

[AllowAnonymous]
public sealed class FederatedLogoutModel(IConfiguration configuration) : PageModel
{
    public async Task<IActionResult> OnGetAsync(string? proof)
    {
        var secret = configuration["CrossAppLogout:Secret"];
        if (!CrossAppLogoutToken.TryValidate(proof, secret ?? string.Empty, "life")) return NotFound();

        await HttpContext.SignOutAsync(LifeAuthOptions.CookieScheme);
        Response.Headers.CacheControl = "no-store, no-cache";
        return RedirectToPage("/Account/SignedOut");
    }
}
