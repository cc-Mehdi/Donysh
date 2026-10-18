using System.Security.Cryptography;
using System.Text;

namespace Donysh.Sms;

public sealed record ParsedTransaction(long AmountRial, string Kind, string TemplateName);
public sealed record SmsEnvelope(string Sender, string Body, long SentAtUnixMs, ParsedTransaction? Parsed = null, string? Title = null)
{
    public string Key => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{Sender.Trim().ToUpperInvariant()}\n{SentAtUnixMs}\n{Body.Replace("\r", "").Trim()}")));
}
public sealed record SmsBatch(List<SmsEnvelope> Messages);
public sealed record SmsResult(string Key, string Status);
public sealed record SmsBatchResult(List<SmsResult> Results);
public sealed record PairRequest(string Code);
public sealed record PairResult(string Token, string WorkspaceName, string CategoryName);
public sealed record MobileLoginRequest(string Email, string Password, string InstallationId, string DeviceName);
public sealed record MobileWorkspace(Guid Id, string Name, string Type);
public sealed record MobileSessionResult(
    string Token,
    string DisplayName,
    Guid WorkspaceId,
    string WorkspaceName,
    string CategoryName,
    List<MobileWorkspace> Workspaces);
public sealed record MobileWorkspaceList(Guid WorkspaceId, string WorkspaceName, string CategoryName, List<MobileWorkspace> Workspaces);
public sealed record MobileDestinationRequest(Guid WorkspaceId);
public sealed record MobileDestinationResult(Guid WorkspaceId, string WorkspaceName, string CategoryName);

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
        && !sms.Sender.Any(char.IsControl)
        && !string.IsNullOrWhiteSpace(sms.Body) && sms.Body.Length <= 2048
        && sms.SentAtUnixMs >= 1577836800000L && sms.SentAtUnixMs <= now.AddMinutes(10).ToUnixTimeMilliseconds()
        && (sms.Title is null || (!string.IsNullOrWhiteSpace(sms.Title) && sms.Title.Length <= 160
            && !sms.Title.Any(char.IsControl)))
        && (sms.Parsed is null || ValidParsed(sms.Parsed));

    private static bool ValidParsed(ParsedTransaction parsed) =>
        parsed.AmountRial is >= 10 and <= 9_999_999_999_990
        && parsed.AmountRial % 10 == 0
        && parsed.Kind == "withdrawal"
        && !string.IsNullOrWhiteSpace(parsed.TemplateName)
        && parsed.TemplateName.Length <= 80
        && !parsed.TemplateName.Any(char.IsControl);
}
