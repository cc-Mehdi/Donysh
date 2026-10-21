using System.Net;
using System.Text;
using HesabYar.Web.Data;
using HesabYar.Web.Configuration;
using HesabYar.Web.Domain;
using HesabYar.Web.Helpers;
using HesabYar.Web.Pages.Expenses;
using HesabYar.Web.Sms;
using HesabYar.Web.Security;
using HesabYar.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var partialSummary = RecurringObligationHelper.SummarizePayments(2_000_000, [500_000, 500_000]);
if (!partialSummary.IsPartiallyPaid || partialSummary.IsPaid || partialSummary.RemainingAmount != 1_000_000 || partialSummary.ProgressPercent != 50)
    throw new Exception("Partial obligation payments must preserve the remaining monthly amount.");
Console.WriteLine("PASS Partial obligation payments preserve the remaining amount");
var completeSummary = RecurringObligationHelper.SummarizePayments(2_000_000, [500_000, 1_500_000]);
if (!completeSummary.IsPaid || completeSummary.IsPartiallyPaid || completeSummary.RemainingAmount != 0 || completeSummary.ProgressPercent != 100)
    throw new Exception("Multiple obligation payments must settle the monthly amount together.");
Console.WriteLine("PASS Multiple obligation payments settle the monthly amount together");
var overpaidSummary = RecurringObligationHelper.SummarizePayments(2_000_000, [2_500_000]);
if (!overpaidSummary.IsPaid || !overpaidSummary.IsOverpaid || overpaidSummary.RemainingAmount != 0 || overpaidSummary.ProgressPercent != 100)
    throw new Exception("Variable monthly expenses must expose over-estimate payments safely.");
Console.WriteLine("PASS Variable monthly expenses expose over-estimate payments");

// The list and member summary must share every user-selected filter.
var selectedCategory = Guid.NewGuid();
var otherCategory = Guid.NewGuid();
var selectedDate = new DateOnly(2026, 9, 19);
var expenses = new[]
{
    new Expense { Reason = "خرید روزانه", CategoryId = selectedCategory, ExpenseDate = selectedDate, NeedsReview = true, Amount = 10 },
    new Expense { Reason = "خرید روزانه", CategoryId = selectedCategory, ExpenseDate = selectedDate.AddDays(-1), NeedsReview = true, Amount = 20 },
    new Expense { Reason = "خرید روزانه", CategoryId = otherCategory, ExpenseDate = selectedDate, NeedsReview = true, Amount = 30 },
    new Expense { Reason = "خرید روزانه", CategoryId = selectedCategory, ExpenseDate = selectedDate, NeedsReview = false, Amount = 40 },
    new Expense { Reason = "هزینه دیگر", CategoryId = selectedCategory, ExpenseDate = selectedDate, NeedsReview = true, Amount = 50 }
}.AsQueryable();
var filtered = ExpenseListFilter.Apply(expenses, "  خرید  ", selectedCategory, true, selectedDate, selectedDate).ToList();
if (filtered.Count != 1 || filtered[0].Amount != 10)
    throw new Exception("Expense filters must apply equally to member summaries.");
Console.WriteLine("PASS Member expenses respect search, category, review and date filters");
if (!ExpenseListFilter.HasAny("خرید", null, false, null, null)
    || ExpenseListFilter.IncludeMember(0, true)
    || !ExpenseListFilter.IncludeMember(1, true)
    || !ExpenseListFilter.IncludeMember(0, false))
    throw new Exception("Filtered member summary must hide members without matching expenses.");
Console.WriteLine("PASS Filtered member summary hides members without matching expenses");

var invitationToken = InvitationToken.Create();
var invitationTokenHash = InvitationToken.Hash(invitationToken);
if (invitationToken.Length != 64 || invitationTokenHash.Length != 64
    || invitationToken == invitationTokenHash
    || InvitationToken.Hash(invitationToken) != invitationTokenHash)
    throw new Exception("Invitation tokens must be random and stored as deterministic one-way digests.");
Console.WriteLine("PASS Invitation tokens are stored as one-way digests");
var privateSms = Donysh.Sms.SmsPrivacy.RemoveBalance("برداشت 1,000,000\nموجودی: 99,000,000 ریال");
if (privateSms.Contains("موجودی", StringComparison.Ordinal) || privateSms.Contains("99,000,000", StringComparison.Ordinal))
    throw new Exception("Balance sanitizer must remove account balances.");
Console.WriteLine("PASS Account balances are removed before expense storage");
if (new CentralAuthOptions { Enabled = true }.FinanceSessionCookieName == new CentralAuthOptions { Enabled = false }.FinanceSessionCookieName)
    throw new Exception("Central authentication must rotate the Finance session cookie name.");
Console.WriteLine("PASS Central authentication cannot reuse legacy Finance session cookies");
var financeProgramSource = await File.ReadAllTextAsync(Path.Combine(
    AppContext.BaseDirectory, "..", "..", "..", "..", "HesabYar.Web", "Program.cs"));
if (!financeProgramSource.Contains("GetClaimsFromUserInfoEndpoint = false", StringComparison.Ordinal))
    throw new Exception("Finance login must avoid the redundant UserInfo network request.");
Console.WriteLine("PASS Finance login avoids a redundant UserInfo network request");
if (!string.Equals(CentralAuthOptions.SecureResponseMode, "query", StringComparison.Ordinal))
    throw new Exception("Central authentication must not require an inline form_post script.");
Console.WriteLine("PASS Central authentication callback preserves strict CSP without inline scripts");
if (!HesabYar.Web.Pages.Account.LoginModel.HasAuthenticationError("1")
    || !HesabYar.Web.Pages.Account.LoginModel.HasAuthenticationError("true")
    || HesabYar.Web.Pages.Account.LoginModel.HasAuthenticationError(null))
    throw new Exception("Finance login must stop automatic OIDC retries after a callback failure.");
Console.WriteLine("PASS Finance login stops automatic OIDC retries after a callback failure");
var financeCsp = SecurityHeaders.BuildContentSecurityPolicy(new Uri("https://auth.donysh.test/connect/authorize"));
if (!financeCsp.Contains("form-action 'self' https://auth.donysh.test", StringComparison.Ordinal)
    || financeCsp.Contains("connect/authorize", StringComparison.Ordinal))
    throw new Exception("Finance CSP must allow only the trusted central Auth origin for OIDC form redirects.");
Console.WriteLine("PASS Finance CSP allows the trusted central Auth origin");
const string financeLogoutSecret = "test-only-shared-logout-secret-0000000000000000";
var financeLogoutConfiguration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Products:LifeUrl"] = "https://life.test",
        ["CrossAppLogout:Secret"] = financeLogoutSecret
    }).Build();
var financeContinueToken = CrossAppLogoutToken.Create(financeLogoutSecret, "finance-continue");
var financeLogout = new HesabYar.Web.Pages.Account.LogoutModel(
    Options.Create(new CentralAuthOptions { Enabled = true }), financeLogoutConfiguration)
    .OnGetComplete(financeContinueToken) as Microsoft.AspNetCore.Mvc.SignOutResult;
if (financeLogout is null
    || !financeLogout.AuthenticationSchemes.Contains(Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme)
    || !financeLogout.AuthenticationSchemes.Contains(CentralAuthOptions.Scheme)
    || financeLogout.Properties?.RedirectUri?.StartsWith("https://life.test/Account/FederatedLogout?proof=", StringComparison.Ordinal) != true)
    throw new Exception("Finance logout must clear both the product and central sessions.");
Console.WriteLine("PASS Finance logout clears both the product and central sessions");
var financePeerProof = new Uri(financeLogout.Properties!.RedirectUri!).Query["?proof=".Length..];
if (!CrossAppLogoutToken.TryValidate(financePeerProof, financeLogoutSecret, "life")
    || CrossAppLogoutToken.TryValidate(financePeerProof, financeLogoutSecret, "finance"))
    throw new Exception("Finance logout must create a signed proof scoped to Life.");
Console.WriteLine("PASS Finance logout proof is short-lived and scoped to Life");
var financePrefetch = new DefaultHttpContext();
financePrefetch.Request.Method = HttpMethods.Get;
financePrefetch.Request.Path = "/Account/Login";
financePrefetch.Request.Headers["Sec-Purpose"] = "prefetch;prerender";
if (!AuthenticationPrefetchGuard.ShouldSkip(financePrefetch.Request))
    throw new Exception("Finance authentication must ignore speculative browser navigation.");
financePrefetch.Request.Method = HttpMethods.Post;
if (AuthenticationPrefetchGuard.ShouldSkip(financePrefetch.Request))
    throw new Exception("Finance authentication must never ignore an interactive form post.");
Console.WriteLine("PASS Finance authentication ignores only speculative browser navigation");

// Starts only a loopback test host. The unavailable database deliberately proves guards do not query it.
var builder = WebApplication.CreateBuilder();
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql("Host=127.0.0.1;Port=1;Database=donysh_unused;Username=unused;Timeout=1"));
builder.Services.AddMobileApi();
await using var app = builder.Build();
app.UseDonyshSecurityHeaders();
app.UseRouting();
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Headers.TryGetValue("Test-Remote-Ip", out var value)
        && IPAddress.TryParse(value, out var address))
        ctx.Connection.RemoteIpAddress = address;
    await next();
});
app.UseRateLimiter();
app.Use(async (ctx, next) => { if (ctx.Request.Headers.ContainsKey("Test-Https")) ctx.Request.Scheme = "https"; await next(); });
app.MapMobileApi();
app.MapGet("/ordinary-page", () => Results.Ok());
app.MapGet("/account-test", () => Results.Ok()).RequireRateLimiting("account");
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
await Check("Mobile login requires HTTPS", "/api/mobile/login",
    "{\"email\":\"user@example.com\",\"password\":\"Secret123\",\"installationId\":\"" + new string('A', 64) + "\",\"deviceName\":\"phone\"}",
    HttpStatusCode.BadRequest, false);
await Check("Malformed mobile login rejected before authentication", "/api/mobile/login", "{}", HttpStatusCode.BadRequest);
await Check("SMS requires HTTPS", "/api/mobile/sms", "{}", HttpStatusCode.BadRequest, false);
await Check("SMS requires device token, cookie is not enough", "/api/mobile/sms", "{}", HttpStatusCode.Unauthorized);
await Check("Short device token rejected without database work", "/api/mobile/sms", "{}", HttpStatusCode.Unauthorized, token: "Bearer invalid");
await Check("Non-hex device token rejected without database work", "/api/mobile/sms", "{}", HttpStatusCode.Unauthorized, token: "Bearer " + new string('Z', 64));
await Check("Pairing missing code", "/api/mobile/pair", "{}", HttpStatusCode.BadRequest);
await Check("Pairing null payload", "/api/mobile/pair", "null", HttpStatusCode.BadRequest);
await Check("Non-hex pairing code rejected without database work", "/api/mobile/pair", "{\"code\":\"" + new string('Z', 32) + "\"}", HttpStatusCode.BadRequest);
await Check("Malformed JSON", "/api/mobile/pair", "{", HttpStatusCode.BadRequest);
using (var workspacesWithoutToken = new HttpRequestMessage(HttpMethod.Get, "/api/mobile/workspaces"))
{
    workspacesWithoutToken.Headers.Add("Test-Https", "1");
    using var response = await client.SendAsync(workspacesWithoutToken);
    var guarded = response.StatusCode == HttpStatusCode.Unauthorized;
    if (!guarded) failed++;
    Console.WriteLine($"{(guarded ? "PASS" : "FAIL")} Workspace list requires device token");
}
await Check("Oversized pairing request", "/api/mobile/pair", "{\"code\":\"" + new string('a', 33000) + "\"}", HttpStatusCode.BadRequest);
using (var chunked = new HttpRequestMessage(HttpMethod.Post, "/api/mobile/pair")
       { Content = new StringContent("{\"code\":\"" + new string('a', 33000) + "\"}", Encoding.UTF8, "application/json") })
{
    chunked.Headers.Add("Test-Https", "1");
    chunked.Headers.TransferEncodingChunked = true;
    using var oversizedChunked = await client.SendAsync(chunked);
    var bounded = oversizedChunked.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge;
    if (!bounded) failed++;
    Console.WriteLine($"{(bounded ? "PASS" : "FAIL")} Chunked body cap rejects without a server error");
}
bool limited = false;
for (int i = 0; i < 61; i++) {
    using var response = await client.PostAsync("/api/mobile/sms", new StringContent("{}"));
    if (response.StatusCode == HttpStatusCode.TooManyRequests) {
        limited = response.Headers.RetryAfter is not null; break;
    }
}
if (!limited) failed++;
Console.WriteLine($"{(limited ? "PASS" : "FAIL")} API flood receives 429 and Retry-After");
using (var otherClientRequest = new HttpRequestMessage(HttpMethod.Post, "/api/mobile/sms")
       { Content = new StringContent("{}", Encoding.UTF8, "application/json") })
{
    otherClientRequest.Headers.Add("Test-Remote-Ip", "127.0.0.2");
    using var otherClientResponse = await client.SendAsync(otherClientRequest);
    var isolated = otherClientResponse.StatusCode != HttpStatusCode.TooManyRequests;
    if (!isolated) failed++;
    Console.WriteLine($"{(isolated ? "PASS" : "FAIL")} One mobile client cannot exhaust another client's rate limit");
}
using var web = await client.GetAsync("/ordinary-page");
if (web.StatusCode != HttpStatusCode.OK) failed++;
Console.WriteLine($"{(web.StatusCode == HttpStatusCode.OK ? "PASS" : "FAIL")} Mobile limits do not block normal web pages");
var csp = web.Headers.GetValues("Content-Security-Policy").Single();
var hardened = web.Headers.GetValues("X-Frame-Options").Single() == "DENY"
    && csp.Contains("script-src 'self'", StringComparison.Ordinal)
    && !csp.Contains("unsafe-eval", StringComparison.Ordinal);
if (!hardened) failed++;
Console.WriteLine($"{(hardened ? "PASS" : "FAIL")} Browser security headers block framing and string evaluation");
bool accountLimited = false;
for (int i = 0; i < 31; i++)
{
    using var response = await client.GetAsync("/account-test");
    if (response.StatusCode == HttpStatusCode.TooManyRequests) { accountLimited = true; break; }
}
if (!accountLimited) failed++;
Console.WriteLine($"{(accountLimited ? "PASS" : "FAIL")} Account endpoints are rate limited per client");
using (var otherAccountRequest = new HttpRequestMessage(HttpMethod.Get, "/account-test"))
{
    otherAccountRequest.Headers.Add("Test-Remote-Ip", "127.0.0.3");
    using var otherAccountResponse = await client.SendAsync(otherAccountRequest);
    var isolated = otherAccountResponse.StatusCode == HttpStatusCode.OK;
    if (!isolated) failed++;
    Console.WriteLine($"{(isolated ? "PASS" : "FAIL")} One login client cannot exhaust another client's rate limit");
}
await app.StopAsync();
var postgres = Environment.GetEnvironmentVariable("DONYSH_TEST_POSTGRES");
if (string.IsNullOrWhiteSpace(postgres)) Console.WriteLine("SKIP PostgreSQL checks: set DONYSH_TEST_POSTGRES to a local disposable PostgreSQL server with CREATE DATABASE privilege.");
else await PostgresChecks.RunAsync(postgres);
return failed == 0 ? 0 : 1;
