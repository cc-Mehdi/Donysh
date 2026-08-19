using System.ComponentModel.DataAnnotations;
using HesabYar.Web.Data;
using HesabYar.Web.Domain;
using HesabYar.Web.Helpers;
using HesabYar.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace HesabYar.Web.Pages.Expenses;

public sealed class CreateModel(ApplicationDbContext db, IWorkspaceContext workspaceContext, BudgetBalanceService budgetBalanceService) : PageModel
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
        var period = PersianCalendarHelper.GetYearMonth(Input.ExpenseDate);
        var states = await budgetBalanceService.GetPeriodAsync(workspaceId, period, cancellationToken);
        var exceeded = states
            .Where(x => (x.Budget.CategoryId == null || x.Budget.CategoryId == Input.CategoryId) && x.Remaining < 0)
            .OrderByDescending(x => Math.Abs(x.Remaining))
            .FirstOrDefault();

        if (exceeded is null) return;

        var name = exceeded.Budget.Category?.Name ?? "کل ماه";
        var carryover = exceeded.Budget.CarryOverOverspend
            ? " اگر با انتقال بودجه جبران نشود، این کسری از سقف مؤثر ماه بعد کم می‌شود."
            : string.Empty;
        TempData["Error"] = $"هشدار: بودجه «{name}» {Formatters.Money(Math.Abs(exceeded.Remaining))} از سقف مؤثر عبور کرده است.{carryover}";
    }
}
