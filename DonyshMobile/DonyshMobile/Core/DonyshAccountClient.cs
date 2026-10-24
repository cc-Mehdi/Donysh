using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace DonyshMobile.Core;

public sealed record DonyshAccountSignIn(string Ticket, string Subject, string DisplayName, string Email,
    bool LifeEnabled);

public sealed class DonyshAccountClient(Func<HttpClient>? clientFactory = null)
{
    private readonly Func<HttpClient> _clientFactory = clientFactory ?? (() => new HttpClient { Timeout = TimeSpan.FromSeconds(20) });
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<DonyshAccountSignIn?> SignInAsync(string identifier, string password,
        string installationId, CancellationToken cancellationToken = default)
    {
        identifier = identifier.Trim();
        if (identifier.Length is < 3 or > 254 || identifier.Any(char.IsControl)
            || string.IsNullOrEmpty(password) || password.Length > 256
            || installationId.Length != 64 || !installationId.All(Uri.IsHexDigit))
            throw new ArgumentException("اطلاعات ورود کامل یا معتبر نیست.");
        using var client = _clientFactory();
        using var response = await client.PostAsJsonAsync("https://auth.donysh.ir/api/mobile/sign-in",
            new { identifier, password, installationId }, Json, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized) return null;
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<DonyshAccountSignIn>(Json, cancellationToken);
        return result is { Ticket.Length: > 20 and < 2048, Email.Length: > 3 }
            ? result : throw new InvalidDataException("پاسخ حساب دُنیش معتبر نیست.");
    }
}
