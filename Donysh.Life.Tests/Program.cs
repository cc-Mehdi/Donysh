using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Encodings.Web;
using Donysh.Life.Configuration;
using Donysh.Life.Data;
using Donysh.Life.Features.Activities;
using Donysh.Life.Features.AiPlanning;
using Donysh.Life.Features.Scheduling;
using Donysh.Life.Features.Tracking;
using Donysh.Life.Features.Reporting;
using Donysh.Life.Security;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
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
Environment.SetEnvironmentVariable("Products__FinanceUrl", "https://finance.test");
Environment.SetEnvironmentVariable("Products__AuthUrl", "https://auth.test");
Environment.SetEnvironmentVariable("CrossAppLogout__Secret", "test-only-shared-logout-secret-0000000000000000");
Environment.SetEnvironmentVariable("Database__ApplyMigrations", "false");
Environment.SetEnvironmentVariable("ReverseProxy__Enabled", "false");

Check("Life login recognizes central callback failure markers",
    Donysh.Life.Pages.Account.LoginModel.HasAuthenticationError("1")
    && Donysh.Life.Pages.Account.LoginModel.HasAuthenticationError("true")
    && !Donysh.Life.Pages.Account.LoginModel.HasAuthenticationError(null));

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
        && html.Contains("همان حسابی که در دُنیش مالی داری", StringComparison.Ordinal)
        && html.Contains("rel=\"icon\" type=\"image/png\" href=\"/images/donysh-life-logo.png\"", StringComparison.Ordinal)
        && html.Contains("rel=\"preload\"", StringComparison.Ordinal)
        && html.Contains("interaction-feedback", StringComparison.Ordinal));
    Check("Life security headers allow only same-origin scripts and block framing",
        response.Headers.TryGetValues("Content-Security-Policy", out var csp)
        && csp.Single().Contains("frame-ancestors 'none'", StringComparison.Ordinal)
        && csp.Single().Contains("form-action 'self' https://auth.test https://finance.test;", StringComparison.Ordinal)
        && csp.Single().Contains("script-src 'self'", StringComparison.Ordinal)
        && !csp.Single().Contains("'unsafe-inline'", StringComparison.Ordinal)
        && !csp.Single().Contains("'unsafe-eval'", StringComparison.Ordinal)
        && response.Headers.TryGetValues("X-Frame-Options", out var frame)
        && frame.Single() == "DENY"
        && response.Headers.TryGetValues("Cross-Origin-Resource-Policy", out var resourcePolicy)
        && resourcePolicy.Single() == "same-origin"
        && response.Headers.TryGetValues("X-Permitted-Cross-Domain-Policies", out var crossDomain)
        && crossDomain.Single() == "none");
}

using (var response = await client.GetAsync("/Account/SessionStatus"))
{
    Check("Anonymous Life session status is uncached and signed out",
        response.StatusCode == HttpStatusCode.Unauthorized
        && response.Headers.CacheControl?.NoStore == true);
}

using (var response = await client.GetAsync("/js/session-sync.js"))
{
    var script = await response.Content.ReadAsStringAsync();
    Check("Life session synchronization refreshes stale tabs",
        response.StatusCode == HttpStatusCode.OK
        && script.Contains("visibilitychange", StringComparison.Ordinal)
        && script.Contains("window.location.replace", StringComparison.Ordinal)
        && script.Contains("data-logout-form", StringComparison.Ordinal)
        && script.Contains("logoutInProgress = true", StringComparison.Ordinal)
        && script.Contains("!logoutInProgress && response.status === 401", StringComparison.Ordinal)
        && !script.Contains("watchPendingLogout", StringComparison.Ordinal));
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
        && response.Content.Headers.ContentType?.MediaType == "text/css"
        && response.Headers.CacheControl?.Public == true
        && response.Headers.CacheControl?.MaxAge == TimeSpan.FromDays(7));
}

using (var response = await client.GetAsync("/images/donysh-life-logo.png"))
{
    Check("Life brand logo is served", response.StatusCode == HttpStatusCode.OK
        && response.Content.Headers.ContentType?.MediaType == "image/png");
}

using (var response = await client.GetAsync("/js/week-planner.js"))
{
    var script = await response.Content.ReadAsStringAsync();
    Check("Life interactive planner script is served from the same origin",
        response.StatusCode == HttpStatusCode.OK
        && response.Content.Headers.ContentType?.MediaType is "text/javascript" or "application/javascript"
        && script.Contains("schedule-resize-handle", StringComparison.Ordinal)
        && script.Contains("pointerdown", StringComparison.Ordinal)
        && script.Contains("data-calendar-board", StringComparison.Ordinal)
        && !script.Contains("eval(", StringComparison.Ordinal)
        && !script.Contains("new Function", StringComparison.Ordinal));
}

using (var response = await client.GetAsync("/js/interaction-feedback.js"))
{
    var script = await response.Content.ReadAsStringAsync();
    Check("Life interaction feedback is same-origin and avoids string evaluation",
        response.StatusCode == HttpStatusCode.OK
        && script.Contains("aria-busy", StringComparison.Ordinal)
        && !script.Contains("eval(", StringComparison.Ordinal)
        && !script.Contains("new Function", StringComparison.Ordinal));
}

using (var response = await client.GetAsync("/js/setup-wizard.js"))
{
    var script = await response.Content.ReadAsStringAsync();
    Check("Life setup guidance is same-origin and updates without string evaluation",
        response.StatusCode == HttpStatusCode.OK
        && script.Contains("data-selected-count", StringComparison.Ordinal)
        && script.Contains("Intl.NumberFormat", StringComparison.Ordinal)
        && !script.Contains("eval(", StringComparison.Ordinal)
        && !script.Contains("new Function", StringComparison.Ordinal));
}

using (var request = new HttpRequestMessage(HttpMethod.Get, "/css/app.css"))
{
    request.Headers.AcceptEncoding.ParseAdd("gzip");
    using var response = await client.SendAsync(request);
    Check("Life compresses public assets when the client supports it",
        response.StatusCode == HttpStatusCode.OK
        && response.Content.Headers.ContentEncoding.Contains("gzip"));
}

using (var request = new HttpRequestMessage(HttpMethod.Get, "/Account/Login"))
{
    request.Headers.AcceptEncoding.ParseAdd("gzip");
    using var response = await client.SendAsync(request);
    Check("Life keeps token-bearing HTML outside HTTPS compression",
        response.StatusCode == HttpStatusCode.OK
        && response.Content.Headers.ContentEncoding.Count == 0);
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
    var decodedHtml = WebUtility.HtmlDecode(html);
    Check("Authenticated Life dashboard renders its initial shell",
        response.StatusCode == HttpStatusCode.OK
        && html.Contains("/images/donysh-life-logo.png", StringComparison.Ordinal)
        && html.Contains("rel=\"icon\" type=\"image/png\" href=\"/images/donysh-life-logo.png\"", StringComparison.Ordinal)
        && html.Contains("برای چیزهای مهم، جا باز کن", StringComparison.Ordinal)
        && html.Contains("برنامهٔ هفته", StringComparison.Ordinal)
        && html.Contains("فعالیت‌های فعال", StringComparison.Ordinal)
        && html.Contains("فعالیتی انجام دادی", StringComparison.Ordinal)
        && html.Contains("aria-current=\"page\"", StringComparison.Ordinal));
    Check("Authenticated Life shell monitors cross-product logout",
        html.Contains("/js/session-sync.js", StringComparison.Ordinal)
        && html.Contains("data-session-status-url=\"/Account/SessionStatus\"", StringComparison.Ordinal));
    using var sessionStatus = await authenticatedClient.GetAsync("/Account/SessionStatus");
    Check("Authenticated Life session status remains active",
        sessionStatus.StatusCode == HttpStatusCode.NoContent
        && sessionStatus.Headers.CacheControl?.NoStore == true);
    Check("Life shell links to the configured Finance product",
        html.Contains("href=\"https://finance.test/\"", StringComparison.Ordinal)
        && html.Contains("دُنیش مالی", StringComparison.Ordinal)
        && html.Contains("رفتن به دُنیش مالی", StringComparison.Ordinal));
    Check("Life shell identifies the signed-in account",
        html.Contains("حساب واردشده", StringComparison.Ordinal)
        && decodedHtml.Contains("کاربر آزمایشی", StringComparison.Ordinal));
    Check("Life shell shows the signed-in account email",
        html.Contains("life-user@example.test", StringComparison.Ordinal));
    Check("Life logout posts locally before central OIDC sign-out",
        html.Contains("action=\"/Account/Logout\"", StringComparison.Ordinal)
        && html.Contains("class=\"logout-form\" data-native-submit", StringComparison.Ordinal));
    Check("Life mobile navigation exposes the same logout flow",
        html.Contains("class=\"mobile-logout-form\" data-native-submit data-logout-form", StringComparison.Ordinal)
        && html.Contains("aria-label=\"خروج از حساب\"", StringComparison.Ordinal));
    Check("Life antiforgery cookie is HttpOnly, secure and same-site strict",
        response.Headers.TryGetValues("Set-Cookie", out var setCookies)
        && setCookies.Any(cookie => cookie.Contains(".Donysh.Life.Antiforgery", StringComparison.Ordinal)
            && cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase)
            && cookie.Contains("secure", StringComparison.OrdinalIgnoreCase)
            && cookie.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase)));

    using (var setupResponse = await authenticatedClient.GetAsync("/Setup?step=2"))
    {
        var setupHtml = await setupResponse.Content.ReadAsStringAsync();
        Check("Life setup wizard offers activity templates and weekly goals",
            setupResponse.StatusCode == HttpStatusCode.OK
            && setupHtml.Contains("چه چیزهایی برایت مهم‌اند", StringComparison.Ordinal)
            && setupHtml.Contains("Suggestions[0].WeeklyHours", StringComparison.Ordinal)
            && setupHtml.Contains("data-selected-count", StringComparison.Ordinal)
            && setupHtml.Contains("data-select-all", StringComparison.Ordinal)
            && setupHtml.Contains("aria-current=\"step\"", StringComparison.Ordinal)
            && setupHtml.Contains("setup-wizard", StringComparison.Ordinal));
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

    using (var weekResponse = await authenticatedClient.GetAsync("/Week?day=Monday"))
    {
        var weekHtml = await weekResponse.Content.ReadAsStringAsync();
        Check("Life weekly planner renders its responsive empty state",
            weekResponse.StatusCode == HttpStatusCode.OK
            && weekHtml.Contains("برنامه‌ریزی تعاملی", StringComparison.Ordinal)
            && weekHtml.Contains("نوع نمایش", StringComparison.Ordinal)
            && weekHtml.Contains("افزودن برنامه", StringComparison.Ordinal) == false
            && weekHtml.Contains("اول یک فعالیت بساز", StringComparison.Ordinal)
            && !weekHtml.Contains("DisplayMode field is required", StringComparison.Ordinal)
            && !weekHtml.Contains("style=", StringComparison.OrdinalIgnoreCase));
    }

    var calendarActivityId = Guid.NewGuid();
    await using (var seedScope = authenticatedFactory.Services.CreateAsyncScope())
    {
        var db = seedScope.ServiceProvider.GetRequiredService<LifeDbContext>();
        var now = DateTimeOffset.UtcNow;
        db.Activities.Add(new Donysh.Life.Domain.LifeActivity
        {
            Id = calendarActivityId,
            UserSubject = "life-test-user",
            Title = "برنامهٔ نمونهٔ بلند",
            NormalizedTitle = "برنامه نمونه بلند",
            ColorHex = "#0D9488",
            IconKey = "book",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        db.SchedulePatterns.Add(new Donysh.Life.Domain.RecurringSchedulePattern
        {
            Id = Guid.NewGuid(),
            UserSubject = "life-test-user",
            ActivityId = calendarActivityId,
            DayOfWeek = DayOfWeek.Wednesday,
            StartMinute = 510,
            DurationMinutes = 90,
            Note = "جزئیات برنامه",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        await db.SaveChangesAsync();
    }

    using (var calendarWeekResponse = await authenticatedClient.GetAsync("/Week?day=Wednesday&view=week"))
    {
        var calendarWeekHtml = await calendarWeekResponse.Content.ReadAsStringAsync();
        Check("Life weekly calendar positions complete event cards on a 30-minute time grid",
            calendarWeekResponse.StatusCode == HttpStatusCode.OK
            && calendarWeekHtml.Contains("calendar-board week-view", StringComparison.Ordinal)
            && calendarWeekHtml.Contains("data-column-count=\"7\"", StringComparison.Ordinal)
            && calendarWeekHtml.Contains("08:30 تا 10:00", StringComparison.Ordinal)
            && calendarWeekHtml.Contains("schedule-resize-handle", StringComparison.Ordinal)
            && calendarWeekHtml.Contains("data-duration=\"90\"", StringComparison.Ordinal)
            && !calendarWeekHtml.Contains("style=", StringComparison.OrdinalIgnoreCase));
    }

    string calendarAntiforgery;
    using (var calendarDayResponse = await authenticatedClient.GetAsync("/Week?day=Wednesday&view=day"))
    {
        var calendarDayHtml = await calendarDayResponse.Content.ReadAsStringAsync();
        calendarAntiforgery = WebUtility.HtmlDecode(Regex.Match(
            calendarDayHtml,
            "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"",
            RegexOptions.CultureInvariant).Groups[1].Value);
        Check("Life day view keeps the selected day and creation dialog accessible",
            calendarDayResponse.StatusCode == HttpStatusCode.OK
            && calendarDayHtml.Contains("calendar-board day-view", StringComparison.Ordinal)
            && calendarDayHtml.Contains("data-column-count=\"1\"", StringComparison.Ordinal)
            && calendarDayHtml.Contains("schedule-create-dialog", StringComparison.Ordinal)
            && calendarDayHtml.Contains("schedule-edit-dialog", StringComparison.Ordinal)
            && !calendarDayHtml.Contains("DisplayMode field is required", StringComparison.Ordinal)
            && calendarDayHtml.Contains("aria-current=\"page\"", StringComparison.Ordinal));
    }

    var calendarWeekStart = WeekCalendar.CurrentSaturday();
    using (var createScheduleContent = new FormUrlEncodedContent(new Dictionary<string, string>
    {
        ["__RequestVerificationToken"] = calendarAntiforgery,
        ["Input.ActivityId"] = calendarActivityId.ToString(),
        ["Input.DayOfWeek"] = DayOfWeek.Saturday.ToString(),
        ["Input.StartTime"] = "12:00",
        ["Input.DurationMinutes"] = "60",
        ["Input.Note"] = "آزمون ساخت",
        ["WeekStart"] = calendarWeekStart.ToString("yyyy-MM-dd"),
        ["view"] = "day"
    }))
    using (var createScheduleResponse = await authenticatedClient.PostAsync("/Week?handler=Save", createScheduleContent))
    {
        Check("Life create dialog submits without unrelated interaction validation",
            createScheduleResponse.StatusCode == HttpStatusCode.Redirect
            && createScheduleResponse.Headers.Location?.OriginalString.Contains("view=day", StringComparison.Ordinal) == true);
    }

    Guid createdPatternId;
    await using (var readCreatedScope = authenticatedFactory.Services.CreateAsyncScope())
    {
        var db = readCreatedScope.ServiceProvider.GetRequiredService<LifeDbContext>();
        createdPatternId = await db.SchedulePatterns
            .Where(item => item.UserSubject == "life-test-user" && item.DayOfWeek == DayOfWeek.Saturday)
            .Select(item => item.Id)
            .SingleAsync();
    }

    using (var editScheduleContent = new FormUrlEncodedContent(new Dictionary<string, string>
    {
        ["__RequestVerificationToken"] = calendarAntiforgery,
        ["PatternId"] = createdPatternId.ToString(),
        ["Input.ActivityId"] = calendarActivityId.ToString(),
        ["Input.DayOfWeek"] = DayOfWeek.Saturday.ToString(),
        ["Input.StartTime"] = "13:00",
        ["Input.DurationMinutes"] = "90",
        ["Input.Note"] = "آزمون ویرایش",
        ["WeekStart"] = calendarWeekStart.ToString("yyyy-MM-dd"),
        ["view"] = "day"
    }))
    using (var editScheduleResponse = await authenticatedClient.PostAsync("/Week?handler=Save", editScheduleContent))
    {
        Check("Life edit dialog updates a schedule without unrelated model errors",
            editScheduleResponse.StatusCode == HttpStatusCode.Redirect
            && editScheduleResponse.Headers.Location?.OriginalString.Contains("view=day", StringComparison.Ordinal) == true);
    }

    await using (var readEditedScope = authenticatedFactory.Services.CreateAsyncScope())
    {
        var db = readEditedScope.ServiceProvider.GetRequiredService<LifeDbContext>();
        var editedPattern = await db.SchedulePatterns.SingleAsync(item => item.Id == createdPatternId);
        Check("Life edit dialog persists time, duration and note",
            editedPattern.StartMinute == 13 * 60
            && editedPattern.DurationMinutes == 90
            && editedPattern.Note == "آزمون ویرایش");
    }

    using (var editedDayResponse = await authenticatedClient.GetAsync("/Week?day=Saturday&view=day"))
    {
        var editedDayHtml = await editedDayResponse.Content.ReadAsStringAsync();
        Check("Life edited schedule renders the new time in its centered dialog flow",
            editedDayResponse.StatusCode == HttpStatusCode.OK
            && editedDayHtml.Contains("13:00 تا 14:30", StringComparison.Ordinal)
            && !editedDayHtml.Contains("DurationMinutes must be between", StringComparison.Ordinal)
            && !editedDayHtml.Contains("DisplayMode field is required", StringComparison.Ordinal));
    }

    await using (var cleanupScope = authenticatedFactory.Services.CreateAsyncScope())
    {
        var db = cleanupScope.ServiceProvider.GetRequiredService<LifeDbContext>();
        db.SchedulePatterns.RemoveRange(db.SchedulePatterns.Where(item => item.UserSubject == "life-test-user"));
        db.Activities.RemoveRange(db.Activities.Where(item => item.Id == calendarActivityId));
        await db.SaveChangesAsync();
    }

    using (var progressResponse = await authenticatedClient.GetAsync("/Progress"))
    {
        var progressHtml = await progressResponse.Content.ReadAsStringAsync();
        Check("Life progress report renders its weekly summary and empty guidance",
            progressResponse.StatusCode == HttpStatusCode.OK
            && progressHtml.Contains("تصویر واقعی هفته", StringComparison.Ordinal)
            && progressHtml.Contains("این هفته هنوز داده‌ای برای مقایسه ندارد", StringComparison.Ordinal)
            && progressHtml.Contains("منبع زمان واقعی", StringComparison.Ordinal)
            && progressHtml.Contains("تعیین هدف هفتگی", StringComparison.Ordinal)
            && !progressHtml.Contains("style=", StringComparison.OrdinalIgnoreCase));
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
    Check("Life logout uses a Safari-safe same-origin continuation",
        antiforgeryMatch.Success
        && (int)logoutResponse.StatusCode == StatusCodes.Status303SeeOther
        && logoutLocation.StartsWith("/Account/Logout?", StringComparison.Ordinal)
        && logoutLocation.Contains("handler=Complete", StringComparison.Ordinal)
        && logoutLocation.Contains("token=", StringComparison.Ordinal)
        && logoutResponse.Content.Headers.ContentDisposition is null);

    using var completeLogoutResponse = await authenticatedClient.GetAsync(logoutLocation);
    var peerLogoutLocation = completeLogoutResponse.Headers.Location?.OriginalString ?? string.Empty;
    Check("Life logout visits Finance before ending the central session",
        completeLogoutResponse.StatusCode == HttpStatusCode.Redirect
        && peerLogoutLocation.StartsWith("https://finance.test/Account/FederatedLogout?proof=", StringComparison.Ordinal));

    var finalizeToken = CrossAppLogoutToken.Create("test-only-shared-logout-secret-0000000000000000", "life-finalize");
    using var finalizeResponse = await authenticatedClient.GetAsync(
        "/Account/Logout?handler=Finalize&token=" + Uri.EscapeDataString(finalizeToken));
    var centralLogoutLocation = finalizeResponse.Headers.Location?.OriginalString ?? string.Empty;
    Check("Life finalization ends the central session after peer logout",
        finalizeResponse.StatusCode == HttpStatusCode.Redirect
        && centralLogoutLocation.StartsWith("https://auth.test/connect/logout", StringComparison.Ordinal)
        && centralLogoutLocation.Contains("post_logout_redirect_uri=https%3A%2F%2Flife.test%2Fsignout-callback-oidc", StringComparison.OrdinalIgnoreCase));

    var logoutState = QueryHelpers.ParseQuery(new Uri(centralLogoutLocation).Query)["state"].ToString();
    using var logoutCallbackResponse = await authenticatedClient.GetAsync(
        "/signout-callback-oidc?state=" + Uri.EscapeDataString(logoutState));
    Check("Life OIDC callback shows the completed logout page",
        !string.IsNullOrWhiteSpace(logoutState)
        && logoutCallbackResponse.StatusCode == HttpStatusCode.Redirect
        && logoutCallbackResponse.Headers.Location?.OriginalString == "/Account/SignedOut"
        && logoutCallbackResponse.Content.Headers.ContentDisposition is null);
}

const string sharedLogoutSecret = "test-only-shared-logout-secret-0000000000000000";
var lifeLogoutConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Products:FinanceUrl"] = "https://finance.test",
    ["CrossAppLogout:Secret"] = sharedLogoutSecret
}).Build();
var lifeContinueToken = Donysh.Life.Security.CrossAppLogoutToken.Create(sharedLogoutSecret, "life-continue");
var lifeLogout = new Donysh.Life.Pages.Account.LogoutModel(lifeLogoutConfiguration);
var lifePeerRedirect = lifeLogout.OnGetComplete(lifeContinueToken) as Microsoft.AspNetCore.Mvc.RedirectResult;
Check("Life logout visits Finance before clearing its own session",
    lifePeerRedirect?.Url?.StartsWith("https://finance.test/Account/FederatedLogout?proof=", StringComparison.Ordinal) == true);
var lifePeerProof = new Uri(lifePeerRedirect!.Url!).Query["?proof=".Length..];
Check("Life creates a short-lived signed proof for Finance logout",
    Donysh.Life.Security.CrossAppLogoutToken.TryValidate(lifePeerProof, sharedLogoutSecret, "finance")
    && !Donysh.Life.Security.CrossAppLogoutToken.TryValidate(lifePeerProof, sharedLogoutSecret, "life"));
var lifeFinalizeToken = CrossAppLogoutToken.Create(sharedLogoutSecret, "life-finalize");
var lifeFinalSignOut = lifeLogout.OnGetFinalize(lifeFinalizeToken) as Microsoft.AspNetCore.Mvc.SignOutResult;
Check("Life accepts only its signed finalization proof",
    lifeFinalSignOut?.AuthenticationSchemes.Contains(LifeAuthOptions.CookieScheme) == true
    && lifeFinalSignOut.AuthenticationSchemes.Contains(LifeAuthOptions.OidcScheme)
    && lifeFinalSignOut.Properties?.RedirectUri == "/Account/SignedOut"
    && lifeLogout.OnGetFinalize(lifePeerProof) is Microsoft.AspNetCore.Mvc.NotFoundResult);
var expiredLogoutProof = Donysh.Life.Security.CrossAppLogoutToken.Create(
    sharedLogoutSecret, "life", DateTimeOffset.UtcNow.AddMinutes(-5));
Check("Cross-app logout rejects expired and tampered proofs",
    !Donysh.Life.Security.CrossAppLogoutToken.TryValidate(expiredLogoutProof, sharedLogoutSecret, "life")
    && !Donysh.Life.Security.CrossAppLogoutToken.TryValidate(lifePeerProof + "x", sharedLogoutSecret, "finance"));
var lifeProgramSource = await File.ReadAllTextAsync(Path.Combine(
    AppContext.BaseDirectory, "..", "..", "..", "..", "Donysh.Life", "Program.cs"));
Check("Life login avoids a redundant UserInfo network request",
    lifeProgramSource.Contains("GetClaimsFromUserInfoEndpoint = false", StringComparison.Ordinal));

var aiJson = """{"version":1,"activities":[{"title":"مطالعهٔ AI","category":"یادگیری","color":"#2563EB","icon":"book","weeklyHours":2,"slots":[{"day":"Saturday","start":"09:00","durationMinutes":60}]},{"title":"ورزش AI","category":"سلامت","color":"#EA580C","icon":"activity","weeklyHours":1,"slots":[{"day":"Sunday","start":"10:00","durationMinutes":60}]}]}""";
Check("Life AI parser accepts a valid weekly plan", LifeAiPlanService.Parse(aiJson).Plan?.Activities?.Count == 2);
Check("Life AI parser lets users repair overlapping suggestions in preview",
    LifeAiPlanService.Parse(aiJson.Replace("Sunday", "Saturday").Replace("10:00", "09:30")).Plan is not null);
Check("Life AI parser rejects unsupported colors and malformed days",
    LifeAiPlanService.Parse(aiJson.Replace("#2563EB", "#FFFFFF")).Error is not null
    && LifeAiPlanService.Parse(aiJson.Replace("Saturday", "Funday")).Error is not null);

await using (var aiFactory = new AuthenticatedLifeApplicationFactory())
using (var aiClient = aiFactory.CreateClient(new WebApplicationFactoryClientOptions
{
    AllowAutoRedirect = false,
    BaseAddress = new Uri("https://life.test")
}))
{
    using var aiPage = await aiClient.GetAsync("/Ai");
    var aiHtml = await aiPage.Content.ReadAsStringAsync();
    var aiCsrf = WebUtility.HtmlDecode(Regex.Match(aiHtml, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value);
    Check("Life AI page exposes prompt, upload, paste and mobile navigation",
        aiPage.StatusCode == HttpStatusCode.OK && aiHtml.Contains("فایل JSON", StringComparison.Ordinal)
        && aiHtml.Contains("متن JSON", StringComparison.Ordinal) && aiHtml.Contains("Saturday", StringComparison.Ordinal)
        && aiHtml.Contains("asp-page=\"/Ai\"", StringComparison.Ordinal) == false
        && aiHtml.Contains("href=\"/Ai\"", StringComparison.Ordinal));
    using var previewContent = new FormUrlEncodedContent(new Dictionary<string, string>
    {
        ["__RequestVerificationToken"] = aiCsrf, ["JsonText"] = aiJson
    });
    using var previewResponse = await aiClient.PostAsync("/Ai?handler=Preview", previewContent);
    var previewHtml = await previewResponse.Content.ReadAsStringAsync();
    var decodedPreviewHtml = WebUtility.HtmlDecode(previewHtml);
    var token = WebUtility.HtmlDecode(Regex.Match(previewHtml, "name=\"PreviewToken\" value=\"([^\"]+)\"").Groups[1].Value);
    var applyCsrf = WebUtility.HtmlDecode(Regex.Match(previewHtml, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value);
    Check("Life AI preview shows editable activities and schedule slots",
        previewResponse.StatusCode == HttpStatusCode.OK && !string.IsNullOrWhiteSpace(token)
        && decodedPreviewHtml.Contains("مطالعهٔ AI", StringComparison.Ordinal)
        && previewHtml.Contains("Selections[0].Slots[0].Day", StringComparison.Ordinal)
        && decodedPreviewHtml.Contains("ساخت برنامهٔ انتخاب‌شده", StringComparison.Ordinal));
    var applyValues = new Dictionary<string, string>
    {
        ["__RequestVerificationToken"] = applyCsrf, ["PreviewToken"] = token,
        ["Selections[0].Included"] = "true", ["Selections[0].Slots[0].Included"] = "true",
        ["Selections[0].Slots[0].Day"] = "Monday", ["Selections[0].Slots[0].Start"] = "14:30",
        ["Selections[0].Slots[0].DurationMinutes"] = "90",
        ["Selections[1].Included"] = "false", ["Selections[1].Slots[0].Included"] = "true",
        ["Selections[1].Slots[0].Day"] = "Sunday", ["Selections[1].Slots[0].Start"] = "10:00",
        ["Selections[1].Slots[0].DurationMinutes"] = "60"
    };
    var conflictingValues = new Dictionary<string, string>(applyValues)
    {
        ["Selections[1].Included"] = "true",
        ["Selections[1].Slots[0].Day"] = "Monday",
        ["Selections[1].Slots[0].Start"] = "15:00"
    };
    using var conflictingContent = new FormUrlEncodedContent(conflictingValues);
    using var conflictingResponse = await aiClient.PostAsync("/Ai?handler=Apply", conflictingContent);
    var conflictingHtml = WebUtility.HtmlDecode(await conflictingResponse.Content.ReadAsStringAsync());
    await using var conflictScope = aiFactory.Services.CreateAsyncScope();
    var conflictDb = conflictScope.ServiceProvider.GetRequiredService<LifeDbContext>();
    Check("Life AI rejects overlapping edits without partially creating activities",
        conflictingResponse.StatusCode == HttpStatusCode.OK
        && conflictingHtml.Contains("تداخل", StringComparison.Ordinal)
        && !await conflictDb.Activities.AnyAsync(x => x.Title.EndsWith("AI")));
    using var applyContent = new FormUrlEncodedContent(applyValues);
    using var applyResponse = await aiClient.PostAsync("/Ai?handler=Apply", applyContent);
    await using var aiScope = aiFactory.Services.CreateAsyncScope();
    var aiDb = aiScope.ServiceProvider.GetRequiredService<LifeDbContext>();
    var importedActivities = await aiDb.Activities.Where(x => x.Title.EndsWith("AI")).ToListAsync();
    var importedSlots = await aiDb.SchedulePatterns.Where(x => importedActivities.Select(a => a.Id).Contains(x.ActivityId)).ToListAsync();
    Check("Life AI applies only selected activities with edited weekday and time",
        applyResponse.StatusCode == HttpStatusCode.Redirect && applyResponse.Headers.Location?.OriginalString == "/Week"
        && importedActivities.Count == 1 && importedActivities[0].Title == "مطالعهٔ AI"
        && importedSlots.Count == 1 && importedSlots[0].DayOfWeek == DayOfWeek.Monday
        && importedSlots[0].StartMinute == 870 && importedSlots[0].DurationMinutes == 90);
    using var repeatedContent = new FormUrlEncodedContent(applyValues);
    using var repeatedResponse = await aiClient.PostAsync("/Ai?handler=Apply", repeatedContent);
    var repeatedHtml = WebUtility.HtmlDecode(await repeatedResponse.Content.ReadAsStringAsync());
    Check("Life AI repeated confirmation does not duplicate activities or schedule",
        repeatedResponse.StatusCode == HttpStatusCode.OK
        && repeatedHtml.Contains("از قبل وجود دارد", StringComparison.Ordinal)
        && await aiDb.Activities.CountAsync(x => x.Title == "مطالعهٔ AI") == 1
        && await aiDb.SchedulePatterns.CountAsync(x => x.ActivityId == importedActivities[0].Id) == 1);
}

await using (var federatedFactory = new AuthenticatedLifeApplicationFactory())
using (var federatedClient = federatedFactory.CreateClient(new WebApplicationFactoryClientOptions
{
    AllowAutoRedirect = false,
    BaseAddress = new Uri("https://life.test")
}))
{
    using (var invalidFederatedLogout = await federatedClient.GetAsync("/Account/FederatedLogout?proof=invalid"))
        Check("Life federated logout rejects unsigned requests", invalidFederatedLogout.StatusCode == HttpStatusCode.NotFound);

    var validLifeProof = Donysh.Life.Security.CrossAppLogoutToken.Create(sharedLogoutSecret, "life");
    using (var federatedLogout = await federatedClient.GetAsync(
        "/Account/FederatedLogout?proof=" + Uri.EscapeDataString(validLifeProof)))
    {
        Check("Life federated logout clears the local session without a download response",
            federatedLogout.StatusCode == HttpStatusCode.Redirect
            && federatedLogout.Headers.Location?.OriginalString.StartsWith(
                "https://finance.test/Account/Logout?handler=Finalize&token=", StringComparison.Ordinal) == true
            && federatedLogout.Content.Headers.ContentDisposition is null
            && federatedLogout.Headers.TryGetValues("Set-Cookie", out var logoutCookies)
            && logoutCookies.Any(value => value.Contains(".Donysh.Life.Session=", StringComparison.Ordinal)));
    }
    var authProof = CrossAppLogoutToken.Create(sharedLogoutSecret, "life-auth");
    using var authFederatedLogout = await federatedClient.GetAsync(
        "/Account/FederatedLogout?proof=" + Uri.EscapeDataString(authProof));
    Check("Life returns central-initiated logout to Auth",
        authFederatedLogout.StatusCode == HttpStatusCode.Redirect
        && authFederatedLogout.Headers.Location?.OriginalString.StartsWith(
            "https://auth.test/Account/Logout?handler=Finalize&token=", StringComparison.Ordinal) == true);
}

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

    httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "activity-owner-a")], "test"));
    var scheduler = new WeeklyScheduleService(activityDb, new HttpContextAccessor { HttpContext = httpContext });
    var scheduled = await scheduler.SaveAsync(null, new SchedulePatternForm
    {
        ActivityId = ownerItems[0].Id,
        DayOfWeek = DayOfWeek.Saturday,
        StartTime = "08:00",
        DurationMinutes = 60,
        Note = "شروع هفته"
    });
    var overlap = await scheduler.SaveAsync(null, new SchedulePatternForm
    {
        ActivityId = ownerItems[0].Id,
        DayOfWeek = DayOfWeek.Saturday,
        StartTime = "08:30",
        DurationMinutes = 60
    });
    var ownerWeek = await scheduler.GetWeekAsync(DayOfWeek.Saturday);
    Check("Life weekly planner stores 30-minute slots and rejects overlaps",
        scheduled.Succeeded
        && !overlap.Succeeded
        && ownerWeek.Days.Single(day => day.DayOfWeek == DayOfWeek.Saturday).Blocks.Count == 1
        && await activityDb.ScheduleSlots.CountAsync() == 2);

    var ownerPatternId = ownerWeek.Days.Single(day => day.DayOfWeek == DayOfWeek.Saturday).Blocks[0].Id;
    var weekStart = ownerWeek.WeekStart;
    var occurrenceMoved = await scheduler.ApplyInteractionAsync(new ScheduleInteractionForm
    {
        PatternId = ownerPatternId,
        OriginalDate = weekStart,
        TargetDate = weekStart.AddDays(1),
        StartMinute = 600,
        DurationMinutes = 30,
        Scope = "occurrence"
    });
    var changedWeek = await scheduler.GetWeekAsync(DayOfWeek.Sunday, weekStart);
    var nextWeek = await scheduler.GetWeekAsync(DayOfWeek.Saturday, weekStart.AddDays(7));
    var occurrenceReset = await scheduler.ApplyInteractionAsync(new ScheduleInteractionForm
    {
        PatternId = ownerPatternId,
        OriginalDate = weekStart,
        TargetDate = weekStart,
        StartMinute = 480,
        DurationMinutes = 60,
        Scope = "occurrence"
    });
    Check("Life occurrence edits affect one week without changing the recurring series",
        occurrenceMoved.Succeeded
        && changedWeek.Days.Single(day => day.DayOfWeek == DayOfWeek.Saturday).Blocks.Count == 0
        && changedWeek.Days.Single(day => day.DayOfWeek == DayOfWeek.Sunday).Blocks.Single().IsOccurrenceOverride
        && nextWeek.Days.Single(day => day.DayOfWeek == DayOfWeek.Saturday).Blocks.Single().StartMinute == 480
        && occurrenceReset.Succeeded
        && await activityDb.ScheduleOccurrenceOverrides.CountAsync() == 0);

    var occurrenceCancelled = await scheduler.CancelOccurrenceAsync(ownerPatternId, weekStart);
    var cancelledWeek = await scheduler.GetWeekAsync(DayOfWeek.Saturday, weekStart);
    var occurrenceRestored = await scheduler.ApplyInteractionAsync(new ScheduleInteractionForm
    {
        PatternId = ownerPatternId, OriginalDate = weekStart, TargetDate = weekStart,
        StartMinute = 480, DurationMinutes = 60, Scope = "occurrence"
    });
    Check("Life can cancel one occurrence without deleting its recurring pattern",
        occurrenceCancelled.Succeeded
        && cancelledWeek.Days.Single(day => day.DayOfWeek == DayOfWeek.Saturday).Blocks.Count == 0
        && occurrenceRestored.Succeeded
        && await activityDb.SchedulePatterns.AnyAsync(item => item.Id == ownerPatternId));

    var tracking = new OccurrenceTrackingService(activityDb, new HttpContextAccessor { HttpContext = httpContext }, scheduler);
    var completed = await tracking.RecordAsync(new OccurrenceRecordForm
    {
        PatternId = ownerPatternId, OriginalDate = weekStart,
        Status = Donysh.Life.Domain.OccurrenceOutcomeStatus.Completed
    });
    var completedMinutes = await activityDb.ScheduleOccurrenceRecords.Select(item => item.ActualMinutes).SingleAsync();
    var partialWithoutMinutes = await tracking.RecordAsync(new OccurrenceRecordForm
    {
        PatternId = ownerPatternId, OriginalDate = weekStart,
        Status = Donysh.Life.Domain.OccurrenceOutcomeStatus.Partial
    });
    var unknownPartialMinutes = await activityDb.ScheduleOccurrenceRecords.Select(item => item.ActualMinutes).SingleAsync();
    var partial = await tracking.RecordAsync(new OccurrenceRecordForm
    {
        PatternId = ownerPatternId, OriginalDate = weekStart,
        Status = Donysh.Life.Domain.OccurrenceOutcomeStatus.Partial,
        ActualMinutes = 20, Note = "بخشی انجام شد"
    });
    var invalidPartial = await tracking.RecordAsync(new OccurrenceRecordForm
    {
        PatternId = ownerPatternId, OriginalDate = weekStart,
        Status = Donysh.Life.Domain.OccurrenceOutcomeStatus.Partial,
        ActualMinutes = 60
    });
    var invalidMoved = await tracking.RecordAsync(new OccurrenceRecordForm
    {
        PatternId = ownerPatternId, OriginalDate = weekStart,
        Status = Donysh.Life.Domain.OccurrenceOutcomeStatus.Moved
    });
    var trackedWeek = await scheduler.GetWeekAsync(DayOfWeek.Saturday, weekStart);
    var protectedHistory = await scheduler.DeleteAsync(ownerPatternId);
    var protectedActivityHistory = await service.DeleteAsync(ownerItems[0].Id);
    var trackedBlock = trackedWeek.Days.Single(day => day.DayOfWeek == DayOfWeek.Saturday).Blocks.Single();
    Check("Life records actual outcomes idempotently and protects recorded history",
        completed.Succeeded
        && completedMinutes == 60
        && partialWithoutMinutes.Succeeded
        && unknownPartialMinutes is null
        && partial.Succeeded
        && !invalidPartial.Succeeded
        && !invalidMoved.Succeeded
        && await activityDb.ScheduleOccurrenceRecords.CountAsync() == 1
        && trackedBlock.OutcomeStatus == Donysh.Life.Domain.OccurrenceOutcomeStatus.Partial
        && trackedBlock.ActualMinutes == 20
        && !protectedHistory.Succeeded
        && !protectedActivityHistory.Succeeded);
    var clearedOutcome = await tracking.ClearAsync(ownerPatternId, weekStart);
    Check("Life can clear an outcome before changing its recurring schedule",
        clearedOutcome.Succeeded && await activityDb.ScheduleOccurrenceRecords.CountAsync() == 0);

    var movedOutcome = await tracking.RecordAsync(new OccurrenceRecordForm
    {
        PatternId = ownerPatternId, OriginalDate = weekStart,
        Status = Donysh.Life.Domain.OccurrenceOutcomeStatus.Moved,
        MovedToDate = weekStart.AddDays(2)
    });
    var movedOutcomeWeek = await scheduler.GetWeekAsync(DayOfWeek.Monday, weekStart);
    var movedOutcomeBlock = movedOutcomeWeek.Days.Single(day => day.DayOfWeek == DayOfWeek.Monday).Blocks.Single();
    var restoredOutcome = await tracking.ClearAsync(ownerPatternId, weekStart);
    var restoredWeek = await scheduler.GetWeekAsync(DayOfWeek.Saturday, weekStart);
    Check("Life moved outcomes relocate occurrences and clearing restores them",
        movedOutcome.Succeeded
        && movedOutcomeBlock.OriginalDate == weekStart
        && movedOutcomeBlock.OutcomeStatus == Donysh.Life.Domain.OccurrenceOutcomeStatus.Moved
        && movedOutcomeBlock.MovedToDate == weekStart.AddDays(2)
        && restoredOutcome.Succeeded
        && restoredWeek.Days.Single(day => day.DayOfWeek == DayOfWeek.Saturday).Blocks.Count == 1
        && await activityDb.ScheduleOccurrenceOverrides.CountAsync() == 0);

    var actualService = new ActualActivityService(activityDb, new HttpContextAccessor { HttpContext = httpContext });
    var actualCreated = await actualService.SaveAsync(null, new ActualActivityForm
    {
        ActivityId = ownerItems[0].Id, OccurredDate = WeekCalendar.Today(), StartTime = "18:15",
        ActualMinutes = 45, Note = "تمرین واقعی"
    });
    var actualEntry = (await actualService.ListRecentAsync()).Single();
    var completedForReport = await tracking.RecordAsync(new OccurrenceRecordForm
    {
        PatternId = ownerPatternId, OriginalDate = weekStart,
        Status = Donysh.Life.Domain.OccurrenceOutcomeStatus.Completed
    });
    var reportService = new ProgressReportService(activityDb, new HttpContextAccessor { HttpContext = httpContext }, scheduler);
    var report = await reportService.GetWeekAsync(weekStart);
    var boundedReport = await reportService.GetWeekAsync(DateOnly.MaxValue);
    var protectedByManualHistory = await service.DeleteAsync(ownerItems[0].Id);
    httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "activity-owner-b")], "test"));
    var ownerBActualService = new ActualActivityService(activityDb, new HttpContextAccessor { HttpContext = httpContext });
    var forbiddenActualDelete = await ownerBActualService.DeleteAsync(actualEntry.Id);
    var emptyOwnerBScheduler = new WeeklyScheduleService(activityDb, new HttpContextAccessor { HttpContext = httpContext });
    var ownerBReportService = new ProgressReportService(activityDb, new HttpContextAccessor { HttpContext = httpContext }, emptyOwnerBScheduler);
    var ownerBEmptyReport = await ownerBReportService.GetWeekAsync(weekStart);
    httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "activity-owner-a")], "test"));
    var actualDeleted = await actualService.DeleteAsync(actualEntry.Id);
    var clearedReportOutcome = await tracking.ClearAsync(ownerPatternId, weekStart);
    Check("Life manual actual entries validate ownership and protect history",
        actualCreated.Succeeded
        && actualEntry.ActualMinutes == 45
        && actualEntry.StartLabel == "18:15"
        && !protectedByManualHistory.Succeeded
        && !forbiddenActualDelete.Succeeded
        && actualDeleted.Succeeded
        && await activityDb.ActualActivityEntries.CountAsync() == 0);
    Check("Life progress reports combine distinct actual sources without cross-user leakage",
        completedForReport.Succeeded
        && report.TargetMinutes == 240
        && report.PlannedMinutes == 60
        && report.ScheduledActualMinutes == 60
        && report.ManualActualMinutes == 45
        && report.ActualMinutes == 105
        && report.CompletedCount == 1
        && report.UnrecordedCount == 0
        && report.Activities.Single().ActualMinutes == 105
        && report.Days.Sum(day => day.ActualMinutes) == 105
        && boundedReport.WeekStart == WeekCalendar.CurrentSaturday()
        && !ownerBEmptyReport.HasData
        && clearedReportOutcome.Succeeded);

    var moved = await scheduler.SaveAsync(ownerPatternId, new SchedulePatternForm
    {
        ActivityId = ownerItems[0].Id,
        DayOfWeek = DayOfWeek.Saturday,
        StartTime = "09:00",
        DurationMinutes = 30
    });
    var paused = await scheduler.ToggleAsync(ownerPatternId);
    var replacement = await scheduler.SaveAsync(null, new SchedulePatternForm
    {
        ActivityId = ownerItems[0].Id,
        DayOfWeek = DayOfWeek.Saturday,
        StartTime = "09:00",
        DurationMinutes = 30
    });
    var conflictingResume = await scheduler.ToggleAsync(ownerPatternId);
    Check("Life schedule edits rebuild reservations and paused blocks release their slots",
        moved.Succeeded
        && paused.Succeeded
        && replacement.Succeeded
        && !conflictingResume.Succeeded
        && await activityDb.ScheduleSlots.CountAsync(item => item.UserSubject == "activity-owner-a") == 1);

    httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "activity-owner-b")], "test"));
    var ownerBService = new LifeActivityService(activityDb, new HttpContextAccessor { HttpContext = httpContext });
    var ownerBActivityResult = await ownerBService.SaveAsync(null, new ActivityForm
    {
        Title = "مطالعه", ColorHex = "#2563EB", IconKey = "book", WeeklyHours = 3
    });
    var ownerBActivity = (await ownerBService.ListAsync()).Single();
    var ownerBScheduler = new WeeklyScheduleService(activityDb, new HttpContextAccessor { HttpContext = httpContext });
    var sameTimeForOtherUser = await ownerBScheduler.SaveAsync(null, new SchedulePatternForm
    {
        ActivityId = ownerBActivity.Id,
        DayOfWeek = DayOfWeek.Saturday,
        StartTime = "08:00",
        DurationMinutes = 60
    });
    var forbiddenScheduleDelete = await ownerBScheduler.DeleteAsync(
        ownerPatternId);
    var forbiddenOccurrenceEdit = await ownerBScheduler.ApplyInteractionAsync(new ScheduleInteractionForm
    {
        PatternId = ownerPatternId, OriginalDate = weekStart, TargetDate = weekStart,
        StartMinute = 600, DurationMinutes = 30, Scope = "occurrence"
    });
    var ownerBTracking = new OccurrenceTrackingService(activityDb, new HttpContextAccessor { HttpContext = httpContext }, ownerBScheduler);
    var forbiddenOutcome = await ownerBTracking.RecordAsync(new OccurrenceRecordForm
    {
        PatternId = ownerPatternId, OriginalDate = weekStart,
        Status = Donysh.Life.Domain.OccurrenceOutcomeStatus.Completed
    });
    Check("Life weekly schedule slots are isolated per user",
        ownerBActivityResult.Succeeded
        && sameTimeForOtherUser.Succeeded
        && !forbiddenScheduleDelete.Succeeded
        && !forbiddenOccurrenceEdit.Succeeded
        && !forbiddenOutcome.Succeeded);
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
                ["Products:FinanceUrl"] = "https://finance.test",
                ["Products:AuthUrl"] = "https://auth.test",
                ["CrossAppLogout:Secret"] = "test-only-shared-logout-secret-0000000000000000",
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
        var identity = new ClaimsIdentity(
        [
            new Claim("sub", "life-test-user"),
            new Claim("name", "کاربر آزمایشی"),
            new Claim(ClaimTypes.Name, "کاربر آزمایشی"),
            new Claim(ClaimTypes.Email, "life-user@example.test")
        ],
        SchemeName,
        ClaimTypes.Name,
        ClaimTypes.Role);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
