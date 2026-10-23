namespace Donysh.Life.Features.Scheduling;

public sealed record ScheduleReminder(string Id, string Title, string StartLabel, string ScheduledDate, DateTimeOffset RemindAtUtc);

public sealed class ReminderScheduleService(WeeklyScheduleService schedules)
{
    public async Task<IReadOnlyList<ScheduleReminder>> UpcomingAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var saturday = WeekCalendar.CurrentSaturday();
        var zone = TehranTimeZone();
        var weeks = new List<WeekScheduleView> { await schedules.GetWeekAsync(DayOfWeek.Saturday, saturday, cancellationToken) };
        var earliestDueDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now.AddMinutes(-5), zone).DateTime);
        if (earliestDueDate < saturday)
            weeks.Add(await schedules.GetWeekAsync(DayOfWeek.Saturday, saturday.AddDays(-7), cancellationToken));
        var latestPossibleStart = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now.AddHours(26), zone).DateTime);
        if (latestPossibleStart >= saturday.AddDays(7))
            weeks.Add(await schedules.GetWeekAsync(DayOfWeek.Saturday, saturday.AddDays(7), cancellationToken));
        return weeks.SelectMany(week => week.Days).SelectMany(day => day.Blocks)
            .Where(block => block.IsActive && block.OutcomeStatus is null && block.ReminderMinutesBefore.HasValue)
            .Select(block =>
            {
                var localStart = block.ScheduledDate.ToDateTime(TimeOnly.MinValue).AddMinutes(block.StartMinute);
                var localReminder = localStart.AddMinutes(-block.ReminderMinutesBefore!.Value);
                var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localReminder, DateTimeKind.Unspecified), zone);
                return new ScheduleReminder(
                    $"{block.Id:N}:{block.OriginalDate:yyyyMMdd}:{block.ScheduledDate:yyyyMMdd}:{block.StartMinute}",
                    block.ActivityTitle, block.StartLabel, block.ScheduledDate.ToString("yyyy-MM-dd"),
                    new DateTimeOffset(utc, TimeSpan.Zero));
            })
            .Where(item => item.RemindAtUtc >= now.AddMinutes(-5) && item.RemindAtUtc <= now.AddHours(24))
            .OrderBy(item => item.RemindAtUtc)
            .ToList();
    }

    public static TimeZoneInfo TehranTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.CreateCustomTimeZone("Asia/Tehran", TimeSpan.FromHours(3.5), "Tehran", "Tehran"); }
    }
}
