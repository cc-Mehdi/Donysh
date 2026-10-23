using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace DonyshMobile.Core;

public sealed record PairRequest(string Code, string? InstallationId = null, string? PreviousToken = null);
public sealed record PairResult(string Token, string WorkspaceName, string CategoryName, string? UserId = null);
public sealed record MobileLoginRequest(string Email, string Password, string InstallationId, string DeviceName,
    string? PreviousToken = null);
public sealed record MobileWorkspace(Guid Id, string Name, string Type);
public sealed record MobileSessionResult(string Token, string DisplayName, Guid WorkspaceId,
    string WorkspaceName, string CategoryName, List<MobileWorkspace> Workspaces, string? UserId = null);
public sealed record MobileWorkspaceList(Guid WorkspaceId, string WorkspaceName,
    string CategoryName, List<MobileWorkspace> Workspaces);
public sealed record MobileDestinationRequest(Guid WorkspaceId);
public sealed record MobileDestinationResult(Guid WorkspaceId, string WorkspaceName, string CategoryName);
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
public sealed record MobileConnection(string Server, string Token, string Destination,
    Guid WorkspaceId = default, string? DisplayName = null, string? UserId = null);

public enum SyncRunResult
{
    NothingToSend,
    Sent,
    RetryScheduled,
    ConnectionRequired
}
