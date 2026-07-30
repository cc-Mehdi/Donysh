using System.ComponentModel.DataAnnotations;
using HesabYar.Web.Data;
using HesabYar.Web.Domain;
using HesabYar.Web.Helpers;
using HesabYar.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace HesabYar.Web.Pages.Expenses;

public sealed class CreateModel(ApplicationDbContext db, IWorkspaceContext workspaceContext) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IReadOnlyList<ExpenseCategory> Categories { get; private set; } = [];

    public sealed class InputModel
    {
        [Required(ErrorMessage = "دلیل خرج را وارد کنید.")]
        [StringLength(200, MinimumLength = 2, ErrorMessage = "شرح خرج باید بین ۲ تا ۲۰۰ کاراکتر باشد.")]
        [Display(Name = "دلیل خرج")]
        public string Reason { get; set; } = string.Empty;

        [Range(1, 999_999_999_999, ErrorMessage = "مبلغ باید بیشتر از صفر باشد.")]
        [Display(Name = "مبلغ (تومان)")]
        public decimal Amount { get; set; }

        [Required(ErrorMessage = "دسته‌بندی را انتخاب کنید.")]
        [Display(Name = "دسته‌بندی")]
        public Guid CategoryId { get; set; }

        [Required(ErrorMessage = "تاریخ را وارد کنید.")]
        [Display(Name = "تاریخ خرج")]
        public DateOnly ExpenseDate { get; set; } = DateOnly.FromDateTime(DateTime.Now);
    }

    public async Task OnGetAsync(CancellationToken cancellationToken) => await LoadCategoriesAsync(cancellationToken);

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        var categoryExists = await db.ExpenseCategories.AnyAsync(
            x => x.Id == Input.CategoryId && x.WorkspaceId == workspace.Id && !x.IsArchived,
            cancellationToken);

        if (!categoryExists)
        {
            ModelState.AddModelError("Input.CategoryId", "دسته‌بندی معتبر نیست.");
        }

        if (!ModelState.IsValid)
        {
            await LoadCategoriesAsync(cancellationToken);
            return Page();
        }

        db.Expenses.Add(new Expense
        {
            WorkspaceId = workspace.Id,
            CategoryId = Input.CategoryId,
            CreatedByUserId = workspaceContext.UserId!,
            Reason = Input.Reason.Trim(),
            Amount = Input.Amount,
            ExpenseDate = Input.ExpenseDate
        });
        await db.SaveChangesAsync(cancellationToken);

        TempData["Success"] = "خرج با موفقیت ثبت شد.";
        await SetBudgetAlertAsync(workspace.Id, cancellationToken);
        return RedirectToPage("Index");
    }

    private async Task LoadCategoriesAsync(CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        Categories = await db.ExpenseCategories
            .Where(x => x.WorkspaceId == workspace.Id && !x.IsArchived)
            .OrderBy(x => x.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    private async Task SetBudgetAlertAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var date = Input.ExpenseDate;
        var period = PersianCalendarHelper.GetYearMonth(date);
        var monthStart = PersianCalendarHelper.StartOfMonth(period);
        var monthEnd = PersianCalendarHelper.EndOfMonthExclusive(period);
        var budgets = await db.Budgets
            .Where(x => x.WorkspaceId == workspaceId && x.Year == period.Year && x.Month == period.Month && (x.CategoryId == null || x.CategoryId == Input.CategoryId))
            .Include(x => x.Category)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        foreach (var budget in budgets)
        {
            var spent = await db.Expenses
                .Where(x => x.WorkspaceId == workspaceId && x.ExpenseDate >= monthStart && x.ExpenseDate < monthEnd && (!budget.CategoryId.HasValue || x.CategoryId == budget.CategoryId.Value))
                .SumAsync(x => x.Amount, cancellationToken);
            if (spent >= budget.Amount)
            {
                TempData["Error"] = $"هشدار: بودجه «{budget.Category?.Name ?? "کل ماه"}» رد شده است؛ {Formatters.Money(spent - budget.Amount)} بیشتر از سقف.";
                return;
            }
        }
    }
}
