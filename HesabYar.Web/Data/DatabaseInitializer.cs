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
    }
}
