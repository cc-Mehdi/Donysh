namespace Donysh.Life.Configuration;

public sealed class LifeAuthOptions
{
    public const string SectionName = "CentralAuth";
    public const string OidcScheme = "DonyshLifeOidc";
    public const string CookieScheme = "DonyshLifeSession";

    public string Authority { get; set; } = string.Empty;
    public string ClientId { get; set; } = "life-web";
    public string ClientSecret { get; set; } = string.Empty;
    public bool RequireHttpsMetadata { get; set; } = true;
}
