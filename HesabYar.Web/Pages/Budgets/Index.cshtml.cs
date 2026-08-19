using System.ComponentModel.DataAnnotations;
using HesabYar.Web.Data;
using HesabYar.Web.Domain;
using HesabYar.Web.Helpers;
using HesabYar.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace HesabYar.Web.Pages.Budgets;

public sealed class IndexModel(
    ApplicationDbContext db,
    IWorkspaceContext workspaceContext,
    BudgetBalanceService budgetBalanceService) : PageModel
{
    [BindProperty(SupportsGet = true)] public int? Year { get; set; }
    [BindProperty(SupportsGet = true)] public int? Month { get; set; }
    [BindProperty] public InputModel Input { get; set; } = new();

    public IReadOnlyList<ExpenseCategory> Categories { get; private set; } = [];
    public IReadOnlyList<BudgetRow> Items { get; private set; } = [];
    public IReadOnlyList<BudgetTransferRow> TransferHistory { get; private set; } = [];
    public decimal TotalBudgetAmount => Items.Sum(x => x.OriginalAmount);
    public decimal TotalEffectiveBudgetAmount => Items.Sum(x => x.EffectiveAmount);
    public decimal TotalCarryoverDeduction => Items.Sum(x => x.CarryoverDeduction);
    public int BudgetCount => Items.Count;
    public DateOnly PeriodStart { get; private set; }
    public DateOnly PeriodEndExclusive { get; private set; }
    public PersianYearMonth PreviousPeriod { get; private set; }
    public PersianYearMonth NextPeriod { get; private set; }
    public string PeriodTitle { get; private set; } = string.Empty;
    public string DefaultTransferDate { get; private set; } = string.Empty;

    public sealed class InputModel
    {
        public Guid? Id { get; set; }
        public Guid? CategoryId { get; set; }
        [Range(1300, 1600)] public int Year { get; set; }
        [Range(1, 12)] public int Month { get; set; }
        [Range(1, 999_999_999_999, ErrorMessage = "مبلغ بودجه باید بیشتر از صفر باشد.")] public decimal Amount { get; set; }
        [Range(1, 100, ErrorMessage = "درصد هشدار باید بین ۱ تا ۱۰۰ باشد.")] public int WarningPercent { get; set; } = 80;
        public bool CarryOverOverspend { get; set; } = true;
    }

    public sealed record TransferSourceOption(Guid BudgetId, string Name, string Icon, decimal AvailableAmount);

    public sealed record BudgetRow(
        Budget Budget,
        decimal OriginalAmount,
        decimal IncomingAmount,
        decimal OutgoingAmount,
        decimal CarryoverDeduction,
        decimal EffectiveAmount,
        decimal Spent,
        decimal Remaining,
        decimal Percent,
        decimal NextMonthCarryover,
        bool IsExceeded,
        bool IsWarning,
        IReadOnlyList<TransferSourceOption> TransferSources);

    public sealed record BudgetTransferRow(
        Guid Id, DateOnly TransferDate, string SourceName, string SourceIcon,
        string DestinationName, string DestinationIcon, decimal Amount,
        string CreatedBy, string? Note);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        NormalizePeriod();
        Input.Year = Year!.Value;
        Input.Month = Month!.Value;
        Input.CarryOverOverspend = true;
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        Year = Input.Year;
        Month = Input.Month;
        NormalizePeriod();
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);

        if (Input.CategoryId.HasValue)
        {
            var validCategory = await db.ExpenseCategories.AnyAsync(
                x => x.Id == Input.CategoryId.Value && x.WorkspaceId == workspace.Id && !x.IsArchived,
                cancellationToken);
            if (!validCategory) ModelState.AddModelError("Input.CategoryId", "دسته‌بندی معتبر نیست.");
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        Budget? budget = null;
        if (Input.Id.HasValue)
        {
            budget = await db.Budgets.SingleOrDefaultAsync(x => x.Id == Input.Id.Value && x.WorkspaceId == workspace.Id, cancellationToken);
        }

        budget ??= await db.Budgets.SingleOrDefaultAsync(
            x => x.WorkspaceId == workspace.Id && x.Year == Input.Year && x.Month == Input.Month && x.CategoryId == Input.CategoryId,
            cancellationToken);

        if (budget is null)
        {
            budget = new Budget { WorkspaceId = workspace.Id };
            db.Budgets.Add(budget);
        }

        budget.CategoryId = Input.CategoryId;
        budget.Year = Input.Year;
        budget.Month = Input.Month;
        budget.Amount = Input.Amount;
        budget.WarningPercent = Input.WarningPercent;
        budget.CarryOverOverspend = Input.CarryOverOverspend;
        await db.SaveChangesAsync(cancellationToken);

        TempData["Success"] = "بودجه ذخیره شد.";
        return RedirectToPage(new { year = Input.Year, month = Input.Month });
    }

    public async Task<IActionResult> OnPostTransferAsync(CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        var form = await Request.ReadFormAsync(cancellationToken);
        _ = int.TryParse(InputNormalization.ToLatinDigits(form["Transfer.Year"].FirstOrDefault()), out var year);
        _ = int.TryParse(InputNormalization.ToLatinDigits(form["Transfer.Month"].FirstOrDefault()), out var month);
        Year = year; Month = month; NormalizePeriod();

        if (!Guid.TryParse(form["Transfer.DestinationBudgetId"].FirstOrDefault(), out var destinationBudgetId) ||
            !Guid.TryParse(form["Transfer.SourceBudgetId"].FirstOrDefault(), out var sourceBudgetId))
        {
            TempData["Error"] = "بودجه مبدا یا مقصد معتبر نیست.";
            return RedirectToPage(new { year = Year, month = Month });
        }
        if (sourceBudgetId == destinationBudgetId)
        {
            TempData["Error"] = "بودجه مبدا و مقصد باید متفاوت باشند.";
            return RedirectToPage(new { year = Year, month = Month });
        }
        if (!InputNormalization.TryParseMoney(form["Transfer.Amount"].FirstOrDefault(), out var amount) || amount <= 0)
        {
            TempData["Error"] = "مبلغ انتقال بودجه باید بیشتر از صفر باشد.";
            return RedirectToPage(new { year = Year, month = Month });
        }
        if (!PersianCalendarHelper.TryParseInput(form["Transfer.Date"].FirstOrDefault(), out var transferDate) || transferDate < PeriodStart || transferDate >= PeriodEndExclusive)
        {
            TempData["Error"] = $"تاریخ انتقال باید داخل دوره {PeriodTitle} باشد.";
            return RedirectToPage(new { year = Year, month = Month });
        }
        var note = form["Transfer.Note"].FirstOrDefault()?.Trim();
        if (note?.Length > 200)
        {
            TempData["Error"] = "توضیح انتقال نمی‌تواند بیشتر از ۲۰۰ کاراکتر باشد.";
            return RedirectToPage(new { year = Year, month = Month });
        }

        var budgets = await db.Budgets
            .Where(x => x.WorkspaceId == workspace.Id && x.Year == Year && x.Month == Month &&
                        (x.Id == sourceBudgetId || x.Id == destinationBudgetId))
            .Include(x => x.Category)
            .ToListAsync(cancellationToken);
        var source = budgets.SingleOrDefault(x => x.Id == sourceBudgetId);
        var destination = budgets.SingleOrDefault(x => x.Id == destinationBudgetId);
        if (source is null || destination is null || source.CategoryId is null || destination.CategoryId is null)
        {
            TempData["Error"] = "انتقال بودجه فقط بین بودجه‌های دسته‌بندی‌شده همین ماه امکان‌پذیر است.";
            return RedirectToPage(new { year = Year, month = Month });
        }

        var states = await budgetBalanceService.GetPeriodAsync(workspace.Id, new PersianYearMonth(Year!.Value, Month!.Value), cancellationToken);
        var sourceState = states.SingleOrDefault(x => x.Budget.Id == source.Id);
        var destinationState = states.SingleOrDefault(x => x.Budget.Id == destination.Id);
        if (sourceState is null || destinationState is null)
        {
            TempData["Error"] = "وضعیت بودجه قابل محاسبه نیست.";
            return RedirectToPage(new { year = Year, month = Month });
        }

        var sourceAvailable = Math.Max(0, sourceState.Remaining);
        var destinationDeficit = Math.Max(0, -destinationState.Remaining);
        if (destinationDeficit <= 0)
        {
            TempData["Error"] = $"بودجه «{destination.Category?.Name}» در حال حاضر از سقف موثر خود عبور نکرده است.";
            return RedirectToPage(new { year = Year, month = Month });
        }
        if (amount > destinationDeficit)
        {
            TempData["Error"] = $"حداکثر مبلغ موردنیاز برای جبران کسری این بودجه {Formatters.Money(destinationDeficit)} است.";
            return RedirectToPage(new { year = Year, month = Month });
        }
        if (amount > sourceAvailable)
        {
            TempData["Error"] = $"بودجه «{source.Category?.Name}» فقط {Formatters.Money(sourceAvailable)} ظرفیت آزاد برای انتقال دارد.";
            return RedirectToPage(new { year = Year, month = Month });
        }

        db.BudgetTransfers.Add(new BudgetTransfer
        {
            WorkspaceId = workspace.Id,
            SourceBudgetId = source.Id,
            DestinationBudgetId = destination.Id,
            CreatedByUserId = workspaceContext.UserId!,
            Amount = amount,
            TransferDate = transferDate,
            Note = string.IsNullOrWhiteSpace(note) ? null : note
        });
        await db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = $"{Formatters.Money(amount)} از بودجه «{source.Category?.Name}» به «{destination.Category?.Name}» منتقل شد.";
        return RedirectToPage(new { year = Year, month = Month });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, int year, int month, CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        var budget = await db.Budgets.SingleOrDefaultAsync(x => x.Id == id && x.WorkspaceId == workspace.Id, cancellationToken);
        if (budget is null) return NotFound();

        var hasTransferHistory = await db.BudgetTransfers.AnyAsync(x => x.SourceBudgetId == id || x.DestinationBudgetId == id, cancellationToken);
        if (hasTransferHistory)
        {
            TempData["Error"] = "این بودجه سابقه انتقال دارد و برای حفظ گزارش مالی قابل حذف نیست. می‌توانید مبلغ آن را ویرایش کنید.";
            return RedirectToPage(new { year, month });
        }

        db.Budgets.Remove(budget);
        await db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "بودجه حذف شد.";
        return RedirectToPage(new { year, month });
    }

    private void NormalizePeriod()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var current = PersianCalendarHelper.GetYearMonth(today);
        var year = Year is >= 1300 and <= 1600 ? Year.Value : current.Year;
        var month = Month is >= 1 and <= 12 ? Month.Value : current.Month;
        var period = new PersianYearMonth(year, month);
        Year = year; Month = month;
        PeriodStart = PersianCalendarHelper.StartOfMonth(period);
        PeriodEndExclusive = PersianCalendarHelper.EndOfMonthExclusive(period);
        PreviousPeriod = period.AddMonths(-1);
        NextPeriod = period.AddMonths(1);
        PeriodTitle = PersianCalendarHelper.Title(period);
        var defaultDate = today >= PeriodStart && today < PeriodEndExclusive ? today : PeriodEndExclusive.AddDays(-1);
        DefaultTransferDate = PersianCalendarHelper.ToInput(defaultDate);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        Categories = await db.ExpenseCategories
            .Where(x => x.WorkspaceId == workspace.Id && !x.IsArchived)
            .OrderBy(x => x.Name).AsNoTracking().ToListAsync(cancellationToken);

        var period = new PersianYearMonth(Year!.Value, Month!.Value);
        var states = await budgetBalanceService.GetPeriodAsync(workspace.Id, period, cancellationToken);
        Items = states.Select(row =>
        {
            IReadOnlyList<TransferSourceOption> sources = row.Budget.CategoryId.HasValue && row.Remaining < 0
                ? states.Where(source => source.Budget.Id != row.Budget.Id && source.Budget.CategoryId.HasValue && source.Remaining > 0)
                    .OrderByDescending(source => source.Remaining)
                    .Select(source => new TransferSourceOption(source.Budget.Id, source.Budget.Category?.Name ?? "دسته", source.Budget.Category?.Icon ?? "📦", source.Remaining))
                    .ToList()
                : new List<TransferSourceOption>();

            return new BudgetRow(
                row.Budget, row.BaseAmount, row.IncomingAmount, row.OutgoingAmount, row.CarryoverDeduction,
                row.EffectiveAmount, row.Spent, row.Remaining, row.Percent, row.NextMonthCarryover,
                row.Remaining < 0, row.Percent >= row.Budget.WarningPercent, sources);
        }).ToList();

        var budgetIds = states.Select(x => x.Budget.Id).ToList();
        var transfers = budgetIds.Count == 0
            ? new List<BudgetTransfer>()
            : await db.BudgetTransfers
                .Where(x => x.WorkspaceId == workspace.Id && (budgetIds.Contains(x.SourceBudgetId) || budgetIds.Contains(x.DestinationBudgetId)))
                .Include(x => x.SourceBudget).ThenInclude(x => x.Category)
                .Include(x => x.DestinationBudget).ThenInclude(x => x.Category)
                .Include(x => x.CreatedByUser)
                .OrderByDescending(x => x.TransferDate).ThenByDescending(x => x.CreatedAtUtc)
                .AsNoTracking().ToListAsync(cancellationToken);

        TransferHistory = transfers.Select(x => new BudgetTransferRow(
            x.Id, x.TransferDate,
            x.SourceBudget.Category?.Name ?? "بودجه", x.SourceBudget.Category?.Icon ?? "📦",
            x.DestinationBudget.Category?.Name ?? "بودجه", x.DestinationBudget.Category?.Icon ?? "📦",
            x.Amount,
            string.IsNullOrWhiteSpace(x.CreatedByUser.DisplayName) ? x.CreatedByUser.Email ?? "کاربر" : x.CreatedByUser.DisplayName,
            x.Note)).ToList();
    }
}
