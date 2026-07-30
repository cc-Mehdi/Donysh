using HesabYar.Web.Data;
using HesabYar.Web.Domain;
using HesabYar.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace HesabYar.Web.Pages.Workspaces;

public sealed class AcceptModel(
    ApplicationDbContext db,
    IWorkspaceContext workspaceContext,
    UserManager<ApplicationUser> userManager) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string Token { get; set; } = string.Empty;

    public WorkspaceInvitation Invitation { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var result = await LoadAndValidateAsync(cancellationToken);
        return result ?? Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var result = await LoadAndValidateAsync(cancellationToken);
        if (result is not null)
        {
            return result;
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var exists = await db.WorkspaceMembers.AnyAsync(
            x => x.WorkspaceId == Invitation.WorkspaceId && x.UserId == user.Id,
            cancellationToken);
        if (!exists)
        {
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                WorkspaceId = Invitation.WorkspaceId,
                UserId = user.Id,
                Role = WorkspaceRole.Member
            });
        }

        Invitation.AcceptedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await workspaceContext.SetCurrentAsync(Invitation.WorkspaceId, cancellationToken);
        TempData["Success"] = $"به فضای مشترک «{Invitation.Workspace.Name}» اضافه شدید.";
        return RedirectToPage("/Index");
    }

    private async Task<IActionResult?> LoadAndValidateAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Token))
        {
            return NotFound();
        }

        Invitation = await db.WorkspaceInvitations
            .Include(x => x.Workspace)
            .SingleOrDefaultAsync(x => x.Token == Token, cancellationToken) ?? null!;
        if (Invitation is null)
        {
            return NotFound();
        }

        if (Invitation.AcceptedAtUtc.HasValue)
        {
            TempData["Error"] = "این دعوت قبلاً استفاده شده است.";
            return RedirectToPage("/Workspaces/Index");
        }

        if (Invitation.ExpiresAtUtc <= DateTime.UtcNow)
        {
            TempData["Error"] = "اعتبار این دعوت تمام شده است.";
            return RedirectToPage("/Workspaces/Index");
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        if (!string.Equals(user.Email, Invitation.Email, StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = $"این دعوت برای ایمیل {Invitation.Email} ساخته شده است. با همان ایمیل وارد شوید.";
            return RedirectToPage("/Workspaces/Index");
        }

        return null;
    }
}
