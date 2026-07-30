using System.ComponentModel.DataAnnotations;
using HesabYar.Web.Data;
using HesabYar.Web.Domain;
using HesabYar.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace HesabYar.Web.Pages.Categories;

public sealed class IndexModel(ApplicationDbContext db, IWorkspaceContext workspaceContext) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IReadOnlyList<CategoryRow> Items { get; private set; } = [];

    public sealed class InputModel
    {
        public Guid? Id { get; set; }

        [Required(ErrorMessage = "نام دسته را وارد کنید.")]
        [StringLength(80, MinimumLength = 2, ErrorMessage = "نام دسته باید بین ۲ تا ۸۰ کاراکتر باشد.")]
        public string Name { get; set; } = string.Empty;

        [Required]
        [StringLength(16)]
        public string Icon { get; set; } = "📦";
    }

    public sealed record CategoryRow(ExpenseCategory Category, int ExpenseCount, decimal TotalSpent);

    public async Task OnGetAsync(CancellationToken cancellationToken) => await LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        var normalizedName = Input.Name.Trim();
        var duplicate = await db.ExpenseCategories.AnyAsync(
            x => x.WorkspaceId == workspace.Id
                 && x.Name.ToLower() == normalizedName.ToLower()
                 && (!Input.Id.HasValue || x.Id != Input.Id.Value),
            cancellationToken);

        if (duplicate)
        {
            ModelState.AddModelError("Input.Name", "دسته‌ای با این نام وجود دارد.");
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        ExpenseCategory category;
        if (Input.Id.HasValue)
        {
            category = await db.ExpenseCategories.SingleOrDefaultAsync(
                x => x.Id == Input.Id.Value && x.WorkspaceId == workspace.Id,
                cancellationToken) ?? throw new InvalidOperationException("Category not found.");
            category.Name = normalizedName;
            category.Icon = Input.Icon.Trim();
            category.IsArchived = false;
        }
        else
        {
            category = new ExpenseCategory
            {
                WorkspaceId = workspace.Id,
                Name = normalizedName,
                Icon = Input.Icon.Trim(),
                Color = "slate"
            };
            db.ExpenseCategories.Add(category);
        }

        await db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = Input.Id.HasValue ? "دسته‌بندی ویرایش شد." : "دسته‌بندی ساخته شد.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostArchiveAsync(Guid id, CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        var category = await db.ExpenseCategories.SingleOrDefaultAsync(x => x.Id == id && x.WorkspaceId == workspace.Id, cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        category.IsArchived = true;
        await db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "دسته‌بندی بایگانی شد؛ مخارج قبلی حفظ می‌شوند.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRestoreAsync(Guid id, CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        var category = await db.ExpenseCategories.SingleOrDefaultAsync(x => x.Id == id && x.WorkspaceId == workspace.Id, cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        category.IsArchived = false;
        await db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "دسته‌بندی فعال شد.";
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        Items = await db.ExpenseCategories
            .Where(x => x.WorkspaceId == workspace.Id)
            .OrderBy(x => x.IsArchived)
            .ThenBy(x => x.Name)
            .Select(x => new CategoryRow(x, x.Expenses.Count, x.Expenses.Sum(e => (decimal?)e.Amount) ?? 0))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
}
