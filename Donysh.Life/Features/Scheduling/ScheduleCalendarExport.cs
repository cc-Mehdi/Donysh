namespace Donysh.Life.Features.Scheduling;

public static class ScheduleCalendarExport
{
    public static string Build(ScheduleBlockItem block)
    {
        var start = block.ScheduledDate.ToDateTime(TimeOnly.MinValue).AddMinutes(block.StartMinute);
        var end = start.AddMinutes(block.DurationMinutes);
        var zone = ReminderScheduleService.TehranTimeZone();
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(start, DateTimeKind.Unspecified), zone);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(end, DateTimeKind.Unspecified), zone);
        var trigger = block.ReminderMinutesBefore == 0 ? "PT0S" : $"-PT{block.ReminderMinutesBefore}M";
        var title = Escape(block.ActivityTitle);
        var note = Escape(block.Note ?? "برنامهٔ دنیش زندگی");
        return string.Join("\r\n", new[]
        {
            "BEGIN:VCALENDAR", "VERSION:2.0", "CALSCALE:GREGORIAN", "PRODID:-//Donysh//Life//FA",
            "BEGIN:VEVENT", $"UID:{block.Id:N}-{block.OriginalDate:yyyyMMdd}@life.donysh.ir",
            $"DTSTAMP:{DateTime.UtcNow:yyyyMMdd'T'HHmmss'Z'}", $"DTSTART:{startUtc:yyyyMMdd'T'HHmmss'Z'}",
            $"DTEND:{endUtc:yyyyMMdd'T'HHmmss'Z'}", $"SUMMARY:{title}", $"DESCRIPTION:{note}",
            "BEGIN:VALARM", "ACTION:DISPLAY", $"DESCRIPTION:{title}", $"TRIGGER:{trigger}",
            "END:VALARM", "END:VEVENT", "END:VCALENDAR", ""
        });
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace(";", "\\;", StringComparison.Ordinal).Replace(",", "\\,", StringComparison.Ordinal)
        .Replace("\r\n", "\\n", StringComparison.Ordinal).Replace("\r", "\\n", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal);
}
