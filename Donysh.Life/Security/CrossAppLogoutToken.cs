using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Donysh.Life.Security;

public static class CrossAppLogoutToken
{
    private const int MinimumSecretLength = 32;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    public static string Create(string secret, string audience, DateTimeOffset? now = null)
    {
        EnsureInputs(secret, audience);
        var expiresAt = (now ?? DateTimeOffset.UtcNow).Add(Lifetime).ToUnixTimeSeconds();
        var nonce = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16));
        var payload = Encoding.UTF8.GetBytes($"{audience}\n{expiresAt}\n{nonce}");
        var signature = Sign(secret, payload);
        return $"{WebEncoders.Base64UrlEncode(payload)}.{WebEncoders.Base64UrlEncode(signature)}";
    }

    public static bool TryValidate(string? token, string secret, string audience, DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 512
            || string.IsNullOrWhiteSpace(secret) || secret.Length < MinimumSecretLength
            || string.IsNullOrWhiteSpace(audience)) return false;

        var separator = token.IndexOf('.');
        if (separator <= 0 || separator == token.Length - 1 || token.IndexOf('.', separator + 1) >= 0) return false;

        try
        {
            var payload = WebEncoders.Base64UrlDecode(token[..separator]);
            var suppliedSignature = WebEncoders.Base64UrlDecode(token[(separator + 1)..]);
            var expectedSignature = Sign(secret, payload);
            if (suppliedSignature.Length != expectedSignature.Length
                || !CryptographicOperations.FixedTimeEquals(suppliedSignature, expectedSignature)) return false;

            var parts = Encoding.UTF8.GetString(payload).Split('\n');
            return parts.Length == 3
                && string.Equals(parts[0], audience, StringComparison.Ordinal)
                && long.TryParse(parts[1], out var expiry)
                && expiry >= (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] Sign(string secret, byte[] payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return hmac.ComputeHash(payload);
    }

    private static void EnsureInputs(string secret, string audience)
    {
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < MinimumSecretLength)
            throw new InvalidOperationException("Cross-app logout secret must contain at least 32 characters.");
        if (string.IsNullOrWhiteSpace(audience) || audience.Contains('\n', StringComparison.Ordinal))
            throw new ArgumentException("A valid logout audience is required.", nameof(audience));
    }
}
