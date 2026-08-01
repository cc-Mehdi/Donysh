using System.ComponentModel.DataAnnotations;
using HesabYar.Web.Data;
using HesabYar.Web.Domain;
using HesabYar.Web.Helpers;
using HesabYar.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace HesabYar.Web.Pages.Savings;

public sealed class IndexModel(ApplicationDbContext db, IWorkspaceContext workspaceContext) : PageModel
{
    [BindProperty]
    public GoalInput NewGoal { get; set; } = new();

    [BindProperty]
    public ContributionInput Contribution { get; set; } = new();

    public IReadOnlyList<GoalCard> Goals { get; private set; } = [];

    public sealed class GoalInput
    {
        [Required(ErrorMessage = "نام هدف را وارد کنید.")]
        [StringLength(120, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        [Range(1, 999_999_999_999, ErrorMessage = "مبلغ هدف باید بیشتر از صفر باشد.")]
        public decimal TargetAmount { get; set; }

        [Range(0, 999_999_999_999, ErrorMessage = "هدف ماهانه معتبر نیست.")]
        public decimal MonthlyTargetAmount { get; set; }

        public DateOnly? TargetDate { get; set; }
    }

    public sealed class ContributionInput
    {
        [Required]
        public Guid GoalId { get; set; }

        public decimal Amount { get; set; }

        [Required]
        public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Now);

        [StringLength(200)]
        public string? Note { get; set; }
    }

    public sealed record GoalCard(
        SavingsGoal Goal,
        decimal TotalSaved,
        decimal MonthSaved,
        decimal Remaining,
        decimal TotalPercent,
        decimal MonthlyPercent,
        IReadOnlyList<SavingsContribution> RecentContributions);

    public async Task OnGetAsync(CancellationToken cancellationToken) => await LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);

        RemoveModelStatePrefix(nameof(Contribution));

        if (!ModelState.IsValid)
        {
            var errors = ModelState
                .Where(x => x.Value?.Errors.Count > 0)
                .SelectMany(x => x.Value!.Errors)
                .Select(x => x.ErrorMessage)
                .ToList();

            TempData["Error"] = string.Join(" | ", errors);

            await LoadAsync(cancellationToken);
            return Page();
        }

        if (string.IsNullOrWhiteSpace(NewGoal.Name))
        {
            ModelState.AddModelError(
                "NewGoal.Name",
                "نام هدف الزامی است.");

            await LoadAsync(cancellationToken);
            return Page();
        }

        var goalName = NewGoal.Name.Trim();

        var duplicateGoal = await db.SavingsGoals
            .AnyAsync(
                x => x.WorkspaceId == workspace.Id &&
                     x.Name.Trim() == goalName,
                cancellationToken);

        if (duplicateGoal)
        {
            ModelState.AddModelError(
                "NewGoal.Name",
                "هدف پس‌اندازی با این عنوان قبلاً ثبت شده است.");

            await LoadAsync(cancellationToken);
            return Page();
        }

        db.SavingsGoals.Add(new SavingsGoal
        {
            WorkspaceId = workspace.Id,
            Name = goalName,
            TargetAmount = NewGoal.TargetAmount,
            MonthlyTargetAmount = NewGoal.MonthlyTargetAmount,
            TargetDate = NewGoal.TargetDate
        });

        await db.SaveChangesAsync(cancellationToken);

        TempData["Success"] = "هدف پس‌انداز ساخته شد.";

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostContributeAsync(CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        RemoveModelStatePrefix(nameof(NewGoal));
        if (Contribution.Amount <= 0)
        {
            TempData["Error"] = "مبلغ واریزی باید بیشتر از صفر باشد.";
            return RedirectToPage();
        }
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "مبلغ و تاریخ شمسی واریزی را بررسی کنید.";
            return RedirectToPage();
        }

        var goalExists = await db.SavingsGoals.AnyAsync(
            x => x.Id == Contribution.GoalId && x.WorkspaceId == workspace.Id && !x.IsCompleted,
            cancellationToken);
        if (!goalExists)
        {
            return NotFound();
        }

        db.SavingsContributions.Add(new SavingsContribution
        {
            SavingsGoalId = Contribution.GoalId,
            CreatedByUserId = workspaceContext.UserId!,
            Amount = Contribution.Amount,
            ContributionDate = Contribution.Date,
            Note = string.IsNullOrWhiteSpace(Contribution.Note) ? null : Contribution.Note.Trim()
        });
        await db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "مبلغ پس‌انداز ثبت شد.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleCompleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        var goal = await db.SavingsGoals.SingleOrDefaultAsync(x => x.Id == id && x.WorkspaceId == workspace.Id, cancellationToken);
        if (goal is null)
        {
            return NotFound();
        }

        goal.IsCompleted = !goal.IsCompleted;
        await db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = goal.IsCompleted ? "هدف تکمیل‌شده علامت خورد." : "هدف دوباره فعال شد.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteContributionAsync(Guid id, CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        var item = await db.SavingsContributions
            .Include(x => x.SavingsGoal)
            .SingleOrDefaultAsync(x => x.Id == id && x.SavingsGoal.WorkspaceId == workspace.Id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        db.SavingsContributions.Remove(item);
        await db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "واریزی حذف شد.";
        return RedirectToPage();
    }

    private void RemoveModelStatePrefix(string prefix)
    {
        foreach (var key in ModelState.Keys
                     .Where(key => key.Equals(prefix, StringComparison.Ordinal) ||
                                   key.StartsWith(prefix + ".", StringComparison.Ordinal))
                     .ToArray())
        {
            ModelState.Remove(key);
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.Now);
        var currentPeriod = PersianCalendarHelper.GetYearMonth(today);
        var monthStart = PersianCalendarHelper.StartOfMonth(currentPeriod);
        var monthEnd = PersianCalendarHelper.EndOfMonthExclusive(currentPeriod);

        var goals = await db.SavingsGoals
            .Where(x => x.WorkspaceId == workspace.Id)
            .Include(x => x.Contributions)
                .ThenInclude(x => x.CreatedByUser)
            .OrderBy(x => x.IsCompleted)
            .ThenBy(x => x.TargetDate)
            .ThenBy(x => x.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        Goals = goals.Select(goal =>
        {
            var total = goal.Contributions.Sum(x => x.Amount);
            var monthly = goal.Contributions
                .Where(x => x.ContributionDate >= monthStart && x.ContributionDate < monthEnd)
                .Sum(x => x.Amount);
            return new GoalCard(
                goal,
                total,
                monthly,
                Math.Max(goal.TargetAmount - total, 0),
                goal.TargetAmount <= 0 ? 0 : Math.Round(total / goal.TargetAmount * 100, 1),
                goal.MonthlyTargetAmount <= 0 ? 0 : Math.Round(monthly / goal.MonthlyTargetAmount * 100, 1),
                goal.Contributions.OrderByDescending(x => x.ContributionDate).ThenByDescending(x => x.CreatedAtUtc).Take(5).ToList());
        }).ToList();
    }
}
