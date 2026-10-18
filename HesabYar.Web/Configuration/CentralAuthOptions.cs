namespace HesabYar.Web.Configuration;

public sealed class CentralAuthOptions
{
    public const string SectionName = "CentralAuth";
    public const string Scheme = "DonyshCentralAuth";
    public const string SecureResponseMode = "query";

    public bool Enabled { get; set; }
    public string Authority { get; set; } = string.Empty;
    public string ClientId { get; set; } = "finance-web";
    public string ClientSecret { get; set; } = string.Empty;
    public bool RequireHttpsMetadata { get; set; } = true;

    public string FinanceSessionCookieName => Enabled
        ? ".Donysh.Finance.CentralSession"
        : ".HesabYar.Auth";
}
