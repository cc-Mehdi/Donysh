using Donysh.Auth.Data;
using Donysh.Auth.Domain;
using Donysh.IdentityMigration;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

internal static class PostgresChecks
{
    public static async Task RunAsync(string adminConnection)
    {
        var databaseName = "donysh_auth_test_" + Guid.NewGuid().ToString("N");
        var legacyDatabaseName = "donysh_legacy_test_" + Guid.NewGuid().ToString("N");
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
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{legacyDatabaseName}\"", admin))
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
                // Identity and OpenIddict use quoted, case-sensitive table names. Passing an
                // unquoted name to to_regclass folds it to lowercase and reports a false miss.
                await using var command = new NpgsqlCommand("""
                    SELECT EXISTS (
                        SELECT 1
                        FROM information_schema.tables
                        WHERE table_schema = current_schema()
                          AND table_name = @name)
                    """, verify);
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

            var targetConnection = connection.ConnectionString;
            var registrationServices = new ServiceCollection();
            registrationServices.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
            registrationServices.AddDataProtection();
            registrationServices.AddDbContext<AuthDbContext>(builder => builder
                .UseNpgsql(targetConnection)
                .UseOpenIddict());
            registrationServices.AddIdentityCore<AuthUser>(identity =>
                {
                    identity.User.RequireUniqueEmail = true;
                    identity.SignIn.RequireConfirmedEmail = true;
                    identity.Password.RequiredLength = 8;
                    identity.Password.RequireDigit = true;
                    identity.Password.RequireLowercase = true;
                    identity.Password.RequireUppercase = false;
                    identity.Password.RequireNonAlphanumeric = false;
                })
                .AddRoles<IdentityRole>()
                .AddEntityFrameworkStores<AuthDbContext>()
                .AddDefaultTokenProviders();

            await using (var registrationProvider = registrationServices.BuildServiceProvider())
            await using (var registrationScope = registrationProvider.CreateAsyncScope())
            {
                var userManager = registrationScope.ServiceProvider.GetRequiredService<UserManager<AuthUser>>();
                var registered = new AuthUser
                {
                    UserName = "new-user@example.com",
                    Email = "new-user@example.com",
                    DisplayName = "کاربر تازه",
                    EmailConfirmed = false
                };
                var created = await userManager.CreateAsync(registered, "securepass1");
                var confirmationToken = created.Succeeded
                    ? await userManager.GenerateEmailConfirmationTokenAsync(registered)
                    : string.Empty;
                var confirmed = created.Succeeded
                    ? await userManager.ConfirmEmailAsync(registered, confirmationToken)
                    : IdentityResult.Failed();
                var persisted = await userManager.FindByEmailAsync("new-user@example.com");
                if (!created.Succeeded || !confirmed.Succeeded || persisted?.EmailConfirmed != true
                    || !await userManager.CheckPasswordAsync(persisted, "securepass1"))
                    throw new Exception("FAIL PostgreSQL registration and email confirmation workflow");
            }
            Console.WriteLine("PASS PostgreSQL registration requires and persists email confirmation");

            connection.Database = legacyDatabaseName;
            await CreateLegacyUserAsync(connection.ConnectionString, cancellationToken: default);

            var preview = await LegacyIdentityMigrator.RunAsync(
                connection.ConnectionString, targetConnection, apply: false);
            if (preview.Added != 1 || preview.Unchanged != 0)
                throw new Exception("FAIL PostgreSQL identity import dry run");

            var applied = await LegacyIdentityMigrator.RunAsync(
                connection.ConnectionString, targetConnection, apply: true);
            var repeated = await LegacyIdentityMigrator.RunAsync(
                connection.ConnectionString, targetConnection, apply: true);
            if (applied.Added != 1 || repeated.Unchanged != 1)
                throw new Exception("FAIL PostgreSQL identity import idempotency");

            connection.Database = databaseName;
            await using var importedConnection = new NpgsqlConnection(connection.ConnectionString);
            await importedConnection.OpenAsync();
            await using var importedCommand = new NpgsqlCommand(
                "SELECT \"PasswordHash\" FROM \"AspNetUsers\" WHERE \"Id\" = 'legacy-user-id'", importedConnection);
            if (!string.Equals(await importedCommand.ExecuteScalarAsync() as string, "legacy-password-hash", StringComparison.Ordinal))
                throw new Exception("FAIL PostgreSQL identity import preserves password hashes");

            Console.WriteLine("PASS PostgreSQL identity import is dry-run safe and idempotent");
            Console.WriteLine("PASS PostgreSQL identity import preserves user IDs and password hashes");
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            connection.Database = "postgres";
            await using var cleanup = new NpgsqlConnection(connection.ConnectionString);
            await cleanup.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", cleanup);
            await drop.ExecuteNonQueryAsync();
            await using var dropLegacy = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{legacyDatabaseName}\" WITH (FORCE)", cleanup);
            await dropLegacy.ExecuteNonQueryAsync();
        }
    }

    private static async Task CreateLegacyUserAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            CREATE TABLE "AspNetUsers" (
                "Id" text PRIMARY KEY, "UserName" text NULL, "NormalizedUserName" text NULL,
                "Email" text NULL, "NormalizedEmail" text NULL, "EmailConfirmed" boolean NOT NULL,
                "PasswordHash" text NULL, "SecurityStamp" text NULL, "ConcurrencyStamp" text NULL,
                "PhoneNumber" text NULL, "PhoneNumberConfirmed" boolean NOT NULL,
                "TwoFactorEnabled" boolean NOT NULL, "LockoutEnd" timestamp with time zone NULL,
                "LockoutEnabled" boolean NOT NULL, "AccessFailedCount" integer NOT NULL,
                "DisplayName" text NOT NULL, "CreatedAtUtc" timestamp with time zone NOT NULL);
            INSERT INTO "AspNetUsers" VALUES (
                'legacy-user-id', 'legacy@example.com', 'LEGACY@EXAMPLE.COM',
                'legacy@example.com', 'LEGACY@EXAMPLE.COM', true,
                'legacy-password-hash', 'legacy-security-stamp', 'legacy-concurrency-stamp',
                NULL, false, false, NULL, true, 0, 'کاربر قدیمی', CURRENT_TIMESTAMP);
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
