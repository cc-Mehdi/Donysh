using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using HesabYar.Web.Data;
using HesabYar.Web.Domain;
using HesabYar.Web.Helpers;
using HesabYar.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace HesabYar.Web.Pages.Reports;

public sealed class IndexModel(
    ApplicationDbContext db,
    IWorkspaceContext workspaceContext,
    BudgetBalanceService budgetBalanceService) : PageModel
{
    private static readonly string[] ValidPeriods = ["daily", "weekly", "monthly", "yearly"];

    [BindProperty(SupportsGet = true)] public string Period { get; set; } = "monthly";
    [BindProperty(SupportsGet = true)] public DateOnly? Anchor { get; set; }

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
    public IReadOnlyList<MonthlyObligationReport> MonthlyObligations { get; private set; } = [];
    public decimal MonthlyObligationExpected => MonthlyObligations.Sum(x => x.ExpectedAmount);
    public decimal MonthlyObligationPaid => MonthlyObligations.Where(x => x.IsPaid).Sum(x => x.PaidAmount);
    public decimal MonthlyObligationUnpaid => MonthlyObligations.Where(x => !x.IsPaid).Sum(x => x.ExpectedAmount);
    public decimal MaxTrendAmount => Trend.Count == 0 ? 0 : Trend.Max(x => x.Amount);

    public sealed record CategoryReport(Guid CategoryId, string Name, string Icon, decimal Amount, int Count, decimal Percent);
    public sealed record TrendPoint(string Label, decimal Amount, int Count);
    public sealed record MemberCategoryReport(Guid CategoryId, string Name, string Icon, decimal Amount, int Count, decimal Percent);
    public sealed record MemberGoalSaving(Guid GoalId, string GoalName, decimal Amount, int Count);
    public sealed record MemberMonthlyReport(string UserId, string DisplayName, string Email, decimal ExpenseAmount, int ExpenseCount,
        IReadOnlyList<MemberCategoryReport> Categories, decimal SavingsAmount, int SavingsCount, IReadOnlyList<MemberGoalSaving> SavingsGoals);
    public sealed record BudgetTransferReport(DateOnly TransferDate, string SourceName, string SourceIcon, string DestinationName,
        string DestinationIcon, decimal Amount, string CreatedBy, string? Note);
    public sealed record MonthlyObligationReport(string Title, RecurringObligationType Type, string CategoryName, string CategoryIcon,
        decimal ExpectedAmount, DateOnly DueDate, bool IsPaid, decimal PaidAmount, DateOnly? PaidDate, string Status);

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
            .Select(group => new CategoryReport(group.Key.CategoryId, group.Key.Name, group.Key.Icon,
                group.Sum(x => x.Amount), group.Count(), Total <= 0 ? 0 : Math.Round(group.Sum(x => x.Amount) / Total * 100, 1)))
            .OrderByDescending(x => x.Amount).ToList();

        TopCategory = Categories.FirstOrDefault();
        Trend = BuildTrend(expenses);
        await LoadBudgetTransfersAsync(workspace.Id, cancellationToken);
        await LoadMemberMonthlyReportAsync(workspace.Id, cancellationToken);
        await LoadMonthlyObligationsAsync(workspace.Id, cancellationToken);
    }

    public async Task<IActionResult> OnGetAiExportAsync(CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        NormalizePeriod();

        var expenses = await db.Expenses
            .Where(x => x.WorkspaceId == workspace.Id && x.ExpenseDate >= Start && x.ExpenseDate < EndExclusive)
            .Include(x => x.Category)
            .Include(x => x.CreatedByUser)
            .OrderBy(x => x.ExpenseDate).ThenBy(x => x.CreatedAtUtc)
            .AsNoTracking().ToListAsync(cancellationToken);

        var contributions = await db.SavingsContributions
            .Where(x => x.SavingsGoal.WorkspaceId == workspace.Id && x.ContributionDate >= Start && x.ContributionDate < EndExclusive)
            .Include(x => x.SavingsGoal)
            .Include(x => x.CreatedByUser)
            .OrderBy(x => x.ContributionDate)
            .AsNoTracking().ToListAsync(cancellationToken);

        var goals = await db.SavingsGoals
            .Where(x => x.WorkspaceId == workspace.Id)
            .Include(x => x.Contributions)
            .OrderBy(x => x.IsCancelled)
            .ThenBy(x => x.IsCompleted)
            .ThenBy(x => x.Priority)
            .ThenBy(x => x.TargetDate)
            .AsNoTracking().ToListAsync(cancellationToken);

        var transfers = await db.BudgetTransfers
            .Where(x => x.WorkspaceId == workspace.Id && x.TransferDate >= Start && x.TransferDate < EndExclusive)
            .Include(x => x.SourceBudget).ThenInclude(x => x.Category)
            .Include(x => x.DestinationBudget).ThenInclude(x => x.Category)
            .Include(x => x.CreatedByUser)
            .OrderBy(x => x.TransferDate)
            .AsNoTracking().ToListAsync(cancellationToken);

        var members = await db.WorkspaceMembers
            .Where(x => x.WorkspaceId == workspace.Id)
            .Include(x => x.User)
            .AsNoTracking().ToListAsync(cancellationToken);

        var periods = EnumeratePersianMonths(Start, EndExclusive).ToList();
        var budgetPeriods = new List<object>();
        foreach (var period in periods)
        {
            var states = await budgetBalanceService.GetPeriodAsync(workspace.Id, period, cancellationToken);
            budgetPeriods.Add(new
            {
                year = period.Year,
                month = period.Month,
                title = PersianCalendarHelper.Title(period),
                budgets = states.Select(x => new
                {
                    category = x.Budget.Category?.Name ?? "کل مخارج ماه",
                    plannedAmount = x.BaseAmount,
                    incomingTransfers = x.IncomingAmount,
                    outgoingTransfers = x.OutgoingAmount,
                    previousMonthDeficitDeduction = x.CarryoverDeduction,
                    effectiveAmount = x.EffectiveAmount,
                    spent = x.Spent,
                    remaining = x.Remaining,
                    usagePercent = x.Percent,
                    carryOverOverspendEnabled = x.Budget.CarryOverOverspend,
                    projectedDeficitToNextMonth = x.NextMonthCarryover
                }).ToList()
            });
        }

        var obligations = await db.RecurringObligations
            .Where(x => x.WorkspaceId == workspace.Id)
            .Include(x => x.Category)
            .AsNoTracking().ToListAsync(cancellationToken);
        var obligationIds = obligations.Select(x => x.Id).ToList();
        var obligationPayments = obligationIds.Count == 0
            ? new List<RecurringObligationPayment>()
            : await db.RecurringObligationPayments
                .Where(x => obligationIds.Contains(x.RecurringObligationId))
                .Include(x => x.PaidByUser)
                .AsNoTracking().ToListAsync(cancellationToken);

        var obligationOccurrences = new List<object>();
        foreach (var period in periods)
        {
            foreach (var obligation in obligations.Where(x => RecurringObligationHelper.IsScheduledForPeriod(x, period)))
            {
                var payment = obligationPayments.FirstOrDefault(x => x.RecurringObligationId == obligation.Id && x.PeriodYear == period.Year && x.PeriodMonth == period.Month);
                obligationOccurrences.Add(new
                {
                    year = period.Year,
                    month = period.Month,
                    title = obligation.Title,
                    type = obligation.Type == RecurringObligationType.Installment ? "installment" : "fixed_monthly_expense",
                    category = obligation.Category.Name,
                    expectedAmount = obligation.Amount,
                    dueDate = Formatters.PersianDate(RecurringObligationHelper.GetDueDate(obligation, period)),
                    installmentNumber = RecurringObligationHelper.GetInstallmentNumber(obligation, period),
                    totalInstallments = obligation.DurationMonths,
                    paid = payment is not null,
                    paidAmount = payment?.Amount,
                    paidDate = payment is null ? null : Formatters.PersianDate(payment.PaidDate),
                    paidBy = payment is null ? null : (string.IsNullOrWhiteSpace(payment.PaidByUser.DisplayName) ? payment.PaidByUser.Email : payment.PaidByUser.DisplayName)
                });
            }
        }

        var expenseTotal = expenses.Sum(x => x.Amount);
        var savingsInPeriod = contributions.Sum(x => x.Amount);
        var report = new
        {
            schema = "donysh-financial-ai-report/v1",
            generatedAtUtc = DateTime.UtcNow,
            currency = "TOMAN",
            workspace = new
            {
                workspace.Name,
                type = workspace.Type.ToString(),
                monthlySpendingLimit = workspace.MonthlySpendingLimit
            },
            period = new
            {
                mode = Period,
                title = PeriodTitle,
                startGregorian = Start.ToString("yyyy-MM-dd"),
                endExclusiveGregorian = EndExclusive.ToString("yyyy-MM-dd"),
                startPersian = Formatters.PersianDate(Start),
                endPersian = Formatters.PersianDate(EndExclusive.AddDays(-1))
            },
            summary = new
            {
                totalExpenses = expenseTotal,
                expenseCount = expenses.Count,
                averageExpensePerDay = Math.Round(expenseTotal / Math.Max(EndExclusive.DayNumber - Start.DayNumber, 1), 0),
                totalSavingsContributions = savingsInPeriod,
                netCashOutflowExcludingSavings = expenseTotal
            },
            expenses = expenses.Select(x => new
            {
                date = Formatters.PersianDate(x.ExpenseDate),
                reason = x.Reason,
                category = x.Category.Name,
                amount = x.Amount,
                createdBy = string.IsNullOrWhiteSpace(x.CreatedByUser.DisplayName) ? x.CreatedByUser.Email : x.CreatedByUser.DisplayName
            }).ToList(),
            expenseCategorySummary = expenses.GroupBy(x => new { x.Category.Name, x.Category.Icon })
                .Select(g => new { category = g.Key.Name, icon = g.Key.Icon, amount = g.Sum(x => x.Amount), count = g.Count(), percent = expenseTotal <= 0 ? 0 : Math.Round(g.Sum(x => x.Amount) / expenseTotal * 100, 1) })
                .OrderByDescending(x => x.amount).ToList(),
            members = members.Select(member =>
            {
                var memberExpenses = expenses.Where(x => x.CreatedByUserId == member.UserId).ToList();
                var memberSavings = contributions.Where(x => x.CreatedByUserId == member.UserId).ToList();
                return new
                {
                    name = string.IsNullOrWhiteSpace(member.User.DisplayName) ? member.User.Email : member.User.DisplayName,
                    expenseAmount = memberExpenses.Sum(x => x.Amount),
                    expenseCount = memberExpenses.Count,
                    expensesByCategory = memberExpenses.GroupBy(x => x.Category.Name).Select(g => new { category = g.Key, amount = g.Sum(x => x.Amount), count = g.Count() }).OrderByDescending(x => x.amount).ToList(),
                    savingsAmount = memberSavings.Sum(x => x.Amount),
                    savingsCount = memberSavings.Count
                };
            }).ToList(),
            budgets = budgetPeriods,
            budgetTransfers = transfers.Select(x => new
            {
                date = Formatters.PersianDate(x.TransferDate),
                source = x.SourceBudget.Category?.Name ?? "بودجه",
                destination = x.DestinationBudget.Category?.Name ?? "بودجه",
                amount = x.Amount,
                createdBy = string.IsNullOrWhiteSpace(x.CreatedByUser.DisplayName) ? x.CreatedByUser.Email : x.CreatedByUser.DisplayName,
                note = x.Note
            }).ToList(),
            savingsGoals = goals.Select(goal => new
            {
                goal.Name,
                goal.Description,
                targetAmount = goal.TargetAmount,
                monthlyTargetAmount = goal.MonthlyTargetAmount,
                priority = goal.Priority,
                totalSaved = goal.Contributions.Sum(x => x.Amount),
                savedInSelectedPeriod = goal.Contributions.Where(x => x.ContributionDate >= Start && x.ContributionDate < EndExclusive).Sum(x => x.Amount),
                targetDate = goal.TargetDate.HasValue ? Formatters.PersianDate(goal.TargetDate.Value) : null,
                goal.IsCompleted,
                goal.IsCancelled
            }).ToList(),
            recurringObligations = obligationOccurrences,
            analysisGuidance = new[]
            {
                "الگوهای هزینه و دسته‌های پرمصرف را شناسایی کن.",
                "موارد عبور از بودجه و اثر کسری منتقل‌شده از ماه قبل/به ماه بعد را بررسی کن.",
                "انتقال بودجه بین دسته‌ها را از نظر تکرار و علت احتمالی تحلیل کن.",
                "تعهدات ماهانه پرداخت‌نشده یا عقب‌افتاده و فشار آن‌ها بر جریان نقدی را مشخص کن.",
                "هزینه‌ها و بودجه‌های پیشنهادی را با monthlySpendingLimit فضای مالی مقایسه کن؛ null یعنی سقف تعیین نشده است.",
                "وضعیت پس‌انداز را با توجه به priority هدف‌ها بررسی کن؛ عدد کمتر یعنی اولویت بالاتر.",
                "برای ماه بعد چند اقدام مشخص، واقع‌بینانه و قابل‌اندازه‌گیری پیشنهاد بده."
            }
        };

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        var anchor = Anchor ?? Start;
        var anchorPeriod = PersianCalendarHelper.GetYearMonth(anchor);
        var fileName = $"donysh-ai-report-{Period}-{anchorPeriod.Year:0000}-{anchorPeriod.Month:00}.json";
        return File(Encoding.UTF8.GetBytes(json), "application/json; charset=utf-8", fileName);
    }

    private async Task LoadBudgetTransfersAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var transfers = await db.BudgetTransfers
            .Where(x => x.WorkspaceId == workspaceId && x.TransferDate >= Start && x.TransferDate < EndExclusive)
            .Include(x => x.SourceBudget).ThenInclude(x => x.Category)
            .Include(x => x.DestinationBudget).ThenInclude(x => x.Category)
            .Include(x => x.CreatedByUser)
            .OrderByDescending(x => x.TransferDate).ThenByDescending(x => x.CreatedAtUtc)
            .AsNoTracking().ToListAsync(cancellationToken);

        BudgetTransfers = transfers.Select(x => new BudgetTransferReport(x.TransferDate,
            x.SourceBudget.Category?.Name ?? "بودجه", x.SourceBudget.Category?.Icon ?? "📦",
            x.DestinationBudget.Category?.Name ?? "بودجه", x.DestinationBudget.Category?.Icon ?? "📦",
            x.Amount, string.IsNullOrWhiteSpace(x.CreatedByUser.DisplayName) ? x.CreatedByUser.Email ?? "کاربر" : x.CreatedByUser.DisplayName, x.Note)).ToList();
    }

    private async Task LoadMemberMonthlyReportAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var anchor = Anchor ?? DateOnly.FromDateTime(DateTime.Now);
        var month = PersianCalendarHelper.GetYearMonth(anchor);
        var monthStart = PersianCalendarHelper.StartOfMonth(month);
        var monthEnd = PersianCalendarHelper.EndOfMonthExclusive(month);
        MemberMonthTitle = PersianCalendarHelper.Title(month);

        var members = await db.WorkspaceMembers.Where(x => x.WorkspaceId == workspaceId).Include(x => x.User)
            .OrderBy(x => x.Role).ThenBy(x => x.User.DisplayName).AsNoTracking().ToListAsync(cancellationToken);
        var monthExpenses = await db.Expenses.Where(x => x.WorkspaceId == workspaceId && x.ExpenseDate >= monthStart && x.ExpenseDate < monthEnd)
            .Include(x => x.Category).AsNoTracking().ToListAsync(cancellationToken);
        var monthContributions = await db.SavingsContributions.Where(x => x.SavingsGoal.WorkspaceId == workspaceId && x.ContributionDate >= monthStart && x.ContributionDate < monthEnd)
            .Include(x => x.SavingsGoal).AsNoTracking().ToListAsync(cancellationToken);

        Members = members.Select(member =>
        {
            var memberExpenses = monthExpenses.Where(x => x.CreatedByUserId == member.UserId).ToList();
            var expenseTotal = memberExpenses.Sum(x => x.Amount);
            var categories = memberExpenses.GroupBy(x => new { x.CategoryId, x.Category.Name, x.Category.Icon })
                .Select(group => new MemberCategoryReport(group.Key.CategoryId, group.Key.Name, group.Key.Icon, group.Sum(x => x.Amount), group.Count(),
                    expenseTotal <= 0 ? 0 : Math.Round(group.Sum(x => x.Amount) / expenseTotal * 100, 1)))
                .OrderByDescending(x => x.Amount).ToList();
            var savings = monthContributions.Where(x => x.CreatedByUserId == member.UserId).ToList();
            var goals = savings.GroupBy(x => new { x.SavingsGoalId, x.SavingsGoal.Name })
                .Select(group => new MemberGoalSaving(group.Key.SavingsGoalId, group.Key.Name, group.Sum(x => x.Amount), group.Count()))
                .OrderByDescending(x => x.Amount).ToList();
            var displayName = string.IsNullOrWhiteSpace(member.User.DisplayName) ? member.User.Email ?? "کاربر" : member.User.DisplayName;
            return new MemberMonthlyReport(member.UserId, displayName, member.User.Email ?? string.Empty, expenseTotal, memberExpenses.Count, categories,
                savings.Sum(x => x.Amount), savings.Count, goals);
        }).ToList();
    }

    private async Task LoadMonthlyObligationsAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var anchor = Anchor ?? DateOnly.FromDateTime(DateTime.Now);
        var period = PersianCalendarHelper.GetYearMonth(anchor);
        var today = DateOnly.FromDateTime(DateTime.Now);
        var obligations = await db.RecurringObligations.Where(x => x.WorkspaceId == workspaceId && x.IsActive)
            .Include(x => x.Category).AsNoTracking().ToListAsync(cancellationToken);
        var ids = obligations.Select(x => x.Id).ToList();
        var payments = ids.Count == 0 ? new List<RecurringObligationPayment>() : await db.RecurringObligationPayments
            .Where(x => ids.Contains(x.RecurringObligationId) && x.PeriodYear == period.Year && x.PeriodMonth == period.Month)
            .AsNoTracking().ToListAsync(cancellationToken);

        MonthlyObligations = obligations.Where(x => RecurringObligationHelper.IsScheduledForPeriod(x, period)).Select(item =>
        {
            var payment = payments.FirstOrDefault(x => x.RecurringObligationId == item.Id);
            var due = RecurringObligationHelper.GetDueDate(item, period);
            var status = payment is not null ? "پرداخت‌شده" : due < today ? "عقب‌افتاده" : "در انتظار پرداخت";
            return new MonthlyObligationReport(item.Title, item.Type, item.Category.Name, item.Category.Icon, item.Amount, due,
                payment is not null, payment?.Amount ?? 0, payment?.PaidDate, status);
        }).OrderBy(x => x.IsPaid).ThenBy(x => x.DueDate).ToList();
    }

    private void NormalizePeriod()
    {
        Period = ValidPeriods.Contains(Period, StringComparer.OrdinalIgnoreCase) ? Period.ToLowerInvariant() : "monthly";
        var anchor = Anchor ?? DateOnly.FromDateTime(DateTime.Now);
        Anchor = anchor;
        switch (Period)
        {
            case "daily":
                Start = anchor; EndExclusive = anchor.AddDays(1); PreviousAnchor = anchor.AddDays(-1); NextAnchor = anchor.AddDays(1);
                PeriodTitle = $"روز {Formatters.PersianDate(anchor)}"; break;
            case "weekly":
                Start = anchor.AddDays(-(((int)anchor.DayOfWeek + 1) % 7)); EndExclusive = Start.AddDays(7);
                PreviousAnchor = anchor.AddDays(-7); NextAnchor = anchor.AddDays(7);
                PeriodTitle = $"هفته {Formatters.PersianDate(Start)} تا {Formatters.PersianDate(EndExclusive.AddDays(-1))}"; break;
            case "yearly":
                var persianYear = PersianCalendarHelper.GetYearMonth(anchor).Year;
                Start = PersianCalendarHelper.StartOfYear(persianYear); EndExclusive = PersianCalendarHelper.StartOfYear(persianYear + 1);
                PreviousAnchor = Start.AddDays(-1); NextAnchor = EndExclusive; PeriodTitle = $"سال {persianYear}"; break;
            default:
                var persianMonth = PersianCalendarHelper.GetYearMonth(anchor);
                Start = PersianCalendarHelper.StartOfMonth(persianMonth); EndExclusive = PersianCalendarHelper.EndOfMonthExclusive(persianMonth);
                PreviousAnchor = Start.AddDays(-1); NextAnchor = EndExclusive; PeriodTitle = PersianCalendarHelper.Title(persianMonth); break;
        }
    }

    private IReadOnlyList<TrendPoint> BuildTrend(IReadOnlyList<Expense> expenses)
    {
        if (Period == "yearly")
        {
            var year = PersianCalendarHelper.GetYearMonth(Start).Year;
            return Enumerable.Range(1, 12).Select(month =>
            {
                var period = new PersianYearMonth(year, month);
                var monthStart = PersianCalendarHelper.StartOfMonth(period);
                var monthEnd = PersianCalendarHelper.EndOfMonthExclusive(period);
                var items = expenses.Where(x => x.ExpenseDate >= monthStart && x.ExpenseDate < monthEnd).ToList();
                return new TrendPoint(PersianCalendarHelper.MonthName(month), items.Sum(x => x.Amount), items.Count);
            }).ToList();
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

    private static IEnumerable<PersianYearMonth> EnumeratePersianMonths(DateOnly start, DateOnly endExclusive)
    {
        var period = PersianCalendarHelper.GetYearMonth(start);
        while (PersianCalendarHelper.StartOfMonth(period) < endExclusive)
        {
            yield return period;
            period = period.AddMonths(1);
        }
    }
}
