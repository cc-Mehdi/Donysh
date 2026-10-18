namespace Donysh.Auth.Configuration;

public sealed class AuthClientOptions
{
    public const string SectionName = "OidcClients";

    public string ClientId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? ClientSecret { get; set; }
    public bool PublicClient { get; set; }
    public List<string> RedirectUris { get; set; } = [];
    public List<string> PostLogoutRedirectUris { get; set; } = [];
}
