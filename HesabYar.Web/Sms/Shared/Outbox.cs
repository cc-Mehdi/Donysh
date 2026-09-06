using System.Text.Json;

namespace Donysh.Sms;

public sealed record QueuedSms(SmsEnvelope Sms, bool Held);
public sealed class OutboxState
{
    public List<QueuedSms> Items { get; set; } = [];
    public List<string> SentKeys { get; set; } = [];
    public DateTimeOffset NextAttempt { get; set; }
    public int Failures { get; set; }
    public int Overflow { get; set; }
    public long Delivered { get; set; }
    public string Status { get; set; } = "هنوز ارسالی انجام نشده است.";
}

public sealed class Outbox(string path)
{
    private static readonly object Gate = new();
    private OutboxState Read() => File.Exists(path)
        ? JsonSerializer.Deserialize<OutboxState>(File.ReadAllText(path)) ?? throw new IOException("Invalid outbox")
        : new();
    private void Save(OutboxState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        // Atomic replacement: never silently reset a corrupt queue or overwrite it with an empty one.
        using (var stream = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None)) {
            JsonSerializer.Serialize(stream, state); stream.Flush(true);
        }
        File.Move(path + ".tmp", path, true);
    }
    public OutboxState Snapshot() { lock (Gate) return Read(); }
    public bool Enqueue(SmsEnvelope sms, bool held = false)
    {
        lock (Gate) {
            if (!SyncPolicy.Valid(sms, DateTimeOffset.UtcNow)) return false;
            var s = Read();
            if (s.SentKeys.Contains(sms.Key) || s.Items.Any(x => x.Sms.Key == sms.Key)) return false;
            if (s.Items.Count >= SyncPolicy.MaxQueueSize) { s.Overflow++; s.Status = "صف پر است؛ پیامک جدید ذخیره نشد. در برنامه پیامک گوشی بررسی کنید."; Save(s); return false; }
            s.Items.Add(new(sms, held)); Save(s); return true;
        }
    }
    public List<SmsEnvelope> Take(DateTimeOffset now)
    {
        lock (Gate) {
            var s = Read();
            if (s.NextAttempt > now) return [];
            var batch = s.Items.Where(x => !x.Held).Take(SyncPolicy.BatchSize).Select(x => x.Sms).ToList();
            // JSON escapes non-ASCII characters; character count is not a wire-size limit.
            while (batch.Count > 0 && JsonSerializer.SerializeToUtf8Bytes(new SmsBatch(batch)).Length > 32000)
                batch.RemoveAt(batch.Count - 1);
            if (batch.Count > 0) { s.NextAttempt = now.Add(SyncPolicy.Delay(0)); Save(s); }
            return batch;
        }
    }
    public void Complete(IEnumerable<SmsResult> results, DateTimeOffset now)
    {
        lock (Gate) {
            var s = Read();
            foreach (var result in results) {
                var item = s.Items.SingleOrDefault(x => x.Sms.Key == result.Key);
                if (item is null) continue;
                if (result.Status is "created" or "duplicate") {
                    s.Items.Remove(item); s.SentKeys.Add(result.Key); s.Delivered++;
                } else if (result.Status == "rejected") {
                    s.Items[s.Items.IndexOf(item)] = item with { Held = true };
                }
            }
            s.SentKeys = s.SentKeys.TakeLast(5000).ToList();
            s.Failures = 0; s.NextAttempt = now.Add(SyncPolicy.Delay(0)).AddSeconds(Random.Shared.Next(0, 10));
            s.Status = "آخرین ارسال موفق بود؛ موارد مبهم فقط روی گوشی باقی می‌مانند."; Save(s);
        }
    }
    public void Fail(DateTimeOffset now, TimeSpan? retryAfter = null)
    {
        lock (Gate) {
            var s = Read(); s.Failures = Math.Min(10, s.Failures + 1);
            s.NextAttempt = now.Add(SyncPolicy.Delay(s.Failures, retryAfter)).AddSeconds(Random.Shared.Next(0, 10));
            s.Status = "ارسال کامل نشد؛ پیامک‌ها محفوظ‌اند و بعداً دوباره تلاش می‌شود."; Save(s);
        }
    }
    public void Discard(string key) { lock (Gate) { var s = Read(); s.Items.RemoveAll(x => x.Sms.Key == key); Save(s); } }
}
