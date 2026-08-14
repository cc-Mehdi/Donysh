using System.ComponentModel.DataAnnotations;
using HesabYar.Web.Data;
using HesabYar.Web.Domain;
using HesabYar.Web.Helpers;
using HesabYar.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace HesabYar.Web.Pages.Budgets;

public sealed class IndexModel(ApplicationDbContext db, IWorkspaceContext workspaceContext) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int? Year { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Month { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IReadOnlyList<ExpenseCategory> Categories { get; private set; } = [];
    public IReadOnlyList<BudgetRow> Items { get; private set; } = [];
    public decimal TotalBudgetAmount => Items.Sum(x => x.Budget.Amount);
    public int BudgetCount => Items.Count;
    public DateOnly PeriodStart { get; private set; }
    public PersianYearMonth PreviousPeriod { get; private set; }
    public PersianYearMonth NextPeriod { get; private set; }
    public string PeriodTitle { get; private set; } = string.Empty;

    public sealed class InputModel
    {
        public Guid? Id { get; set; }
        public Guid? CategoryId { get; set; }

        [Range(1300, 1600)]
        public int Year { get; set; }

        [Range(1, 12)]
        public int Month { get; set; }

        [Range(1, 999_999_999_999, ErrorMessage = "مبلغ بودجه باید بیشتر از صفر باشد.")]
        public decimal Amount { get; set; }

        [Range(1, 100, ErrorMessage = "درصد هشدار باید بین ۱ تا ۱۰۰ باشد.")]
        public int WarningPercent { get; set; } = 80;
    }

    public sealed record BudgetRow(Budget Budget, decimal Spent, decimal Remaining, decimal Percent, bool IsExceeded, bool IsWarning);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        NormalizePeriod();
        Input.Year = Year!.Value;
        Input.Month = Month!.Value;
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
            if (!validCategory)
            {
                ModelState.AddModelError("Input.CategoryId", "دسته‌بندی معتبر نیست.");
            }
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
            x => x.WorkspaceId == workspace.Id
                 && x.Year == Input.Year
                 && x.Month == Input.Month
                 && x.CategoryId == Input.CategoryId,
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
        await db.SaveChangesAsync(cancellationToken);

        TempData["Success"] = "بودجه ذخیره شد.";
        return RedirectToPage(new { year = Input.Year, month = Input.Month });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, int year, int month, CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        var budget = await db.Budgets.SingleOrDefaultAsync(x => x.Id == id && x.WorkspaceId == workspace.Id, cancellationToken);
        if (budget is null)
        {
            return NotFound();
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
        Year = year;
        Month = month;
        PeriodStart = PersianCalendarHelper.StartOfMonth(period);
        PreviousPeriod = period.AddMonths(-1);
        NextPeriod = period.AddMonths(1);
        PeriodTitle = PersianCalendarHelper.Title(period);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        Categories = await db.ExpenseCategories
            .Where(x => x.WorkspaceId == workspace.Id && !x.IsArchived)
            .OrderBy(x => x.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var periodEnd = PeriodStart.AddMonths(1);
        var expenses = await db.Expenses
            .Where(x => x.WorkspaceId == workspace.Id && x.ExpenseDate >= PeriodStart && x.ExpenseDate < periodEnd)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var budgets = await db.Budgets
            .Where(x => x.WorkspaceId == workspace.Id && x.Year == Year && x.Month == Month)
            .Include(x => x.Category)
            .OrderBy(x => x.CategoryId == null ? 0 : 1)
            .ThenBy(x => x.Category!.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        Items = budgets.Select(budget =>
        {
            var spent = budget.CategoryId.HasValue
                ? expenses.Where(x => x.CategoryId == budget.CategoryId.Value).Sum(x => x.Amount)
                : expenses.Sum(x => x.Amount);
            var percent = budget.Amount <= 0 ? 0 : Math.Round(spent / budget.Amount * 100, 1);
            return new BudgetRow(budget, spent, budget.Amount - spent, percent, percent >= 100, percent >= budget.WarningPercent);
        }).ToList();
    }
}
