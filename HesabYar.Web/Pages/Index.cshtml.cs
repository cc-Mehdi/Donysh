using HesabYar.Web.Data;
using HesabYar.Web.Domain;
using HesabYar.Web.Helpers;
using HesabYar.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace HesabYar.Web.Pages;

public sealed class IndexModel(
    ApplicationDbContext db,
    IWorkspaceContext workspaceContext,
    BudgetBalanceService budgetBalanceService) : PageModel
{
    public Workspace CurrentWorkspace { get; private set; } = null!;
    public decimal MonthTotal { get; private set; }
    public decimal WeekTotal { get; private set; }
    public decimal MonthSavings { get; private set; }
    public decimal MonthObligationTotal { get; private set; }
    public decimal MonthObligationUnpaid { get; private set; }
    public int MonthExpenseCount { get; private set; }
    public IReadOnlyList<Expense> RecentExpenses { get; private set; } = [];
    public IReadOnlyList<BudgetCard> BudgetCards { get; private set; } = [];
    public IReadOnlyList<SavingsCard> SavingsCards { get; private set; } = [];
    public IReadOnlyList<ObligationAlert> ObligationAlerts { get; private set; } = [];
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

        var budgetStates = await budgetBalanceService.GetPeriodAsync(CurrentWorkspace.Id, currentPeriod, cancellationToken);
        BudgetCards = budgetStates.Select(state => new BudgetCard(
            state.Budget.Id,
            state.Budget.Category?.Name ?? "بودجه کل ماه",
            state.Budget.Category?.Icon ?? "💳",
            state.EffectiveAmount,
            state.Spent,
            state.Remaining,
            state.Percent,
            state.CarryoverDeduction,
            state.Remaining < 0,
            state.Percent >= state.Budget.WarningPercent)).ToList();

        var goals = await db.SavingsGoals
            .Where(x => x.WorkspaceId == CurrentWorkspace.Id && !x.IsCompleted && !x.IsCancelled)
            .Include(x => x.Contributions)
            .OrderBy(x => x.TargetDate)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        SavingsCards = goals.Select(goal =>
        {
            var total = goal.Contributions.Sum(x => x.Amount);
            var monthly = goal.Contributions.Where(x => x.ContributionDate >= monthStart && x.ContributionDate < monthEnd).Sum(x => x.Amount);
            return new SavingsCard(goal.Id, goal.Name, goal.TargetAmount, goal.MonthlyTargetAmount, total, monthly,
                goal.TargetAmount <= 0 ? 0 : Math.Round(total / goal.TargetAmount * 100, 1));
        }).ToList();
        MonthSavings = SavingsCards.Sum(x => x.MonthlySaved);

        await LoadObligationsAsync(currentPeriod, cancellationToken);
    }

    private async Task LoadObligationsAsync(PersianYearMonth period, CancellationToken cancellationToken)
    {
        var obligations = await db.RecurringObligations
            .Where(x => x.WorkspaceId == CurrentWorkspace.Id && x.IsActive)
            .Include(x => x.Category)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var ids = obligations.Select(x => x.Id).ToList();
        var payments = ids.Count == 0
            ? new List<RecurringObligationPayment>()
            : await db.RecurringObligationPayments
                .Where(x => ids.Contains(x.RecurringObligationId) && x.PeriodYear == period.Year && x.PeriodMonth == period.Month)
                .AsNoTracking().ToListAsync(cancellationToken);

        var scheduled = obligations.Where(x => RecurringObligationHelper.IsScheduledForPeriod(x, period)).ToList();
        MonthObligationTotal = scheduled.Sum(x => x.Amount);
        var alerts = new List<ObligationAlert>();
        foreach (var item in scheduled)
        {
            if (payments.Any(x => x.RecurringObligationId == item.Id)) continue;
            MonthObligationUnpaid += item.Amount;
            var due = RecurringObligationHelper.GetDueDate(item, period);
            var days = due.DayNumber - Today.DayNumber;
            var overdue = due < Today;
            var dueSoon = days >= 0 && days <= item.ReminderDaysBefore;
            if (overdue || dueSoon)
            {
                alerts.Add(new ObligationAlert(item.Id, item.Title, item.Category.Icon, item.Amount, due, overdue, days));
            }
        }

        ObligationAlerts = alerts.OrderByDescending(x => x.IsOverdue).ThenBy(x => x.DueDate).Take(5).ToList();
    }

    public sealed record BudgetCard(Guid Id, string Name, string Icon, decimal Amount, decimal Spent, decimal Remaining,
        decimal Percent, decimal CarryoverDeduction, bool IsExceeded, bool IsWarning);

    public sealed record SavingsCard(Guid Id, string Name, decimal Target, decimal MonthlyTarget, decimal Saved, decimal MonthlySaved, decimal Percent);

    public sealed record ObligationAlert(Guid Id, string Title, string Icon, decimal Amount, DateOnly DueDate, bool IsOverdue, int DaysUntilDue);
}
