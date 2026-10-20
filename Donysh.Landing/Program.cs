using Donysh.Landing.Configuration;
using Donysh.Landing.Infrastructure;
using Donysh.Landing.Security;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

var reverseProxyEnabled = builder.Configuration.GetValue<bool>("ReverseProxy:Enabled");
var productLinks = builder.Configuration
    .GetSection(LandingLinksOptions.SectionName)
    .Get<LandingLinksOptions>() ?? new LandingLinksOptions();

foreach (var (name, value) in new[]
{
    (nameof(productLinks.FinanceUrl), productLinks.FinanceUrl),
    (nameof(productLinks.AuthUrl), productLinks.AuthUrl),
    (nameof(productLinks.LifeUrl), productLinks.LifeUrl)
})
{
    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
        || (!builder.Environment.IsDevelopment() && uri.Scheme != Uri.UriSchemeHttps))
    {
        throw new InvalidOperationException($"Products:{name} must be an absolute HTTPS URL.");
    }
}

builder.Services.Configure<LandingLinksOptions>(
    builder.Configuration.GetSection(LandingLinksOptions.SectionName));
builder.Services.AddRazorPages();
builder.Services.AddSingleton<LandingAssetUrlBuilder>();

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

var app = builder.Build();

if (reverseProxyEnabled) app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    if (reverseProxyEnabled) app.UseHsts();
}

app.UseLandingSecurityHeaders();

var sharedWebRoot = Path.GetFullPath(Path.Combine(
    app.Environment.ContentRootPath, "..", "HesabYar.Web", "wwwroot"));
if (Directory.Exists(sharedWebRoot))
{
    // Linked content is copied into wwwroot when published. During local `dotnet run`,
    // serve the same source assets directly so local and container rendering match.
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(Path.Combine(sharedWebRoot, "images")),
        RequestPath = "/landing-assets/images",
        OnPrepareResponse = context =>
            context.Context.Response.Headers.CacheControl = "public,max-age=3600"
    });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(Path.Combine(sharedWebRoot, "fonts")),
        RequestPath = "/landing-assets/fonts",
        OnPrepareResponse = context =>
            context.Context.Response.Headers.CacheControl = "public,max-age=3600"
    });
}

app.UseStaticFiles(new StaticFileOptions
{
    RequestPath = "/landing-assets",
    OnPrepareResponse = context =>
    {
        // Versioned references can be cached permanently, while an unversioned
        // request must revalidate so a previously cached stylesheet can never
        // outlive a deployment.
        context.Context.Response.Headers.CacheControl =
            context.Context.Request.Query.ContainsKey("v")
                ? "public,max-age=31536000,immutable"
                : "public,no-cache,must-revalidate";
    }
});
app.UseRouting();
app.MapRazorPages();
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "donysh-landing" }));

app.Run();

public partial class LandingProgram;
