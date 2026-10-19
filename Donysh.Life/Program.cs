using System.Security.Claims;
using Donysh.Life.Configuration;
using Donysh.Life.Data;
using Donysh.Life.Features.Activities;
using Donysh.Life.Features.Scheduling;
using Donysh.Life.Features.Tracking;
using Donysh.Life.Features.Reporting;
using Donysh.Life.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var reverseProxyEnabled = builder.Configuration.GetValue<bool>("ReverseProxy:Enabled");
var applyMigrations = builder.Configuration.GetValue<bool>("Database:ApplyMigrations");
var connectionString = builder.Configuration.GetConnectionString("LifeConnection")
    ?? throw new InvalidOperationException("Connection string 'LifeConnection' was not found.");
var auth = builder.Configuration.GetSection(LifeAuthOptions.SectionName).Get<LifeAuthOptions>()
    ?? new LifeAuthOptions();

if (!Uri.TryCreate(auth.Authority, UriKind.Absolute, out var authority)
    || (auth.RequireHttpsMetadata && authority.Scheme != Uri.UriSchemeHttps))
    throw new InvalidOperationException("CentralAuth:Authority must be an absolute HTTPS URI.");
if (string.IsNullOrWhiteSpace(auth.ClientId)
    || string.IsNullOrWhiteSpace(auth.ClientSecret)
    || auth.ClientSecret.Length < 32)
    throw new InvalidOperationException("Life requires an OIDC client ID and a secret of at least 32 characters.");

builder.Services.AddDbContext<LifeDbContext>(options => options.UseNpgsql(connectionString, npgsql =>
    npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null)));
builder.Services.AddScoped<LifeDatabaseMigrator>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<LifeActivityService>();
builder.Services.AddScoped<WeeklyScheduleService>();
builder.Services.AddScoped<OccurrenceTrackingService>();
builder.Services.AddScoped<ActualActivityService>();
builder.Services.AddScoped<ProgressReportService>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = LifeAuthOptions.CookieScheme;
        options.DefaultAuthenticateScheme = LifeAuthOptions.CookieScheme;
        options.DefaultSignInScheme = LifeAuthOptions.CookieScheme;
        options.DefaultChallengeScheme = LifeAuthOptions.OidcScheme;
    })
    .AddCookie(LifeAuthOptions.CookieScheme, options =>
    {
        options.Cookie.Name = ".Donysh.Life.Session";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = reverseProxyEnabled ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
    })
    .AddOpenIdConnect(LifeAuthOptions.OidcScheme, options =>
    {
        options.Authority = authority.AbsoluteUri.TrimEnd('/');
        options.ClientId = auth.ClientId;
        options.ClientSecret = auth.ClientSecret;
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.ResponseMode = "query";
        options.UsePkce = true;
        options.RequireHttpsMetadata = auth.RequireHttpsMetadata;
        options.SignInScheme = LifeAuthOptions.CookieScheme;
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.MapInboundClaims = false;
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
        options.NonceCookie.SameSite = SameSiteMode.Lax;
        options.NonceCookie.SecurePolicy = CookieSecurePolicy.Always;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = "name",
            RoleClaimType = "role"
        };
        options.Events = new OpenIdConnectEvents
        {
            OnTicketReceived = context =>
            {
                var idToken = context.Properties?.GetTokenValue(OpenIdConnectParameterNames.IdToken);
                context.Properties?.StoreTokens(string.IsNullOrWhiteSpace(idToken)
                    ? []
                    : [new AuthenticationToken { Name = OpenIdConnectParameterNames.IdToken, Value = idToken }]);
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                if (string.IsNullOrWhiteSpace(context.Principal?.FindFirstValue("sub")))
                    context.Fail("The identity token has no subject.");
                return Task.CompletedTask;
            },
            OnRemoteFailure = context =>
            {
                context.HandleResponse();
                context.Response.Redirect("/Account/Login?authError=1");
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToFolder("/Account");
    options.Conventions.AllowAnonymousToPage("/Error");
});

var dataProtection = builder.Services.AddDataProtection().SetApplicationName("Donysh.Life");
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
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        options.ForwardLimit = 1;
    });
    builder.Services.AddHsts(options => options.MaxAge = TimeSpan.FromDays(365));
}

var app = builder.Build();
if (reverseProxyEnabled) app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    if (reverseProxyEnabled) app.UseHsts();
}
app.UseLifeSecurityHeaders(authority);
var sharedWebRoot = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "HesabYar.Web", "wwwroot"));
if (Directory.Exists(sharedWebRoot))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(Path.Combine(sharedWebRoot, "images")),
        RequestPath = "/images"
    });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(Path.Combine(sharedWebRoot, "fonts")),
        RequestPath = "/fonts"
    });
}
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        if (context.User.Identity?.IsAuthenticated == true)
            context.Response.Headers.CacheControl = "no-store, no-cache";
        return Task.CompletedTask;
    });
    await next();
});
app.UseAuthorization();
app.MapRazorPages();
app.MapGet("/health/live", () => Results.Ok(new { status = "live", service = "donysh-life" })).AllowAnonymous();
app.MapGet("/health/ready", async (LifeDbContext db, CancellationToken cancellationToken) =>
    await db.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok(new { status = "ready", service = "donysh-life" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable)).AllowAnonymous();

if (applyMigrations)
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<LifeDatabaseMigrator>().MigrateAsync();
}

app.Run();

public partial class LifeProgram;
