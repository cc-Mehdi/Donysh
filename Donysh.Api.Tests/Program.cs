using System.Net;
using System.Text;
using HesabYar.Web.Data;
using HesabYar.Web.Sms;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Starts only a loopback test host. The unavailable database deliberately proves guards do not query it.
var builder = WebApplication.CreateBuilder();
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql("Host=127.0.0.1;Port=1;Database=donysh_unused;Username=unused;Timeout=1"));
builder.Services.AddMobileApi();
await using var app = builder.Build();
app.UseRouting();
app.UseRateLimiter();
app.Use(async (ctx, next) => { if (ctx.Request.Headers.ContainsKey("Test-Https")) ctx.Request.Scheme = "https"; await next(); });
app.MapMobileApi();
app.MapGet("/ordinary-page", () => Results.Ok());
await app.StartAsync();
var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
using var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(5) };
int failed = 0;
async Task Check(string name, string path, string json, HttpStatusCode expected, bool https = true, string? token = null)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    if (https) request.Headers.Add("Test-Https", "1");
    if (token is not null) request.Headers.Add("Authorization", token);
    using var response = await client.SendAsync(request);
    if (response.StatusCode != expected) { failed++; Console.WriteLine($"FAIL {name}: {response.StatusCode}"); }
    else Console.WriteLine($"PASS {name}");
}
await Check("Pairing requires HTTPS", "/api/mobile/pair", "{}", HttpStatusCode.BadRequest, false);
await Check("SMS requires HTTPS", "/api/mobile/sms", "{}", HttpStatusCode.BadRequest, false);
await Check("SMS requires device token, cookie is not enough", "/api/mobile/sms", "{}", HttpStatusCode.Unauthorized);
await Check("Short device token rejected without database work", "/api/mobile/sms", "{}", HttpStatusCode.Unauthorized, token: "Bearer invalid");
await Check("Pairing missing code", "/api/mobile/pair", "{}", HttpStatusCode.BadRequest);
await Check("Pairing null payload", "/api/mobile/pair", "null", HttpStatusCode.BadRequest);
await Check("Malformed JSON", "/api/mobile/pair", "{", HttpStatusCode.BadRequest);
await Check("Oversized pairing request", "/api/mobile/pair", "{\"code\":\"" + new string('a', 33000) + "\"}", HttpStatusCode.BadRequest);
bool limited = false;
for (int i = 0; i < 61; i++) {
    using var response = await client.PostAsync("/api/mobile/sms", new StringContent("{}"));
    if (response.StatusCode == HttpStatusCode.TooManyRequests) {
        limited = response.Headers.RetryAfter is not null; break;
    }
}
if (!limited) failed++;
Console.WriteLine($"{(limited ? "PASS" : "FAIL")} API flood receives 429 and Retry-After");
using var web = await client.GetAsync("/ordinary-page");
if (web.StatusCode != HttpStatusCode.OK) failed++;
Console.WriteLine($"{(web.StatusCode == HttpStatusCode.OK ? "PASS" : "FAIL")} Mobile limits do not block normal web pages");
await app.StopAsync();
var postgres = Environment.GetEnvironmentVariable("DONYSH_TEST_POSTGRES");
if (string.IsNullOrWhiteSpace(postgres)) Console.WriteLine("SKIP PostgreSQL checks: set DONYSH_TEST_POSTGRES to a local disposable PostgreSQL server with CREATE DATABASE privilege.");
else await PostgresChecks.RunAsync(postgres);
return failed == 0 ? 0 : 1;
