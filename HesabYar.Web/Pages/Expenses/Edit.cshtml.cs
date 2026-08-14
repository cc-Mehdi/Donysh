using System.ComponentModel.DataAnnotations;
using HesabYar.Web.Data;
using HesabYar.Web.Domain;
using HesabYar.Web.Helpers;
using HesabYar.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace HesabYar.Web.Pages.Expenses;

public sealed class EditModel(ApplicationDbContext db, IWorkspaceContext workspaceContext) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IReadOnlyList<ExpenseCategory> Categories { get; private set; } = [];

    public sealed class InputModel
    {
        public Guid Id { get; set; }

        [Required(ErrorMessage = "دلیل خرج را وارد کنید.")]
        [StringLength(200, MinimumLength = 2)]
        [Display(Name = "دلیل خرج")]
        public string Reason { get; set; } = string.Empty;

        [Range(1, 999_999_999_999, ErrorMessage = "مبلغ باید بیشتر از صفر باشد.")]
        [Display(Name = "مبلغ (تومان)")]
        public decimal Amount { get; set; }

        [Required(ErrorMessage = "دسته‌بندی را انتخاب کنید.")]
        [Display(Name = "دسته‌بندی")]
        public Guid CategoryId { get; set; }

        [Required]
        [Display(Name = "تاریخ خرج")]
        public DateOnly ExpenseDate { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        var expense = await db.Expenses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.WorkspaceId == workspace.Id, cancellationToken);
        if (expense is null)
        {
            return NotFound();
        }

        Input = new InputModel
        {
            Id = expense.Id,
            Reason = expense.Reason,
            Amount = expense.Amount,
            CategoryId = expense.CategoryId,
            ExpenseDate = expense.ExpenseDate
        };
        await LoadCategoriesAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        var expense = await db.Expenses.SingleOrDefaultAsync(x => x.Id == Input.Id && x.WorkspaceId == workspace.Id, cancellationToken);
        if (expense is null)
        {
            return NotFound();
        }

        var categoryExists = await db.ExpenseCategories.AnyAsync(x => x.Id == Input.CategoryId && x.WorkspaceId == workspace.Id && !x.IsArchived, cancellationToken);
        if (!categoryExists)
        {
            ModelState.AddModelError("Input.CategoryId", "دسته‌بندی معتبر نیست.");
        }

        if (!ModelState.IsValid)
        {
            await LoadCategoriesAsync(cancellationToken);
            return Page();
        }

        expense.Reason = Input.Reason.Trim();
        expense.Amount = Input.Amount;
        expense.CategoryId = Input.CategoryId;
        expense.ExpenseDate = Input.ExpenseDate;
        expense.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        TempData["Success"] = "خرج ویرایش شد.";
        await SetBudgetAlertAsync(workspace.Id, cancellationToken);
        return RedirectToPage("Index");
    }

    private async Task SetBudgetAlertAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var period = PersianCalendarHelper.GetYearMonth(Input.ExpenseDate);
        var monthStart = PersianCalendarHelper.StartOfMonth(period);
        var monthEnd = PersianCalendarHelper.EndOfMonthExclusive(period);
        var budgets = await db.Budgets
            .Where(x => x.WorkspaceId == workspaceId && x.Year == period.Year && x.Month == period.Month &&
                        (x.CategoryId == null || x.CategoryId == Input.CategoryId))
            .Include(x => x.Category)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var budgetIds = budgets.Select(x => x.Id).ToList();
        var transfers = budgetIds.Count == 0
            ? new List<BudgetTransfer>()
            : await db.BudgetTransfers
                .Where(x => x.WorkspaceId == workspaceId &&
                            (budgetIds.Contains(x.SourceBudgetId) || budgetIds.Contains(x.DestinationBudgetId)))
                .AsNoTracking()
                .ToListAsync(cancellationToken);

        foreach (var budget in budgets)
        {
            var spent = await db.Expenses
                .Where(x => x.WorkspaceId == workspaceId && x.ExpenseDate >= monthStart && x.ExpenseDate < monthEnd &&
                            (!budget.CategoryId.HasValue || x.CategoryId == budget.CategoryId.Value))
                .SumAsync(x => x.Amount, cancellationToken);

            var incoming = transfers.Where(x => x.DestinationBudgetId == budget.Id).Sum(x => x.Amount);
            var outgoing = transfers.Where(x => x.SourceBudgetId == budget.Id).Sum(x => x.Amount);
            var effectiveAmount = budget.Amount + incoming - outgoing;

            if (spent > effectiveAmount)
            {
                TempData["Error"] = $"هشدار: بودجه «{budget.Category?.Name ?? "کل ماه"}» رد شده است؛ {Formatters.Money(spent - effectiveAmount)} بیشتر از سقف موثر. از صفحه بودجه‌ها می‌توانید کسری را از یک دسته دیگر جبران کنید.";
                return;
            }
        }
    }

    private async Task LoadCategoriesAsync(CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        Categories = await db.ExpenseCategories
            .Where(x => x.WorkspaceId == workspace.Id && (!x.IsArchived || x.Id == Input.CategoryId))
            .OrderBy(x => x.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
}
