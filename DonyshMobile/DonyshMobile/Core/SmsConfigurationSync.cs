namespace DonyshMobile.Core;

/// <summary>Backs up setup after local edits and restores it on a fresh installation.</summary>
public static class SmsConfigurationSync
{
    private const string PendingKey = "sms-configuration-backup-pending-v1";
    private const string OwnerKey = "sms-configuration-backup-owner-v1";
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static void MarkChanged() => Preferences.Default.Set(PendingKey,
        Math.Max(1, Preferences.Default.Get(PendingKey, 0) + 1));

    public static void Queue(SmsMonitorStore store)
    {
        _ = Task.Run(async () =>
        {
            try { await TrySyncAsync(store); }
            catch (Exception exception) { System.Diagnostics.Debug.WriteLine(exception); }
        });
    }

    public static async Task<bool> TrySyncAsync(SmsMonitorStore store, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        cancellationToken = timeout.Token;
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var connection = await MobileConnectionStore.LoadAsync();
            if (connection is null) return false;
            var owner = Preferences.Default.Get(OwnerKey, string.Empty);
            var local = await store.ExportConfigurationAsync(cancellationToken);
            var pending = Preferences.Default.Get(PendingKey, 0);
            if (owner.Length > 0 && !string.Equals(owner, connection.UserId, StringComparison.Ordinal))
            {
                if (local.Senders.Count > 0 || local.Templates.Count > 0 || pending > 0) return false;
                Preferences.Default.Set(OwnerKey, connection.UserId ?? string.Empty);
            }
            var api = new MobileApiClient();
            var remote = await api.GetConfigurationAsync(connection, cancellationToken);
            if (pending > 0 || (remote.Revision == 0 && local.Senders.Count > 0))
            {
                await api.PutConfigurationAsync(connection, new SmsConfigurationBackup(remote.Revision, local), cancellationToken);
                if (!string.IsNullOrWhiteSpace(connection.UserId)) Preferences.Default.Set(OwnerKey, connection.UserId);
                if (Preferences.Default.Get(PendingKey, 0) == pending) Preferences.Default.Set(PendingKey, 0);
                return false;
            }
            if (local.Senders.Count == 0 && local.Templates.Count == 0 && remote.Snapshot.Senders.Count > 0)
            {
                var restored = await store.RestoreConfigurationIfEmptyAsync(remote.Snapshot, cancellationToken);
                if (restored && !string.IsNullOrWhiteSpace(connection.UserId)) Preferences.Default.Set(OwnerKey, connection.UserId);
                return restored;
            }
            return false;
        }
        finally { Gate.Release(); }
    }
}
