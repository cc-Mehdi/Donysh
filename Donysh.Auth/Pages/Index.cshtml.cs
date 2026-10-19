using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Donysh.Auth.Configuration;

namespace Donysh.Auth.Pages;

[Authorize]
public sealed class IndexModel(IOptions<AuthNavigationOptions> navigationOptions) : PageModel
{
    public IActionResult OnGet()
    {
        var destination = navigationOptions.Value.DefaultReturnUrl;
        return Uri.TryCreate(destination, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Fragment)
                ? Redirect(uri.AbsoluteUri)
                : Page();
    }
}
