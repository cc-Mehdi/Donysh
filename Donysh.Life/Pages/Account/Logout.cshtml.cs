using Donysh.Life.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Donysh.Life.Pages.Account;

public sealed class LogoutModel : PageModel
{
    public IActionResult OnGet() => Redirect("/");

    public IActionResult OnPost() => SignOut(new AuthenticationProperties
    {
        RedirectUri = "/Account/SignedOut"
    }, LifeAuthOptions.CookieScheme, LifeAuthOptions.OidcScheme);
}
