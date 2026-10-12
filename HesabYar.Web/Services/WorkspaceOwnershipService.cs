using HesabYar.Web.Data;
using HesabYar.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace HesabYar.Web.Services;

public enum OwnershipTransferResult
{
    Success,
    WorkspaceNotFound,
    NotCurrentOwner,
    TargetNotMember,
    InvalidTarget,
    MembershipConflict
}

public sealed class WorkspaceOwnershipService(ApplicationDbContext db)
{
    public async Task<OwnershipTransferResult> TransferAsync(
        Guid workspaceId,
        string currentOwnerUserId,
        string targetUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(currentOwnerUserId)
            || string.IsNullOrWhiteSpace(targetUserId)
            || currentOwnerUserId == targetUserId
            || targetUserId.Length > 450)
        {
            return OwnershipTransferResult.InvalidTarget;
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            var workspace = await db.Workspaces
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.Id == workspaceId && x.Type == WorkspaceType.Shared,
                    cancellationToken);

            if (workspace is null)
            {
                return OwnershipTransferResult.WorkspaceNotFound;
            }

            if (workspace.OwnerUserId != currentOwnerUserId)
            {
                return OwnershipTransferResult.NotCurrentOwner;
            }

            var targetExists = await db.WorkspaceMembers
                .AnyAsync(
                    x => x.WorkspaceId == workspaceId &&
                         x.UserId == targetUserId &&
                         x.Role == WorkspaceRole.Member,
                    cancellationToken);
            if (!targetExists)
            {
                return OwnershipTransferResult.TargetNotMember;
            }

            // The conditional update is the concurrency guard. If two transfers race,
            // only the request that still matches the current owner can commit.
            var workspaceChanges = await db.Workspaces
                .Where(x => x.Id == workspaceId &&
                            x.Type == WorkspaceType.Shared &&
                            x.OwnerUserId == currentOwnerUserId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.OwnerUserId, targetUserId),
                    cancellationToken);
            if (workspaceChanges != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return OwnershipTransferResult.NotCurrentOwner;
            }

            var formerOwnerChanges = await db.WorkspaceMembers
                .Where(x => x.WorkspaceId == workspaceId &&
                            x.UserId == currentOwnerUserId &&
                            x.Role == WorkspaceRole.Owner)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.Role, WorkspaceRole.Member),
                    cancellationToken);

            var newOwnerChanges = await db.WorkspaceMembers
                .Where(x => x.WorkspaceId == workspaceId &&
                            x.UserId == targetUserId &&
                            x.Role == WorkspaceRole.Member)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.Role, WorkspaceRole.Owner),
                    cancellationToken);

            if (formerOwnerChanges != 1 || newOwnerChanges != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return OwnershipTransferResult.MembershipConflict;
            }

            await transaction.CommitAsync(cancellationToken);
            return OwnershipTransferResult.Success;
        });
    }
}
