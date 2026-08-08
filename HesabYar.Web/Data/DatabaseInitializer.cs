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
    }
}
