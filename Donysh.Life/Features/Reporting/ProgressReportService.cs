using System.Security.Claims;
using Donysh.Life.Data;
using Donysh.Life.Domain;
using Donysh.Life.Features.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace Donysh.Life.Features.Reporting;

public sealed class ProgressReportService(
    LifeDbContext db,
    IHttpContextAccessor httpContextAccessor,
    WeeklyScheduleService weeklySchedules)
{
    public async Task<ProgressReport> GetWeekAsync(DateOnly? requestedWeekStart = null, CancellationToken cancellationToken = default)
    {
        var subject = GetSubject();
        var today = WeekCalendar.Today();
        var requestedDate = requestedWeekStart is null
            || requestedWeekStart < today.AddYears(-10)
            || requestedWeekStart > today.AddYears(2)
                ? today
                : requestedWeekStart.Value;
        var weekStart = StartOfWeek(requestedDate);
        var weekEnd = weekStart.AddDays(6);
        var schedule = await weeklySchedules.GetWeekAsync(DayOfWeek.Saturday, weekStart, cancellationToken);
        var activeBlocks = schedule.Days.SelectMany(day => day.Blocks).Where(block => block.IsActive).ToList();

        var activities = await db.Activities.AsNoTracking()
            .Where(activity => activity.UserSubject == subject)
            .Select(activity => new ActivitySource(
                activity.Id, activity.Title, activity.ColorHex, activity.IconKey, activity.IsActive,
                activity.WeeklyGoal == null || !activity.IsActive ? 0 : activity.WeeklyGoal.TargetMinutes))
            .ToListAsync(cancellationToken);
        var records = await db.ScheduleOccurrenceRecords.AsNoTracking()
            .Where(record => record.UserSubject == subject
                && ((record.Status == OccurrenceOutcomeStatus.Moved
                        && record.MovedToDate.HasValue && record.MovedToDate >= weekStart && record.MovedToDate <= weekEnd)
                    || (record.Status != OccurrenceOutcomeStatus.Moved
                        && record.ScheduledDate >= weekStart && record.ScheduledDate <= weekEnd)))
            .Select(record => new OccurrenceSource(
                record.PatternId, record.OriginalDate, record.Pattern.ActivityId,
                record.Status == OccurrenceOutcomeStatus.Moved ? record.MovedToDate!.Value : record.ScheduledDate,
                record.Status, record.PlannedMinutes, record.ActualMinutes))
            .ToListAsync(cancellationToken);
        var manualEntries = await db.ActualActivityEntries.AsNoTracking()
            .Where(entry => entry.UserSubject == subject && entry.OccurredDate >= weekStart && entry.OccurredDate <= weekEnd)
            .Select(entry => new ManualSource(entry.ActivityId, entry.OccurredDate, entry.ActualMinutes))
            .ToListAsync(cancellationToken);

        var blockKeys = activeBlocks.Select(block => (block.Id, block.OriginalDate)).ToHashSet();
        var blocksByActivity = activeBlocks.ToLookup(block => block.ActivityId);
        var recordsByActivity = records.ToLookup(record => record.ActivityId);
        var manualByActivity = manualEntries.ToLookup(entry => entry.ActivityId);
        var blocksByDate = activeBlocks.ToLookup(block => block.ScheduledDate);
        var recordsByDate = records.ToLookup(record => record.EffectiveDate);
        var manualByDate = manualEntries.ToLookup(entry => entry.OccurredDate);
        var activityRows = activities.Select(activity =>
        {
            var activityBlocks = blocksByActivity[activity.Id];
            var activityRecords = recordsByActivity[activity.Id];
            var snapshotMinutes = activityRecords.Where(record => !blockKeys.Contains((record.PatternId, record.OriginalDate)))
                .Sum(record => record.PlannedMinutes);
            var manualMinutes = manualByActivity[activity.Id].Sum(entry => entry.ActualMinutes);
            return new ActivityProgressItem(
                activity.Id, activity.Title, activity.ColorHex, activity.IconKey, activity.IsActive, activity.TargetMinutes,
                activityBlocks.Sum(block => block.VisibleDurationMinutes) + snapshotMinutes,
                activityRecords.Sum(record => record.ActualMinutes ?? 0), manualMinutes,
                activityRecords.Count(record => record.Status == OccurrenceOutcomeStatus.Completed),
                activityRecords.Count(record => record.Status == OccurrenceOutcomeStatus.Partial),
                activityRecords.Count(record => record.Status == OccurrenceOutcomeStatus.Missed),
                activityRecords.Count(record => record.Status == OccurrenceOutcomeStatus.Moved));
        }).Where(item => item.IsActive || item.PlannedMinutes > 0 || item.ActualMinutes > 0)
            .OrderByDescending(item => item.ActualMinutes).ThenBy(item => item.Title).ToList();

        var days = WeekCalendar.Days.Select((day, index) =>
        {
            var date = weekStart.AddDays(index);
            var dayRecords = recordsByDate[date];
            var plannedFromBlocks = blocksByDate[date].Sum(block => block.VisibleDurationMinutes);
            var snapshotMinutes = dayRecords.Where(record => !blockKeys.Contains((record.PatternId, record.OriginalDate)))
                .Sum(record => record.PlannedMinutes);
            return new DailyProgressItem(date, day, plannedFromBlocks + snapshotMinutes,
                dayRecords.Sum(record => record.ActualMinutes ?? 0),
                manualByDate[date].Sum(entry => entry.ActualMinutes));
        }).ToList();

        var recordKeys = records.Select(record => (record.PatternId, record.OriginalDate)).ToHashSet();
        return new ProgressReport(
            weekStart, weekEnd,
            activityRows.Sum(item => item.TargetMinutes),
            activityRows.Sum(item => item.PlannedMinutes),
            activityRows.Sum(item => item.ScheduledActualMinutes),
            activityRows.Sum(item => item.ManualActualMinutes),
            records.Count(record => record.Status == OccurrenceOutcomeStatus.Completed),
            records.Count(record => record.Status == OccurrenceOutcomeStatus.Partial),
            records.Count(record => record.Status == OccurrenceOutcomeStatus.Missed),
            records.Count(record => record.Status == OccurrenceOutcomeStatus.Moved),
            activeBlocks.Where(block => !recordKeys.Contains((block.Id, block.OriginalDate)))
                .Select(block => (block.Id, block.OriginalDate)).Distinct().Count(),
            activityRows, days);
    }

    private static DateOnly StartOfWeek(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek - (int)DayOfWeek.Saturday + 7) % 7));

    private string GetSubject() => httpContextAccessor.HttpContext?.User.FindFirstValue("sub") is { Length: > 0 } subject
        ? subject : throw new InvalidOperationException("شناسهٔ حساب کاربر در نشست ورود وجود ندارد.");

    private sealed record ActivitySource(Guid Id, string Title, string ColorHex, string IconKey, bool IsActive, int TargetMinutes);
    private sealed record OccurrenceSource(Guid PatternId, DateOnly OriginalDate, Guid ActivityId, DateOnly EffectiveDate,
        OccurrenceOutcomeStatus Status, int PlannedMinutes, int? ActualMinutes);
    private sealed record ManualSource(Guid ActivityId, DateOnly OccurredDate, int ActualMinutes);
}
