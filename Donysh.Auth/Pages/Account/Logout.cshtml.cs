using Donysh.Auth.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Donysh.Auth.Pages.Account;

[Authorize]
public sealed class LogoutModel(SignInManager<AuthUser> signInManager) : PageModel
{
    public async Task<IActionResult> OnPostAsync()
    {
        await signInManager.SignOutAsync();
        return RedirectToPage("/Account/SignedOut");
    }
}
