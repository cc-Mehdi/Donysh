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
            ALTER TABLE "SavingsGoals"
            ADD COLUMN IF NOT EXISTS "Priority" integer NOT NULL DEFAULT 3;

            ALTER TABLE "Workspaces"
            ADD COLUMN IF NOT EXISTS "MonthlySpendingLimit" numeric(18,0);
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
            ALTER TABLE "Budgets"
            ADD COLUMN IF NOT EXISTS "CarryOverOverspend" boolean NOT NULL DEFAULT TRUE;
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "RecurringObligations" (
                "Id" uuid NOT NULL,
                "WorkspaceId" uuid NOT NULL,
                "CategoryId" uuid NOT NULL,
                "CreatedByUserId" text NOT NULL,
                "Title" character varying(140) NOT NULL,
                "Type" integer NOT NULL,
                "Amount" numeric(18,0) NOT NULL,
                "StartYear" integer NOT NULL,
                "StartMonth" integer NOT NULL,
                "DurationMonths" integer,
                "DueDay" integer NOT NULL,
                "ReminderDaysBefore" integer NOT NULL,
                "IsActive" boolean NOT NULL,
                "Note" character varying(500),
                "CreatedAtUtc" timestamp with time zone NOT NULL,
                "UpdatedAtUtc" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_RecurringObligations" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_RecurringObligations_Workspaces_WorkspaceId" FOREIGN KEY ("WorkspaceId") REFERENCES "Workspaces" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_RecurringObligations_ExpenseCategories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES "ExpenseCategories" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_RecurringObligations_AspNetUsers_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT
            );
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "RecurringObligationPayments" (
                "Id" uuid NOT NULL,
                "RecurringObligationId" uuid NOT NULL,
                "ExpenseId" uuid NOT NULL,
                "PaidByUserId" text NOT NULL,
                "PeriodYear" integer NOT NULL,
                "PeriodMonth" integer NOT NULL,
                "Amount" numeric(18,0) NOT NULL,
                "PaidDate" date NOT NULL,
                "Note" character varying(200),
                "CreatedAtUtc" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_RecurringObligationPayments" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_RecurringObligationPayments_RecurringObligations_RecurringObligationId" FOREIGN KEY ("RecurringObligationId") REFERENCES "RecurringObligations" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_RecurringObligationPayments_Expenses_ExpenseId" FOREIGN KEY ("ExpenseId") REFERENCES "Expenses" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_RecurringObligationPayments_AspNetUsers_PaidByUserId" FOREIGN KEY ("PaidByUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT
            );
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_RecurringObligations_WorkspaceId_IsActive"
            ON "RecurringObligations" ("WorkspaceId", "IsActive");

            CREATE INDEX IF NOT EXISTS "IX_RecurringObligations_CategoryId"
            ON "RecurringObligations" ("CategoryId");

            CREATE INDEX IF NOT EXISTS "IX_RecurringObligations_CreatedByUserId"
            ON "RecurringObligations" ("CreatedByUserId");

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_RecurringObligationPayments_RecurringObligationId_PeriodYear_PeriodMonth"
            ON "RecurringObligationPayments" ("RecurringObligationId", "PeriodYear", "PeriodMonth");

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_RecurringObligationPayments_ExpenseId"
            ON "RecurringObligationPayments" ("ExpenseId");

            CREATE INDEX IF NOT EXISTS "IX_RecurringObligationPayments_PaidByUserId"
            ON "RecurringObligationPayments" ("PaidByUserId");
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
