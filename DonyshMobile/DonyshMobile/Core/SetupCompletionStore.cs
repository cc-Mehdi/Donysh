using System.Security.Cryptography;
using System.Text;

namespace DonyshMobile.Core;

public static class SetupCompletionStore
{
    private const string CompletedAccountPrefix = "setup-wizard-completed-account-v2-";
    private const string LegacyFinishedKey = "setup-wizard-finished-v1";
    private const string LegacyMigrationDoneKey = "setup-wizard-migration-done-v2";

    public static bool IsFinishedFor(MobileConnection connection)
    {
        return Preferences.Default.Get(CompletedAccountPrefix + AccountKey(connection), false);
    }

    public static void MigrateLegacyFor(MobileConnection? connection)
    {
        // Consume the old installation-wide flag at startup, even without a saved
        // connection, so a later login cannot inherit another account's completion.
        if (Preferences.Default.Get(LegacyMigrationDoneKey, false)) return;
        if (connection is not null && Preferences.Default.Get(LegacyFinishedKey, false))
            MarkFinished(connection);
        Preferences.Default.Set(LegacyMigrationDoneKey, true);
    }

    public static void MarkFinished(MobileConnection connection) =>
        Preferences.Default.Set(CompletedAccountPrefix + AccountKey(connection), true);

    private static string AccountKey(MobileConnection connection)
    {
        var identity = !string.IsNullOrWhiteSpace(connection.UserId)
            ? connection.UserId.Trim()
            : connection.WorkspaceId != Guid.Empty
                ? connection.WorkspaceId.ToString("N")
                : connection.Token;
        var value = $"{connection.Server.TrimEnd('/').ToLowerInvariant()}\n{identity}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
