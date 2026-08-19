using System.ComponentModel.DataAnnotations;
using HesabYar.Web.Data;
using HesabYar.Web.Domain;
using HesabYar.Web.Helpers;
using HesabYar.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace HesabYar.Web.Pages.Obligations;

public sealed class IndexModel(
    ApplicationDbContext db,
    IWorkspaceContext workspaceContext,
    BudgetBalanceService budgetBalanceService) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int? Year { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Month { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IReadOnlyList<ExpenseCategory> Categories { get; private set; } = [];
    public IReadOnlyList<ObligationRow> Items { get; private set; } = [];
    public IReadOnlyList<RecurringObligation> Definitions { get; private set; } = [];
    public PersianYearMonth PreviousPeriod { get; private set; }
    public PersianYearMonth NextPeriod { get; private set; }
    public string PeriodTitle { get; private set; } = string.Empty;
    public string DefaultPaidDate { get; private set; } = string.Empty;
    public decimal MonthExpectedTotal => Items.Sum(x => x.ExpectedAmount);
    public decimal MonthPaidTotal => Items.Where(x => x.IsPaid).Sum(x => x.PaidAmount);
    public decimal MonthUnpaidTotal => Items.Where(x => !x.IsPaid).Sum(x => x.ExpectedAmount);
    public int UnpaidCount => Items.Count(x => !x.IsPaid);
    public int AlertCount => Items.Count(x => x.IsOverdue || x.IsDueSoon);

    public sealed class InputModel
    {
        public Guid? Id { get; set; }

        [Required(ErrorMessage = "عنوان را وارد کنید.")]
        [StringLength(140, MinimumLength = 2, ErrorMessage = "عنوان باید بین ۲ تا ۱۴۰ کاراکتر باشد.")]
        public string Title { get; set; } = string.Empty;

        [Required]
        public RecurringObligationType Type { get; set; } = RecurringObligationType.Installment;

        [Required(ErrorMessage = "دسته‌بندی را انتخاب کنید.")]
        public Guid CategoryId { get; set; }

        [Range(1, 999_999_999_999, ErrorMessage = "مبلغ باید بیشتر از صفر باشد.")]
        public decimal Amount { get; set; }

        [Range(1300, 1600)]
        public int StartYear { get; set; }

        [Range(1, 12)]
        public int StartMonth { get; set; }

        [Range(1, 600, ErrorMessage = "تعداد ماه‌ها باید بیشتر از صفر باشد.")]
        public int? DurationMonths { get; set; }

        [Range(1, 31, ErrorMessage = "روز سررسید باید بین ۱ تا ۳۱ باشد.")]
        public int DueDay { get; set; } = 1;

        [Range(0, 30, ErrorMessage = "یادآوری می‌تواند بین صفر تا ۳۰ روز قبل باشد.")]
        public int ReminderDaysBefore { get; set; } = 3;

        [StringLength(500)]
        public string? Note { get; set; }
    }

    public sealed record ObligationRow(
        RecurringObligation Obligation,
        DateOnly DueDate,
        decimal ExpectedAmount,
        bool IsPaid,
        decimal PaidAmount,
        DateOnly? PaidDate,
        Guid? PaymentId,
        bool IsOverdue,
        bool IsDueSoon,
        int DaysUntilDue,
        int? InstallmentNumber,
        int? RemainingInstallments,
        string StatusText,
        string StatusClass);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        NormalizePeriod();
        var current = PersianCalendarHelper.GetYearMonth(DateOnly.FromDateTime(DateTime.Now));
        Input.StartYear = current.Year;
        Input.StartMonth = current.Month;
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);

        if (Input.Type == RecurringObligationType.Installment && !Input.DurationMonths.HasValue)
        {
            ModelState.AddModelError("Input.DurationMonths", "برای قسط، تعداد ماه‌های پرداخت را مشخص کنید.");
        }

        if (Input.Type == RecurringObligationType.FixedMonthlyExpense)
        {
            Input.DurationMonths = null;
        }

        var categoryExists = await db.ExpenseCategories.AnyAsync(
            x => x.Id == Input.CategoryId && x.WorkspaceId == workspace.Id && !x.IsArchived,
            cancellationToken);
        if (!categoryExists)
        {
            ModelState.AddModelError("Input.CategoryId", "دسته‌بندی معتبر نیست.");
        }

        if (!ModelState.IsValid)
        {
            NormalizePeriod();
            await LoadAsync(cancellationToken);
            return Page();
        }

        RecurringObligation? item = null;
        if (Input.Id.HasValue)
        {
            item = await db.RecurringObligations.SingleOrDefaultAsync(
                x => x.Id == Input.Id.Value && x.WorkspaceId == workspace.Id,
                cancellationToken);
            if (item is null)
            {
                return NotFound();
            }
        }

        if (item is null)
        {
            item = new RecurringObligation
            {
                WorkspaceId = workspace.Id,
                CreatedByUserId = workspaceContext.UserId!,
                IsActive = true
            };
            db.RecurringObligations.Add(item);
        }

        item.Title = Input.Title.Trim();
        item.Type = Input.Type;
        item.CategoryId = Input.CategoryId;
        item.Amount = Input.Amount;
        item.StartYear = Input.StartYear;
        item.StartMonth = Input.StartMonth;
        item.DurationMonths = Input.DurationMonths;
        item.DueDay = Input.DueDay;
        item.ReminderDaysBefore = Input.ReminderDaysBefore;
        item.Note = string.IsNullOrWhiteSpace(Input.Note) ? null : Input.Note.Trim();
        item.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = Input.Id.HasValue ? "تعهد ماهانه ویرایش شد." : "تعهد ماهانه اضافه شد.";
        return RedirectToPage(new { year = Year, month = Month });
    }

    public async Task<IActionResult> OnPostPayAsync(CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        var form = await Request.ReadFormAsync(cancellationToken);

        if (!Guid.TryParse(form["Payment.ObligationId"].FirstOrDefault(), out var obligationId) ||
            !int.TryParse(InputNormalization.ToLatinDigits(form["Payment.Year"].FirstOrDefault()), out var year) ||
            !int.TryParse(InputNormalization.ToLatinDigits(form["Payment.Month"].FirstOrDefault()), out var month))
        {
            TempData["Error"] = "اطلاعات پرداخت معتبر نیست.";
            return RedirectToPage();
        }

        Year = year;
        Month = month;
        NormalizePeriod();
        var period = new PersianYearMonth(Year!.Value, Month!.Value);

        var obligation = await db.RecurringObligations
            .Include(x => x.Category)
            .SingleOrDefaultAsync(x => x.Id == obligationId && x.WorkspaceId == workspace.Id, cancellationToken);
        if (obligation is null)
        {
            return NotFound();
        }

        if (!RecurringObligationHelper.IsScheduledForPeriod(obligation, period))
        {
            TempData["Error"] = "این تعهد برای ماه انتخاب‌شده فعال نیست.";
            return RedirectToPage(new { year = Year, month = Month });
        }

        var exists = await db.RecurringObligationPayments.AnyAsync(
            x => x.RecurringObligationId == obligation.Id && x.PeriodYear == period.Year && x.PeriodMonth == period.Month,
            cancellationToken);
        if (exists)
        {
            TempData["Error"] = "پرداخت این ماه قبلاً ثبت شده است.";
            return RedirectToPage(new { year = Year, month = Month });
        }

        if (!InputNormalization.TryParseMoney(form["Payment.Amount"].FirstOrDefault(), out var amount) || amount <= 0)
        {
            TempData["Error"] = "مبلغ پرداخت معتبر نیست.";
            return RedirectToPage(new { year = Year, month = Month });
        }

        if (!PersianCalendarHelper.TryParseInput(form["Payment.PaidDate"].FirstOrDefault(), out var paidDate))
        {
            TempData["Error"] = "تاریخ پرداخت معتبر نیست.";
            return RedirectToPage(new { year = Year, month = Month });
        }

        var note = form["Payment.Note"].FirstOrDefault()?.Trim();
        if (note?.Length > 200)
        {
            TempData["Error"] = "توضیح پرداخت نمی‌تواند بیشتر از ۲۰۰ کاراکتر باشد.";
            return RedirectToPage(new { year = Year, month = Month });
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var expense = new Expense
        {
            WorkspaceId = workspace.Id,
            CategoryId = obligation.CategoryId,
            CreatedByUserId = workspaceContext.UserId!,
            Reason = obligation.Type == RecurringObligationType.Installment
                ? $"قسط: {obligation.Title}"
                : $"هزینه ثابت: {obligation.Title}",
            Amount = amount,
            ExpenseDate = paidDate
        };
        db.Expenses.Add(expense);

        db.RecurringObligationPayments.Add(new RecurringObligationPayment
        {
            RecurringObligationId = obligation.Id,
            ExpenseId = expense.Id,
            PaidByUserId = workspaceContext.UserId!,
            PeriodYear = period.Year,
            PeriodMonth = period.Month,
            Amount = amount,
            PaidDate = paidDate,
            Note = string.IsNullOrWhiteSpace(note) ? null : note
        });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        TempData["Success"] = $"پرداخت «{obligation.Title}» ثبت و به مخارج اضافه شد.";

        var paidPeriod = PersianCalendarHelper.GetYearMonth(paidDate);
        var budgetStates = await budgetBalanceService.GetPeriodAsync(workspace.Id, paidPeriod, cancellationToken);
        var exceeded = budgetStates.FirstOrDefault(x => x.Budget.CategoryId == obligation.CategoryId && x.Remaining < 0);
        if (exceeded is not null)
        {
            TempData["Error"] = $"هشدار بودجه: با این پرداخت، بودجه «{obligation.Category.Name}» {Formatters.Money(Math.Abs(exceeded.Remaining))} کسری دارد. اگر انتقال بودجه انجام نشود، این کسری طبق تنظیم بودجه به ماه بعد منتقل می‌شود.";
        }

        return RedirectToPage(new { year = Year, month = Month });
    }

    public async Task<IActionResult> OnPostUndoPaymentAsync(Guid paymentId, int year, int month, CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        var payment = await db.RecurringObligationPayments
            .Include(x => x.RecurringObligation)
            .Include(x => x.Expense)
            .SingleOrDefaultAsync(
                x => x.Id == paymentId && x.RecurringObligation.WorkspaceId == workspace.Id,
                cancellationToken);
        if (payment is null)
        {
            return NotFound();
        }

        db.RecurringObligationPayments.Remove(payment);
        db.Expenses.Remove(payment.Expense);
        await db.SaveChangesAsync(cancellationToken);

        TempData["Success"] = "ثبت پرداخت لغو شد و خرج مرتبط نیز حذف شد.";
        return RedirectToPage(new { year, month });
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid id, int year, int month, CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        var item = await db.RecurringObligations.SingleOrDefaultAsync(
            x => x.Id == id && x.WorkspaceId == workspace.Id,
            cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        item.IsActive = !item.IsActive;
        item.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = item.IsActive ? "تعهد دوباره فعال شد." : "تعهد متوقف شد و برای ماه‌های بعد اعلان ایجاد نمی‌کند.";
        return RedirectToPage(new { year, month });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, int year, int month, CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        var item = await db.RecurringObligations.SingleOrDefaultAsync(
            x => x.Id == id && x.WorkspaceId == workspace.Id,
            cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        var hasPayments = await db.RecurringObligationPayments.AnyAsync(x => x.RecurringObligationId == id, cancellationToken);
        if (hasPayments)
        {
            TempData["Error"] = "این تعهد سابقه پرداخت دارد و برای حفظ گزارش مالی قابل حذف نیست؛ آن را متوقف کنید.";
            return RedirectToPage(new { year, month });
        }

        db.RecurringObligations.Remove(item);
        await db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "تعهد حذف شد.";
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
        PreviousPeriod = period.AddMonths(-1);
        NextPeriod = period.AddMonths(1);
        PeriodTitle = PersianCalendarHelper.Title(period);
        DefaultPaidDate = PersianCalendarHelper.ToInput(today);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var workspace = await workspaceContext.RequireCurrentAsync(cancellationToken);
        var period = new PersianYearMonth(Year!.Value, Month!.Value);
        var today = DateOnly.FromDateTime(DateTime.Now);

        Categories = await db.ExpenseCategories
            .Where(x => x.WorkspaceId == workspace.Id && !x.IsArchived)
            .OrderBy(x => x.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        Definitions = await db.RecurringObligations
            .Where(x => x.WorkspaceId == workspace.Id)
            .Include(x => x.Category)
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Type)
            .ThenBy(x => x.Title)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var definitionIds = Definitions.Select(x => x.Id).ToList();
        var payments = definitionIds.Count == 0
            ? new List<RecurringObligationPayment>()
            : await db.RecurringObligationPayments
                .Where(x => definitionIds.Contains(x.RecurringObligationId) && x.PeriodYear == period.Year && x.PeriodMonth == period.Month)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

        var rows = new List<ObligationRow>();
        foreach (var item in Definitions)
        {
            var payment = payments.SingleOrDefault(x => x.RecurringObligationId == item.Id);
            var scheduled = RecurringObligationHelper.IsScheduledForPeriod(item, period);
            if (!scheduled && payment is null)
            {
                continue;
            }

            var dueDate = RecurringObligationHelper.GetDueDate(item, period);
            var daysUntilDue = dueDate.DayNumber - today.DayNumber;
            var isPaid = payment is not null;
            var isOverdue = !isPaid && dueDate < today;
            var isDueSoon = !isPaid && daysUntilDue >= 0 && daysUntilDue <= item.ReminderDaysBefore;

            var (statusText, statusClass) = isPaid
                ? ("پرداخت‌شده", "bg-emerald-100 text-emerald-700")
                : isOverdue
                    ? ("عقب‌افتاده", "bg-rose-100 text-rose-700")
                    : isDueSoon
                        ? ("نزدیک سررسید", "bg-amber-100 text-amber-700")
                        : ("در انتظار پرداخت", "bg-slate-100 text-slate-700");

            rows.Add(new ObligationRow(
                item,
                dueDate,
                item.Amount,
                isPaid,
                payment?.Amount ?? 0,
                payment?.PaidDate,
                payment?.Id,
                isOverdue,
                isDueSoon,
                daysUntilDue,
                RecurringObligationHelper.GetInstallmentNumber(item, period),
                RecurringObligationHelper.GetRemainingInstallments(item, period),
                statusText,
                statusClass));
        }

        Items = rows
            .OrderBy(x => x.IsPaid)
            .ThenByDescending(x => x.IsOverdue)
            .ThenBy(x => x.DueDate)
            .ThenBy(x => x.Obligation.Title)
            .ToList();
    }
}
