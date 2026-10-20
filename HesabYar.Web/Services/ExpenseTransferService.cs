using HesabYar.Web.Data;
using HesabYar.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace HesabYar.Web.Services;

public enum ExpenseTransferResult
{
    Success,
    InvalidRequest,
    AccessDenied,
    ExpenseNotFound,
    CategoryNotFound,
    NotAllowed,
    LinkedToObligation
}

public sealed class ExpenseTransferService(ApplicationDbContext db)
{
    public async Task<ExpenseTransferResult> TransferAsync(
        Guid sourceWorkspaceId,
        Guid expenseId,
        Guid destinationWorkspaceId,
        Guid destinationCategoryId,
        string? reason,
        string actingUserId,
        CancellationToken cancellationToken = default)
    {
        var normalizedReason = reason?.Trim();
        if (sourceWorkspaceId == Guid.Empty
            || expenseId == Guid.Empty
            || destinationWorkspaceId == Guid.Empty
            || destinationCategoryId == Guid.Empty
            || sourceWorkspaceId == destinationWorkspaceId
            || string.IsNullOrWhiteSpace(actingUserId)
            || normalizedReason is null
            || normalizedReason.Length is < 2 or > 200)
        {
            return ExpenseTransferResult.InvalidRequest;
        }

        var memberships = await db.WorkspaceMembers
            .Where(x => x.UserId == actingUserId
                        && (x.WorkspaceId == sourceWorkspaceId || x.WorkspaceId == destinationWorkspaceId))
            .Select(x => new { x.WorkspaceId, x.Role })
            .ToListAsync(cancellationToken);

        if (memberships.Select(x => x.WorkspaceId).Distinct().Count() != 2)
        {
            return ExpenseTransferResult.AccessDenied;
        }

        var expense = await db.Expenses
            .SingleOrDefaultAsync(x => x.Id == expenseId && x.WorkspaceId == sourceWorkspaceId, cancellationToken);
        if (expense is null)
        {
            return ExpenseTransferResult.ExpenseNotFound;
        }

        var sourceRole = memberships.Single(x => x.WorkspaceId == sourceWorkspaceId).Role;
        if (expense.CreatedByUserId != actingUserId && sourceRole != WorkspaceRole.Owner)
        {
            return ExpenseTransferResult.NotAllowed;
        }

        if (await db.RecurringObligationPayments.AnyAsync(x => x.ExpenseId == expense.Id, cancellationToken))
        {
            return ExpenseTransferResult.LinkedToObligation;
        }

        var categoryExists = await db.ExpenseCategories.AnyAsync(
            x => x.Id == destinationCategoryId
                 && x.WorkspaceId == destinationWorkspaceId
                 && !x.IsArchived,
            cancellationToken);
        if (!categoryExists)
        {
            return ExpenseTransferResult.CategoryNotFound;
        }

        // Keep the original attribution only when that person is also a member of
        // the destination. Otherwise the user performing the move becomes the
        // registrant so reports never reference a person outside that workspace.
        if (expense.CreatedByUserId != actingUserId)
        {
            var creatorCanAccessDestination = await db.WorkspaceMembers.AnyAsync(
                x => x.WorkspaceId == destinationWorkspaceId && x.UserId == expense.CreatedByUserId,
                cancellationToken);
            if (!creatorCanAccessDestination)
            {
                expense.CreatedByUserId = actingUserId;
            }
        }

        expense.WorkspaceId = destinationWorkspaceId;
        expense.CategoryId = destinationCategoryId;
        expense.Reason = normalizedReason;
        expense.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return ExpenseTransferResult.Success;
    }
}
