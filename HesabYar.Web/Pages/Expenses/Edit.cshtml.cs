using System.ComponentModel.DataAnnotations;
using HesabYar.Web.Data;
using HesabYar.Web.Domain;
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
        return RedirectToPage("Index");
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
