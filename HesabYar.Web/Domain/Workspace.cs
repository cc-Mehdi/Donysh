namespace HesabYar.Web.Domain;

public sealed class Workspace
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public WorkspaceType Type { get; set; }
    public string OwnerUserId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ApplicationUser OwnerUser { get; set; } = null!;
    public ICollection<WorkspaceMember> Members { get; set; } = [];
    public ICollection<ExpenseCategory> Categories { get; set; } = [];
    public ICollection<Expense> Expenses { get; set; } = [];
    public ICollection<Budget> Budgets { get; set; } = [];
    public ICollection<BudgetTransfer> BudgetTransfers { get; set; } = [];
    public ICollection<SavingsGoal> SavingsGoals { get; set; } = [];
    public ICollection<InstallmentPlan> InstallmentPlans { get; set; } = [];
}

public sealed class WorkspaceMember
{
    public Guid WorkspaceId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public WorkspaceRole Role { get; set; }
    public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;

    public Workspace Workspace { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;
}

public sealed class WorkspaceInvitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string InvitedByUserId { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Workspace Workspace { get; set; } = null!;
    public ApplicationUser InvitedByUser { get; set; } = null!;
}
