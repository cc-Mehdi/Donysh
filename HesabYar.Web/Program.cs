using System.Globalization;
using HesabYar.Web.Data;
using HesabYar.Web.Domain;
using HesabYar.Web.ModelBinding;
using HesabYar.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var reverseProxyEnabled = builder.Configuration.GetValue<bool>("ReverseProxy:Enabled");
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorCodesToAdd: null);
    }));

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = ".HesabYar.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = reverseProxyEnabled
        ? CookieSecurePolicy.Always
        : CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

if (reverseProxyEnabled)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor |
            ForwardedHeaders.XForwardedProto |
            ForwardedHeaders.XForwardedHost;

        // Only the reverse proxy can reach the web container in domain mode.
        options.KnownNetworks.Clear();
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

var keysPath = builder.Configuration["DataProtection:KeysPath"];
var dataProtection = builder.Services
    .AddDataProtection()
    .SetApplicationName("HesabYar");

if (!string.IsNullOrWhiteSpace(keysPath))
{
    Directory.CreateDirectory(keysPath);
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
}

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IWorkspaceContext, WorkspaceContext>();
builder.Services.AddScoped<AiWorkspaceService>();
builder.Services.AddScoped<DatabaseInitializer>();
builder.Services.AddScoped<BudgetRolloverService>();
builder.Services.AddScoped<BudgetBalanceService>();

builder.Services
    .AddRazorPages(options =>
    {
        options.Conventions.AuthorizeFolder("/");
        options.Conventions.AllowAnonymousToFolder("/Account");
        options.Conventions.AllowAnonymousToPage("/Error");
    })
    .AddMvcOptions(options =>
    {
        // Persian DateOnly values and comma-separated money values are parsed
        // before the default binders.
        options.ModelBinderProviders.Insert(0, new PersianDateOnlyModelBinderProvider());
        options.ModelBinderProviders.Insert(1, new FlexibleDecimalModelBinderProvider());
    });

var app = builder.Build();

if (reverseProxyEnabled)
{
    app.UseForwardedHeaders();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    if (reverseProxyEnabled)
    {
        app.UseHsts();
    }
}

app.UseStaticFiles();

var supportedCultures = new[] { new CultureInfo("fa-IR"), new CultureInfo("en-US") };
var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture("fa-IR")
    .AddSupportedCultures(supportedCultures.Select(c => c.Name).ToArray())
    .AddSupportedUICultures(supportedCultures.Select(c => c.Name).ToArray());
app.UseRequestLocalization(localizationOptions);

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapGet("/health", async (ApplicationDbContext db, CancellationToken ct) =>
{
    var healthy = await db.Database.CanConnectAsync(ct);
    return healthy
        ? Results.Ok(new { status = "ok" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
}).AllowAnonymous();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider
        .GetRequiredService<DatabaseInitializer>()
        .InitializeAsync();
}

app.Run();
