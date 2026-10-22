using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

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

await using var factory = new LandingApplicationFactory();
using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
{
    AllowAutoRedirect = false,
    BaseAddress = new Uri("https://donysh.test")
});

using (var response = await client.GetAsync("/"))
{
    var html = await response.Content.ReadAsStringAsync();
    Check("Landing renders in Persian RTL", response.StatusCode == HttpStatusCode.OK
        && html.Contains("lang=\"fa\"", StringComparison.Ordinal)
        && html.Contains("dir=\"rtl\"", StringComparison.Ordinal));
    Check("Landing links to the dedicated Finance host",
        html.Contains("https://finance.test", StringComparison.Ordinal)
        && html.Contains("ورود به دُنیش مالی", StringComparison.Ordinal));
    Check("Landing account links start a complete Finance sign-in flow",
        html.Contains("href=\"https://finance.test/Account/Login\"", StringComparison.Ordinal)
        && !html.Contains("href=\"https://auth.test/Account/Login\"", StringComparison.Ordinal));
    Check("Landing account actions make sense before and after sign-in",
        html.Contains("ادامه با حساب دُنیش", StringComparison.Ordinal)
        && !html.Contains("ورود به حساب", StringComparison.Ordinal));
    Check("Landing warms both authentication hops before sign-in",
        html.Contains("rel=\"preconnect\" href=\"https://finance.test\"", StringComparison.Ordinal)
        && html.Contains("rel=\"preconnect\" href=\"https://auth.test\"", StringComparison.Ordinal));
    Check("Landing links to the initial Life product",
        html.Contains("دُنیش زندگی", StringComparison.Ordinal)
        && html.Contains("نسخهٔ آغازین", StringComparison.Ordinal)
        && html.Contains("href=\"https://life.test\"", StringComparison.Ordinal));
    Check("Landing exposes the fixed Android download URL",
        html.Contains("دانلود برنامهٔ اندروید", StringComparison.Ordinal)
        && html.Contains("href=\"/downloads/donysh-mobile.apk\"", StringComparison.Ordinal));
    Check("Landing separates the approved Finance logo from the mother wordmark",
        html.Contains("/landing-assets/images/donysh-mark.png", StringComparison.Ordinal)
        && html.Contains("/landing-assets/images/donysh-wordmark.png", StringComparison.Ordinal));
    Check("Landing versions both brand images for browser caches",
        html.Contains("/landing-assets/images/donysh-mark.png?v=", StringComparison.Ordinal)
        && html.Contains("/landing-assets/images/donysh-wordmark.png?v=", StringComparison.Ordinal));
    Check("Landing cache-busts its stylesheet",
        html.Contains("href=\"/landing-assets/css/app.css?v=", StringComparison.Ordinal));
    Check("Landing security headers block scripts and framing",
        response.Headers.TryGetValues("Content-Security-Policy", out var csp)
        && csp.Single().Contains("script-src 'none'", StringComparison.Ordinal)
        && csp.Single().Contains("frame-ancestors 'none'", StringComparison.Ordinal)
        && response.Headers.TryGetValues("X-Frame-Options", out var frame)
        && frame.Single() == "DENY");
}

using (var response = await client.GetAsync("/health"))
{
    using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    Check("Landing health check succeeds",
        response.StatusCode == HttpStatusCode.OK
        && payload.RootElement.GetProperty("status").GetString() == "ok"
        && payload.RootElement.GetProperty("service").GetString() == "donysh-landing");
}

using (var response = await client.GetAsync("/landing-assets/css/app.css"))
{
    Check("Landing stylesheet is served", response.StatusCode == HttpStatusCode.OK
        && response.Content.Headers.ContentType?.MediaType == "text/css");
    Check("Unversioned landing assets must revalidate",
        response.Headers.CacheControl?.NoCache == true
        && response.Headers.CacheControl?.MustRevalidate == true);
}

using (var response = await client.GetAsync("/landing-assets/css/app.css?v=test-version"))
{
    Check("Versioned landing assets are immutable",
        response.StatusCode == HttpStatusCode.OK
        && response.Headers.CacheControl?.Public == true
        && response.Headers.CacheControl?.MaxAge == TimeSpan.FromDays(365)
        && response.Headers.CacheControl?.Extensions.Any(extension =>
            extension.Name.Equals("immutable", StringComparison.OrdinalIgnoreCase)) == true);
}

using (var response = await client.GetAsync("/landing-assets/images/donysh-mark.png"))
{
    var hash = Convert.ToHexString(SHA256.HashData(await response.Content.ReadAsByteArrayAsync()));
    Check("Landing approved Finance brand asset is served", response.StatusCode == HttpStatusCode.OK
        && response.Content.Headers.ContentType?.MediaType == "image/png"
        && hash == "4599AAE7CC8BD382E2BC3F4591FA58F6EF3FA6B6FE075EE894C3FA9B5C556E85");
}

using (var response = await client.GetAsync("/landing-assets/images/donysh-wordmark.png"))
{
    var hash = Convert.ToHexString(SHA256.HashData(await response.Content.ReadAsByteArrayAsync()));
    Check("Landing mother brand wordmark is served independently", response.StatusCode == HttpStatusCode.OK
        && response.Content.Headers.ContentType?.MediaType == "image/png"
        && hash == "38AEC9AA1984B2B03DFF58FEB25125C41D370D427D57EB6B25413404B8747B6F");
}

using (var response = await client.GetAsync("/landing-assets/images/donysh-life-logo.png"))
{
    Check("Landing serves the Donysh Life brand asset", response.StatusCode == HttpStatusCode.OK
        && response.Content.Headers.ContentType?.MediaType == "image/png");
}

using (var response = await client.GetAsync("/landing-assets/fonts/YekanBakh-Regular.woff2"))
{
    Check("Landing Persian font is served", response.StatusCode == HttpStatusCode.OK
        && response.Content.Headers.ContentType?.MediaType == "font/woff2");
}

if (failed > 0) throw new Exception($"{failed} Landing checks failed.");
Console.WriteLine("All Donysh Landing checks passed.");

internal sealed class LandingApplicationFactory : WebApplicationFactory<global::LandingProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "*",
                ["ReverseProxy:Enabled"] = "false",
                ["Products:FinanceUrl"] = "https://finance.test",
                ["Products:AuthUrl"] = "https://auth.test",
                ["Products:LifeUrl"] = "https://life.test"
            }));
    }
}
