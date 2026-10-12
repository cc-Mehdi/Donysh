using System.Security.Cryptography;
using System.Text;

namespace DonyshMobile.Core;

public sealed record PairRequest(string Code);
public sealed record PairResult(string Token, string WorkspaceName, string CategoryName);
public sealed record SmsEnvelope(string Sender, string Body, long SentAtUnixMs)
{
    public string Key => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{Sender.Trim().ToUpperInvariant()}\n{SentAtUnixMs}\n{Body.Replace("\r", "").Trim()}")));
}
public sealed record SmsBatch(List<SmsEnvelope> Messages);
public sealed record SmsResult(string Key, string Status);
public sealed record SmsBatchResult(List<SmsResult> Results);
public sealed record MobileConnection(string Server, string Token, string Destination);

public enum SyncRunResult
{
    NothingToSend,
    Sent,
    RetryScheduled,
    ConnectionRequired
}
