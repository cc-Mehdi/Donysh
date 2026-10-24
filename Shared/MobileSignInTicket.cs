using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Donysh.Shared;

public sealed record MobileSignInClaims(string Subject, string Email, string DisplayName,
    string InstallationId, bool EmailVerified, bool LifeEnabled, long ExpiresAtUnixSeconds);

public static class MobileSignInTicket
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    public static string Create(string secret, string subject, string email, string displayName,
        string installationId, bool emailVerified, bool lifeEnabled = true, DateTimeOffset? now = null)
    {
        ValidateSecret(secret);
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(email)
            || installationId.Length != 64 || !installationId.All(Uri.IsHexDigit))
            throw new ArgumentException("Invalid mobile identity.");
        var claims = new MobileSignInClaims(subject, email, displayName, installationId.ToUpperInvariant(),
            emailVerified, lifeEnabled,
            (now ?? DateTimeOffset.UtcNow).Add(Lifetime).ToUnixTimeSeconds());
        var payload = JsonSerializer.SerializeToUtf8Bytes(claims);
        return $"{Encode(payload)}.{Encode(Sign(secret, payload))}";
    }

    public static bool TryValidate(string? ticket, string? secret, string installationId,
        out MobileSignInClaims? claims, DateTimeOffset? now = null)
    {
        claims = null;
        if (string.IsNullOrWhiteSpace(ticket) || ticket.Length > 2048
            || string.IsNullOrWhiteSpace(secret) || secret.Length < 32
            || installationId.Length != 64 || !installationId.All(Uri.IsHexDigit)) return false;
        var separator = ticket.IndexOf('.');
        if (separator <= 0 || ticket.IndexOf('.', separator + 1) >= 0) return false;
        try
        {
            var payload = Decode(ticket[..separator]);
            var signature = Decode(ticket[(separator + 1)..]);
            var expected = Sign(secret, payload);
            if (signature.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(signature, expected)) return false;
            var parsed = JsonSerializer.Deserialize<MobileSignInClaims>(payload);
            var current = (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.Subject) || parsed.Subject.Length > 450
                || string.IsNullOrWhiteSpace(parsed.Email) || parsed.Email.Length > 254
                || !string.Equals(parsed.InstallationId, installationId, StringComparison.OrdinalIgnoreCase)
                || parsed.ExpiresAtUnixSeconds < current - 30
                || parsed.ExpiresAtUnixSeconds > current + (long)Lifetime.TotalSeconds + 30) return false;
            claims = parsed;
            return true;
        }
        catch (Exception error) when (error is FormatException or JsonException or ArgumentException) { return false; }
    }

    private static byte[] Sign(string secret, byte[] payload)
    {
        var key = SHA256.HashData(Encoding.UTF8.GetBytes("donysh-mobile-sign-in-v1\n" + secret));
        return HMACSHA256.HashData(key, payload);
    }

    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Decode(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/')
        .PadRight((value.Length + 3) / 4 * 4, '='));
    private static void ValidateSecret(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
            throw new InvalidOperationException("Mobile sign-in secret must contain at least 32 characters.");
    }
}
