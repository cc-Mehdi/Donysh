using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Donysh.Life.Data;
using Donysh.Life.Domain;
using Donysh.Life.Features.Scheduling;
using Donysh.Life.Features.Tracking;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Donysh.Life.Features.Mobile;

public static class LifeMobileApi
{
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string NewSecret(int bytes) => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes));
    private static bool IsHex(string? value, int length) => value is { Length: > 0 } && value.Length == length && value.All(Uri.IsHexDigit);

    public static void MapLifeMobileApi(this WebApplication app)
    {
        app.MapPost("/api/life-mobile/pair", PairAsync).AllowAnonymous().RequireRateLimiting("life-mobile-pair");
        var group = app.MapGroup("/api/life-mobile").AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            http.Response.Headers.CacheControl = "no-store";
            if (!http.Request.IsHttps) return Results.BadRequest();
            var header = http.Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return Results.Unauthorized();
            var token = header[7..].Trim();
            if (!IsHex(token, 64)) return Results.Unauthorized();
            var db = http.RequestServices.GetRequiredService<LifeDbContext>();
            var hash = Hash(token.ToUpperInvariant());
            var device = await db.MobileDevices.AsNoTracking().SingleOrDefaultAsync(
                item => item.TokenHash == hash && !item.Revoked, http.RequestAborted);
            if (device is null) return Results.Unauthorized();
            http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", device.UserSubject)], "LifeMobile"));
            return await next(context);
        });
        group.MapGet("/week", async (WeeklyScheduleService schedules, CancellationToken ct) =>
        {
            var today = WeekCalendar.Today();
            var week = await schedules.GetWeekAsync(today.DayOfWeek, cancellationToken: ct);
            return Results.Ok(new
            {
                today = today.ToString("yyyy-MM-dd"),
                days = week.Days.Select(day => new
                {
                    date = day.Date.ToString("yyyy-MM-dd"), day.Name, day.PersianDate,
                    blocks = day.Blocks.Where(block => block.IsActive).Select(block => new
                    {
                        patternId = block.Id, originalDate = block.OriginalDate.ToString("yyyy-MM-dd"),
                        block.ActivityId, title = block.ActivityTitle, block.ColorHex,
                        startMinute = block.VisibleStartMinute, durationMinutes = block.VisibleDurationMinutes,
                        block.IsContinuation, status = block.OutcomeStatus?.ToString().ToLowerInvariant()
                    })
                }),
                activities = week.Activities.Select(item => new { item.Id, item.Title })
            });
        });
        group.MapGet("/reminders", async (ReminderScheduleService reminders, CancellationToken ct) =>
            Results.Ok(await reminders.UpcomingAsync(ct, 168)));
        group.MapPost("/occurrences/{patternId:guid}/{originalDate}", async (
            Guid patternId, string originalDate, [FromBody] LifeOccurrenceRequest input,
            OccurrenceTrackingService tracking, CancellationToken ct) =>
        {
            if (!DateOnly.TryParseExact(originalDate, "yyyy-MM-dd", out var date)
                || !Enum.TryParse<OccurrenceOutcomeStatus>(input.Status, true, out var status)
                || status is not (OccurrenceOutcomeStatus.Completed or OccurrenceOutcomeStatus.Missed or OccurrenceOutcomeStatus.Partial))
                return Results.BadRequest(new { error = "وضعیت یا تاریخ معتبر نیست." });
            var result = await tracking.RecordAsync(new OccurrenceRecordForm
            {
                PatternId = patternId, OriginalDate = date, Status = status,
                ActualMinutes = input.ActualMinutes, Note = input.Note
            }, ct);
            return result.Succeeded ? Results.Ok(new { message = result.Message }) : Results.BadRequest(new { error = result.Message });
        });
        group.MapPost("/actual", async ([FromBody] LifeActualRequest input, ActualActivityService actual, CancellationToken ct) =>
        {
            if (!DateOnly.TryParseExact(input.Date, "yyyy-MM-dd", out var date))
                return Results.BadRequest(new { error = "تاریخ معتبر نیست." });
            var result = await actual.SaveAsync(null, new ActualActivityForm
            {
                ActivityId = input.ActivityId, OccurredDate = date,
                StartTime = input.StartTime, ActualMinutes = input.ActualMinutes, Note = input.Note
            }, ct);
            return result.Succeeded ? Results.Ok(new { message = result.Message }) : Results.BadRequest(new { error = result.Message });
        });
        group.MapPost("/logout", async (HttpContext http, LifeDbContext db, CancellationToken ct) =>
        {
            var hash = Hash(http.Request.Headers.Authorization.ToString()[7..].Trim().ToUpperInvariant());
            await db.MobileDevices.Where(item => item.TokenHash == hash).ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.TokenHash, (string?)null).SetProperty(item => item.Revoked, true), ct);
            return Results.NoContent();
        });
    }

    private static async Task<IResult> PairAsync(HttpContext http, LifeDbContext db, [FromBody] LifePairRequest input, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!http.Request.IsHttps || !IsHex(input.Code, 32)) return Results.BadRequest();
        var codeHash = Hash(input.Code.ToUpperInvariant());
        var token = NewSecret(32);
        var now = DateTimeOffset.UtcNow;
        var changed = await db.MobileDevices.Where(item => item.PairCodeHash == codeHash
                && item.TokenHash == null && !item.Revoked && item.PairExpiresAtUtc > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.PairCodeHash, (string?)null)
                .SetProperty(item => item.TokenHash, Hash(token)).SetProperty(item => item.LastSeenAtUtc, now), ct);
        return changed == 1 ? Results.Ok(new { token }) : Results.Unauthorized();
    }

    public static string CreatePairCode() => NewSecret(16);
    public static string HashPairCode(string code) => Hash(code);
}

public sealed record LifePairRequest(string Code);
public sealed record LifeOccurrenceRequest(string Status, int? ActualMinutes, string? Note);
public sealed record LifeActualRequest(Guid ActivityId, string Date, string? StartTime, int ActualMinutes, string? Note);
