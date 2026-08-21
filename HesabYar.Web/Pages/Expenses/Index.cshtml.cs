using HesabYar.Web.Data;
using HesabYar.Web.Domain;
using HesabYar.Web.Helpers;
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
    public IReadOnlyList<MemberExpenseSummary> MemberExpenses { get; private set; } = [];
    public decimal Total { get; private set; }
    public decimal CurrentMonthTotal { get; private set; }
    public string CurrentMonthTitle { get; private set; } = string.Empty;
    public bool IsSharedWorkspace { get; private set; }

    public sealed record MemberExpenseCategory(Guid CategoryId, string Name, string Icon, decimal Amount, int Count);

    public sealed record MemberExpenseSummary(
        string UserId,
        string DisplayName,
        string Email,
        decimal Amount,
        int Count,
        IReadOnlyList<MemberExpenseCategory> Categories);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        IsSharedWorkspace = workspace.Type == WorkspaceType.Shared;

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
        await LoadMemberExpenseSummaryAsync(workspace.Id, cancellationToken);
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        var expense = await db.Expenses.SingleOrDefaultAsync(x => x.Id == id && x.WorkspaceId == workspace.Id, cancellationToken);
        if (expense is null)
        {
            return NotFound();
        }

        var recurringPayment = await db.RecurringObligationPayments
            .SingleOrDefaultAsync(x => x.ExpenseId == expense.Id, cancellationToken);
        if (recurringPayment is not null)
        {
            db.RecurringObligationPayments.Remove(recurringPayment);
        }

        db.Expenses.Remove(expense);
        await db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = recurringPayment is null
            ? "خرج حذف شد."
            : "خرج حذف شد و پرداخت تعهد ماهانه مرتبط دوباره به حالت پرداخت‌نشده برگشت.";
        return RedirectToPage();
    }

    private async Task LoadMemberExpenseSummaryAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var currentPeriod = PersianCalendarHelper.GetYearMonth(today);
        var monthStart = PersianCalendarHelper.StartOfMonth(currentPeriod);
        var monthEnd = PersianCalendarHelper.EndOfMonthExclusive(currentPeriod);
        CurrentMonthTitle = PersianCalendarHelper.Title(currentPeriod);

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

        CurrentMonthTotal = monthExpenses.Sum(x => x.Amount);

        MemberExpenses = members.Select(member =>
        {
            var memberItems = monthExpenses
                .Where(x => x.CreatedByUserId == member.UserId)
                .ToList();

            var categories = memberItems
                .GroupBy(x => new { x.CategoryId, x.Category.Name, x.Category.Icon })
                .Select(group => new MemberExpenseCategory(
                    group.Key.CategoryId,
                    group.Key.Name,
                    group.Key.Icon,
                    group.Sum(x => x.Amount),
                    group.Count()))
                .OrderByDescending(x => x.Amount)
                .ToList();

            var displayName = string.IsNullOrWhiteSpace(member.User.DisplayName)
                ? member.User.Email ?? "کاربر"
                : member.User.DisplayName;

            return new MemberExpenseSummary(
                member.UserId,
                displayName,
                member.User.Email ?? string.Empty,
                memberItems.Sum(x => x.Amount),
                memberItems.Count,
                categories);
        }).ToList();
    }
}
