using HesabYar.Web.Configuration;
using HesabYar.Web.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace HesabYar.Web.Pages.Account;

[Authorize]
public sealed class LogoutModel(
    SignInManager<ApplicationUser> signInManager,
    IOptions<CentralAuthOptions> centralAuthOptions) : PageModel
{
    public async Task<IActionResult> OnPostAsync()
    {
        await signInManager.SignOutAsync();
        if (centralAuthOptions.Value.Enabled)
        {
            return SignOut(
                new AuthenticationProperties { RedirectUri = Url.Page("/Account/Login") },
                CentralAuthOptions.Scheme);
        }

        return RedirectToPage("/Account/Login");
    }
}
