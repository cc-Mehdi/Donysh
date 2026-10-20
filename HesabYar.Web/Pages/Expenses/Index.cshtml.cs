using System.ComponentModel.DataAnnotations;
using HesabYar.Web.Data;
using HesabYar.Web.Domain;
using HesabYar.Web.Helpers;
using HesabYar.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace HesabYar.Web.Pages.Expenses;

public sealed class IndexModel(
    ApplicationDbContext db,
    IWorkspaceContext workspaceContext,
    ExpenseTransferService expenseTransferService) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public bool NeedsReview { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? CategoryId { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? To { get; set; }

    public IReadOnlyList<Expense> Items { get; private set; } = [];
    public IReadOnlyList<ExpenseCategory> Categories { get; private set; } = [];
    public IReadOnlyList<MemberExpenseSummary> MemberExpenses { get; private set; } = [];
    public IReadOnlyList<Workspace> TransferDestinations { get; private set; } = [];
    public IReadOnlyList<TransferCategoryOption> TransferCategories { get; private set; } = [];
    public string CurrentWorkspaceName { get; private set; } = string.Empty;
    public decimal Total { get; private set; }
    public decimal MemberExpensesTotal { get; private set; }
    public string MemberExpensesPeriodTitle { get; private set; } = string.Empty;
    public bool HasDateFilter => From.HasValue || To.HasValue;
    public bool HasActiveFilter => ExpenseListFilter.HasAny(Q, CategoryId, NeedsReview, From, To);
    public bool IsSharedWorkspace { get; private set; }

    [BindProperty]
    public TransferExpenseInput Transfer { get; set; } = new();

    public sealed class TransferExpenseInput
    {
        [Required]
        public Guid ExpenseId { get; set; }

        [Required]
        public Guid DestinationWorkspaceId { get; set; }

        [Required]
        public Guid DestinationCategoryId { get; set; }

        [Required(ErrorMessage = "عنوان خرج را وارد کنید.")]
        [StringLength(200, MinimumLength = 2, ErrorMessage = "عنوان خرج باید بین ۲ تا ۲۰۰ کاراکتر باشد.")]
        public string Reason { get; set; } = string.Empty;
    }

    public sealed record TransferCategoryOption(Guid Id, Guid WorkspaceId, string Name, string Icon);

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
        CurrentWorkspaceName = workspace.Name;

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
        query = ExpenseListFilter.Apply(query, Q, CategoryId, NeedsReview, From, To);

        Items = await query
            .OrderByDescending(x => x.ExpenseDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Take(250)
            .ToListAsync(cancellationToken);

        Total = Items.Sum(x => x.Amount);
        await LoadMemberExpenseSummaryAsync(workspace.Id, cancellationToken);
        await LoadTransferOptionsAsync(workspace.Id, cancellationToken);
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

    public async Task<IActionResult> OnPostTransferAsync(CancellationToken cancellationToken)
    {
        var sourceWorkspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "اطلاعات انتقال را کامل و درست وارد کنید.";
            return RedirectToPage();
        }

        var userId = workspaceContext.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Forbid();
        }

        var result = await expenseTransferService.TransferAsync(
            sourceWorkspace.Id,
            Transfer.ExpenseId,
            Transfer.DestinationWorkspaceId,
            Transfer.DestinationCategoryId,
            Transfer.Reason,
            userId,
            cancellationToken);

        if (result == ExpenseTransferResult.Success)
        {
            var destinationName = await db.Workspaces
                .Where(x => x.Id == Transfer.DestinationWorkspaceId)
                .Select(x => x.Name)
                .SingleAsync(cancellationToken);
            TempData["Success"] = $"خرج با موفقیت به فضای «{destinationName}» منتقل شد.";
            return RedirectToPage();
        }

        TempData["Error"] = result switch
        {
            ExpenseTransferResult.AccessDenied => "به فضای مبدأ یا مقصد دسترسی ندارید.",
            ExpenseTransferResult.ExpenseNotFound => "خرج موردنظر پیدا نشد یا دیگر در این فضا نیست.",
            ExpenseTransferResult.CategoryNotFound => "دستهٔ انتخاب‌شده در فضای مقصد معتبر نیست.",
            ExpenseTransferResult.NotAllowed => "فقط ثبت‌کنندهٔ خرج یا مالک فضای مالی می‌تواند آن را منتقل کند.",
            ExpenseTransferResult.LinkedToObligation => "خرجی که از پرداخت قسط یا هزینهٔ ثابت ساخته شده است قابل انتقال نیست.",
            _ => "اطلاعات انتقال معتبر نیست. دوباره تلاش کنید."
        };
        return RedirectToPage();
    }

    private async Task LoadTransferOptionsAsync(Guid currentWorkspaceId, CancellationToken cancellationToken)
    {
        TransferDestinations = (await workspaceContext.GetAccessibleAsync(cancellationToken))
            .Where(x => x.Id != currentWorkspaceId)
            .OrderBy(x => x.Type)
            .ThenBy(x => x.Name)
            .ToList();

        if (TransferDestinations.Count == 0)
        {
            return;
        }

        var destinationIds = TransferDestinations.Select(x => x.Id).ToArray();
        TransferCategories = await db.ExpenseCategories
            .Where(x => destinationIds.Contains(x.WorkspaceId) && !x.IsArchived)
            .OrderBy(x => x.Name)
            .Select(x => new TransferCategoryOption(x.Id, x.WorkspaceId, x.Name, x.Icon))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    private async Task LoadMemberExpenseSummaryAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var currentPeriod = PersianCalendarHelper.GetYearMonth(today);
        var monthStart = PersianCalendarHelper.StartOfMonth(currentPeriod);
        var monthEnd = PersianCalendarHelper.EndOfMonthExclusive(currentPeriod);
        MemberExpensesPeriodTitle = HasDateFilter
            ? From.HasValue && To.HasValue
                ? $"از {PersianCalendarHelper.ToInput(From.Value)} تا {PersianCalendarHelper.ToInput(To.Value)}"
                : From.HasValue
                    ? $"از {PersianCalendarHelper.ToInput(From.Value)}"
                    : $"تا {PersianCalendarHelper.ToInput(To!.Value)}"
            : PersianCalendarHelper.Title(currentPeriod);

        var members = await db.WorkspaceMembers
            .Where(x => x.WorkspaceId == workspaceId)
            .Include(x => x.User)
            .OrderBy(x => x.Role)
            .ThenBy(x => x.User.DisplayName)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        IQueryable<Expense> memberQuery = db.Expenses
            .Where(x => x.WorkspaceId == workspaceId)
            .Include(x => x.Category)
            .AsNoTracking();
        if (!HasDateFilter)
            memberQuery = memberQuery.Where(x => x.ExpenseDate >= monthStart && x.ExpenseDate < monthEnd);

        var monthExpenses = await ExpenseListFilter.Apply(memberQuery, Q, CategoryId, NeedsReview, From, To)
            .ToListAsync(cancellationToken);

        MemberExpensesTotal = monthExpenses.Sum(x => x.Amount);

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
        }).Where(x => ExpenseListFilter.IncludeMember(x.Count, HasActiveFilter)).ToList();
    }
}
