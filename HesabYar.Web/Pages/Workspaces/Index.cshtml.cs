using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using HesabYar.Web.Data;
using HesabYar.Web.Domain;
using HesabYar.Web.Helpers;
using HesabYar.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace HesabYar.Web.Pages.Workspaces;

public sealed class IndexModel(
    ApplicationDbContext db,
    IWorkspaceContext workspaceContext,
    UserManager<ApplicationUser> userManager,
    BudgetRolloverService budgetRolloverService) : PageModel
{
    [BindProperty]
    public CreateWorkspaceInput CreateWorkspace { get; set; } = new();

    [BindProperty]
    public EditWorkspaceInput EditWorkspace { get; set; } = new();

    [BindProperty]
    public InviteInput Invite { get; set; } = new();

    public IReadOnlyList<WorkspaceCard> Workspaces { get; private set; } = [];
    public Guid? CurrentWorkspaceId { get; private set; }
    public string CurrentBudgetPeriodTitle { get; private set; } = string.Empty;
    public string? InviteLink => TempData.Peek("InviteLink") as string;

    public sealed class CreateWorkspaceInput
    {
        [Required(ErrorMessage = "نام فضای مشترک را وارد کنید.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "نام فضا باید بین ۲ تا ۱۰۰ کاراکتر باشد.")]
        public string Name { get; set; } = string.Empty;
    }

    public sealed class EditWorkspaceInput
    {
        [Required]
        public Guid Id { get; set; }

        [Required(ErrorMessage = "نام فضای مالی را وارد کنید.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "نام فضا باید بین ۲ تا ۱۰۰ کاراکتر باشد.")]
        public string Name { get; set; } = string.Empty;
    }

    public sealed class InviteInput
    {
        [Required]
        public Guid WorkspaceId { get; set; }

        [Required(ErrorMessage = "ایمیل عضو را وارد کنید.")]
        [EmailAddress(ErrorMessage = "ایمیل معتبر نیست.")]
        public string Email { get; set; } = string.Empty;
    }

    public sealed record MemberRow(string UserId, string DisplayName, string Email, WorkspaceRole Role);
    public sealed record WorkspaceCard(
        Workspace Workspace,
        WorkspaceRole MyRole,
        IReadOnlyList<MemberRow> Members,
        decimal CurrentMonthBudgetTotal,
        int CurrentMonthBudgetCount);

    public async Task OnGetAsync(CancellationToken cancellationToken) => await LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        ModelState.Clear();
        if (!TryValidateModel(CreateWorkspace, nameof(CreateWorkspace)))
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        var userId = workspaceContext.UserId!;
        var workspace = new Workspace
        {
            Name = CreateWorkspace.Name.Trim(),
            Type = WorkspaceType.Shared,
            OwnerUserId = userId
        };

        db.Workspaces.Add(workspace);
        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            WorkspaceId = workspace.Id,
            UserId = userId,
            Role = WorkspaceRole.Owner
        });
        db.ExpenseCategories.AddRange(DefaultCategories.For(workspace.Id));

        await db.SaveChangesAsync(cancellationToken);
        await workspaceContext.SetCurrentAsync(workspace.Id, cancellationToken);

        TempData["Success"] = "فضای مالی مشترک ساخته شد.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEditWorkspaceAsync(CancellationToken cancellationToken)
    {
        ModelState.Clear();
        if (!TryValidateModel(EditWorkspace, nameof(EditWorkspace)))
        {
            TempData["Error"] = "نام فضای مالی باید بین ۲ تا ۱۰۰ کاراکتر باشد.";
            return RedirectToPage();
        }

        if (!await workspaceContext.IsOwnerAsync(EditWorkspace.Id, cancellationToken))
        {
            return Forbid();
        }

        var workspace = await db.Workspaces.SingleOrDefaultAsync(
            x => x.Id == EditWorkspace.Id,
            cancellationToken);

        if (workspace is null)
        {
            return NotFound();
        }

        workspace.Name = EditWorkspace.Name.Trim();
        await db.SaveChangesAsync(cancellationToken);

        TempData["Success"] = "نام فضای مالی ویرایش شد.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteWorkspaceAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await workspaceContext.IsOwnerAsync(id, cancellationToken))
        {
            return Forbid();
        }

        var userId = workspaceContext.UserId!;
        var workspace = await db.Workspaces.SingleOrDefaultAsync(
            x => x.Id == id && x.OwnerUserId == userId,
            cancellationToken);

        if (workspace is null)
        {
            return NotFound();
        }

        var remainingWorkspaceId = await db.WorkspaceMembers
            .Where(x => x.UserId == userId && x.WorkspaceId != id)
            .OrderBy(x => x.Workspace.Type)
            .ThenBy(x => x.Workspace.CreatedAtUtc)
            .Select(x => (Guid?)x.WorkspaceId)
            .FirstOrDefaultAsync(cancellationToken);

        if (!remainingWorkspaceId.HasValue)
        {
            TempData["Error"] = "آخرین فضای مالی حساب قابل حذف نیست. ابتدا یک فضای مالی دیگر بسازید.";
            return RedirectToPage();
        }

        var currentWorkspaceId = (await workspaceContext.GetCurrentAsync(cancellationToken))?.Id;

        db.Workspaces.Remove(workspace);
        await db.SaveChangesAsync(cancellationToken);

        if (currentWorkspaceId == id)
        {
            await workspaceContext.SetCurrentAsync(remainingWorkspaceId.Value, cancellationToken);
        }

        TempData["Success"] = "فضای مالی و تمام اطلاعات وابسته به آن حذف شد.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostInviteAsync(CancellationToken cancellationToken)
    {
        ModelState.Clear();
        if (!TryValidateModel(Invite, nameof(Invite)))
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        if (!await workspaceContext.IsOwnerAsync(Invite.WorkspaceId, cancellationToken))
        {
            return Forbid();
        }

        var workspace = await db.Workspaces.SingleOrDefaultAsync(
            x => x.Id == Invite.WorkspaceId && x.Type == WorkspaceType.Shared,
            cancellationToken);

        if (workspace is null)
        {
            return NotFound();
        }

        var email = Invite.Email.Trim().ToLowerInvariant();
        var existingUser = await userManager.FindByEmailAsync(email);

        if (existingUser is not null && await db.WorkspaceMembers.AnyAsync(
                x => x.WorkspaceId == workspace.Id && x.UserId == existingUser.Id,
                cancellationToken))
        {
            ModelState.AddModelError("Invite.Email", "این کاربر عضو فضای مشترک است.");
            await LoadAsync(cancellationToken);
            return Page();
        }

        var oldInvites = await db.WorkspaceInvitations
            .Where(x => x.WorkspaceId == workspace.Id &&
                        x.Email == email &&
                        x.AcceptedAtUtc == null)
            .ToListAsync(cancellationToken);

        db.WorkspaceInvitations.RemoveRange(oldInvites);

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

        db.WorkspaceInvitations.Add(new WorkspaceInvitation
        {
            WorkspaceId = workspace.Id,
            Email = email,
            Token = token,
            InvitedByUserId = workspaceContext.UserId!,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7)
        });

        await db.SaveChangesAsync(cancellationToken);

        TempData["InviteLink"] = Url.Page(
            "/Workspaces/Accept",
            null,
            new { token },
            Request.Scheme);

        TempData["Success"] = "لینک دعوت ساخته شد و تا ۷ روز معتبر است.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveMemberAsync(
        Guid workspaceId,
        string userId,
        CancellationToken cancellationToken)
    {
        if (!await workspaceContext.IsOwnerAsync(workspaceId, cancellationToken))
        {
            return Forbid();
        }

        var workspace = await db.Workspaces.SingleOrDefaultAsync(
            x => x.Id == workspaceId && x.Type == WorkspaceType.Shared,
            cancellationToken);

        if (workspace is null)
        {
            return NotFound();
        }

        if (workspace.OwnerUserId == userId)
        {
            TempData["Error"] = "مالک فضای مالی قابل حذف نیست.";
            return RedirectToPage();
        }

        var member = await db.WorkspaceMembers.SingleOrDefaultAsync(
            x => x.WorkspaceId == workspaceId && x.UserId == userId,
            cancellationToken);

        if (member is not null)
        {
            db.WorkspaceMembers.Remove(member);
            await db.SaveChangesAsync(cancellationToken);
            TempData["Success"] = "عضو از فضای مشترک حذف شد.";
        }

        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var userId = workspaceContext.UserId!;
        CurrentWorkspaceId = (await workspaceContext.GetCurrentAsync(cancellationToken))?.Id;

        // Query from Workspace instead of WorkspaceMember. Starting from a membership and
        // including Workspace -> Members creates a navigation cycle that EF Core rejects for
        // no-tracking queries at runtime.
        var workspaces = await db.Workspaces
            .Where(x => x.Members.Any(member => member.UserId == userId))
            .Include(x => x.Members)
                .ThenInclude(x => x.User)
            .OrderBy(x => x.Type)
            .ThenBy(x => x.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.Now);
        var currentPeriod = PersianCalendarHelper.GetYearMonth(today);
        CurrentBudgetPeriodTitle = PersianCalendarHelper.Title(currentPeriod);

        if (CurrentWorkspaceId.HasValue)
        {
            await budgetRolloverService.EnsureCurrentPeriodAsync(CurrentWorkspaceId.Value, currentPeriod, cancellationToken);
        }

        var workspaceIds = workspaces.Select(x => x.Id).ToList();
        var budgetRows = await db.Budgets
            .Where(x => workspaceIds.Contains(x.WorkspaceId) &&
                        x.Year == currentPeriod.Year &&
                        x.Month == currentPeriod.Month)
            .Select(x => new { x.WorkspaceId, x.Amount })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var budgetByWorkspace = budgetRows
            .GroupBy(x => x.WorkspaceId)
            .ToDictionary(
                group => group.Key,
                group => new
                {
                    Total = group.Sum(x => x.Amount),
                    Count = group.Count()
                });

        Workspaces = workspaces.Select(workspace =>
        {
            var myMembership = workspace.Members.Single(x => x.UserId == userId);

            var members = workspace.Members
                .OrderBy(x => x.Role)
                .ThenBy(x => x.User.DisplayName)
                .Select(x => new MemberRow(
                    x.UserId,
                    string.IsNullOrWhiteSpace(x.User.DisplayName)
                        ? x.User.Email ?? "کاربر"
                        : x.User.DisplayName,
                    x.User.Email ?? string.Empty,
                    x.Role))
                .ToList();

            var budget = budgetByWorkspace.GetValueOrDefault(workspace.Id);
            return new WorkspaceCard(
                workspace,
                myMembership.Role,
                members,
                budget?.Total ?? 0,
                budget?.Count ?? 0);
        }).ToList();
    }
}
