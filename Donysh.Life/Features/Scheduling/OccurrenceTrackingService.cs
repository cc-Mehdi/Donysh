using System.Security.Claims;
using Donysh.Life.Data;
using Donysh.Life.Domain;
using Donysh.Life.Features.Activities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Donysh.Life.Features.Scheduling;

public sealed class OccurrenceTrackingService(
    LifeDbContext db,
    IHttpContextAccessor httpContextAccessor,
    WeeklyScheduleService weeklySchedules)
{
    public async Task<ActivityCommandResult> RecordAsync(OccurrenceRecordForm input, CancellationToken cancellationToken = default)
    {
        var subject = GetSubject();
        if (!Enum.IsDefined(input.Status)) return ActivityCommandResult.Failure("وضعیت انتخاب‌شده معتبر نیست.");
        var pattern = await db.SchedulePatterns.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == input.PatternId && item.UserSubject == subject, cancellationToken);
        if (pattern is null || input.OriginalDate.DayOfWeek != pattern.DayOfWeek)
            return ActivityCommandResult.Failure("رخداد انتخاب‌شده در حساب شما پیدا نشد.");

        var occurrenceOverride = await db.ScheduleOccurrenceOverrides.SingleOrDefaultAsync(item =>
            item.PatternId == pattern.Id && item.UserSubject == subject && item.OriginalDate == input.OriginalDate,
            cancellationToken);
        if (occurrenceOverride?.IsCancelled == true)
            return ActivityCommandResult.Failure("رخداد حذف‌شده را نمی‌توان به‌عنوان عملکرد ثبت کرد.");
        var scheduledDate = occurrenceOverride?.ScheduledDate ?? input.OriginalDate;
        var plannedMinutes = occurrenceOverride?.DurationMinutes ?? pattern.DurationMinutes;
        var record = await db.ScheduleOccurrenceRecords.SingleOrDefaultAsync(item =>
            item.PatternId == pattern.Id && item.UserSubject == subject && item.OriginalDate == input.OriginalDate,
            cancellationToken);
        var restoreDate = record?.Status == OccurrenceOutcomeStatus.Moved ? record.ScheduledDate : scheduledDate;
        var validation = Validate(input, restoreDate, plannedMinutes, out var actualMinutes, out var movedToDate);
        if (validation is not null) return ActivityCommandResult.Failure(validation);

        var now = DateTimeOffset.UtcNow;
        var wasMoved = record?.Status == OccurrenceOutcomeStatus.Moved;
        if (input.Status == OccurrenceOutcomeStatus.Moved)
        {
            var conflict = await FindMoveConflictAsync(pattern.Id, input.OriginalDate, movedToDate!.Value,
                occurrenceOverride?.StartMinute ?? pattern.StartMinute, plannedMinutes, cancellationToken);
            if (conflict is not null)
                return ActivityCommandResult.Failure($"این انتقال با «{conflict.ActivityTitle}» از {conflict.StartLabel} تا {conflict.EndLabel} تداخل دارد.");

            if (occurrenceOverride is null)
            {
                occurrenceOverride = new ScheduleOccurrenceOverride
                {
                    Id = Guid.NewGuid(), PatternId = pattern.Id, UserSubject = subject,
                    OriginalDate = input.OriginalDate, StartMinute = pattern.StartMinute,
                    DurationMinutes = pattern.DurationMinutes, CreatedByOutcome = true, CreatedAtUtc = now
                };
                db.ScheduleOccurrenceOverrides.Add(occurrenceOverride);
            }
            occurrenceOverride.ScheduledDate = movedToDate.Value;
            occurrenceOverride.IsCancelled = false;
            occurrenceOverride.UpdatedAtUtc = now;
        }
        else if (wasMoved && occurrenceOverride is not null)
        {
            RestoreMovedOverride(occurrenceOverride, restoreDate);
        }
        if (record is null)
        {
            record = new ScheduleOccurrenceRecord
            {
                Id = Guid.NewGuid(), PatternId = pattern.Id, UserSubject = subject,
                OriginalDate = input.OriginalDate, CreatedAtUtc = now
            };
            db.ScheduleOccurrenceRecords.Add(record);
        }
        record.ScheduledDate = restoreDate;
        record.Status = input.Status;
        record.PlannedMinutes = plannedMinutes;
        record.ActualMinutes = actualMinutes;
        record.MovedToDate = movedToDate;
        record.Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        record.UpdatedAtUtc = now;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return ActivityCommandResult.Success("عملکرد این برنامه ثبت شد.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return ActivityCommandResult.Failure("عملکرد این رخداد هم‌زمان تغییر کرده است؛ صفحه را تازه کنید.");
        }
    }

    public async Task<ActivityCommandResult> ClearAsync(Guid patternId, DateOnly originalDate, CancellationToken cancellationToken = default)
    {
        var subject = GetSubject();
        var record = await db.ScheduleOccurrenceRecords.SingleOrDefaultAsync(item =>
            item.PatternId == patternId && item.UserSubject == subject && item.OriginalDate == originalDate,
            cancellationToken);
        if (record is null) return ActivityCommandResult.Failure("عملکرد ثبت‌شده‌ای برای حذف پیدا نشد.");
        if (record.Status == OccurrenceOutcomeStatus.Moved)
        {
            var occurrenceOverride = await db.ScheduleOccurrenceOverrides.SingleOrDefaultAsync(item =>
                item.PatternId == patternId && item.UserSubject == subject && item.OriginalDate == originalDate,
                cancellationToken);
            if (occurrenceOverride is not null) RestoreMovedOverride(occurrenceOverride, record.ScheduledDate);
        }
        db.ScheduleOccurrenceRecords.Remove(record);
        await db.SaveChangesAsync(cancellationToken);
        return ActivityCommandResult.Success("وضعیت عملکرد پاک شد.");
    }

    private async Task<ScheduleBlockItem?> FindMoveConflictAsync(Guid patternId, DateOnly originalDate, DateOnly targetDate,
        int startMinute, int durationMinutes, CancellationToken cancellationToken)
    {
        var week = await weeklySchedules.GetWeekAsync(targetDate.DayOfWeek, targetDate, cancellationToken);
        var endMinute = startMinute + durationMinutes;
        return week.Days.Single(day => day.Date == targetDate).Blocks.FirstOrDefault(block =>
            block.IsActive && (block.Id != patternId || block.OriginalDate != originalDate)
            && block.StartMinute < endMinute && startMinute < block.EndMinute);
    }

    private void RestoreMovedOverride(ScheduleOccurrenceOverride occurrenceOverride, DateOnly restoreDate)
    {
        if (occurrenceOverride.CreatedByOutcome)
        {
            db.ScheduleOccurrenceOverrides.Remove(occurrenceOverride);
            return;
        }
        occurrenceOverride.ScheduledDate = restoreDate;
        occurrenceOverride.IsCancelled = false;
        occurrenceOverride.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static string? Validate(OccurrenceRecordForm input, DateOnly scheduledDate, int plannedMinutes,
        out int? actualMinutes, out DateOnly? movedToDate)
    {
        actualMinutes = null;
        movedToDate = null;
        if (input.Note?.Trim().Length > 160) return "یادداشت بیش از حد طولانی است.";
        switch (input.Status)
        {
            case OccurrenceOutcomeStatus.Completed:
                actualMinutes = input.ActualMinutes ?? plannedMinutes;
                if (actualMinutes is < 1 or > 1440) return "زمان واقعی انجام کار باید بین ۱ تا ۱۴۴۰ دقیقه باشد.";
                return null;
            case OccurrenceOutcomeStatus.Missed:
                actualMinutes = 0;
                return null;
            case OccurrenceOutcomeStatus.Partial:
                if (input.ActualMinutes is not null && input.ActualMinutes is < 1)
                    return "زمان واقعی باید بیشتر از صفر دقیقه باشد.";
                if (input.ActualMinutes >= plannedMinutes)
                    return "زمان برنامهٔ نیمه‌تمام باید کمتر از زمان برنامه‌ریزی‌شده باشد.";
                actualMinutes = input.ActualMinutes;
                return null;
            case OccurrenceOutcomeStatus.Moved:
                if (input.MovedToDate is null || input.MovedToDate <= scheduledDate)
                    return "برای برنامهٔ انتقال‌یافته، یک تاریخ بعد از روز برنامه انتخاب کنید.";
                movedToDate = input.MovedToDate;
                return null;
            default:
                return "وضعیت انتخاب‌شده معتبر نیست.";
        }
    }

    private string GetSubject() => httpContextAccessor.HttpContext?.User.FindFirstValue("sub") is { Length: > 0 } subject
        ? subject : throw new InvalidOperationException("شناسهٔ حساب کاربر در نشست ورود وجود ندارد.");
}
