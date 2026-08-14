using System.ComponentModel.DataAnnotations;

namespace HesabYar.Web.Domain;

public sealed class ExpenseCategory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }

    [MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(16)]
    public string Icon { get; set; } = "📦";

    [MaxLength(20)]
    public string Color { get; set; } = "slate";

    public bool IsArchived { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Workspace Workspace { get; set; } = null!;
    public ICollection<Expense> Expenses { get; set; } = [];
    public ICollection<Budget> Budgets { get; set; } = [];
}

public sealed class Expense
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid CategoryId { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Reason { get; set; } = string.Empty;

    public decimal Amount { get; set; }
    public DateOnly ExpenseDate { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public Workspace Workspace { get; set; } = null!;
    public ExpenseCategory Category { get; set; } = null!;
    public ApplicationUser CreatedByUser { get; set; } = null!;
}

public sealed class Budget
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid? CategoryId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Amount { get; set; }
    public int WarningPercent { get; set; } = 80;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Workspace Workspace { get; set; } = null!;
    public ExpenseCategory? Category { get; set; }
    public ICollection<BudgetTransfer> OutgoingTransfers { get; set; } = [];
    public ICollection<BudgetTransfer> IncomingTransfers { get; set; } = [];
}

public sealed class BudgetTransfer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid SourceBudgetId { get; set; }
    public Guid DestinationBudgetId { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateOnly TransferDate { get; set; }

    [MaxLength(200)]
    public string? Note { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Workspace Workspace { get; set; } = null!;
    public Budget SourceBudget { get; set; } = null!;
    public Budget DestinationBudget { get; set; } = null!;
    public ApplicationUser CreatedByUser { get; set; } = null!;
}

public sealed class SavingsGoal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }

    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public decimal TargetAmount { get; set; }
    public decimal MonthlyTargetAmount { get; set; }
    public DateOnly? TargetDate { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsCancelled { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Workspace Workspace { get; set; } = null!;
    public ICollection<SavingsContribution> Contributions { get; set; } = [];
}

public sealed class SavingsContribution
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SavingsGoalId { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateOnly ContributionDate { get; set; }

    [MaxLength(200)]
    public string? Note { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public SavingsGoal SavingsGoal { get; set; } = null!;
    public ApplicationUser CreatedByUser { get; set; } = null!;
}
