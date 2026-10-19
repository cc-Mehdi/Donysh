using System.Globalization;
using System.Security.Claims;
using Donysh.Life.Data;
using Donysh.Life.Domain;
using Donysh.Life.Features.Activities;
using Donysh.Life.Features.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace Donysh.Life.Features.Tracking;

public sealed class ActualActivityService(LifeDbContext db, IHttpContextAccessor httpContextAccessor)
{
    public async Task<IReadOnlyList<ActualActivityItem>> ListRecentAsync(int days = 7, CancellationToken cancellationToken = default)
    {
        var subject = GetSubject();
        var from = WeekCalendar.Today().AddDays(-Math.Clamp(days, 1, 31) + 1);
        return await db.ActualActivityEntries.AsNoTracking()
            .Where(item => item.UserSubject == subject && item.OccurredDate >= from)
            .OrderByDescending(item => item.OccurredDate).ThenByDescending(item => item.StartMinute).ThenByDescending(item => item.CreatedAtUtc)
            .Select(item => new ActualActivityItem(item.Id, item.ActivityId, item.Activity.Title, item.Activity.ColorHex,
                item.Activity.IconKey, item.OccurredDate, item.StartMinute, item.ActualMinutes, item.Note))
            .ToListAsync(cancellationToken);
    }

    public async Task<ActivityCommandResult> SaveAsync(Guid? entryId, ActualActivityForm input, CancellationToken cancellationToken = default)
    {
        var subject = GetSubject();
        if (input.OccurredDate == default || input.OccurredDate > WeekCalendar.Today() || input.OccurredDate < WeekCalendar.Today().AddYears(-5))
            return ActivityCommandResult.Failure("تاریخ فعالیت باید امروز یا یکی از روزهای گذشته باشد.");
        if (input.ActualMinutes is < 1 or > 1440) return ActivityCommandResult.Failure("مدت واقعی معتبر نیست.");
        if (input.Note?.Trim().Length > 160) return ActivityCommandResult.Failure("یادداشت بیش از حد طولانی است.");
        int? startMinute = null;
        if (!string.IsNullOrWhiteSpace(input.StartTime))
        {
            if (!TimeOnly.TryParseExact(input.StartTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
                return ActivityCommandResult.Failure("ساعت شروع معتبر نیست.");
            startMinute = start.Hour * 60 + start.Minute;
            if (startMinute + input.ActualMinutes > 1440)
                return ActivityCommandResult.Failure("مدت فعالیت نمی‌تواند از پایان همان روز عبور کند.");
        }
        ActualActivityEntry? entry = null;
        if (entryId is not null)
        {
            entry = await db.ActualActivityEntries.SingleOrDefaultAsync(item =>
                item.Id == entryId && item.UserSubject == subject, cancellationToken);
            if (entry is null) return ActivityCommandResult.Failure("فعالیت ثبت‌شده پیدا نشد.");
        }
        var activityExists = await db.Activities.AnyAsync(item => item.Id == input.ActivityId
            && item.UserSubject == subject && (item.IsActive || (entry != null && item.Id == entry.ActivityId)), cancellationToken);
        if (!activityExists) return ActivityCommandResult.Failure("فعالیت انتخاب‌شده در حساب شما پیدا نشد یا غیرفعال است.");

        var now = DateTimeOffset.UtcNow;
        if (entryId is null)
        {
            entry = new ActualActivityEntry { Id = Guid.NewGuid(), UserSubject = subject, CreatedAtUtc = now };
            db.ActualActivityEntries.Add(entry);
        }
        // Existing historical entries remain editable after their activity is deactivated.
        if (entry is null) throw new InvalidOperationException("رکورد فعالیت برای ذخیره آماده نشده است.");
        entry.ActivityId = input.ActivityId;
        entry.OccurredDate = input.OccurredDate;
        entry.StartMinute = startMinute;
        entry.ActualMinutes = input.ActualMinutes;
        entry.Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        entry.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);
        return ActivityCommandResult.Success(entryId is null ? "فعالیت انجام‌شده ثبت شد." : "فعالیت انجام‌شده ویرایش شد.");
    }

    public async Task<ActivityCommandResult> DeleteAsync(Guid entryId, CancellationToken cancellationToken = default)
    {
        var subject = GetSubject();
        var entry = await db.ActualActivityEntries.SingleOrDefaultAsync(item => item.Id == entryId && item.UserSubject == subject, cancellationToken);
        if (entry is null) return ActivityCommandResult.Failure("فعالیت ثبت‌شده پیدا نشد.");
        db.ActualActivityEntries.Remove(entry);
        await db.SaveChangesAsync(cancellationToken);
        return ActivityCommandResult.Success("فعالیت انجام‌شده حذف شد.");
    }

    private string GetSubject() => httpContextAccessor.HttpContext?.User.FindFirstValue("sub") is { Length: > 0 } subject
        ? subject : throw new InvalidOperationException("شناسهٔ حساب کاربر در نشست ورود وجود ندارد.");
}
