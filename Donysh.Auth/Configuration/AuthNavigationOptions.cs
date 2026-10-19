namespace Donysh.Auth.Configuration;

public sealed class AuthNavigationOptions
{
    public const string SectionName = "Navigation";

    public string DefaultReturnUrl { get; set; } = string.Empty;
}
