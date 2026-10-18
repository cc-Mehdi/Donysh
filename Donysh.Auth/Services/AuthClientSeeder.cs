using Donysh.Auth.Configuration;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Donysh.Auth.Services;

public sealed class AuthClientSeeder(
    IOpenIddictApplicationManager applications,
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ILogger<AuthClientSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var clients = configuration.GetSection(AuthClientOptions.SectionName).Get<List<AuthClientOptions>>() ?? [];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var client in clients)
        {
            Validate(client, seen);
            var descriptor = BuildDescriptor(client);
            var existing = await applications.FindByClientIdAsync(client.ClientId, cancellationToken);
            if (existing is null)
            {
                await applications.CreateAsync(descriptor, cancellationToken);
                logger.LogInformation("Registered OIDC client {ClientId}.", client.ClientId);
            }
            else
            {
                await applications.UpdateAsync(existing, descriptor, cancellationToken);
                logger.LogInformation("Updated OIDC client {ClientId}.", client.ClientId);
            }
        }
    }

    private void Validate(AuthClientOptions client, HashSet<string> seen)
    {
        if (string.IsNullOrWhiteSpace(client.ClientId) || client.ClientId.Length > 100
            || client.ClientId.Any(char.IsControl) || !seen.Add(client.ClientId))
            throw new InvalidOperationException("Every OIDC client must have a unique, valid ClientId.");
        if (string.IsNullOrWhiteSpace(client.DisplayName) || client.DisplayName.Length > 100)
            throw new InvalidOperationException($"OIDC client '{client.ClientId}' needs a valid display name.");
        if (!client.PublicClient && (string.IsNullOrWhiteSpace(client.ClientSecret)
            || client.ClientSecret.Length < 32 || client.ClientSecret.Any(char.IsControl)))
            throw new InvalidOperationException($"Confidential OIDC client '{client.ClientId}' requires a strong secret outside source control.");
        if (client.RedirectUris.Count == 0)
            throw new InvalidOperationException($"OIDC client '{client.ClientId}' requires an exact redirect URI.");
        foreach (var value in client.RedirectUris.Concat(client.PostLogoutRedirectUris))
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.Fragment)
                || (!environment.IsDevelopment() && uri.Scheme != Uri.UriSchemeHttps))
                throw new InvalidOperationException($"OIDC client '{client.ClientId}' contains an invalid redirect URI.");
        }
    }

    private static OpenIddictApplicationDescriptor BuildDescriptor(AuthClientOptions client)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = client.ClientId.Trim(),
            DisplayName = client.DisplayName.Trim(),
            ClientSecret = client.PublicClient ? null : client.ClientSecret,
            ClientType = client.PublicClient ? ClientTypes.Public : ClientTypes.Confidential,
            ConsentType = ConsentTypes.Implicit
        };
        descriptor.Permissions.UnionWith([
            Permissions.Endpoints.Authorization,
            Permissions.Endpoints.EndSession,
            Permissions.Endpoints.Token,
            Permissions.GrantTypes.AuthorizationCode,
            Permissions.ResponseTypes.Code,
            Permissions.Scopes.Email,
            Permissions.Scopes.Profile
        ]);
        descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
        foreach (var value in client.RedirectUris) descriptor.RedirectUris.Add(new Uri(value));
        foreach (var value in client.PostLogoutRedirectUris) descriptor.PostLogoutRedirectUris.Add(new Uri(value));
        return descriptor;
    }
}
