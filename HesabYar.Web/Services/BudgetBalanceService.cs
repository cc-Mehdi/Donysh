using HesabYar.Web.Data;
using HesabYar.Web.Domain;
using HesabYar.Web.Helpers;
using Microsoft.EntityFrameworkCore;

namespace HesabYar.Web.Services;

public sealed record BudgetBalanceState(
    Budget Budget,
    decimal BaseAmount,
    decimal IncomingAmount,
    decimal OutgoingAmount,
    decimal CarryoverDeduction,
    decimal EffectiveAmount,
    decimal Spent,
    decimal Remaining,
    decimal Percent)
{
    public decimal NextMonthCarryover => Budget.CarryOverOverspend ? Math.Max(0, -Remaining) : 0;
}

public sealed class BudgetBalanceService(ApplicationDbContext db)
{
    private const int MaxCarryoverDepth = 60;

    public async Task<IReadOnlyList<BudgetBalanceState>> GetPeriodAsync(
        Guid workspaceId,
        PersianYearMonth period,
        CancellationToken cancellationToken = default)
    {
        var cache = new Dictionary<(int Year, int Month), IReadOnlyList<BudgetBalanceState>>();
        return await GetPeriodInternalAsync(workspaceId, period, 0, cache, cancellationToken);
    }

    private async Task<IReadOnlyList<BudgetBalanceState>> GetPeriodInternalAsync(
        Guid workspaceId,
        PersianYearMonth period,
        int depth,
        Dictionary<(int Year, int Month), IReadOnlyList<BudgetBalanceState>> cache,
        CancellationToken cancellationToken)
    {
        var key = (period.Year, period.Month);
        if (cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var budgets = await db.Budgets
            .Where(x => x.WorkspaceId == workspaceId && x.Year == period.Year && x.Month == period.Month)
            .Include(x => x.Category)
            .OrderBy(x => x.CategoryId == null ? 0 : 1)
            .ThenBy(x => x.Category!.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (budgets.Count == 0)
        {
            cache[key] = Array.Empty<BudgetBalanceState>();
            return cache[key];
        }

        IReadOnlyList<BudgetBalanceState> previous = Array.Empty<BudgetBalanceState>();
        if (depth < MaxCarryoverDepth)
        {
            var previousPeriod = period.AddMonths(-1);
            var hasPreviousBudget = await db.Budgets.AnyAsync(
                x => x.WorkspaceId == workspaceId && x.Year == previousPeriod.Year && x.Month == previousPeriod.Month,
                cancellationToken);

            if (hasPreviousBudget)
            {
                previous = await GetPeriodInternalAsync(
                    workspaceId,
                    previousPeriod,
                    depth + 1,
                    cache,
                    cancellationToken);
            }
        }

        var start = PersianCalendarHelper.StartOfMonth(period);
        var end = PersianCalendarHelper.EndOfMonthExclusive(period);
        var expenses = await db.Expenses
            .Where(x => x.WorkspaceId == workspaceId && x.ExpenseDate >= start && x.ExpenseDate < end)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var budgetIds = budgets.Select(x => x.Id).ToList();
        var transfers = await db.BudgetTransfers
            .Where(x => x.WorkspaceId == workspaceId &&
                        (budgetIds.Contains(x.SourceBudgetId) || budgetIds.Contains(x.DestinationBudgetId)))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var states = budgets.Select(budget =>
        {
            var spent = budget.CategoryId.HasValue
                ? expenses.Where(x => x.CategoryId == budget.CategoryId.Value).Sum(x => x.Amount)
                : expenses.Sum(x => x.Amount);

            var incoming = transfers.Where(x => x.DestinationBudgetId == budget.Id).Sum(x => x.Amount);
            var outgoing = transfers.Where(x => x.SourceBudgetId == budget.Id).Sum(x => x.Amount);

            var previousState = previous.FirstOrDefault(x => x.Budget.CategoryId == budget.CategoryId);
            var carryover = previousState is not null && previousState.Budget.CarryOverOverspend
                ? Math.Max(0, -previousState.Remaining)
                : 0;

            var effective = budget.Amount + incoming - outgoing - carryover;
            var remaining = effective - spent;
            var percent = effective <= 0
                ? (spent > 0 || carryover > 0 ? 100 : 0)
                : Math.Round(spent / effective * 100, 1);

            return new BudgetBalanceState(
                budget,
                budget.Amount,
                incoming,
                outgoing,
                carryover,
                effective,
                spent,
                remaining,
                percent);
        }).ToList();

        cache[key] = states;
        return states;
    }
}
