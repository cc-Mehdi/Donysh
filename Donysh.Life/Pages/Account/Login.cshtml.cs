using Donysh.Life.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Donysh.Life.Pages.Account;

public sealed class LoginModel : PageModel
{
    public bool AuthError { get; private set; }

    public IActionResult OnGet(string? authError = null)
    {
        AuthError = HasAuthenticationError(authError);
        return User.Identity?.IsAuthenticated == true ? LocalRedirect("/") : Page();
    }

    public static bool HasAuthenticationError(string? value) =>
        string.Equals(value, "1", StringComparison.Ordinal)
        || string.Equals(value, bool.TrueString, StringComparison.OrdinalIgnoreCase);

    public IActionResult OnPost() => Challenge(new AuthenticationProperties
    {
        RedirectUri = "/"
    }, LifeAuthOptions.OidcScheme);
}
