namespace Donysh.Landing.Configuration;

public sealed class LandingLinksOptions
{
    public const string SectionName = "Products";

    public string FinanceUrl { get; set; } = "https://finance.donysh.ir";
    public string AuthUrl { get; set; } = "https://auth.donysh.ir";
    public string LifeUrl { get; set; } = "https://life.donysh.ir";
}
