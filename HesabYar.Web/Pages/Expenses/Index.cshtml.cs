using HesabYar.Web.Data;
using HesabYar.Web.Domain;
using HesabYar.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace HesabYar.Web.Pages.Expenses;

public sealed class IndexModel(ApplicationDbContext db, IWorkspaceContext workspaceContext) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? CategoryId { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? To { get; set; }

    public IReadOnlyList<Expense> Items { get; private set; } = [];
    public IReadOnlyList<ExpenseCategory> Categories { get; private set; } = [];
    public decimal Total { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        Categories = await db.ExpenseCategories
            .Where(x => x.WorkspaceId == workspace.Id && !x.IsArchived)
            .OrderBy(x => x.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var query = db.Expenses
            .Where(x => x.WorkspaceId == workspace.Id)
            .Include(x => x.Category)
            .Include(x => x.CreatedByUser)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var search = Q.Trim();
            query = query.Where(x => x.Reason.Contains(search));
        }

        if (CategoryId.HasValue)
        {
            query = query.Where(x => x.CategoryId == CategoryId.Value);
        }

        if (From.HasValue)
        {
            query = query.Where(x => x.ExpenseDate >= From.Value);
        }

        if (To.HasValue)
        {
            query = query.Where(x => x.ExpenseDate <= To.Value);
        }

        Items = await query
            .OrderByDescending(x => x.ExpenseDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Take(250)
            .ToListAsync(cancellationToken);

        Total = Items.Sum(x => x.Amount);
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        var expense = await db.Expenses.SingleOrDefaultAsync(x => x.Id == id && x.WorkspaceId == workspace.Id, cancellationToken);
        if (expense is null)
        {
            return NotFound();
        }

        db.Expenses.Remove(expense);
        await db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "خرج حذف شد.";
        return RedirectToPage();
    }
}
