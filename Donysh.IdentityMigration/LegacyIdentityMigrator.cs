using System.Data;
using Npgsql;

namespace Donysh.IdentityMigration;

public static class LegacyIdentityMigrator
{
    private const string Columns = """
        "Id", "UserName", "NormalizedUserName", "Email", "NormalizedEmail", "EmailConfirmed",
        "PasswordHash", "SecurityStamp", "ConcurrencyStamp", "PhoneNumber", "PhoneNumberConfirmed",
        "TwoFactorEnabled", "LockoutEnd", "LockoutEnabled", "AccessFailedCount", "DisplayName", "CreatedAtUtc"
        """;

    public static async Task<MigrationResult> RunAsync(
        string sourceConnectionString,
        string targetConnectionString,
        bool apply,
        CancellationToken cancellationToken = default)
    {
        EnsureDifferentDatabases(sourceConnectionString, targetConnectionString);

        await using var source = new NpgsqlConnection(sourceConnectionString);
        await using var target = new NpgsqlConnection(targetConnectionString);
        await source.OpenAsync(cancellationToken);
        await target.OpenAsync(cancellationToken);

        var users = await ReadUsersAsync(source, cancellationToken);
        await using var transaction = await target.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var added = 0;
        var unchanged = 0;
        foreach (var user in users)
        {
            ValidateSourceUser(user);
            var existing = await FindByIdAsync(target, transaction, user.Id, cancellationToken);
            if (existing is not null)
            {
                if (!existing.Equals(user))
                    throw new InvalidOperationException("A target account has the same ID but different identity data.");
                unchanged++;
                continue;
            }

            if (await HasNormalizedIdentityCollisionAsync(target, transaction, user, cancellationToken))
                throw new InvalidOperationException("A target account conflicts with a source user name or email.");

            added++;
            if (apply)
                await InsertAsync(target, transaction, user, cancellationToken);
        }

        if (apply)
            await transaction.CommitAsync(cancellationToken);
        else
            await transaction.RollbackAsync(cancellationToken);

        return new MigrationResult(added, unchanged);
    }

    private static void ValidateSourceUser(LegacyUser user)
    {
        if (string.IsNullOrWhiteSpace(user.Id)
            || string.IsNullOrWhiteSpace(user.NormalizedUserName)
            || string.IsNullOrWhiteSpace(user.NormalizedEmail)
            || string.IsNullOrWhiteSpace(user.PasswordHash))
            throw new InvalidOperationException("A source account is missing required identity data.");

        if (!user.EmailConfirmed)
            throw new InvalidOperationException("A source account has no confirmed email and cannot sign in centrally yet.");
    }

    private static void EnsureDifferentDatabases(string source, string target)
    {
        var sourceBuilder = new NpgsqlConnectionStringBuilder(source);
        var targetBuilder = new NpgsqlConnectionStringBuilder(target);
        if (string.Equals(sourceBuilder.Host, targetBuilder.Host, StringComparison.OrdinalIgnoreCase)
            && sourceBuilder.Port == targetBuilder.Port
            && string.Equals(sourceBuilder.Database, targetBuilder.Database, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Source and target databases must be different.");
    }

    private static async Task<List<LegacyUser>> ReadUsersAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"SELECT {Columns} FROM \"AspNetUsers\" ORDER BY \"Id\"", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var users = new List<LegacyUser>();
        while (await reader.ReadAsync(cancellationToken))
            users.Add(Read(reader));
        return users;
    }

    private static async Task<LegacyUser?> FindByIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string id,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"SELECT {Columns} FROM \"AspNetUsers\" WHERE \"Id\" = @id", connection, transaction);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    private static async Task<bool> HasNormalizedIdentityCollisionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        LegacyUser user,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM "AspNetUsers"
                WHERE ("NormalizedEmail" IS NOT NULL AND "NormalizedEmail" = @email)
                   OR ("NormalizedUserName" IS NOT NULL AND "NormalizedUserName" = @userName))
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("email", (object?)user.NormalizedEmail ?? DBNull.Value);
        command.Parameters.AddWithValue("userName", (object?)user.NormalizedUserName ?? DBNull.Value);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task InsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        LegacyUser user,
        CancellationToken cancellationToken)
    {
        var parameterNames = string.Join(", ", Enumerable.Range(0, 17).Select(index => $"@p{index}"));
        await using var command = new NpgsqlCommand(
            $"INSERT INTO \"AspNetUsers\" ({Columns}) VALUES ({parameterNames})", connection, transaction);
        object?[] values =
        [
            user.Id, user.UserName, user.NormalizedUserName, user.Email, user.NormalizedEmail, user.EmailConfirmed,
            user.PasswordHash, user.SecurityStamp, user.ConcurrencyStamp, user.PhoneNumber, user.PhoneNumberConfirmed,
            user.TwoFactorEnabled, user.LockoutEnd, user.LockoutEnabled, user.AccessFailedCount, user.DisplayName,
            user.CreatedAtUtc
        ];
        for (var index = 0; index < values.Length; index++)
            command.Parameters.AddWithValue($"p{index}", values[index] ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static LegacyUser Read(NpgsqlDataReader reader) => new(
        reader.GetString(0),
        GetNullableString(reader, 1),
        GetNullableString(reader, 2),
        GetNullableString(reader, 3),
        GetNullableString(reader, 4),
        reader.GetBoolean(5),
        GetNullableString(reader, 6),
        GetNullableString(reader, 7),
        GetNullableString(reader, 8),
        GetNullableString(reader, 9),
        reader.GetBoolean(10),
        reader.GetBoolean(11),
        reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12),
        reader.GetBoolean(13),
        reader.GetInt32(14),
        reader.GetString(15),
        reader.GetDateTime(16));

    private static string? GetNullableString(NpgsqlDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : reader.GetString(index);
}

public sealed record LegacyUser(
    string Id,
    string? UserName,
    string? NormalizedUserName,
    string? Email,
    string? NormalizedEmail,
    bool EmailConfirmed,
    string? PasswordHash,
    string? SecurityStamp,
    string? ConcurrencyStamp,
    string? PhoneNumber,
    bool PhoneNumberConfirmed,
    bool TwoFactorEnabled,
    DateTimeOffset? LockoutEnd,
    bool LockoutEnabled,
    int AccessFailedCount,
    string DisplayName,
    DateTime CreatedAtUtc);

public sealed record MigrationResult(int Added, int Unchanged);
