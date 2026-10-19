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
    public async Task<WeekScheduleView> GetWeekAsync(DayOfWeek selectedDay, DateOnly? requestedWeekStart = null, CancellationToken cancellationToken = default)
    {
        var subject = GetSubject();
        if (!WeekCalendar.Days.Contains(selectedDay)) selectedDay = DayOfWeek.Saturday;
        var weekStart = requestedWeekStart is null ? WeekCalendar.CurrentSaturday() : SaturdayOf(requestedWeekStart.Value);
        var weekEnd = weekStart.AddDays(6);
        var activities = await db.Activities.AsNoTracking().Where(x => x.UserSubject == subject && x.IsActive)
            .OrderBy(x => x.CreatedAtUtc).Select(x => new ScheduleActivityOption(x.Id, x.Title, x.ColorHex, x.IconKey)).ToListAsync(cancellationToken);
        var patterns = await db.SchedulePatterns.AsNoTracking().Where(x => x.UserSubject == subject).Include(x => x.Activity)
            .OrderBy(x => x.DayOfWeek).ThenBy(x => x.StartMinute).ToListAsync(cancellationToken);
        var overrides = await db.ScheduleOccurrenceOverrides.AsNoTracking().Where(x => x.UserSubject == subject
            && ((x.OriginalDate >= weekStart && x.OriginalDate <= weekEnd) || (x.ScheduledDate >= weekStart && x.ScheduledDate <= weekEnd)))
            .ToListAsync(cancellationToken);
        var overrideMap = overrides.ToDictionary(x => (x.PatternId, x.OriginalDate));
        var blocks = new List<ScheduleBlockItem>();

        foreach (var pattern in patterns)
        {
            var originalDate = DateForDay(weekStart, pattern.DayOfWeek);
            if (overrideMap.TryGetValue((pattern.Id, originalDate), out var changed))
            {
                if (!changed.IsCancelled && changed.ScheduledDate >= weekStart && changed.ScheduledDate <= weekEnd)
                    blocks.Add(ToBlock(pattern, originalDate, changed.ScheduledDate, changed.StartMinute, changed.DurationMinutes, true));
            }
            else blocks.Add(ToBlock(pattern, originalDate, originalDate, pattern.StartMinute, pattern.DurationMinutes, false));
        }
        foreach (var changed in overrides.Where(x => !x.IsCancelled && (x.OriginalDate < weekStart || x.OriginalDate > weekEnd)))
        {
            var pattern = patterns.SingleOrDefault(x => x.Id == changed.PatternId);
            if (pattern is not null && changed.ScheduledDate >= weekStart && changed.ScheduledDate <= weekEnd)
                blocks.Add(ToBlock(pattern, changed.OriginalDate, changed.ScheduledDate, changed.StartMinute, changed.DurationMinutes, true));
        }

        var goals = await db.Activities.AsNoTracking().Where(x => x.UserSubject == subject && x.IsActive).OrderBy(x => x.CreatedAtUtc)
            .Select(x => new { x.Id, x.Title, x.ColorHex, Goal = x.WeeklyGoal == null ? 0 : x.WeeklyGoal.TargetMinutes }).ToListAsync(cancellationToken);
        var summaries = goals.Select(x => new ActivityPlanSummary(x.Id, x.Title, x.ColorHex, x.Goal,
            blocks.Where(b => b.ActivityId == x.Id && b.IsActive).Sum(b => b.DurationMinutes))).ToList();
        var days = WeekCalendar.Days.Select((day, index) =>
        {
            var date = weekStart.AddDays(index);
            return new WeekDayView(date, day, WeekCalendar.DayName(day), WeekCalendar.PersianDate(date), day == selectedDay,
                blocks.Where(x => x.ScheduledDate == date).OrderBy(x => x.StartMinute).ToList());
        }).ToList();
        return new WeekScheduleView(weekStart, activities, days, summaries, blocks.Where(x => x.IsActive).Sum(x => x.DurationMinutes));
    }

    public async Task<ActivityCommandResult> SaveAsync(Guid? patternId, SchedulePatternForm input, CancellationToken cancellationToken = default)
    {
        var subject = GetSubject();
        if (!TryParse(input, out var startMinute, out var error)) return ActivityCommandResult.Failure(error);
        if (!await db.Activities.AnyAsync(x => x.Id == input.ActivityId && x.UserSubject == subject && x.IsActive, cancellationToken))
            return ActivityCommandResult.Failure("فعالیت انتخاب‌شده در حساب شما پیدا نشد یا غیرفعال است.");
        RecurringSchedulePattern pattern;
        if (patternId is null)
        {
            var now = DateTimeOffset.UtcNow;
            pattern = new RecurringSchedulePattern { Id = Guid.NewGuid(), UserSubject = subject, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
            db.SchedulePatterns.Add(pattern);
        }
        else
        {
            pattern = await db.SchedulePatterns.Include(x => x.Slots).SingleOrDefaultAsync(x => x.Id == patternId && x.UserSubject == subject, cancellationToken)
                ?? null!;
            if (pattern is null) return ActivityCommandResult.Failure("بازهٔ زمانی پیدا نشد.");
        }
        if (pattern.IsActive)
        {
            var conflict = await FindPatternConflictAsync(subject, input.DayOfWeek, startMinute, input.DurationMinutes, patternId, cancellationToken);
            if (conflict is not null) return ActivityCommandResult.Failure($"این زمان با «{conflict.ActivityTitle}» از {conflict.StartLabel} تا {conflict.EndLabel} تداخل دارد.");
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
            return ActivityCommandResult.Success(patternId is null ? "زمان ثابت به برنامهٔ هفته اضافه شد." : "همهٔ رخدادهای این برنامه ویرایش شدند.");
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        { return ActivityCommandResult.Failure("این بازه با زمان دیگری تداخل دارد؛ برنامه را تازه کنید و دوباره تلاش کنید."); }
    }

    public async Task<ActivityCommandResult> ApplyInteractionAsync(ScheduleInteractionForm input, CancellationToken cancellationToken = default)
    {
        var subject = GetSubject();
        if (input.StartMinute is < 0 or > 1410 || input.StartMinute % 30 != 0 || input.DurationMinutes is < 30 or > 1440
            || input.DurationMinutes % 30 != 0 || input.StartMinute + input.DurationMinutes > 1440)
            return ActivityCommandResult.Failure("زمان انتخاب‌شده معتبر نیست.");
        var pattern = await db.SchedulePatterns.Include(x => x.Slots).SingleOrDefaultAsync(x => x.Id == input.PatternId && x.UserSubject == subject, cancellationToken);
        if (pattern is null) return ActivityCommandResult.Failure("برنامهٔ انتخاب‌شده پیدا نشد.");
        if (input.Scope == "series")
            return await SaveAsync(pattern.Id, new SchedulePatternForm { ActivityId = pattern.ActivityId, DayOfWeek = input.TargetDate.DayOfWeek,
                StartTime = FormatMinute(input.StartMinute), DurationMinutes = input.DurationMinutes, Note = pattern.Note }, cancellationToken);
        if (input.Scope != "occurrence") return ActivityCommandResult.Failure("دامنهٔ تغییر معتبر نیست.");
        if (input.OriginalDate.DayOfWeek != pattern.DayOfWeek) return ActivityCommandResult.Failure("تاریخ رخداد با الگوی برنامه هماهنگ نیست.");
        var conflict = await FindOccurrenceConflictAsync(subject, input.TargetDate, input.StartMinute, input.DurationMinutes, pattern.Id, input.OriginalDate, cancellationToken);
        if (conflict is not null) return ActivityCommandResult.Failure($"این زمان با «{conflict.ActivityTitle}» از {conflict.StartLabel} تا {conflict.EndLabel} تداخل دارد.");

        var changed = await db.ScheduleOccurrenceOverrides.SingleOrDefaultAsync(x => x.UserSubject == subject && x.PatternId == pattern.Id
            && x.OriginalDate == input.OriginalDate, cancellationToken);
        if (input.TargetDate == input.OriginalDate && input.StartMinute == pattern.StartMinute && input.DurationMinutes == pattern.DurationMinutes)
        {
            if (changed is not null) db.ScheduleOccurrenceOverrides.Remove(changed);
        }
        else
        {
            var now = DateTimeOffset.UtcNow;
            if (changed is null)
            {
                changed = new ScheduleOccurrenceOverride { Id = Guid.NewGuid(), PatternId = pattern.Id, UserSubject = subject,
                    OriginalDate = input.OriginalDate, CreatedAtUtc = now };
                db.ScheduleOccurrenceOverrides.Add(changed);
            }
            changed.ScheduledDate = input.TargetDate;
            changed.StartMinute = input.StartMinute;
            changed.DurationMinutes = input.DurationMinutes;
            changed.IsCancelled = false;
            changed.UpdatedAtUtc = now;
        }
        try { await db.SaveChangesAsync(cancellationToken); return ActivityCommandResult.Success("فقط همین رخداد ویرایش شد."); }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        { return ActivityCommandResult.Failure("این رخداد هم‌زمان تغییر کرده است؛ صفحه را تازه کنید و دوباره تلاش کنید."); }
    }

    public async Task<ActivityCommandResult> CancelOccurrenceAsync(Guid patternId, DateOnly originalDate, CancellationToken cancellationToken = default)
    {
        var subject = GetSubject();
        var pattern = await db.SchedulePatterns.SingleOrDefaultAsync(x => x.Id == patternId && x.UserSubject == subject, cancellationToken);
        if (pattern is null || originalDate.DayOfWeek != pattern.DayOfWeek) return ActivityCommandResult.Failure("رخداد انتخاب‌شده پیدا نشد.");
        var changed = await db.ScheduleOccurrenceOverrides.SingleOrDefaultAsync(x => x.PatternId == patternId && x.UserSubject == subject
            && x.OriginalDate == originalDate, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (changed is null)
        {
            changed = new ScheduleOccurrenceOverride { Id = Guid.NewGuid(), PatternId = patternId, UserSubject = subject,
                OriginalDate = originalDate, ScheduledDate = originalDate, StartMinute = pattern.StartMinute, DurationMinutes = pattern.DurationMinutes,
                CreatedAtUtc = now };
            db.ScheduleOccurrenceOverrides.Add(changed);
        }
        changed.IsCancelled = true;
        changed.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);
        return ActivityCommandResult.Success("این رخداد از هفته حذف شد و الگوی هفته‌های بعد باقی ماند.");
    }

    public async Task<ActivityCommandResult> ToggleAsync(Guid patternId, CancellationToken cancellationToken = default)
    {
        var subject = GetSubject();
        var pattern = await db.SchedulePatterns.Include(x => x.Slots).Include(x => x.Activity)
            .SingleOrDefaultAsync(x => x.Id == patternId && x.UserSubject == subject, cancellationToken);
        if (pattern is null) return ActivityCommandResult.Failure("بازهٔ زمانی پیدا نشد.");
        if (!pattern.IsActive)
        {
            var conflict = await FindPatternConflictAsync(subject, pattern.DayOfWeek, pattern.StartMinute, pattern.DurationMinutes, pattern.Id, cancellationToken);
            if (conflict is not null) return ActivityCommandResult.Failure($"فعال‌سازی با «{conflict.ActivityTitle}» تداخل دارد.");
            pattern.IsActive = true; SyncSlots(pattern, SlotIndexes(pattern.StartMinute, pattern.DurationMinutes));
        }
        else { pattern.IsActive = false; db.ScheduleSlots.RemoveRange(pattern.Slots); pattern.Slots.Clear(); }
        pattern.UpdatedAtUtc = DateTimeOffset.UtcNow;
        try { await db.SaveChangesAsync(cancellationToken); return ActivityCommandResult.Success(pattern.IsActive ? "بازه دوباره فعال شد." : "بازه موقتاً غیرفعال شد."); }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { return ActivityCommandResult.Failure("این بازه با زمان دیگری تداخل دارد."); }
    }

    public async Task<ActivityCommandResult> DeleteAsync(Guid patternId, CancellationToken cancellationToken = default)
    {
        var subject = GetSubject();
        var pattern = await db.SchedulePatterns.SingleOrDefaultAsync(x => x.Id == patternId && x.UserSubject == subject, cancellationToken);
        if (pattern is null) return ActivityCommandResult.Failure("بازهٔ زمانی پیدا نشد.");
        db.SchedulePatterns.Remove(pattern); await db.SaveChangesAsync(cancellationToken);
        return ActivityCommandResult.Success("الگوی تکرارشونده و همهٔ استثناهای آن حذف شد.");
    }

    private async Task<ScheduleBlockItem?> FindPatternConflictAsync(string subject, DayOfWeek day, int start, int duration, Guid? excludedId, CancellationToken ct)
    {
        var end = start + duration;
        var item = await db.SchedulePatterns.AsNoTracking().Include(x => x.Activity).FirstOrDefaultAsync(x => x.UserSubject == subject && x.IsActive
            && x.DayOfWeek == day && x.Id != excludedId && x.StartMinute < end && start < x.StartMinute + x.DurationMinutes, ct);
        if (item is null) return null;
        var date = DateForDay(WeekCalendar.CurrentSaturday(), day);
        return ToBlock(item, date, date, item.StartMinute, item.DurationMinutes, false);
    }

    private async Task<ScheduleBlockItem?> FindOccurrenceConflictAsync(string subject, DateOnly date, int start, int duration,
        Guid excludedPatternId, DateOnly excludedOriginalDate, CancellationToken ct)
    {
        var end = start + duration;
        var patterns = await db.SchedulePatterns.AsNoTracking().Include(x => x.Activity)
            .Where(x => x.UserSubject == subject && x.IsActive && x.DayOfWeek == date.DayOfWeek).ToListAsync(ct);
        var overrides = await db.ScheduleOccurrenceOverrides.AsNoTracking()
            .Where(x => x.UserSubject == subject && (x.OriginalDate == date || x.ScheduledDate == date)).ToListAsync(ct);
        var map = overrides.ToDictionary(x => (x.PatternId, x.OriginalDate));
        foreach (var pattern in patterns)
        {
            if (pattern.Id == excludedPatternId && date == excludedOriginalDate) continue;
            if (map.TryGetValue((pattern.Id, date), out var changed))
            {
                if (changed.IsCancelled || changed.ScheduledDate != date) continue;
                if (Overlaps(start, end, changed.StartMinute, changed.DurationMinutes)) return ToBlock(pattern, date, date, changed.StartMinute, changed.DurationMinutes, true);
            }
            else if (Overlaps(start, end, pattern.StartMinute, pattern.DurationMinutes)) return ToBlock(pattern, date, date, pattern.StartMinute, pattern.DurationMinutes, false);
        }
        foreach (var changed in overrides.Where(x => x.ScheduledDate == date && x.OriginalDate != date && !x.IsCancelled))
        {
            if (changed.PatternId == excludedPatternId && changed.OriginalDate == excludedOriginalDate) continue;
            if (!Overlaps(start, end, changed.StartMinute, changed.DurationMinutes)) continue;
            var pattern = await db.SchedulePatterns.AsNoTracking().Include(x => x.Activity)
                .SingleAsync(x => x.Id == changed.PatternId && x.UserSubject == subject, ct);
            return ToBlock(pattern, changed.OriginalDate, date, changed.StartMinute, changed.DurationMinutes, true);
        }
        return null;
    }

    private static ScheduleBlockItem ToBlock(RecurringSchedulePattern p, DateOnly original, DateOnly scheduled, int start, int duration, bool changed) =>
        new(p.Id, p.ActivityId, p.Activity.Title, p.Activity.ColorHex, p.Activity.IconKey, scheduled.DayOfWeek, start, duration,
            p.Note, p.IsActive, original, scheduled, changed);

    private static bool TryParse(SchedulePatternForm input, out int startMinute, out string error)
    {
        startMinute = 0; error = string.Empty;
        if (!WeekCalendar.Days.Contains(input.DayOfWeek)) { error = "روز انتخاب‌شده معتبر نیست."; return false; }
        if (!TimeOnly.TryParseExact(input.StartTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)) { error = "ساعت شروع معتبر نیست."; return false; }
        startMinute = start.Hour * 60 + start.Minute;
        if (startMinute % 30 != 0) { error = "ساعت شروع باید روی نیم‌ساعت باشد."; return false; }
        if (input.DurationMinutes is < 30 or > 1440 || input.DurationMinutes % 30 != 0) { error = "مدت فعالیت باید مضربی از ۳۰ دقیقه باشد."; return false; }
        if (startMinute + input.DurationMinutes > 1440) { error = "بازهٔ زمانی نمی‌تواند از پایان روز عبور کند."; return false; }
        if (input.Note?.Trim().Length > 160) { error = "یادداشت بیش از حد طولانی است."; return false; }
        return true;
    }

    private static bool Overlaps(int start, int end, int otherStart, int otherDuration) => otherStart < end && start < otherStart + otherDuration;
    private static string FormatMinute(int minute) => $"{minute / 60:00}:{minute % 60:00}";
    private static DateOnly SaturdayOf(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek - (int)DayOfWeek.Saturday + 7) % 7));
    private static DateOnly DateForDay(DateOnly start, DayOfWeek day) => start.AddDays(((int)day - (int)DayOfWeek.Saturday + 7) % 7);
    private static IReadOnlyList<int> SlotIndexes(int start, int duration) => Enumerable.Range(start / 30, duration / 30).ToList();
    private static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static void SyncSlots(RecurringSchedulePattern pattern, IReadOnlyCollection<int> desiredIndexes)
    {
        var desired = desiredIndexes.ToHashSet();
        foreach (var obsolete in pattern.Slots.Where(x => !desired.Contains(x.SlotIndex)).ToList()) pattern.Slots.Remove(obsolete);
        foreach (var slot in pattern.Slots) { slot.UserSubject = pattern.UserSubject; slot.DayOfWeek = pattern.DayOfWeek; }
        var existing = pattern.Slots.Select(x => x.SlotIndex).ToHashSet();
        foreach (var index in desired.Where(x => !existing.Contains(x))) pattern.Slots.Add(new RecurringScheduleSlot
        { PatternId = pattern.Id, UserSubject = pattern.UserSubject, DayOfWeek = pattern.DayOfWeek, SlotIndex = index });
    }

    private string GetSubject() => httpContextAccessor.HttpContext?.User.FindFirstValue("sub") is { Length: > 0 } subject
        ? subject : throw new InvalidOperationException("شناسهٔ حساب کاربر در نشست ورود وجود ندارد.");
}
