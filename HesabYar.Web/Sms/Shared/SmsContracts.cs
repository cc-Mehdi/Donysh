using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Donysh.Sms;

public sealed record ParsedTransaction(long AmountRial, string Kind, string TemplateName);
public sealed record SmsEnvelope(string Sender, string Body, long SentAtUnixMs, ParsedTransaction? Parsed = null,
    string? Title = null, string? ClientKey = null)
{
    [JsonIgnore]
    public string Key => ClientKey is { Length: 64 } && ClientKey.All(Uri.IsHexDigit)
        ? ClientKey.ToUpperInvariant()
        : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{Sender.Trim().ToUpperInvariant()}\n{SentAtUnixMs}\n{Body.Replace("\r", "").Trim()}")));
}
public sealed record SmsBatch(List<SmsEnvelope> Messages);
public sealed record SmsResult(string Key, string Status);
public sealed record SmsBatchResult(List<SmsResult> Results);
public sealed record PairRequest(string Code, string? InstallationId = null, string? PreviousToken = null);
public sealed record PairResult(string Token, string WorkspaceName, string CategoryName, string? UserId = null);
public sealed record MobileLoginRequest(string Email, string Password, string InstallationId, string DeviceName,
    string? PreviousToken = null);
public sealed record MobileWorkspace(Guid Id, string Name, string Type);
public sealed record MobileSessionResult(
    string Token,
    string DisplayName,
    Guid WorkspaceId,
    string WorkspaceName,
    string CategoryName,
    List<MobileWorkspace> Workspaces, string? UserId = null);
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
        && (sms.ClientKey is null || sms.ClientKey.Length == 64 && sms.ClientKey.All(Uri.IsHexDigit))
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

public static partial class SmsPrivacy
{
    [GeneratedRegex(@"(?im)(م\s*و\s*ج\s*و\s*د\s*[یي]|م\s*ا\s*ن\s*د\s*ه|balance)[^\r\n]*", RegexOptions.CultureInvariant, 100)]
    private static partial Regex BalanceLine();

    public static string RemoveBalance(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "متن حساس پیامک روی گوشی نگهداری شد.";
        var clean = body.Replace("\u200e", "").Replace("\u200f", "");
        try { clean = BalanceLine().Replace(clean, string.Empty); }
        catch (RegexMatchTimeoutException) { return "متن حساس پیامک روی گوشی نگهداری شد."; }
        clean = clean.Trim();
        return string.IsNullOrWhiteSpace(clean) ? "متن حساس پیامک روی گوشی نگهداری شد." : clean;
    }
}
