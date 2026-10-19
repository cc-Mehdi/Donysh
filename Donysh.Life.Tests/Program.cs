using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Encodings.Web;
using Donysh.Life.Configuration;
using Donysh.Life.Data;
using Donysh.Life.Features.Activities;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.EntityFrameworkCore;

var failed = 0;
void Check(string name, bool condition)
{
    if (condition) Console.WriteLine($"PASS {name}");
    else { failed++; Console.WriteLine($"FAIL {name}"); }
}

Environment.SetEnvironmentVariable("ConnectionStrings__LifeConnection", "Host=127.0.0.1;Database=unused;Username=unused;Password=unused");
Environment.SetEnvironmentVariable("CentralAuth__Authority", "https://auth.test");
Environment.SetEnvironmentVariable("CentralAuth__ClientId", "life-web");
Environment.SetEnvironmentVariable("CentralAuth__ClientSecret", "test-only-life-client-secret-000000000000");
Environment.SetEnvironmentVariable("CentralAuth__RequireHttpsMetadata", "false");
Environment.SetEnvironmentVariable("Database__ApplyMigrations", "false");
Environment.SetEnvironmentVariable("ReverseProxy__Enabled", "false");

await using var factory = new LifeApplicationFactory();
using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
{
    AllowAutoRedirect = false,
    BaseAddress = new Uri("https://life.test")
});

using (var response = await client.GetAsync("/Account/Login"))
{
    var html = await response.Content.ReadAsStringAsync();
    Check("Life login renders in Persian RTL", response.StatusCode == HttpStatusCode.OK
        && html.Contains("lang=\"fa\"", StringComparison.Ordinal)
        && html.Contains("dir=\"rtl\"", StringComparison.Ordinal)
        && html.Contains("همان حسابی که در دونیش مالی داری", StringComparison.Ordinal));
    Check("Life security headers block framing and scripts",
        response.Headers.TryGetValues("Content-Security-Policy", out var csp)
        && csp.Single().Contains("frame-ancestors 'none'", StringComparison.Ordinal)
        && csp.Single().Contains("form-action 'self' https://auth.test", StringComparison.Ordinal)
        && csp.Single().Contains("script-src 'none'", StringComparison.Ordinal)
        && response.Headers.TryGetValues("X-Frame-Options", out var frame)
        && frame.Single() == "DENY");
}

using (var response = await client.GetAsync("/"))
{
    Check("Anonymous Life dashboard starts central authentication",
        response.StatusCode == HttpStatusCode.Redirect
        && response.Headers.Location?.OriginalString.StartsWith("https://auth.test/connect/authorize", StringComparison.Ordinal) == true);
}

using (var response = await client.GetAsync("/health/live"))
{
    using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    Check("Life liveness succeeds without database access",
        response.StatusCode == HttpStatusCode.OK
        && payload.RootElement.GetProperty("status").GetString() == "live"
        && payload.RootElement.GetProperty("service").GetString() == "donysh-life");
}

using (var response = await client.GetAsync("/css/app.css"))
{
    Check("Life stylesheet is served", response.StatusCode == HttpStatusCode.OK
        && response.Content.Headers.ContentType?.MediaType == "text/css");
}

await using (var authenticatedFactory = new AuthenticatedLifeApplicationFactory())
using (var authenticatedClient = authenticatedFactory.CreateClient(new WebApplicationFactoryClientOptions
{
    AllowAutoRedirect = false,
    BaseAddress = new Uri("https://life.test")
}))
{
    using var response = await authenticatedClient.GetAsync("/");
    var html = await response.Content.ReadAsStringAsync();
    Check("Authenticated Life dashboard renders its initial shell",
        response.StatusCode == HttpStatusCode.OK
        && html.Contains("برای چیزهای مهم، جا باز کن", StringComparison.Ordinal)
        && html.Contains("برنامهٔ هفته", StringComparison.Ordinal)
        && html.Contains("فعالیت‌های فعال", StringComparison.Ordinal));
    Check("Life logout posts locally before central OIDC sign-out",
        html.Contains("action=\"/Account/Logout\"", StringComparison.Ordinal));

    using (var setupResponse = await authenticatedClient.GetAsync("/Setup?step=2"))
    {
        var setupHtml = await setupResponse.Content.ReadAsStringAsync();
        Check("Life setup wizard offers activity templates and weekly goals",
            setupResponse.StatusCode == HttpStatusCode.OK
            && setupHtml.Contains("این هفته برای چه چیزهایی وقت می‌گذاری", StringComparison.Ordinal)
            && setupHtml.Contains("Suggestions[0].WeeklyHours", StringComparison.Ordinal));
        Check("Life setup wizard does not require inline styles",
            !setupHtml.Contains("style=", StringComparison.OrdinalIgnoreCase));
    }

    using (var activitiesResponse = await authenticatedClient.GetAsync("/Activities"))
    {
        var activitiesHtml = await activitiesResponse.Content.ReadAsStringAsync();
        Check("Life activity management renders create and empty states",
            activitiesResponse.StatusCode == HttpStatusCode.OK
            && activitiesHtml.Contains("فعالیت تازه", StringComparison.Ordinal)
            && activitiesHtml.Contains("هنوز فعالیتی نداری", StringComparison.Ordinal));
    }

    var antiforgeryMatch = Regex.Match(
        html,
        "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"",
        RegexOptions.CultureInvariant);
    using var logoutContent = new FormUrlEncodedContent(new Dictionary<string, string>
    {
        ["__RequestVerificationToken"] = WebUtility.HtmlDecode(antiforgeryMatch.Groups[1].Value)
    });
    using var logoutResponse = await authenticatedClient.PostAsync("/Account/Logout", logoutContent);
    var logoutLocation = logoutResponse.Headers.Location?.OriginalString ?? string.Empty;
    Check("Life logout redirects through the central end-session endpoint",
        antiforgeryMatch.Success
        && logoutResponse.StatusCode == HttpStatusCode.Redirect
        && logoutLocation.StartsWith("https://auth.test/connect/logout", StringComparison.Ordinal)
        && logoutLocation.Contains("post_logout_redirect_uri=https%3A%2F%2Flife.test%2Fsignout-callback-oidc", StringComparison.OrdinalIgnoreCase));
}

var lifeLogout = new Donysh.Life.Pages.Account.LogoutModel().OnPost() as Microsoft.AspNetCore.Mvc.SignOutResult;
Check("Life logout clears both the product and central sessions",
    lifeLogout is not null
    && lifeLogout.AuthenticationSchemes.Contains(LifeAuthOptions.CookieScheme)
    && lifeLogout.AuthenticationSchemes.Contains(LifeAuthOptions.OidcScheme)
    && lifeLogout.Properties?.RedirectUri == "/Account/SignedOut");

var activityDatabase = "life-activity-tests-" + Guid.NewGuid().ToString("N");
var activityOptions = new DbContextOptionsBuilder<LifeDbContext>()
    .UseInMemoryDatabase(activityDatabase)
    .Options;
await using (var activityDb = new LifeDbContext(activityOptions))
{
    var httpContext = new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "activity-owner-a")], "test"))
    };
    var service = new LifeActivityService(activityDb, new HttpContextAccessor { HttpContext = httpContext });
    var created = await service.SaveAsync(null, new ActivityForm
    {
        Title = "ورزش",
        Category = "سلامت",
        ColorHex = "#EA580C",
        IconKey = "activity",
        WeeklyHours = 4
    });
    var ownerItems = await service.ListAsync();
    Check("Life activity creation keeps the weekly goal separate",
        created.Succeeded
        && ownerItems.Count == 1
        && ownerItems[0].TargetMinutes == 240);

    httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "activity-owner-b")], "test"));
    var otherItems = await service.ListAsync();
    var forbiddenToggle = await service.ToggleAsync(ownerItems[0].Id);
    var forbiddenDelete = await service.DeleteAsync(ownerItems[0].Id);
    Check("Life activity reads and mutations are isolated by central subject",
        otherItems.Count == 0
        && !forbiddenToggle.Succeeded
        && !forbiddenDelete.Succeeded);
}

var postgres = Environment.GetEnvironmentVariable("DONYSH_LIFE_TEST_POSTGRES");
if (!string.IsNullOrWhiteSpace(postgres)) await PostgresChecks.RunAsync(postgres);
else Console.WriteLine("SKIP PostgreSQL Life migration checks (DONYSH_LIFE_TEST_POSTGRES is not set)");

if (failed > 0) throw new Exception($"{failed} Donysh Life checks failed.");
Console.WriteLine("All Donysh Life checks passed.");

internal class LifeApplicationFactory : WebApplicationFactory<global::LifeProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "*",
                ["ConnectionStrings:LifeConnection"] = "Host=127.0.0.1;Database=unused;Username=unused;Password=unused",
                ["CentralAuth:Authority"] = "https://auth.test",
                ["CentralAuth:ClientId"] = "life-web",
                ["CentralAuth:ClientSecret"] = "test-only-life-client-secret-000000000000",
                ["CentralAuth:RequireHttpsMetadata"] = "false",
                ["Database:ApplyMigrations"] = "false",
                ["ReverseProxy:Enabled"] = "false"
            }));
        builder.ConfigureServices(services => services.Configure<OpenIdConnectOptions>(
            LifeAuthOptions.OidcScheme,
            options => options.Configuration = new OpenIdConnectConfiguration
            {
                AuthorizationEndpoint = "https://auth.test/connect/authorize",
                TokenEndpoint = "https://auth.test/connect/token",
                EndSessionEndpoint = "https://auth.test/connect/logout",
                Issuer = "https://auth.test/"
            }));
    }
}

internal sealed class AuthenticatedLifeApplicationFactory : LifeApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services => services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = TestAuthHandler.SchemeName;
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<LifeDbContext>();
            services.AddScoped(_ => new LifeDbContext(
                new DbContextOptionsBuilder<LifeDbContext>()
                    .UseInMemoryDatabase("life-http-tests")
                    .Options));
        });
    }
}

internal sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "LifeTestAuth";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity([new Claim("sub", "life-test-user")], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
