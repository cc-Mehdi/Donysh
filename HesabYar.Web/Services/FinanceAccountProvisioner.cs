using HesabYar.Web.Data;
using HesabYar.Web.Domain;
using HesabYar.Web.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HesabYar.Web.Services;

public sealed record FinanceAccountLink(string UserId, string? SecurityStamp);

public sealed class FinanceAccountProvisioner(
    ApplicationDbContext db,
    UserManager<ApplicationUser> users)
{
    public async Task<FinanceAccountLink> EnsureAsync(
        string subject,
        string email,
        string? displayName,
        CancellationToken cancellationToken)
    {
        subject = subject.Trim();
        email = email.Trim().ToLowerInvariant();
        if (subject.Length is 0 or > 450 || email.Length is 0 or > 256)
            throw new InvalidOperationException("The central account claims are incomplete.");

        var existing = await FindAsync(subject, cancellationToken);
        if (existing is not null) return existing;

        var normalizedName = string.IsNullOrWhiteSpace(displayName)
            ? email.Split('@', 2)[0]
            : displayName.Trim();
        if (normalizedName.Length > 100) normalizedName = normalizedName[..100];

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var linked = await FindAsync(subject, cancellationToken);
            if (linked is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return linked;
            }

            var user = new ApplicationUser
            {
                Id = subject,
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = normalizedName
            };
            var created = await users.CreateAsync(user);
            if (!created.Succeeded)
            {
                var codes = string.Join(',', created.Errors.Select(error => error.Code));
                throw new InvalidOperationException($"The central account could not be linked: {codes}");
            }

            var workspace = new Workspace
            {
                Name = $"فضای شخصی {normalizedName}",
                Type = WorkspaceType.Personal,
                OwnerUserId = user.Id
            };
            db.Workspaces.Add(workspace);
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                WorkspaceId = workspace.Id,
                UserId = user.Id,
                Role = WorkspaceRole.Owner
            });
            db.ExpenseCategories.AddRange(DefaultCategories.For(workspace.Id));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new FinanceAccountLink(user.Id, user.SecurityStamp);
        });
    }

    private Task<FinanceAccountLink?> FindAsync(string subject, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking()
            .Where(user => user.Id == subject)
            .Select(user => new FinanceAccountLink(user.Id, user.SecurityStamp))
            .SingleOrDefaultAsync(cancellationToken);
}
