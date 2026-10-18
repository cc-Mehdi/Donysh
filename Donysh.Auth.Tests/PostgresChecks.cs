using Donysh.Auth.Data;
using Donysh.Auth.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

internal static class PostgresChecks
{
    public static async Task RunAsync(string adminConnection)
    {
        var databaseName = "donysh_auth_test_" + Guid.NewGuid().ToString("N");
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
            var options = new DbContextOptionsBuilder<AuthDbContext>()
                .UseNpgsql(connection.ConnectionString)
                .UseOpenIddict()
                .Options;
            await using var db = new AuthDbContext(options);
            await db.Database.MigrateAsync();

            var expectedTables = new[] { "AspNetUsers", "OpenIddictApplications", "OpenIddictTokens", "__EFMigrationsHistory" };
            await using var verify = new NpgsqlConnection(connection.ConnectionString);
            await verify.OpenAsync();
            foreach (var table in expectedTables)
            {
                await using var command = new NpgsqlCommand("SELECT to_regclass(@name) IS NOT NULL", verify);
                command.Parameters.AddWithValue("name", table);
                if (!Convert.ToBoolean(await command.ExecuteScalarAsync()))
                    throw new Exception($"FAIL PostgreSQL migration: missing table {table}");
            }

            var legacyHasher = new PasswordHasher<IdentityUser>();
            var hash = legacyHasher.HashPassword(new IdentityUser(), "Migration-Test-123");
            var imported = new AuthUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "migration@example.com",
                NormalizedUserName = "MIGRATION@EXAMPLE.COM",
                Email = "migration@example.com",
                NormalizedEmail = "MIGRATION@EXAMPLE.COM",
                EmailConfirmed = true,
                PasswordHash = hash,
                DisplayName = "کاربر انتقالی"
            };
            db.Users.Add(imported);
            await db.SaveChangesAsync();
            var saved = await db.Users.AsNoTracking().SingleAsync(x => x.Id == imported.Id);
            if (new PasswordHasher<AuthUser>().VerifyHashedPassword(saved, saved.PasswordHash!, "Migration-Test-123")
                == PasswordVerificationResult.Failed)
                throw new Exception("FAIL PostgreSQL migration: imported Identity hash is invalid");

            Console.WriteLine("PASS PostgreSQL migration creates isolated Identity and OpenIddict schema");
            Console.WriteLine("PASS PostgreSQL accepts an imported compatible Identity hash");
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
