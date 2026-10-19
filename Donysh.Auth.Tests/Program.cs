using System.Net;
using System.Text.Json;
using Donysh.Auth.Domain;
using Donysh.Auth.Pages;
using Donysh.Auth.Security;
using Donysh.Auth.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

var failed = 0;

void Check(string name, bool condition)
{
    if (condition) Console.WriteLine($"PASS {name}");
    else
    {
        failed++;
        Console.WriteLine($"FAIL {name}");
    }
}

Check("Local return path is preserved", SafeReturnUrl.Normalize("/connect/authorize?client_id=finance") == "/connect/authorize?client_id=finance");
foreach (var hostile in new[] { "https://evil.example/", "//evil.example/", "/\\evil.example/", "javascript:alert(1)", null })
    Check($"Open redirect is blocked: {hostile ?? "null"}", SafeReturnUrl.Normalize(hostile) == "/");

// ASP.NET Core Identity's password format is independent from the concrete IdentityUser subtype.
// This protects the proposed phase-2 migration path: existing Finance hashes can be copied as-is.
var legacyHasher = new PasswordHasher<IdentityUser>();
var legacyHash = legacyHasher.HashPassword(new IdentityUser(), "Migration-Test-123");
var authHasher = new PasswordHasher<AuthUser>();
Check("Identity password hash remains compatible",
    authHasher.VerifyHashedPassword(new AuthUser(), legacyHash, "Migration-Test-123") is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded);
var ecosystemHome = "https://donysh.test/";
Check("Authenticated Auth home returns to the configured ecosystem destination",
    new IndexModel(Options.Create(new AuthNavigationOptions { DefaultReturnUrl = ecosystemHome })).OnGet()
        is Microsoft.AspNetCore.Mvc.RedirectResult { Url: var destination }
    && destination == ecosystemHome);
Check("Auth home rejects an unsafe configured destination",
    new IndexModel(Options.Create(new AuthNavigationOptions { DefaultReturnUrl = "javascript:alert(1)" })).OnGet()
        is Microsoft.AspNetCore.Mvc.RazorPages.PageResult);
var authCsp = AuthSecurityHeaders.BuildContentSecurityPolicy([
    "https://finance.donysh.test/signin-oidc",
    "https://finance.donysh.test/signout-callback-oidc",
    "https://life.donysh.test/signin-oidc",
    "javascript:alert(1)"
]);
Check("Auth CSP allows only registered product origins for form redirects",
    authCsp.Contains("form-action 'self' https://finance.donysh.test https://life.donysh.test", StringComparison.Ordinal)
    && !authCsp.Contains("signin-oidc", StringComparison.Ordinal)
    && !authCsp.Contains("javascript", StringComparison.Ordinal));
var authPrefetch = new DefaultHttpContext();
authPrefetch.Request.Method = HttpMethods.Get;
authPrefetch.Request.Path = "/connect/authorize";
authPrefetch.Request.Headers["Sec-Purpose"] = "prefetch;prerender";
Check("Auth ignores speculative authorization requests",
    AuthenticationPrefetchGuard.ShouldSkip(authPrefetch.Request));
authPrefetch.Request.Path = "/health/live";
Check("Auth prefetch guard leaves ordinary endpoints unchanged",
    !AuthenticationPrefetchGuard.ShouldSkip(authPrefetch.Request));

Environment.SetEnvironmentVariable("Database__ApplyMigrations", "false");
Environment.SetEnvironmentVariable("AllowedHosts", "*");
Environment.SetEnvironmentVariable("OidcClients__0__RedirectUris__0", "https://finance.test/signin-oidc");
Environment.SetEnvironmentVariable("OidcClients__0__PostLogoutRedirectUris__0", "https://finance.test/signout-callback-oidc");
Environment.SetEnvironmentVariable("OidcClients__1__RedirectUris__0", "https://life.test/signin-oidc");
Environment.SetEnvironmentVariable("OidcClients__1__PostLogoutRedirectUris__0", "https://life.test/signout-callback-oidc");
Environment.SetEnvironmentVariable("ConnectionStrings__AuthConnection",
    "Host=127.0.0.1;Port=1;Database=donysh_auth_test;Username=test;Password=test;Timeout=1;Command Timeout=1");
await using var factory = new AuthApplicationFactory();
using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
{
    AllowAutoRedirect = false,
    BaseAddress = new Uri("https://auth.test")
});

using (var response = await client.GetAsync("/health/live"))
{
    Check("Liveness health check succeeds", response.StatusCode == HttpStatusCode.OK);
}

using (var response = await client.GetAsync("/Account/Login"))
{
    var html = await response.Content.ReadAsStringAsync();
    Check("Persian RTL login page renders", response.StatusCode == HttpStatusCode.OK
        && html.Contains("lang=\"fa\"", StringComparison.Ordinal)
        && html.Contains("dir=\"rtl\"", StringComparison.Ordinal)
        && html.Contains("حساب‌های فعلی بخش مالی هنوز به این صفحه منتقل نشده‌اند", StringComparison.Ordinal));
    Check("Password field has an accessible visibility control",
        html.Contains("data-password-toggle", StringComparison.Ordinal)
        && html.Contains("aria-label=\"نمایش رمز عبور\"", StringComparison.Ordinal)
        && html.Contains("/js/password-toggle.js", StringComparison.Ordinal));
    var hasCsp = response.Headers.TryGetValues("Content-Security-Policy", out var values);
    var cspHeader = hasCsp ? values!.Single() : string.Empty;
    Check("Security headers are present",
        hasCsp
        && cspHeader.Contains("frame-ancestors 'none'", StringComparison.Ordinal)
        && cspHeader.Contains("https://finance.test", StringComparison.Ordinal)
        && cspHeader.Contains("https://life.test", StringComparison.Ordinal)
        && response.Headers.TryGetValues("X-Frame-Options", out var frame)
        && frame.Single() == "DENY");
}

using (var response = await client.GetAsync(
    "/Account/Login?ReturnUrl=%2Fconnect%2Fauthorize%3Fclient_id%3Dfinance-web%26redirect_uri%3Dhttps%253A%252F%252Fdonysh.ir%252Fsignin-oidc"))
{
    var html = await response.Content.ReadAsStringAsync();
    Check("Login form posts to a fixed same-origin path under strict CSP",
        response.StatusCode == HttpStatusCode.OK
        && html.Contains("action=\"/Account/Login\"", StringComparison.Ordinal)
        && html.Contains("name=\"ReturnUrl\"", StringComparison.Ordinal)
        && html.Contains("/connect/authorize?client_id=finance-web", StringComparison.Ordinal));
}

using (var response = await client.GetAsync("/Account/Register"))
{
    var html = await response.Content.ReadAsStringAsync();
    Check("Public registration remains disabled", response.StatusCode == HttpStatusCode.OK
        && html.Contains("ثبت‌نام عمومی هنوز فعال نشده است", StringComparison.Ordinal));
}

using (var response = await client.GetAsync("/Account/Logout"))
{
    var location = response.Headers.Location;
    var locationPath = location?.IsAbsoluteUri == true ? location.AbsolutePath : location?.OriginalString;
    Check("Anonymous logout page is protected", response.StatusCode == HttpStatusCode.Redirect
        && locationPath?.StartsWith("/Account/Login", StringComparison.Ordinal) == true);
}

using (var response = await client.GetAsync("/.well-known/openid-configuration"))
{
    var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    var grants = json.RootElement.GetProperty("grant_types_supported").EnumerateArray().Select(x => x.GetString()).ToArray();
    var methods = json.RootElement.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(x => x.GetString()).ToArray();
    Check("OIDC discovery advertises authorization code", response.StatusCode == HttpStatusCode.OK && grants.Contains("authorization_code"));
    Check("OIDC discovery requires strong PKCE S256", methods.SequenceEqual(["S256"]));
}

var postgres = Environment.GetEnvironmentVariable("DONYSH_AUTH_TEST_POSTGRES");
if (!string.IsNullOrWhiteSpace(postgres)) await PostgresChecks.RunAsync(postgres);
else Console.WriteLine("SKIP PostgreSQL migration checks (DONYSH_AUTH_TEST_POSTGRES is not set)");

if (failed > 0) throw new Exception($"{failed} Auth checks failed.");
Console.WriteLine("All Donysh Auth checks passed.");

internal sealed class AuthApplicationFactory : WebApplicationFactory<global::AuthProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:AuthConnection"] = "Host=127.0.0.1;Port=1;Database=donysh_auth_test;Username=test;Password=test;Timeout=1;Command Timeout=1",
                ["Database:ApplyMigrations"] = "false",
                ["AuthFeatures:PublicRegistration"] = "false",
                ["AuthFeatures:PasswordRecovery"] = "false",
                ["AuthFeatures:LegacyIdentityImportCompleted"] = "false",
                ["ReverseProxy:Enabled"] = "false"
            }));
    }
}
