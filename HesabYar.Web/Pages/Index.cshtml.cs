using HesabYar.Web.Data;
using HesabYar.Web.Domain;
using HesabYar.Web.Helpers;
using HesabYar.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace HesabYar.Web.Pages;

public sealed class IndexModel(ApplicationDbContext db, IWorkspaceContext workspaceContext) : PageModel
{
    public Workspace CurrentWorkspace { get; private set; } = null!;
    public decimal MonthTotal { get; private set; }
    public decimal WeekTotal { get; private set; }
    public decimal MonthSavings { get; private set; }
    public int MonthExpenseCount { get; private set; }
    public IReadOnlyList<Expense> RecentExpenses { get; private set; } = [];
    public IReadOnlyList<BudgetCard> BudgetCards { get; private set; } = [];
    public IReadOnlyList<SavingsCard> SavingsCards { get; private set; } = [];
    public DateOnly Today { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        CurrentWorkspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        Today = DateOnly.FromDateTime(DateTime.Now);
        var currentPeriod = PersianCalendarHelper.GetYearMonth(Today);
        var monthStart = PersianCalendarHelper.StartOfMonth(currentPeriod);
        var monthEnd = PersianCalendarHelper.EndOfMonthExclusive(currentPeriod);
        var weekStart = Today.AddDays(-(((int)Today.DayOfWeek + 1) % 7));

        var monthExpenses = await db.Expenses
            .Where(x => x.WorkspaceId == CurrentWorkspace.Id && x.ExpenseDate >= monthStart && x.ExpenseDate < monthEnd)
            .Include(x => x.Category)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        MonthTotal = monthExpenses.Sum(x => x.Amount);
        WeekTotal = monthExpenses.Where(x => x.ExpenseDate >= weekStart).Sum(x => x.Amount);
        MonthExpenseCount = monthExpenses.Count;

        RecentExpenses = await db.Expenses
            .Where(x => x.WorkspaceId == CurrentWorkspace.Id)
            .Include(x => x.Category)
            .Include(x => x.CreatedByUser)
            .OrderByDescending(x => x.ExpenseDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Take(8)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var budgets = await db.Budgets
            .Where(x => x.WorkspaceId == CurrentWorkspace.Id && x.Year == currentPeriod.Year && x.Month == currentPeriod.Month)
            .Include(x => x.Category)
            .OrderBy(x => x.CategoryId == null ? 0 : 1)
            .ThenBy(x => x.Category!.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        BudgetCards = budgets.Select(budget =>
        {
            var spent = budget.CategoryId.HasValue
                ? monthExpenses.Where(x => x.CategoryId == budget.CategoryId.Value).Sum(x => x.Amount)
                : MonthTotal;
            var percent = budget.Amount <= 0 ? 0 : Math.Round(spent / budget.Amount * 100, 1);
            return new BudgetCard(
                budget.Id,
                budget.Category?.Name ?? "بودجه کل ماه",
                budget.Category?.Icon ?? "💳",
                budget.Amount,
                spent,
                budget.Amount - spent,
                percent,
                percent >= 100,
                percent >= budget.WarningPercent);
        }).ToList();

        var goals = await db.SavingsGoals
            .Where(x => x.WorkspaceId == CurrentWorkspace.Id && !x.IsCompleted && !x.IsCancelled)
            .Include(x => x.Contributions)
            .OrderBy(x => x.TargetDate)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        SavingsCards = goals.Select(goal =>
        {
            var total = goal.Contributions.Sum(x => x.Amount);
            var monthly = goal.Contributions
                .Where(x => x.ContributionDate >= monthStart && x.ContributionDate < monthEnd)
                .Sum(x => x.Amount);
            return new SavingsCard(
                goal.Id,
                goal.Name,
                goal.TargetAmount,
                goal.MonthlyTargetAmount,
                total,
                monthly,
                goal.TargetAmount <= 0 ? 0 : Math.Round(total / goal.TargetAmount * 100, 1));
        }).ToList();

        MonthSavings = SavingsCards.Sum(x => x.MonthlySaved);
    }

    public sealed record BudgetCard(
        Guid Id,
        string Name,
        string Icon,
        decimal Amount,
        decimal Spent,
        decimal Remaining,
        decimal Percent,
        bool IsExceeded,
        bool IsWarning);

    public sealed record SavingsCard(
        Guid Id,
        string Name,
        decimal Target,
        decimal MonthlyTarget,
        decimal Saved,
        decimal MonthlySaved,
        decimal Percent);
}
