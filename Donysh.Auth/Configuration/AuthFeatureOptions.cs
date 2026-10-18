namespace Donysh.Auth.Configuration;

public sealed class AuthFeatureOptions
{
    public const string SectionName = "Features";

    public bool PublicRegistration { get; set; }
    public bool PasswordRecovery { get; set; }
    public bool LegacyIdentityImportCompleted { get; set; }

    public bool CanRegister => PublicRegistration && LegacyIdentityImportCompleted;
    public bool CanRecoverPassword => PasswordRecovery && LegacyIdentityImportCompleted;
}
