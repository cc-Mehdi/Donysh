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
    }
}
