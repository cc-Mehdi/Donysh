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
    public IReadOnlyList<MemberSavingsSummary> MemberSavings { get; private set; } = [];
    public string CurrentMonthTitle { get; private set; } = string.Empty;

    public sealed class GoalInput
    {
        [Required(ErrorMessage = "نام هدف را وارد کنید.")]
        [StringLength(120, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        [StringLength(500, ErrorMessage = "توضیحات هدف نمی‌تواند بیشتر از ۵۰۰ کاراکتر باشد.")]
        public string? Description { get; set; }

        [Range(1, 999_999_999_999, ErrorMessage = "مبلغ هدف باید بیشتر از صفر باشد.")]
        public decimal TargetAmount { get; set; }

        [Range(0, 999_999_999_999, ErrorMessage = "هدف ماهانه معتبر نیست.")]
        public decimal MonthlyTargetAmount { get; set; }

        [Range(1, 5, ErrorMessage = "اولویت هدف معتبر نیست.")]
        public int Priority { get; set; } = 3;

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

    public sealed record MemberGoalSaving(Guid GoalId, string GoalName, decimal Amount, int Count);

    public sealed record MemberSavingsSummary(
        string UserId,
        string DisplayName,
        string Email,
        decimal MonthSaved,
        int MonthContributionCount,
        IReadOnlyList<MemberGoalSaving> Goals);

    public sealed record GoalMemberContribution(
        string UserId,
        string DisplayName,
        decimal TotalSaved,
        decimal MonthSaved,
        int ContributionCount);

    public sealed record GoalCard(
        SavingsGoal Goal,
        decimal TotalSaved,
        decimal MonthSaved,
        decimal Remaining,
        decimal TotalPercent,
        decimal MonthlyPercent,
        IReadOnlyList<SavingsContribution> RecentContributions,
        IReadOnlyList<GoalMemberContribution> MemberContributions);

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
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            TempData["Error"] = errors.Count > 0
                ? string.Join(" | ", errors)
                : "اطلاعات هدف را بررسی کنید.";

            await LoadAsync(cancellationToken);
            return Page();
        }

        var goalName = NewGoal.Name.Trim();

        var duplicateGoal = await db.SavingsGoals
            .AnyAsync(
                x => x.WorkspaceId == workspace.Id &&
                     x.Name == goalName,
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
            Description = string.IsNullOrWhiteSpace(NewGoal.Description)
                ? null
                : NewGoal.Description.Trim(),
            TargetAmount = NewGoal.TargetAmount,
            MonthlyTargetAmount = NewGoal.MonthlyTargetAmount,
            Priority = NewGoal.Priority,
            TargetDate = NewGoal.TargetDate
        });

        await db.SaveChangesAsync(cancellationToken);

        TempData["Success"] = "هدف پس‌انداز ساخته شد.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostContributeAsync(CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        var form = await Request.ReadFormAsync(cancellationToken);

        var goalIdRaw = form["Contribution.GoalId"].FirstOrDefault();
        var amountRaw = form["Contribution.Amount"].FirstOrDefault();
        var dateRaw = form["Contribution.Date"].FirstOrDefault();
        var note = form["Contribution.Note"].FirstOrDefault()?.Trim();

        if (!Guid.TryParse(goalIdRaw, out var goalId))
        {
            TempData["Error"] = "هدف پس‌انداز انتخاب‌شده معتبر نیست.";
            return RedirectToPage();
        }

        if (!InputNormalization.TryParseMoney(amountRaw, out var amount) || amount <= 0)
        {
            TempData["Error"] = "مبلغ واریزی باید بیشتر از صفر باشد.";
            return RedirectToPage();
        }

        if (!PersianCalendarHelper.TryParseInput(dateRaw, out var contributionDate))
        {
            TempData["Error"] = "تاریخ واریزی را به‌صورت شمسی و معتبر وارد کنید.";
            return RedirectToPage();
        }

        if (note?.Length > 200)
        {
            TempData["Error"] = "یادداشت واریزی نمی‌تواند بیشتر از ۲۰۰ کاراکتر باشد.";
            return RedirectToPage();
        }

        var goalExists = await db.SavingsGoals.AnyAsync(
            x => x.Id == goalId &&
                 x.WorkspaceId == workspace.Id &&
                 !x.IsCompleted &&
                 !x.IsCancelled,
            cancellationToken);

        if (!goalExists)
        {
            TempData["Error"] = "امکان ثبت واریزی برای هدف تکمیل‌شده یا لغوشده وجود ندارد.";
            return RedirectToPage();
        }

        db.SavingsContributions.Add(new SavingsContribution
        {
            SavingsGoalId = goalId,
            CreatedByUserId = workspaceContext.UserId!,
            Amount = amount,
            ContributionDate = contributionDate,
            Note = string.IsNullOrWhiteSpace(note) ? null : note
        });

        await db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "مبلغ پس‌انداز ثبت شد.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleCompleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        var goal = await db.SavingsGoals.SingleOrDefaultAsync(
            x => x.Id == id && x.WorkspaceId == workspace.Id,
            cancellationToken);

        if (goal is null)
        {
            return NotFound();
        }

        if (goal.IsCancelled)
        {
            TempData["Error"] = "هدف لغوشده را ابتدا دوباره فعال کنید.";
            return RedirectToPage();
        }

        goal.IsCompleted = !goal.IsCompleted;
        await db.SaveChangesAsync(cancellationToken);

        TempData["Success"] = goal.IsCompleted
            ? "هدف تکمیل‌شده علامت خورد."
            : "هدف دوباره فعال شد.";

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleCancelledAsync(Guid id, CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        var goal = await db.SavingsGoals.SingleOrDefaultAsync(
            x => x.Id == id && x.WorkspaceId == workspace.Id,
            cancellationToken);

        if (goal is null)
        {
            return NotFound();
        }

        if (goal.IsCancelled)
        {
            goal.IsCancelled = false;
            TempData["Success"] = "هدف پس‌انداز دوباره فعال شد.";
        }
        else
        {
            goal.IsCancelled = true;
            goal.IsCompleted = false;
            TempData["Success"] = "هدف پس‌انداز لغو شد.";
        }

        await db.SaveChangesAsync(cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteContributionAsync(Guid id, CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        var item = await db.SavingsContributions
            .Include(x => x.SavingsGoal)
            .SingleOrDefaultAsync(
                x => x.Id == id && x.SavingsGoal.WorkspaceId == workspace.Id,
                cancellationToken);

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
        CurrentMonthTitle = PersianCalendarHelper.Title(currentPeriod);

        var members = await db.WorkspaceMembers
            .Where(x => x.WorkspaceId == workspace.Id)
            .Include(x => x.User)
            .OrderBy(x => x.Role)
            .ThenBy(x => x.User.DisplayName)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var goals = await db.SavingsGoals
            .Where(x => x.WorkspaceId == workspace.Id)
            .Include(x => x.Contributions)
                .ThenInclude(x => x.CreatedByUser)
            .OrderBy(x => x.IsCancelled)
            .ThenBy(x => x.IsCompleted)
            .ThenBy(x => x.Priority)
            .ThenBy(x => x.TargetDate)
            .ThenBy(x => x.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var contributionRows = goals
            .SelectMany(goal => goal.Contributions.Select(contribution => new
            {
                Goal = goal,
                Contribution = contribution
            }))
            .ToList();

        MemberSavings = members.Select(member =>
        {
            var monthItems = contributionRows
                .Where(x => x.Contribution.CreatedByUserId == member.UserId &&
                            x.Contribution.ContributionDate >= monthStart &&
                            x.Contribution.ContributionDate < monthEnd)
                .ToList();

            var goalRows = monthItems
                .GroupBy(x => new { x.Goal.Id, x.Goal.Name })
                .Select(group => new MemberGoalSaving(
                    group.Key.Id,
                    group.Key.Name,
                    group.Sum(x => x.Contribution.Amount),
                    group.Count()))
                .OrderByDescending(x => x.Amount)
                .ToList();

            var displayName = string.IsNullOrWhiteSpace(member.User.DisplayName)
                ? member.User.Email ?? "کاربر"
                : member.User.DisplayName;

            return new MemberSavingsSummary(
                member.UserId,
                displayName,
                member.User.Email ?? string.Empty,
                monthItems.Sum(x => x.Contribution.Amount),
                monthItems.Count,
                goalRows);
        }).ToList();

        Goals = goals.Select(goal =>
        {
            var total = goal.Contributions.Sum(x => x.Amount);
            var monthly = goal.Contributions
                .Where(x => x.ContributionDate >= monthStart && x.ContributionDate < monthEnd)
                .Sum(x => x.Amount);

            var memberContributions = members.Select(member =>
            {
                var items = goal.Contributions
                    .Where(x => x.CreatedByUserId == member.UserId)
                    .ToList();
                var monthItems = items
                    .Where(x => x.ContributionDate >= monthStart && x.ContributionDate < monthEnd)
                    .ToList();
                var displayName = string.IsNullOrWhiteSpace(member.User.DisplayName)
                    ? member.User.Email ?? "کاربر"
                    : member.User.DisplayName;

                return new GoalMemberContribution(
                    member.UserId,
                    displayName,
                    items.Sum(x => x.Amount),
                    monthItems.Sum(x => x.Amount),
                    items.Count);
            })
            .Where(x => x.TotalSaved > 0 || x.MonthSaved > 0)
            .OrderByDescending(x => x.TotalSaved)
            .ToList();

            return new GoalCard(
                goal,
                total,
                monthly,
                Math.Max(goal.TargetAmount - total, 0),
                goal.TargetAmount <= 0 ? 0 : Math.Round(total / goal.TargetAmount * 100, 1),
                goal.MonthlyTargetAmount <= 0 ? 0 : Math.Round(monthly / goal.MonthlyTargetAmount * 100, 1),
                goal.Contributions
                    .OrderByDescending(x => x.ContributionDate)
                    .ThenByDescending(x => x.CreatedAtUtc)
                    .Take(5)
                    .ToList(),
                memberContributions);
        }).ToList();
    }

    public static string PriorityLabel(int priority) => priority switch
    {
        1 => "خیلی بالا",
        2 => "بالا",
        3 => "عادی",
        4 => "پایین",
        5 => "خیلی پایین",
        _ => "عادی"
    };

}
