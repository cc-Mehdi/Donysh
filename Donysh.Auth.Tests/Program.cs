using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Donysh.Auth.Domain;
using Donysh.Auth.Pages;
using Donysh.Auth.Security;
using Donysh.Auth.Configuration;
using Donysh.Auth.Controllers;
using Donysh.Auth.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

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
Check("Direct Auth login can show the product chooser",
    new IndexModel(Options.Create(new AuthNavigationOptions())).OnGet()
        is Microsoft.AspNetCore.Mvc.RazorPages.PageResult);
const string logoutSecret = "test-only-shared-logout-secret-0000000000000000";
var authFinalProof = CrossAppLogoutToken.Create(logoutSecret, "auth-finalize");
Check("Central logout accepts only a short-lived proof for Auth",
    CrossAppLogoutToken.TryValidate(authFinalProof, logoutSecret, "auth-finalize")
    && !CrossAppLogoutToken.TryValidate(authFinalProof, logoutSecret, "life-finalize")
    && !CrossAppLogoutToken.TryValidate(authFinalProof + "x", logoutSecret, "auth-finalize"));
var centralLogoutConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Products:FinanceUrl"] = "https://finance.test",
    ["CrossAppLogout:Secret"] = logoutSecret
}).Build();
var centralLogout = new Donysh.Auth.Pages.Account.LogoutModel(null!, centralLogoutConfiguration)
{
    PageContext = new Microsoft.AspNetCore.Mvc.RazorPages.PageContext
    {
        HttpContext = new DefaultHttpContext()
    }
};
var centralPeerRedirect = centralLogout.OnPost() as Microsoft.AspNetCore.Mvc.RedirectResult;
Check("Auth logout visits Finance before clearing the central session",
    centralPeerRedirect?.Url?.StartsWith("https://finance.test/Account/FederatedLogout?proof=", StringComparison.Ordinal) == true
    && CrossAppLogoutToken.TryValidate(
        Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(centralPeerRedirect.Url).Query)["proof"],
        logoutSecret, "finance-auth")
    && centralLogout.OnGetFinalizeAsync("invalid").Result is Microsoft.AspNetCore.Mvc.NotFoundResult);
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
var confirmationEmail = AuthEmailTemplates.ConfirmAccount("<کاربر>", "https://auth.test/confirm?a=1&b=2");
Check("Confirmation email escapes user-controlled content",
    confirmationEmail.Contains("&lt;کاربر&gt;", StringComparison.Ordinal)
    && confirmationEmail.Contains("a=1&amp;b=2", StringComparison.Ordinal)
    && !confirmationEmail.Contains("<کاربر>", StringComparison.Ordinal));
Check("SMTP configuration requires a valid sender and credential pair",
    new EmailOptions
    {
        Host = "smtp.test", Port = 587, TimeoutSeconds = 20,
        FromAddress = "no-reply@example.test", UserName = "user", Password = "secret"
    }.IsConfigured
    && !new EmailOptions
    {
        Host = "smtp.test", Port = 587, TimeoutSeconds = 20,
        FromAddress = "invalid", UserName = "user"
    }.IsConfigured);

var tokenIdentity = new System.Security.Claims.ClaimsIdentity();
tokenIdentity.SetClaim(Claims.Subject, "auth-user-id")
    .SetClaim(Claims.Name, "Donysh User")
    .SetClaim(Claims.Email, "user@example.test")
    .SetClaim(Claims.EmailVerified, bool.TrueString.ToLowerInvariant());
var tokenPrincipal = new System.Security.Claims.ClaimsPrincipal(tokenIdentity);
tokenPrincipal.SetScopes(Scopes.OpenId, Scopes.Profile, Scopes.Email);
tokenPrincipal.SetDestinations(AuthorizationController.ResolveDestinations);
Check("OIDC ID token receives profile and verified-email claims after scopes are granted",
    tokenIdentity.FindFirst(Claims.Name)?.GetDestinations().Contains(Destinations.IdentityToken) == true
    && tokenIdentity.FindFirst(Claims.Email)?.GetDestinations().Contains(Destinations.IdentityToken) == true
    && tokenIdentity.FindFirst(Claims.EmailVerified)?.GetDestinations().Contains(Destinations.IdentityToken) == true);

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
    Check("Login uses the mother Donysh wordmark instead of the Finance logo",
        html.Contains("/images/donysh-wordmark.png", StringComparison.Ordinal)
        && !html.Contains("donysh-finance-logo.png", StringComparison.Ordinal));
    Check("Login versions the mother wordmark for browser caches",
        html.Contains("/images/donysh-wordmark.png?v=", StringComparison.Ordinal));
    Check("Login uses the parent icon for browser and home screen shortcuts",
        html.Contains("rel=\"icon\" type=\"image/png\" href=\"/images/donysh-icon.png?v=", StringComparison.Ordinal)
        && html.Contains("rel=\"apple-touch-icon\" href=\"/images/donysh-icon.png?v=", StringComparison.Ordinal));
    Check("Password field has an accessible visibility control",
        html.Contains("data-password-toggle", StringComparison.Ordinal)
        && html.Contains("aria-label=\"نمایش رمز عبور\"", StringComparison.Ordinal)
        && html.Contains("/js/password-toggle.js", StringComparison.Ordinal));
    Check("Login page explains its secure product handoff",
        html.Contains("ورود امن به حساب مرکزی", StringComparison.Ordinal)
        && html.Contains("دُنیش مالی", StringComparison.Ordinal)
        && html.Contains("دُنیش زندگی", StringComparison.Ordinal));
    Check("Login uses the parent brand and keeps the real sign-in form",
        html.Contains("class=\"auth-body auth-login-page\"", StringComparison.Ordinal)
        && html.Contains("/images/donysh-icon.png?v=", StringComparison.Ordinal)
        && html.Contains("name=\"Input.Email\"", StringComparison.Ordinal)
        && html.Contains("name=\"Input.Password\"", StringComparison.Ordinal)
        && html.Contains("name=\"Input.RememberMe\"", StringComparison.Ordinal)
        && !html.Contains("id=\"loginForm\"", StringComparison.Ordinal));
    Check("Login page preloads its Persian fonts",
        html.Contains("rel=\"preload\" href=\"/fonts/YekanBakh-Regular.woff2\"", StringComparison.Ordinal)
        && html.Contains("rel=\"preload\" href=\"/fonts/YekanBakh-Bold.woff2\"", StringComparison.Ordinal));
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
        && html.Contains("ثبت‌نام موقتاً در دسترس نیست", StringComparison.Ordinal));
}

await using (var unavailableRecoveryFactory = new RecoveryUnavailableAuthApplicationFactory())
using (var recoveryClient = unavailableRecoveryFactory.CreateClient(new WebApplicationFactoryClientOptions
{
    AllowAutoRedirect = false,
    BaseAddress = new Uri("https://auth.test")
}))
using (var response = await recoveryClient.GetAsync("/Account/Login"))
{
    var html = await response.Content.ReadAsStringAsync();
    Check("Login hides password recovery when email delivery is unavailable",
        response.StatusCode == HttpStatusCode.OK
        && !html.Contains("رمز عبور را فراموش کرده‌اید؟", StringComparison.Ordinal));
}

await using (var registrationFactory = new RegistrationAuthApplicationFactory())
using (var registrationClient = registrationFactory.CreateClient(new WebApplicationFactoryClientOptions
{
    AllowAutoRedirect = false,
    BaseAddress = new Uri("https://auth.test")
}))
{
    using (var response = await registrationClient.GetAsync(
        "/Account/Register?ReturnUrl=%2Fconnect%2Fauthorize%3Fclient_id%3Dfinance-web"))
    {
        var html = await response.Content.ReadAsStringAsync();
        Check("Enabled registration renders a complete confirmation flow",
            response.StatusCode == HttpStatusCode.OK
            && html.Contains("ساخت حساب جدید", StringComparison.Ordinal)
            && html.Contains("name=\"Input.DisplayName\"", StringComparison.Ordinal)
            && html.Contains("name=\"Input.ConfirmPassword\"", StringComparison.Ordinal)
            && html.Contains("name=\"ReturnUrl\"", StringComparison.Ordinal)
            && html.Contains("/connect/authorize?client_id=finance-web", StringComparison.Ordinal)
            && html.Contains("شرایط رمز عبور", StringComparison.Ordinal));
    }

    using (var response = await registrationClient.GetAsync("/Account/Login?ReturnUrl=%2Fcontinue"))
    {
        var html = await response.Content.ReadAsStringAsync();
        Check("Login offers registration only when email delivery is ready",
            response.StatusCode == HttpStatusCode.OK
            && html.Contains("ساخت حساب مرکزی", StringComparison.Ordinal)
            && html.Contains("returnUrl=%2Fcontinue", StringComparison.OrdinalIgnoreCase));
    }

    using (var response = await registrationClient.GetAsync("/Account/ResendConfirmation"))
    {
        var html = await response.Content.ReadAsStringAsync();
        Check("Registration can securely request a fresh confirmation link",
            response.StatusCode == HttpStatusCode.OK
            && html.Contains("ارسال دوبارهٔ پیوند تأیید", StringComparison.Ordinal)
            && html.Contains("وجود یا نبود حساب نمایش داده نمی‌شود", StringComparison.Ordinal));
    }
}

await using (var throttledRegistrationFactory = new RegistrationAuthApplicationFactory())
using (var throttledRegistrationClient = throttledRegistrationFactory.CreateClient(new WebApplicationFactoryClientOptions
{
    AllowAutoRedirect = false,
    BaseAddress = new Uri("https://auth.test")
}))
{
    HttpResponseMessage? lastResponse = null;
    for (var requestNumber = 0; requestNumber < 6; requestNumber++)
    {
        lastResponse?.Dispose();
        lastResponse = await throttledRegistrationClient.GetAsync("/Account/Register");
    }
    using (lastResponse)
        Check("Registration has a stricter anti-abuse rate limit",
            lastResponse?.StatusCode == HttpStatusCode.TooManyRequests
            && lastResponse.Headers.RetryAfter is not null);
}

using (var response = await client.GetAsync("/Account/Logout"))
{
    var location = response.Headers.Location;
    var locationPath = location?.IsAbsoluteUri == true ? location.AbsolutePath : location?.OriginalString;
    Check("Anonymous logout page is protected", response.StatusCode == HttpStatusCode.Redirect
        && locationPath?.StartsWith("/Account/Login", StringComparison.Ordinal) == true);
}

using (var response = await client.GetAsync("/Account/SignedOut"))
{
    var html = await response.Content.ReadAsStringAsync();
    Check("Signed-out page offers clear next steps",
        response.StatusCode == HttpStatusCode.OK
        && html.Contains("با خیال راحت خارج شدید", StringComparison.Ordinal)
        && html.Contains("ورود دوباره", StringComparison.Ordinal)
        && html.Contains("https://donysh.ir/", StringComparison.Ordinal));
}

using (var response = await client.GetAsync("/images/donysh-wordmark.png"))
{
    var hash = Convert.ToHexString(SHA256.HashData(await response.Content.ReadAsByteArrayAsync()));
    Check("Mother wordmark is served in Auth development and published builds",
        response.StatusCode == HttpStatusCode.OK
        && response.Content.Headers.ContentType?.MediaType == "image/png"
        && hash == "15739A26AAF905442B389675562EB986125118A144890769789DFBE8E0D660E4");
}

using (var response = await client.GetAsync("/images/donysh-icon.png"))
{
    var hash = Convert.ToHexString(SHA256.HashData(await response.Content.ReadAsByteArrayAsync()));
    Check("Parent icon is served in Auth development and published builds",
        response.StatusCode == HttpStatusCode.OK
        && response.Content.Headers.ContentType?.MediaType == "image/png"
        && hash == "4E5C4DA87419E3F608366E07B232FEBEF498F61977A6EDBD90B94C17175276A1");
}

using (var response = await client.GetAsync("/css/app.css?v=test-version"))
{
    Check("Versioned Auth assets are cached immutably",
        response.StatusCode == HttpStatusCode.OK
        && response.Headers.CacheControl?.Public == true
        && response.Headers.CacheControl?.MaxAge == TimeSpan.FromDays(365)
        && response.Headers.CacheControl?.Extensions.Any(extension =>
            extension.Name.Equals("immutable", StringComparison.OrdinalIgnoreCase)) == true);
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

internal class AuthApplicationFactory : WebApplicationFactory<global::AuthProgram>
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

internal sealed class RegistrationAuthApplicationFactory : AuthApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Features:PublicRegistration"] = "true",
                ["Features:LegacyIdentityImportCompleted"] = "true"
            }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAuthEmailSender>();
            services.AddSingleton<IAuthEmailSender, ConfiguredTestEmailSender>();
        });
    }
}

internal sealed class RecoveryUnavailableAuthApplicationFactory : AuthApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Features:PasswordRecovery"] = "true",
                ["Features:LegacyIdentityImportCompleted"] = "true"
            }));
    }
}

internal sealed class ConfiguredTestEmailSender : IAuthEmailSender
{
    public bool IsConfigured => true;
    public Task SendAsync(string recipient, string subject, string htmlBody, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
