using Donysh.Life.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Donysh.Life.Pages.Account;

public sealed class LoginModel : PageModel
{
    public bool AuthError { get; private set; }

    public IActionResult OnGet(bool authError = false)
    {
        AuthError = authError;
        return User.Identity?.IsAuthenticated == true ? LocalRedirect("/") : Page();
    }

    public IActionResult OnPost() => Challenge(new AuthenticationProperties
    {
        RedirectUri = "/"
    }, LifeAuthOptions.OidcScheme);
}
