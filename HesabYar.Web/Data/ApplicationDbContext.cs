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
    public DbSet<BudgetTransfer> BudgetTransfers => Set<BudgetTransfer>();
    public DbSet<SavingsGoal> SavingsGoals => Set<SavingsGoal>();
    public DbSet<SavingsContribution> SavingsContributions => Set<SavingsContribution>();
    public DbSet<RecurringObligation> RecurringObligations => Set<RecurringObligation>();
    public DbSet<RecurringObligationPayment> RecurringObligationPayments => Set<RecurringObligationPayment>();
    public DbSet<AiImportReceipt> AiImportReceipts => Set<AiImportReceipt>();

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
            entity.Property(x => x.MonthlySpendingLimit).HasPrecision(18, 0);
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


        builder.Entity<BudgetTransfer>(entity =>
        {
            entity.Property(x => x.Amount).HasPrecision(18, 0);
            entity.Property(x => x.Note).HasMaxLength(200);
            entity.HasIndex(x => new { x.WorkspaceId, x.TransferDate });
            entity.HasIndex(x => x.SourceBudgetId);
            entity.HasIndex(x => x.DestinationBudgetId);
            entity.HasOne(x => x.Workspace)
                .WithMany(x => x.BudgetTransfers)
                .HasForeignKey(x => x.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.SourceBudget)
                .WithMany(x => x.OutgoingTransfers)
                .HasForeignKey(x => x.SourceBudgetId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.DestinationBudget)
                .WithMany(x => x.IncomingTransfers)
                .HasForeignKey(x => x.DestinationBudgetId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.CreatedByUser)
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<SavingsGoal>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.Property(x => x.TargetAmount).HasPrecision(18, 0);
            entity.Property(x => x.MonthlyTargetAmount).HasPrecision(18, 0);
            entity.Property(x => x.Priority).HasDefaultValue(3);
            entity.HasOne(x => x.Workspace)
                .WithMany(x => x.SavingsGoals)
                .HasForeignKey(x => x.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });



        builder.Entity<RecurringObligation>(entity =>
        {
            entity.Property(x => x.Title).HasMaxLength(140);
            entity.Property(x => x.Note).HasMaxLength(500);
            entity.Property(x => x.Amount).HasPrecision(18, 0);
            entity.HasIndex(x => new { x.WorkspaceId, x.IsActive });
            entity.HasOne(x => x.Workspace)
                .WithMany(x => x.RecurringObligations)
                .HasForeignKey(x => x.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Category)
                .WithMany()
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.CreatedByUser)
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<RecurringObligationPayment>(entity =>
        {
            entity.Property(x => x.Amount).HasPrecision(18, 0);
            entity.Property(x => x.Note).HasMaxLength(200);
            entity.HasIndex(x => new { x.RecurringObligationId, x.PeriodYear, x.PeriodMonth }).IsUnique();
            entity.HasIndex(x => x.ExpenseId).IsUnique();
            entity.HasOne(x => x.RecurringObligation)
                .WithMany(x => x.Payments)
                .HasForeignKey(x => x.RecurringObligationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Expense)
                .WithMany()
                .HasForeignKey(x => x.ExpenseId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.PaidByUser)
                .WithMany()
                .HasForeignKey(x => x.PaidByUserId)
                .OnDelete(DeleteBehavior.Restrict);
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

        builder.Entity<AiImportReceipt>(entity =>
        {
            entity.HasIndex(x => new { x.WorkspaceId, x.AppliedAtUtc });
            entity.HasOne(x => x.Workspace)
                .WithMany()
                .HasForeignKey(x => x.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.AppliedByUser)
                .WithMany()
                .HasForeignKey(x => x.AppliedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
