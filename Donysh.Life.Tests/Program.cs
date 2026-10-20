using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Encodings.Web;
using Donysh.Life.Configuration;
using Donysh.Life.Data;
using Donysh.Life.Features.Activities;
using Donysh.Life.Features.Scheduling;
using Donysh.Life.Features.Tracking;
using Donysh.Life.Features.Reporting;
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
Environment.SetEnvironmentVariable("Products__FinanceUrl", "https://finance.test");
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
        && html.Contains("همان حسابی که در دونیش مالی داری", StringComparison.Ordinal)
        && html.Contains("rel=\"preload\"", StringComparison.Ordinal)
        && html.Contains("interaction-feedback", StringComparison.Ordinal));
    Check("Life security headers allow only same-origin scripts and block framing",
        response.Headers.TryGetValues("Content-Security-Policy", out var csp)
        && csp.Single().Contains("frame-ancestors 'none'", StringComparison.Ordinal)
        && csp.Single().Contains("form-action 'self' https://auth.test", StringComparison.Ordinal)
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
    Check("Authenticated Life dashboard renders its initial shell",
        response.StatusCode == HttpStatusCode.OK
        && html.Contains("برای چیزهای مهم، جا باز کن", StringComparison.Ordinal)
        && html.Contains("برنامهٔ هفته", StringComparison.Ordinal)
        && html.Contains("فعالیت‌های فعال", StringComparison.Ordinal)
        && html.Contains("فعالیتی انجام دادی", StringComparison.Ordinal)
        && html.Contains("aria-current=\"page\"", StringComparison.Ordinal));
    Check("Life shell links to the configured Finance product",
        html.Contains("href=\"https://finance.test/\"", StringComparison.Ordinal)
        && html.Contains("دونیش مالی", StringComparison.Ordinal)
        && html.Contains("رفتن به دونیش مالی", StringComparison.Ordinal));
    Check("Life logout posts locally before central OIDC sign-out",
        html.Contains("action=\"/Account/Logout\"", StringComparison.Ordinal));
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

    using (var calendarDayResponse = await authenticatedClient.GetAsync("/Week?day=Wednesday&view=day"))
    {
        var calendarDayHtml = await calendarDayResponse.Content.ReadAsStringAsync();
        Check("Life day view keeps the selected day and creation dialog accessible",
            calendarDayResponse.StatusCode == HttpStatusCode.OK
            && calendarDayHtml.Contains("calendar-board day-view", StringComparison.Ordinal)
            && calendarDayHtml.Contains("data-column-count=\"1\"", StringComparison.Ordinal)
            && calendarDayHtml.Contains("schedule-create-dialog", StringComparison.Ordinal)
            && calendarDayHtml.Contains("aria-current=\"page\"", StringComparison.Ordinal));
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
