using HesabYar.Web.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace HesabYar.Web.Pages.Account;

[Authorize]
public sealed class LogoutModel(
    IOptions<CentralAuthOptions> centralAuthOptions) : PageModel
{
    public IActionResult OnPost()
    {
        if (centralAuthOptions.Value.Enabled)
        {
            return SignOut(
                new AuthenticationProperties { RedirectUri = "/" },
                IdentityConstants.ApplicationScheme,
                CentralAuthOptions.Scheme);
        }

        return SignOut(
            new AuthenticationProperties { RedirectUri = "/Account/Login" },
            IdentityConstants.ApplicationScheme);
    }
}
