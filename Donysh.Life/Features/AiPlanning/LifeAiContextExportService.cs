using System.Security.Claims;
using System.Text.Json;
using Donysh.Life.Data;
using Donysh.Life.Features.Reporting;
using Donysh.Life.Features.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace Donysh.Life.Features.AiPlanning;

public sealed class LifeAiContextExportService(
    LifeDbContext db,
    IHttpContextAccessor context,
    ProgressReportService reports,
    WeeklyScheduleService schedules)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public async Task<byte[]> ExportAsync(CancellationToken cancellationToken = default)
    {
        var subject = context.HttpContext?.User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(subject))
            throw new InvalidOperationException("نشست ورود معتبر نیست.");
        var activities = await db.Activities.AsNoTracking()
            .Where(activity => activity.UserSubject == subject && activity.IsActive)
            .Include(activity => activity.WeeklyGoal)
            .Include(activity => activity.SchedulePatterns)
            .OrderBy(activity => activity.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        var progress = await reports.GetWeekAsync(cancellationToken: cancellationToken);
        var week = await schedules.GetWeekAsync(DayOfWeek.Saturday, progress.WeekStart, cancellationToken);

        var document = new
        {
            version = 1,
            purpose = "improve-existing-plan",
            exportedAtUtc = DateTimeOffset.UtcNow,
            timeZone = "Asia/Tehran",
            progressWeek = new { start = progress.WeekStart, end = progress.WeekEnd },
            activities = activities.Select(activity => new
            {
                existingActivityId = activity.Id,
                title = activity.Title,
                category = activity.Category,
                color = activity.ColorHex,
                icon = activity.IconKey,
                weeklyHours = (activity.WeeklyGoal?.TargetMinutes ?? 0) / 60m,
                slots = activity.SchedulePatterns.Where(pattern => pattern.IsActive)
                    .OrderBy(pattern => WeekCalendar.DayIndex(pattern.DayOfWeek))
                    .ThenBy(pattern => pattern.StartMinute)
                    .Select(pattern => new
                    {
                        day = pattern.DayOfWeek.ToString(),
                        start = FormatMinute(pattern.StartMinute),
                        durationMinutes = pattern.DurationMinutes,
                        reminderMinutesBefore = pattern.ReminderMinutesBefore
                    })
            }),
            currentWeek = week.Days.Select(day => new
            {
                date = day.Date,
                day = day.DayOfWeek.ToString(),
                blocks = day.Blocks.Where(block => block.IsActive).Select(block => new
                {
                    existingActivityId = block.ActivityId,
                    title = block.ActivityTitle,
                    start = block.VisibleStartLabel,
                    durationMinutes = block.VisibleDurationMinutes,
                    continuesFromPreviousDay = block.IsContinuation,
                    status = block.OutcomeStatus?.ToString(),
                    actualMinutes = block.ActualMinutes
                })
            }),
            progress = new
            {
                targetMinutes = progress.TargetMinutes,
                plannedMinutes = progress.PlannedMinutes,
                actualMinutes = progress.ActualMinutes,
                completedCount = progress.CompletedCount,
                partialCount = progress.PartialCount,
                missedCount = progress.MissedCount,
                activities = progress.Activities.Select(item => new
                {
                    existingActivityId = item.ActivityId,
                    targetMinutes = item.TargetMinutes,
                    plannedMinutes = item.PlannedMinutes,
                    actualMinutes = item.ActualMinutes,
                    completedCount = item.CompletedCount,
                    partialCount = item.PartialCount,
                    missedCount = item.MissedCount
                })
            }
        };
        return JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
    }

    private static string FormatMinute(int minute) => $"{minute / 60:00}:{minute % 60:00}";
}
