using System.Globalization;
using System.Security.Claims;
using Donysh.Life.Data;
using Donysh.Life.Domain;
using Donysh.Life.Features.Activities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Donysh.Life.Features.Scheduling;

public sealed class WeeklyScheduleService(LifeDbContext db, IHttpContextAccessor httpContextAccessor)
{
    public async Task<WeekScheduleView> GetWeekAsync(DayOfWeek selectedDay, CancellationToken cancellationToken = default)
    {
        var subject = GetSubject();
        if (!WeekCalendar.Days.Contains(selectedDay)) selectedDay = DayOfWeek.Saturday;

        var activities = await db.Activities.AsNoTracking()
            .Where(item => item.UserSubject == subject && item.IsActive)
            .OrderBy(item => item.CreatedAtUtc)
            .Select(item => new ScheduleActivityOption(item.Id, item.Title, item.ColorHex, item.IconKey))
            .ToListAsync(cancellationToken);
        var blocks = await db.SchedulePatterns.AsNoTracking()
            .Where(item => item.UserSubject == subject)
            .OrderBy(item => item.DayOfWeek)
            .ThenBy(item => item.StartMinute)
            .Select(item => new ScheduleBlockItem(
                item.Id,
                item.ActivityId,
                item.Activity.Title,
                item.Activity.ColorHex,
                item.Activity.IconKey,
                item.DayOfWeek,
                item.StartMinute,
                item.DurationMinutes,
                item.Note,
                item.IsActive))
            .ToListAsync(cancellationToken);
        var summaries = await db.Activities.AsNoTracking()
            .Where(item => item.UserSubject == subject && item.IsActive)
            .OrderBy(item => item.CreatedAtUtc)
            .Select(item => new ActivityPlanSummary(
                item.Id,
                item.Title,
                item.ColorHex,
                item.WeeklyGoal == null ? 0 : item.WeeklyGoal.TargetMinutes,
                item.SchedulePatterns.Where(pattern => pattern.IsActive).Sum(pattern => (int?)pattern.DurationMinutes) ?? 0))
            .ToListAsync(cancellationToken);

        var weekStart = WeekCalendar.CurrentSaturday();
        var days = WeekCalendar.Days.Select((day, index) => new WeekDayView(
            day,
            WeekCalendar.DayName(day),
            WeekCalendar.PersianDate(weekStart.AddDays(index)),
            day == selectedDay,
            blocks.Where(item => item.DayOfWeek == day).ToList())).ToList();
        return new WeekScheduleView(
            weekStart,
            activities,
            days,
            summaries,
            blocks.Where(item => item.IsActive).Sum(item => item.DurationMinutes));
    }

    public async Task<ActivityCommandResult> SaveAsync(
        Guid? patternId,
        SchedulePatternForm input,
        CancellationToken cancellationToken = default)
    {
        var subject = GetSubject();
        if (!TryParse(input, out var startMinute, out var error)) return ActivityCommandResult.Failure(error);

        var activityExists = await db.Activities.AnyAsync(item =>
            item.Id == input.ActivityId && item.UserSubject == subject && item.IsActive,
            cancellationToken);
        if (!activityExists) return ActivityCommandResult.Failure("فعالیت انتخاب‌شده در حساب شما پیدا نشد یا غیرفعال است.");

        RecurringSchedulePattern pattern;
        if (patternId is null)
        {
            var now = DateTimeOffset.UtcNow;
            pattern = new RecurringSchedulePattern
            {
                Id = Guid.NewGuid(),
                UserSubject = subject,
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.SchedulePatterns.Add(pattern);
        }
        else
        {
            var existing = await db.SchedulePatterns.Include(item => item.Slots)
                .SingleOrDefaultAsync(item => item.Id == patternId && item.UserSubject == subject, cancellationToken);
            if (existing is null) return ActivityCommandResult.Failure("بازهٔ زمانی پیدا نشد.");
            pattern = existing;
        }

        if (pattern.IsActive)
        {
            var conflict = await FindConflictAsync(subject, input.DayOfWeek, startMinute, input.DurationMinutes, patternId, cancellationToken);
            if (conflict is not null)
                return ActivityCommandResult.Failure($"این زمان با «{conflict.ActivityTitle}» از {conflict.StartLabel} تا {conflict.EndLabel} تداخل دارد.");
        }

        pattern.ActivityId = input.ActivityId;
        pattern.DayOfWeek = input.DayOfWeek;
        pattern.StartMinute = startMinute;
        pattern.DurationMinutes = input.DurationMinutes;
        pattern.Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        pattern.UpdatedAtUtc = DateTimeOffset.UtcNow;
        SyncSlots(pattern, pattern.IsActive ? SlotIndexes(startMinute, input.DurationMinutes) : []);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return ActivityCommandResult.Success(patternId is null ? "زمان ثابت به برنامهٔ هفته اضافه شد." : "بازهٔ زمانی ویرایش شد.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return ActivityCommandResult.Failure("این بازه با زمان دیگری تداخل دارد؛ برنامه را تازه کنید و دوباره تلاش کنید.");
        }
    }

    public async Task<ActivityCommandResult> ToggleAsync(Guid patternId, CancellationToken cancellationToken = default)
    {
        var subject = GetSubject();
        var pattern = await db.SchedulePatterns.Include(item => item.Slots)
            .Include(item => item.Activity)
            .SingleOrDefaultAsync(item => item.Id == patternId && item.UserSubject == subject, cancellationToken);
        if (pattern is null) return ActivityCommandResult.Failure("بازهٔ زمانی پیدا نشد.");

        if (!pattern.IsActive)
        {
            var conflict = await FindConflictAsync(subject, pattern.DayOfWeek, pattern.StartMinute, pattern.DurationMinutes, pattern.Id, cancellationToken);
            if (conflict is not null)
                return ActivityCommandResult.Failure($"فعال‌سازی با «{conflict.ActivityTitle}» تداخل دارد.");
            pattern.IsActive = true;
            SyncSlots(pattern, SlotIndexes(pattern.StartMinute, pattern.DurationMinutes));
        }
        else
        {
            pattern.IsActive = false;
            db.ScheduleSlots.RemoveRange(pattern.Slots);
            pattern.Slots.Clear();
        }

        pattern.UpdatedAtUtc = DateTimeOffset.UtcNow;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return ActivityCommandResult.Success(pattern.IsActive ? "بازه دوباره فعال شد." : "بازه موقتاً غیرفعال شد.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return ActivityCommandResult.Failure("این بازه با زمان دیگری تداخل دارد.");
        }
    }

    public async Task<ActivityCommandResult> DeleteAsync(Guid patternId, CancellationToken cancellationToken = default)
    {
        var subject = GetSubject();
        var pattern = await db.SchedulePatterns.SingleOrDefaultAsync(
            item => item.Id == patternId && item.UserSubject == subject,
            cancellationToken);
        if (pattern is null) return ActivityCommandResult.Failure("بازهٔ زمانی پیدا نشد.");
        db.SchedulePatterns.Remove(pattern);
        await db.SaveChangesAsync(cancellationToken);
        return ActivityCommandResult.Success("بازه از برنامهٔ هفتگی حذف شد.");
    }

    private async Task<ScheduleBlockItem?> FindConflictAsync(
        string subject,
        DayOfWeek day,
        int startMinute,
        int durationMinutes,
        Guid? excludedId,
        CancellationToken cancellationToken)
    {
        var endMinute = startMinute + durationMinutes;
        return await db.SchedulePatterns.AsNoTracking()
            .Where(item => item.UserSubject == subject
                && item.IsActive
                && item.DayOfWeek == day
                && item.Id != excludedId
                && item.StartMinute < endMinute
                && startMinute < item.StartMinute + item.DurationMinutes)
            .Select(item => new ScheduleBlockItem(
                item.Id, item.ActivityId, item.Activity.Title, item.Activity.ColorHex, item.Activity.IconKey,
                item.DayOfWeek, item.StartMinute, item.DurationMinutes, item.Note, item.IsActive))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static bool TryParse(SchedulePatternForm input, out int startMinute, out string error)
    {
        startMinute = 0;
        error = string.Empty;
        if (!WeekCalendar.Days.Contains(input.DayOfWeek)) { error = "روز انتخاب‌شده معتبر نیست."; return false; }
        if (!TimeOnly.TryParseExact(input.StartTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
        { error = "ساعت شروع معتبر نیست."; return false; }
        startMinute = start.Hour * 60 + start.Minute;
        if (startMinute % 30 != 0) { error = "ساعت شروع باید روی نیم‌ساعت باشد."; return false; }
        if (input.DurationMinutes is < 30 or > 1440 || input.DurationMinutes % 30 != 0)
        { error = "مدت فعالیت باید مضربی از ۳۰ دقیقه باشد."; return false; }
        if (startMinute + input.DurationMinutes > 1440) { error = "بازهٔ زمانی نمی‌تواند از پایان روز عبور کند."; return false; }
        if (input.Note?.Trim().Length > 160) { error = "یادداشت بیش از حد طولانی است."; return false; }
        return true;
    }

    private static IReadOnlyList<int> SlotIndexes(int startMinute, int durationMinutes) =>
        Enumerable.Range(startMinute / 30, durationMinutes / 30).ToList();

    private static void SyncSlots(RecurringSchedulePattern pattern, IReadOnlyCollection<int> desiredIndexes)
    {
        var desired = desiredIndexes.ToHashSet();
        foreach (var obsolete in pattern.Slots.Where(item => !desired.Contains(item.SlotIndex)).ToList())
            pattern.Slots.Remove(obsolete);
        foreach (var slot in pattern.Slots)
        {
            slot.UserSubject = pattern.UserSubject;
            slot.DayOfWeek = pattern.DayOfWeek;
        }
        var existing = pattern.Slots.Select(item => item.SlotIndex).ToHashSet();
        foreach (var slotIndex in desired.Where(item => !existing.Contains(item)))
            pattern.Slots.Add(new RecurringScheduleSlot
            {
                PatternId = pattern.Id,
                UserSubject = pattern.UserSubject,
                DayOfWeek = pattern.DayOfWeek,
                SlotIndex = slotIndex
            });
    }

    private string GetSubject() =>
        httpContextAccessor.HttpContext?.User.FindFirstValue("sub") is { Length: > 0 } subject
            ? subject
            : throw new InvalidOperationException("شناسهٔ حساب کاربر در نشست ورود وجود ندارد.");
}
