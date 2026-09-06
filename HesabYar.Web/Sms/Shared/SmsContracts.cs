using System.Security.Cryptography;
using System.Text;

namespace Donysh.Sms;

public sealed record SmsEnvelope(string Sender, string Body, long SentAtUnixMs)
{
    public string Key => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{Sender.Trim().ToUpperInvariant()}\n{SentAtUnixMs}\n{Body.Replace("\r", "").Trim()}")));
}
public sealed record SmsBatch(List<SmsEnvelope> Messages);
public sealed record SmsResult(string Key, string Status);
public sealed record SmsBatchResult(List<SmsResult> Results);
public sealed record PairRequest(string Code);
public sealed record PairResult(string Token, string WorkspaceName, string CategoryName);

public static class SyncPolicy
{
    public const int BatchSize = 5;
    public const int MaxQueueSize = 1000;
    public static TimeSpan Delay(int failures, TimeSpan? retryAfter = null)
    {
        var seconds = Math.Min(1800, 30 * Math.Pow(2, Math.Clamp(failures, 0, 10)));
        return TimeSpan.FromSeconds(Math.Max(seconds, Math.Clamp(retryAfter?.TotalSeconds ?? 0, 0, 86400)));
    }
    public static bool Valid(SmsEnvelope? sms, DateTimeOffset now) => sms is not null
        && !string.IsNullOrWhiteSpace(sms.Sender) && sms.Sender.Length <= 64
        && !string.IsNullOrWhiteSpace(sms.Body) && sms.Body.Length <= 2048
        && sms.SentAtUnixMs >= 1577836800000L && sms.SentAtUnixMs <= now.AddMinutes(10).ToUnixTimeMilliseconds();
}
