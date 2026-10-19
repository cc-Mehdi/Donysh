using Microsoft.EntityFrameworkCore;

namespace Donysh.Life.Data;

public sealed class LifeDatabaseMigrator(LifeDbContext db, ILogger<LifeDatabaseMigrator> logger)
{
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await db.Database.MigrateAsync(cancellationToken);
        logger.LogInformation("Donysh Life database is ready.");
    }
}
