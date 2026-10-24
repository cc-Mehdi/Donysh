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
        bool emailVerified,
        string? displayName,
        CancellationToken cancellationToken)
    {
        subject = subject.Trim();
        email = email.Trim().ToLowerInvariant();
        if (subject.Length is 0 or > 450 || email.Length is 0 or > 256)
            throw new InvalidOperationException("The central account claims are incomplete.");

        var normalizedEmail = users.NormalizeEmail(email);
        if (string.IsNullOrWhiteSpace(normalizedEmail))
            throw new InvalidOperationException("The central account email could not be normalized.");

        var existing = await FindAsync(subject, normalizedEmail, emailVerified, cancellationToken);
        if (existing is not null) return existing;
        if (!emailVerified && await db.Users.AsNoTracking()
            .AnyAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken))
            throw new InvalidOperationException("An unverified email cannot be linked to an existing Finance account.");

        var normalizedName = string.IsNullOrWhiteSpace(displayName)
            ? email.Split('@', 2)[0]
            : displayName.Trim();
        if (normalizedName.Length > 100) normalizedName = normalizedName[..100];

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var linked = await FindAsync(subject, normalizedEmail, emailVerified, cancellationToken);
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
                EmailConfirmed = false,
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

    private Task<FinanceAccountLink?> FindAsync(
        string subject,
        string normalizedEmail,
        bool emailVerified,
        CancellationToken cancellationToken) =>
        db.Users.AsNoTracking()
            // An unverified address must never grant access to a legacy Finance account.
            .Where(user => user.Id == subject || (emailVerified && user.NormalizedEmail == normalizedEmail))
            .OrderByDescending(user => user.Id == subject)
            .Select(user => new FinanceAccountLink(user.Id, user.SecurityStamp))
            .FirstOrDefaultAsync(cancellationToken);
}
