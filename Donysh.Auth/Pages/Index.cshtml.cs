using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Donysh.Auth.Pages;

[Authorize]
public sealed class IndexModel : PageModel
{
    public IActionResult OnGet() => Page();
}
