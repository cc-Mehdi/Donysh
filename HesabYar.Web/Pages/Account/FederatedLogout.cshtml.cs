using HesabYar.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HesabYar.Web.Pages.Account;

[AllowAnonymous]
public sealed class FederatedLogoutModel(IConfiguration configuration) : PageModel
{
    public async Task<IActionResult> OnGetAsync(string? proof)
    {
        var secret = configuration["CrossAppLogout:Secret"];
        if (!CrossAppLogoutToken.TryValidate(proof, secret ?? string.Empty, "finance")) return NotFound();

        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        Response.Headers.CacheControl = "no-store, no-cache";
        return RedirectToPage("/Account/SignedOut");
    }
}
