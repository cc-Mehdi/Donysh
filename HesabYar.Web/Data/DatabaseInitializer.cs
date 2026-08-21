using Microsoft.EntityFrameworkCore;

namespace HesabYar.Web.Data;

public sealed class DatabaseInitializer(ApplicationDbContext db, ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 12;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await db.Database.EnsureCreatedAsync(cancellationToken);
                await ApplyCompatibilityUpdatesAsync(cancellationToken);

                logger.LogInformation("Database is ready.");
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                logger.LogWarning(ex, "Database initialization attempt {Attempt} failed.", attempt);
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
        }

        await db.Database.EnsureCreatedAsync(cancellationToken);
        await ApplyCompatibilityUpdatesAsync(cancellationToken);
    }

    private async Task ApplyCompatibilityUpdatesAsync(CancellationToken cancellationToken)
    {
        // The project currently uses EnsureCreated instead of EF migrations.
        // These idempotent ALTER statements upgrade existing databases without
        // deleting any saved data.
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "SavingsGoals"
            ADD COLUMN IF NOT EXISTS "Description" character varying(500);
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "SavingsGoals"
            ADD COLUMN IF NOT EXISTS "IsCancelled" boolean NOT NULL DEFAULT FALSE;
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "BudgetTransfers" (
                "Id" uuid NOT NULL,
                "WorkspaceId" uuid NOT NULL,
                "SourceBudgetId" uuid NOT NULL,
                "DestinationBudgetId" uuid NOT NULL,
                "CreatedByUserId" text NOT NULL,
                "Amount" numeric(18,0) NOT NULL,
                "TransferDate" date NOT NULL,
                "Note" character varying(200),
                "CreatedAtUtc" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_BudgetTransfers" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_BudgetTransfers_Workspaces_WorkspaceId" FOREIGN KEY ("WorkspaceId") REFERENCES "Workspaces" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_BudgetTransfers_Budgets_SourceBudgetId" FOREIGN KEY ("SourceBudgetId") REFERENCES "Budgets" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_BudgetTransfers_Budgets_DestinationBudgetId" FOREIGN KEY ("DestinationBudgetId") REFERENCES "Budgets" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_BudgetTransfers_AspNetUsers_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT
            );
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_BudgetTransfers_WorkspaceId_TransferDate"
            ON "BudgetTransfers" ("WorkspaceId", "TransferDate");

            CREATE INDEX IF NOT EXISTS "IX_BudgetTransfers_SourceBudgetId"
            ON "BudgetTransfers" ("SourceBudgetId");

            CREATE INDEX IF NOT EXISTS "IX_BudgetTransfers_DestinationBudgetId"
            ON "BudgetTransfers" ("DestinationBudgetId");
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "InstallmentPlans" (
                "Id" uuid NOT NULL,
                "WorkspaceId" uuid NOT NULL,
                "Title" character varying(120) NOT NULL,
                "Notes" character varying(500),
                "TotalAmount" numeric(18,0) NOT NULL,
                "InstallmentAmount" numeric(18,0) NOT NULL,
                "InstallmentCount" integer NOT NULL,
                "PaidInstallments" integer NOT NULL,
                "FirstDueDate" date NOT NULL,
                "IsCompleted" boolean NOT NULL DEFAULT FALSE,
                "CreatedAtUtc" timestamp with time zone NOT NULL,
                "UpdatedAtUtc" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_InstallmentPlans" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_InstallmentPlans_Workspaces_WorkspaceId" FOREIGN KEY ("WorkspaceId") REFERENCES "Workspaces" ("Id") ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS "IX_InstallmentPlans_WorkspaceId_IsCompleted_FirstDueDate"
            ON "InstallmentPlans" ("WorkspaceId", "IsCompleted", "FirstDueDate");
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "AiImportReceipts" (
                "Id" uuid NOT NULL,
                "WorkspaceId" uuid NOT NULL,
                "AppliedByUserId" text NOT NULL,
                "ChangeCount" integer NOT NULL,
                "AppliedAtUtc" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_AiImportReceipts" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_AiImportReceipts_Workspaces_WorkspaceId" FOREIGN KEY ("WorkspaceId") REFERENCES "Workspaces" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_AiImportReceipts_AspNetUsers_AppliedByUserId" FOREIGN KEY ("AppliedByUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT
            );

            CREATE INDEX IF NOT EXISTS "IX_AiImportReceipts_WorkspaceId_AppliedAtUtc"
            ON "AiImportReceipts" ("WorkspaceId", "AppliedAtUtc");
            """,
            cancellationToken);
    }
}
