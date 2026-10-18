using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Donysh.Auth.Pages;

public sealed class IndexModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Account/Login");
}
