using System.Security.Cryptography.X509Certificates;

namespace Donysh.Auth.Security;

public static class OidcCertificateLoader
{
    public static X509Certificate2 Load(IConfiguration configuration, string name)
    {
        var path = configuration[$"OpenIddict:{name}Certificate:Path"];
        var password = configuration[$"OpenIddict:{name}Certificate:Password"];
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidOperationException($"OpenIddict {name.ToLowerInvariant()} certificate path must be an absolute path.");
        if (!File.Exists(path))
            throw new InvalidOperationException($"OpenIddict {name.ToLowerInvariant()} certificate was not found.");

        var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            path, password, X509KeyStorageFlags.EphemeralKeySet);
        if (!certificate.HasPrivateKey)
        {
            certificate.Dispose();
            throw new InvalidOperationException($"OpenIddict {name.ToLowerInvariant()} certificate has no private key.");
        }
        if (certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow.AddDays(30))
        {
            certificate.Dispose();
            throw new InvalidOperationException($"OpenIddict {name.ToLowerInvariant()} certificate is expired or expires within 30 days.");
        }
        return certificate;
    }
}
