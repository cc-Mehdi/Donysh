using HesabYar.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HesabYar.Web.Pages.Workspaces;

public sealed class SwitchModel(IWorkspaceContext workspaceContext) : PageModel
{
    public async Task<IActionResult> OnPostAsync(Guid workspaceId, string? returnUrl, CancellationToken cancellationToken)
    {
        if (!await workspaceContext.SetCurrentAsync(workspaceId, cancellationToken))
        {
            return Forbid();
        }

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        return RedirectToPage("/Index");
    }
}
