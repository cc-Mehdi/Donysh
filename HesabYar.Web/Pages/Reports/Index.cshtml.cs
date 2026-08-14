using HesabYar.Web.Data;
using HesabYar.Web.Domain;
using HesabYar.Web.Helpers;
using HesabYar.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace HesabYar.Web.Pages.Reports;

public sealed class IndexModel(ApplicationDbContext db, IWorkspaceContext workspaceContext) : PageModel
{
    private static readonly string[] ValidPeriods = ["daily", "weekly", "monthly", "yearly"];

    [BindProperty(SupportsGet = true)]
    public string Period { get; set; } = "monthly";

    [BindProperty(SupportsGet = true)]
    public DateOnly? Anchor { get; set; }

    public DateOnly Start { get; private set; }
    public DateOnly EndExclusive { get; private set; }
    public DateOnly PreviousAnchor { get; private set; }
    public DateOnly NextAnchor { get; private set; }
    public string PeriodTitle { get; private set; } = string.Empty;
    public string MemberMonthTitle { get; private set; } = string.Empty;
    public decimal Total { get; private set; }
    public decimal AveragePerDay { get; private set; }
    public int ExpenseCount { get; private set; }
    public CategoryReport? TopCategory { get; private set; }
    public IReadOnlyList<CategoryReport> Categories { get; private set; } = [];
    public IReadOnlyList<TrendPoint> Trend { get; private set; } = [];
    public IReadOnlyList<MemberMonthlyReport> Members { get; private set; } = [];
    public IReadOnlyList<BudgetTransferReport> BudgetTransfers { get; private set; } = [];
    public decimal MaxTrendAmount => Trend.Count == 0 ? 0 : Trend.Max(x => x.Amount);

    public sealed record CategoryReport(Guid CategoryId, string Name, string Icon, decimal Amount, int Count, decimal Percent);
    public sealed record TrendPoint(string Label, decimal Amount, int Count);
    public sealed record MemberCategoryReport(Guid CategoryId, string Name, string Icon, decimal Amount, int Count, decimal Percent);
    public sealed record MemberGoalSaving(Guid GoalId, string GoalName, decimal Amount, int Count);
    public sealed record MemberMonthlyReport(
        string UserId,
        string DisplayName,
        string Email,
        decimal ExpenseAmount,
        int ExpenseCount,
        IReadOnlyList<MemberCategoryReport> Categories,
        decimal SavingsAmount,
        int SavingsCount,
        IReadOnlyList<MemberGoalSaving> SavingsGoals);

    public sealed record BudgetTransferReport(
        DateOnly TransferDate,
        string SourceName,
        string SourceIcon,
        string DestinationName,
        string DestinationIcon,
        decimal Amount,
        string CreatedBy,
        string? Note);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        NormalizePeriod();

        var expenses = await db.Expenses
            .Where(x => x.WorkspaceId == workspace.Id && x.ExpenseDate >= Start && x.ExpenseDate < EndExclusive)
            .Include(x => x.Category)
            .OrderBy(x => x.ExpenseDate)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        Total = expenses.Sum(x => x.Amount);
        ExpenseCount = expenses.Count;
        var dayCount = Math.Max(EndExclusive.DayNumber - Start.DayNumber, 1);
        AveragePerDay = Math.Round(Total / dayCount, 0);

        Categories = expenses
            .GroupBy(x => new { x.CategoryId, x.Category.Name, x.Category.Icon })
            .Select(group => new CategoryReport(
                group.Key.CategoryId,
                group.Key.Name,
                group.Key.Icon,
                group.Sum(x => x.Amount),
                group.Count(),
                Total <= 0 ? 0 : Math.Round(group.Sum(x => x.Amount) / Total * 100, 1)))
            .OrderByDescending(x => x.Amount)
            .ToList();

        TopCategory = Categories.FirstOrDefault();
        Trend = BuildTrend(expenses);

        await LoadBudgetTransfersAsync(workspace.Id, cancellationToken);
        await LoadMemberMonthlyReportAsync(workspace.Id, cancellationToken);
    }

    private async Task LoadBudgetTransfersAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var transfers = await db.BudgetTransfers
            .Where(x => x.WorkspaceId == workspaceId && x.TransferDate >= Start && x.TransferDate < EndExclusive)
            .Include(x => x.SourceBudget).ThenInclude(x => x.Category)
            .Include(x => x.DestinationBudget).ThenInclude(x => x.Category)
            .Include(x => x.CreatedByUser)
            .OrderByDescending(x => x.TransferDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        BudgetTransfers = transfers.Select(x => new BudgetTransferReport(
            x.TransferDate,
            x.SourceBudget.Category?.Name ?? "بودجه",
            x.SourceBudget.Category?.Icon ?? "📦",
            x.DestinationBudget.Category?.Name ?? "بودجه",
            x.DestinationBudget.Category?.Icon ?? "📦",
            x.Amount,
            string.IsNullOrWhiteSpace(x.CreatedByUser.DisplayName)
                ? x.CreatedByUser.Email ?? "کاربر"
                : x.CreatedByUser.DisplayName,
            x.Note)).ToList();
    }

    private async Task LoadMemberMonthlyReportAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var anchor = Anchor ?? DateOnly.FromDateTime(DateTime.Now);
        var month = PersianCalendarHelper.GetYearMonth(anchor);
        var monthStart = PersianCalendarHelper.StartOfMonth(month);
        var monthEnd = PersianCalendarHelper.EndOfMonthExclusive(month);
        MemberMonthTitle = PersianCalendarHelper.Title(month);

        var members = await db.WorkspaceMembers
            .Where(x => x.WorkspaceId == workspaceId)
            .Include(x => x.User)
            .OrderBy(x => x.Role)
            .ThenBy(x => x.User.DisplayName)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var monthExpenses = await db.Expenses
            .Where(x => x.WorkspaceId == workspaceId && x.ExpenseDate >= monthStart && x.ExpenseDate < monthEnd)
            .Include(x => x.Category)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var monthContributions = await db.SavingsContributions
            .Where(x => x.SavingsGoal.WorkspaceId == workspaceId &&
                        x.ContributionDate >= monthStart &&
                        x.ContributionDate < monthEnd)
            .Include(x => x.SavingsGoal)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        Members = members.Select(member =>
        {
            var memberExpenses = monthExpenses
                .Where(x => x.CreatedByUserId == member.UserId)
                .ToList();
            var expenseTotal = memberExpenses.Sum(x => x.Amount);

            var categories = memberExpenses
                .GroupBy(x => new { x.CategoryId, x.Category.Name, x.Category.Icon })
                .Select(group => new MemberCategoryReport(
                    group.Key.CategoryId,
                    group.Key.Name,
                    group.Key.Icon,
                    group.Sum(x => x.Amount),
                    group.Count(),
                    expenseTotal <= 0 ? 0 : Math.Round(group.Sum(x => x.Amount) / expenseTotal * 100, 1)))
                .OrderByDescending(x => x.Amount)
                .ToList();

            var savings = monthContributions
                .Where(x => x.CreatedByUserId == member.UserId)
                .ToList();

            var goals = savings
                .GroupBy(x => new { x.SavingsGoalId, x.SavingsGoal.Name })
                .Select(group => new MemberGoalSaving(
                    group.Key.SavingsGoalId,
                    group.Key.Name,
                    group.Sum(x => x.Amount),
                    group.Count()))
                .OrderByDescending(x => x.Amount)
                .ToList();

            var displayName = string.IsNullOrWhiteSpace(member.User.DisplayName)
                ? member.User.Email ?? "کاربر"
                : member.User.DisplayName;

            return new MemberMonthlyReport(
                member.UserId,
                displayName,
                member.User.Email ?? string.Empty,
                expenseTotal,
                memberExpenses.Count,
                categories,
                savings.Sum(x => x.Amount),
                savings.Count,
                goals);
        }).ToList();
    }

    private void NormalizePeriod()
    {
        Period = ValidPeriods.Contains(Period, StringComparer.OrdinalIgnoreCase) ? Period.ToLowerInvariant() : "monthly";
        var anchor = Anchor ?? DateOnly.FromDateTime(DateTime.Now);
        Anchor = anchor;

        switch (Period)
        {
            case "daily":
                Start = anchor;
                EndExclusive = anchor.AddDays(1);
                PreviousAnchor = anchor.AddDays(-1);
                NextAnchor = anchor.AddDays(1);
                PeriodTitle = $"روز {Formatters.PersianDate(anchor)}";
                break;
            case "weekly":
                Start = anchor.AddDays(-(((int)anchor.DayOfWeek + 1) % 7));
                EndExclusive = Start.AddDays(7);
                PreviousAnchor = anchor.AddDays(-7);
                NextAnchor = anchor.AddDays(7);
                PeriodTitle = $"هفته {Formatters.PersianDate(Start)} تا {Formatters.PersianDate(EndExclusive.AddDays(-1))}";
                break;
            case "yearly":
                var persianYear = PersianCalendarHelper.GetYearMonth(anchor).Year;
                Start = PersianCalendarHelper.StartOfYear(persianYear);
                EndExclusive = PersianCalendarHelper.StartOfYear(persianYear + 1);
                PreviousAnchor = Start.AddDays(-1);
                NextAnchor = EndExclusive;
                PeriodTitle = $"سال {persianYear}";
                break;
            default:
                var persianMonth = PersianCalendarHelper.GetYearMonth(anchor);
                Start = PersianCalendarHelper.StartOfMonth(persianMonth);
                EndExclusive = PersianCalendarHelper.EndOfMonthExclusive(persianMonth);
                PreviousAnchor = Start.AddDays(-1);
                NextAnchor = EndExclusive;
                PeriodTitle = PersianCalendarHelper.Title(persianMonth);
                break;
        }
    }

    private IReadOnlyList<TrendPoint> BuildTrend(IReadOnlyList<Expense> expenses)
    {
        if (Period == "yearly")
        {
            var year = PersianCalendarHelper.GetYearMonth(Start).Year;
            return Enumerable.Range(1, 12)
                .Select(month =>
                {
                    var period = new PersianYearMonth(year, month);
                    var monthStart = PersianCalendarHelper.StartOfMonth(period);
                    var monthEnd = PersianCalendarHelper.EndOfMonthExclusive(period);
                    var items = expenses.Where(x => x.ExpenseDate >= monthStart && x.ExpenseDate < monthEnd).ToList();
                    return new TrendPoint(PersianCalendarHelper.MonthName(month), items.Sum(x => x.Amount), items.Count);
                })
                .ToList();
        }

        var points = new List<TrendPoint>();
        for (var date = Start; date < EndExclusive; date = date.AddDays(1))
        {
            var items = expenses.Where(x => x.ExpenseDate == date).ToList();
            var label = Period == "monthly" ? PersianCalendarHelper.GetDayOfMonth(date).ToString("00") : Formatters.PersianDate(date);
            points.Add(new TrendPoint(label, items.Sum(x => x.Amount), items.Count));
        }
        return points;
    }
}
