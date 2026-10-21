using Donysh.Auth.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Donysh.Auth.Pages.Account;

[AllowAnonymous]
public sealed class RegistrationSentModel : PageModel
{
    public string ReturnUrl { get; private set; } = "/";

    public void OnGet(string? returnUrl) => ReturnUrl = SafeReturnUrl.Normalize(returnUrl);
}
