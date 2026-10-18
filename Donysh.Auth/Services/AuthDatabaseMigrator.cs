using Donysh.Auth.Data;
using Microsoft.EntityFrameworkCore;

namespace Donysh.Auth.Services;

public sealed class AuthDatabaseMigrator(AuthDbContext db, ILogger<AuthDatabaseMigrator> logger)
{
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 12;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync(cancellationToken);
                logger.LogInformation("Auth database migrations are current.");
                return;
            }
            catch (Exception exception) when (attempt < maxAttempts)
            {
                logger.LogWarning("Auth database migration attempt {Attempt} failed; retrying.", attempt);
                logger.LogDebug(exception, "Auth database migration failure details.");
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
        }

        await db.Database.MigrateAsync(cancellationToken);
    }
}
