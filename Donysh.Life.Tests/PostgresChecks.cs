using Donysh.Life.Data;
using Donysh.Life.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

internal static class PostgresChecks
{
    public static async Task RunAsync(string adminConnection)
    {
        var databaseName = "donysh_life_test_" + Guid.NewGuid().ToString("N");
        var connection = new NpgsqlConnectionStringBuilder(adminConnection)
        {
            Database = "postgres",
            Pooling = false,
            Timeout = 5,
            CommandTimeout = 15
        };

        await using var admin = new NpgsqlConnection(connection.ConnectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin))
            await create.ExecuteNonQueryAsync();

        try
        {
            connection.Database = databaseName;
            var options = new DbContextOptionsBuilder<LifeDbContext>().UseNpgsql(connection.ConnectionString).Options;
            await using var db = new LifeDbContext(options);
            await db.Database.MigrateAsync();

            var profile = new LifeProfile
            {
                Id = Guid.NewGuid(),
                UserSubject = "central-user-id",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            db.Profiles.Add(profile);
            await db.SaveChangesAsync();
            var stored = await db.Profiles.AsNoTracking().SingleAsync();
            if (stored.UserSubject != profile.UserSubject
                || stored.WeekStartsOn != DayOfWeek.Saturday
                || stored.TimeZoneId != "Asia/Tehran")
                throw new Exception("FAIL PostgreSQL Life profile defaults were not preserved");

            await using var verify = new NpgsqlConnection(connection.ConnectionString);
            await verify.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT EXISTS (
                    SELECT 1 FROM information_schema.tables
                    WHERE table_schema = current_schema() AND table_name = 'LifeProfiles')
                """, verify);
            if (!Convert.ToBoolean(await command.ExecuteScalarAsync()))
                throw new Exception("FAIL PostgreSQL Life migration did not create its isolated schema");

            Console.WriteLine("PASS PostgreSQL Life migration creates an isolated schema");
            Console.WriteLine("PASS PostgreSQL Life profile keeps Saturday and Tehran defaults");
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            connection.Database = "postgres";
            await using var cleanup = new NpgsqlConnection(connection.ConnectionString);
            await cleanup.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", cleanup);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
