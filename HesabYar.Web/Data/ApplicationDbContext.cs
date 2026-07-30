using HesabYar.Web.Domain;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HesabYar.Web.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<WorkspaceMember> WorkspaceMembers => Set<WorkspaceMember>();
    public DbSet<WorkspaceInvitation> WorkspaceInvitations => Set<WorkspaceInvitation>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<SavingsGoal> SavingsGoals => Set<SavingsGoal>();
    public DbSet<SavingsContribution> SavingsContributions => Set<SavingsContribution>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(x => x.DisplayName).HasMaxLength(100);
            entity.HasIndex(x => x.NormalizedEmail).IsUnique();
        });

        builder.Entity<Workspace>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(100);
            entity.HasOne(x => x.OwnerUser)
                .WithMany()
                .HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<WorkspaceMember>(entity =>
        {
            entity.HasKey(x => new { x.WorkspaceId, x.UserId });
            entity.HasOne(x => x.Workspace)
                .WithMany(x => x.Members)
                .HasForeignKey(x => x.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.User)
                .WithMany(x => x.WorkspaceMemberships)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WorkspaceInvitation>(entity =>
        {
            entity.Property(x => x.Email).HasMaxLength(256);
            entity.Property(x => x.Token).HasMaxLength(96);
            entity.HasIndex(x => x.Token).IsUnique();
            entity.HasOne(x => x.Workspace)
                .WithMany()
                .HasForeignKey(x => x.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.InvitedByUser)
                .WithMany()
                .HasForeignKey(x => x.InvitedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ExpenseCategory>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(80);
            entity.Property(x => x.Icon).HasMaxLength(16);
            entity.Property(x => x.Color).HasMaxLength(20);
            entity.HasIndex(x => new { x.WorkspaceId, x.Name }).IsUnique();
            entity.HasOne(x => x.Workspace)
                .WithMany(x => x.Categories)
                .HasForeignKey(x => x.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Expense>(entity =>
        {
            entity.Property(x => x.Amount).HasPrecision(18, 0);
            entity.Property(x => x.Reason).HasMaxLength(200);
            entity.HasIndex(x => new { x.WorkspaceId, x.ExpenseDate });
            entity.HasOne(x => x.Workspace)
                .WithMany(x => x.Expenses)
                .HasForeignKey(x => x.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Category)
                .WithMany(x => x.Expenses)
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.CreatedByUser)
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Budget>(entity =>
        {
            entity.Property(x => x.Amount).HasPrecision(18, 0);
            entity.HasIndex(x => new { x.WorkspaceId, x.Year, x.Month });
            entity.HasOne(x => x.Workspace)
                .WithMany(x => x.Budgets)
                .HasForeignKey(x => x.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Category)
                .WithMany(x => x.Budgets)
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<SavingsGoal>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.Property(x => x.TargetAmount).HasPrecision(18, 0);
            entity.Property(x => x.MonthlyTargetAmount).HasPrecision(18, 0);
            entity.HasOne(x => x.Workspace)
                .WithMany(x => x.SavingsGoals)
                .HasForeignKey(x => x.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SavingsContribution>(entity =>
        {
            entity.Property(x => x.Amount).HasPrecision(18, 0);
            entity.Property(x => x.Note).HasMaxLength(200);
            entity.HasIndex(x => new { x.SavingsGoalId, x.ContributionDate });
            entity.HasOne(x => x.SavingsGoal)
                .WithMany(x => x.Contributions)
                .HasForeignKey(x => x.SavingsGoalId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.CreatedByUser)
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
