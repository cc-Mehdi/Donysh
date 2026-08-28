using HesabYar.Web.Data;
using HesabYar.Web.Domain;
using HesabYar.Web.Helpers;
using Microsoft.EntityFrameworkCore;

namespace HesabYar.Web.Services;

public sealed class BudgetRolloverService(ApplicationDbContext db)
{
    public async Task<int> EnsureCurrentPeriodAsync(
        Guid workspaceId,
        PersianYearMonth period,
        CancellationToken cancellationToken = default)
    {
        var current = PersianCalendarHelper.GetYearMonth(DateOnly.FromDateTime(DateTime.Now));
        if (period != current)
        {
            return 0;
        }

        var previous = period.AddMonths(-1);
        var previousBudgets = await db.Budgets
            .Where(x => x.WorkspaceId == workspaceId &&
                        x.Year == previous.Year &&
                        x.Month == previous.Month &&
                        (x.CategoryId == null || !x.Category!.IsArchived))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (previousBudgets.Count == 0)
        {
            return 0;
        }

        var currentCategoryIds = await db.Budgets
            .Where(x => x.WorkspaceId == workspaceId && x.Year == period.Year && x.Month == period.Month)
            .Select(x => x.CategoryId)
            .ToListAsync(cancellationToken);
        var existing = currentCategoryIds.ToHashSet();

        var inherited = previousBudgets
            .GroupBy(x => x.CategoryId)
            .Select(group => group.OrderByDescending(x => x.CreatedAtUtc).First())
            .Where(x => !existing.Contains(x.CategoryId))
            .Select(x => new Budget
            {
                WorkspaceId = workspaceId,
                CategoryId = x.CategoryId,
                Year = period.Year,
                Month = period.Month,
                Amount = x.Amount,
                WarningPercent = x.WarningPercent,
                CarryOverOverspend = x.CarryOverOverspend
            })
            .ToList();

        if (inherited.Count == 0)
        {
            return 0;
        }

        db.Budgets.AddRange(inherited);
        await db.SaveChangesAsync(cancellationToken);
        return inherited.Count;
    }
}
