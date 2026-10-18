using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Donysh.Auth.Configuration;
using Donysh.Auth.Data;
using Donysh.Auth.Domain;
using Donysh.Auth.Security;
using Donysh.Auth.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 256 * 1024);
builder.Services.Configure<FormOptions>(options =>
{
    options.ValueCountLimit = 64;
    options.ValueLengthLimit = 16 * 1024;
    options.MultipartBodyLengthLimit = 256 * 1024;
});

var reverseProxyEnabled = builder.Configuration.GetValue<bool>("ReverseProxy:Enabled");
var applyMigrations = builder.Configuration.GetValue("Database:ApplyMigrations", true);
var connectionString = builder.Configuration.GetConnectionString("AuthConnection")
    ?? throw new InvalidOperationException("Connection string 'AuthConnection' was not found.");

builder.Services.AddOptions<AuthFeatureOptions>()
    .Bind(builder.Configuration.GetSection(AuthFeatureOptions.SectionName));
builder.Services.AddOptions<EmailOptions>()
    .Bind(builder.Configuration.GetSection(EmailOptions.SectionName));

builder.Services.AddDbContext<AuthDbContext>(options =>
{
    options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null));
    options.UseOpenIddict();
});

builder.Services
    .AddIdentity<AuthUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedEmail = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<AuthDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = ".Donysh.Auth.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = reverseProxyEnabled || !builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.Always
        : CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
});
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
    options.ValidationInterval = TimeSpan.FromMinutes(5));

builder.Services.AddOpenIddict()
    .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<AuthDbContext>())
    .AddServer(options =>
    {
        options.SetAuthorizationEndpointUris("connect/authorize")
            .SetEndSessionEndpointUris("connect/logout")
            .SetTokenEndpointUris("connect/token")
            .SetUserInfoEndpointUris("connect/userinfo");
        options.AllowAuthorizationCodeFlow();
        options.RequireProofKeyForCodeExchange();
        options.Configure(server =>
        {
            server.CodeChallengeMethods.Clear();
            server.CodeChallengeMethods.Add(OpenIddictConstants.CodeChallengeMethods.Sha256);
        });
        options.RegisterScopes(
            OpenIddictConstants.Scopes.OpenId,
            OpenIddictConstants.Scopes.Profile,
            OpenIddictConstants.Scopes.Email,
            OpenIddictConstants.Scopes.Roles);
        options.SetAuthorizationCodeLifetime(TimeSpan.FromMinutes(3));
        options.SetAccessTokenLifetime(TimeSpan.FromMinutes(10));
        options.SetIdentityTokenLifetime(TimeSpan.FromMinutes(10));

        if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
        {
            options.AddDevelopmentEncryptionCertificate()
                .AddDevelopmentSigningCertificate();
        }
        else
        {
            X509Certificate2 encryption = OidcCertificateLoader.Load(builder.Configuration, "Encryption");
            X509Certificate2 signing = OidcCertificateLoader.Load(builder.Configuration, "Signing");
            options.AddEncryptionCertificate(encryption).AddSigningCertificate(signing);
        }

        options.UseAspNetCore()
            .EnableAuthorizationEndpointPassthrough()
            .EnableEndSessionEndpointPassthrough()
            .EnableUserInfoEndpointPassthrough()
            .EnableStatusCodePagesIntegration();
    });

var dataProtection = builder.Services.AddDataProtection().SetApplicationName("Donysh.Auth");
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
{
    Directory.CreateDirectory(keysPath);
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
}

if (reverseProxyEnabled)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
            | ForwardedHeaders.XForwardedProto
            | ForwardedHeaders.XForwardedHost;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        options.ForwardLimit = 1;
    });
    builder.Services.AddHsts(options =>
    {
        options.MaxAge = TimeSpan.FromDays(365);
        options.IncludeSubDomains = false;
        options.Preload = false;
    });
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        return ValueTask.CompletedTask;
    };
    options.AddPolicy("account", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

builder.Services.AddScoped<AuthDatabaseMigrator>();
builder.Services.AddScoped<AuthClientSeeder>();
builder.Services.AddSingleton<IAuthEmailSender, SmtpAuthEmailSender>();
builder.Services.AddControllers();
builder.Services.AddRazorPages();

var app = builder.Build();

if (reverseProxyEnabled) app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    if (reverseProxyEnabled) app.UseHsts();
}

app.UseAuthSecurityHeaders();
app.Use(AuthenticationPrefetchGuard.InvokeAsync);
app.UseStaticFiles();
app.UseRequestLocalization(new RequestLocalizationOptions()
    .SetDefaultCulture("fa-IR")
    .AddSupportedCultures("fa-IR", "en-US")
    .AddSupportedUICultures("fa-IR", "en-US"));
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapRazorPages();

app.MapGet("/health/live", () => Results.Ok(new { status = "ok", service = "donysh-auth" }))
    .AllowAnonymous();
app.MapGet("/health/ready", async (AuthDbContext db, CancellationToken cancellationToken) =>
    await db.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok(new { status = "ready", service = "donysh-auth" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable))
    .AllowAnonymous();

if (applyMigrations)
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AuthDatabaseMigrator>().MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<AuthClientSeeder>().SeedAsync();
}

app.Run();

public partial class AuthProgram;
