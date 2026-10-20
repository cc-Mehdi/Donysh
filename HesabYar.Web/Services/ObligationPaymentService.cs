using System.Data;
using HesabYar.Web.Data;
using HesabYar.Web.Domain;
using HesabYar.Web.Helpers;
using Microsoft.EntityFrameworkCore;

namespace HesabYar.Web.Services;

public sealed class ObligationPaymentService(ApplicationDbContext db)
{
    public async Task<ObligationPaymentResult> RecordAsync(
        Guid workspaceId,
        Guid obligationId,
        PersianYearMonth period,
        decimal amount,
        DateOnly paidDate,
        string? note,
        string userId,
        Guid submissionKey,
        CancellationToken cancellationToken)
    {
        if (submissionKey == Guid.Empty || amount <= 0 || amount > 999_999_999_999 || note?.Length > 200)
        {
            return ObligationPaymentResult.Invalid();
        }

        var strategy = db.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

                var duplicate = await db.RecurringObligationPayments
                    .AsNoTracking()
                    .AnyAsync(x => x.SubmissionKey == submissionKey, cancellationToken);
                if (duplicate)
                {
                    return ObligationPaymentResult.Duplicate();
                }

                var obligation = await db.RecurringObligations
                    .Include(x => x.Category)
                    .SingleOrDefaultAsync(x => x.Id == obligationId && x.WorkspaceId == workspaceId, cancellationToken);
                if (obligation is null)
                {
                    return ObligationPaymentResult.NotFound();
                }

                if (!RecurringObligationHelper.IsScheduledForPeriod(obligation, period))
                {
                    return ObligationPaymentResult.NotScheduled(obligation.Title);
                }

                var alreadyPaid = await db.RecurringObligationPayments
                    .Where(x => x.RecurringObligationId == obligation.Id &&
                                x.PeriodYear == period.Year && x.PeriodMonth == period.Month)
                    .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0;
                var remaining = Math.Max(0, obligation.Amount - alreadyPaid);

                if (obligation.Type == RecurringObligationType.Installment && amount > remaining)
                {
                    return ObligationPaymentResult.ExceedsRemaining(obligation.Title, obligation.CategoryId, obligation.Category.Name, remaining);
                }

                var expense = new Expense
                {
                    WorkspaceId = workspaceId,
                    CategoryId = obligation.CategoryId,
                    CreatedByUserId = userId,
                    Reason = obligation.Type == RecurringObligationType.Installment
                        ? $"قسط: {obligation.Title}"
                        : $"هزینه ثابت: {obligation.Title}",
                    Amount = amount,
                    ExpenseDate = paidDate
                };
                var payment = new RecurringObligationPayment
                {
                    SubmissionKey = submissionKey,
                    RecurringObligationId = obligation.Id,
                    ExpenseId = expense.Id,
                    PaidByUserId = userId,
                    PeriodYear = period.Year,
                    PeriodMonth = period.Month,
                    Amount = amount,
                    PaidDate = paidDate,
                    Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
                };

                db.Expenses.Add(expense);
                db.RecurringObligationPayments.Add(payment);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return ObligationPaymentResult.Success(
                    payment.Id,
                    obligation.Title,
                    obligation.CategoryId,
                    obligation.Category.Name,
                    Math.Max(0, obligation.Amount - alreadyPaid - amount));
            });
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            if (await db.RecurringObligationPayments.AsNoTracking()
                    .AnyAsync(x => x.SubmissionKey == submissionKey, cancellationToken))
            {
                return ObligationPaymentResult.Duplicate();
            }

            throw;
        }
    }
}

public enum ObligationPaymentResultKind
{
    Success,
    Duplicate,
    Invalid,
    NotFound,
    NotScheduled,
    ExceedsRemaining
}

public sealed record ObligationPaymentResult(
    ObligationPaymentResultKind Kind,
    Guid? PaymentId = null,
    string? ObligationTitle = null,
    Guid? CategoryId = null,
    string? CategoryName = null,
    decimal RemainingAmount = 0)
{
    public static ObligationPaymentResult Success(Guid paymentId, string title, Guid categoryId, string categoryName, decimal remaining)
        => new(ObligationPaymentResultKind.Success, paymentId, title, categoryId, categoryName, remaining);

    public static ObligationPaymentResult Duplicate() => new(ObligationPaymentResultKind.Duplicate);
    public static ObligationPaymentResult Invalid() => new(ObligationPaymentResultKind.Invalid);
    public static ObligationPaymentResult NotFound() => new(ObligationPaymentResultKind.NotFound);
    public static ObligationPaymentResult NotScheduled(string title) => new(ObligationPaymentResultKind.NotScheduled, ObligationTitle: title);
    public static ObligationPaymentResult ExceedsRemaining(string title, Guid categoryId, string categoryName, decimal remaining)
        => new(ObligationPaymentResultKind.ExceedsRemaining, ObligationTitle: title, CategoryId: categoryId, CategoryName: categoryName, RemainingAmount: remaining);
}
