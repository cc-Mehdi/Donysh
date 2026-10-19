using System.Security.Claims;
using Donysh.Life.Data;
using Donysh.Life.Domain;
using Microsoft.EntityFrameworkCore;

namespace Donysh.Life.Features.Activities;

public sealed class LifeActivityService(LifeDbContext db, IHttpContextAccessor httpContextAccessor)
{
    public async Task<LifeOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var subject = GetSubject();
        var activities = db.Activities.AsNoTracking().Where(item => item.UserSubject == subject);
        var activityCount = await activities.CountAsync(cancellationToken);
        var activeActivityCount = await activities.CountAsync(item => item.IsActive, cancellationToken);
        var weeklyTargetMinutes = await db.WeeklyGoals.AsNoTracking()
                .Where(item => item.UserSubject == subject && item.Activity.IsActive)
                .SumAsync(item => (int?)item.TargetMinutes, cancellationToken) ?? 0;
        var plannedWeeklyMinutes = await db.SchedulePatterns.AsNoTracking()
            .Where(item => item.UserSubject == subject && item.IsActive)
            .SumAsync(item => (int?)item.DurationMinutes, cancellationToken) ?? 0;
        return new LifeOverview(activityCount, activeActivityCount, weeklyTargetMinutes, plannedWeeklyMinutes);
    }

    public async Task<IReadOnlyList<ActivityListItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        var subject = GetSubject();
        return await db.Activities.AsNoTracking()
            .Where(item => item.UserSubject == subject)
            .OrderByDescending(item => item.IsActive)
            .ThenBy(item => item.CreatedAtUtc)
            .Select(item => new ActivityListItem(
                item.Id,
                item.Title,
                item.ColorHex,
                item.IconKey,
                item.Category,
                item.IsActive,
                item.WeeklyGoal == null ? 0 : item.WeeklyGoal.TargetMinutes))
            .ToListAsync(cancellationToken);
    }

    public async Task EnsureProfileAsync(CancellationToken cancellationToken = default)
    {
        var subject = GetSubject();
        if (await db.Profiles.AnyAsync(item => item.UserSubject == subject, cancellationToken)) return;

        var now = DateTimeOffset.UtcNow;
        db.Profiles.Add(new LifeProfile
        {
            Id = Guid.NewGuid(),
            UserSubject = subject,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ActivityCommandResult> SaveAsync(Guid? activityId, ActivityForm input, CancellationToken cancellationToken = default)
    {
        var subject = GetSubject();
        if (string.IsNullOrWhiteSpace(input.Title) || string.IsNullOrWhiteSpace(input.ColorHex) || string.IsNullOrWhiteSpace(input.IconKey))
            return ActivityCommandResult.Failure("اطلاعات ضروری فعالیت کامل نیست.");
        var title = input.Title.Trim();
        var normalizedTitle = NormalizeTitle(title);
        var category = string.IsNullOrWhiteSpace(input.Category) ? null : input.Category.Trim();
        if (title.Length is < 2 or > 80 || category?.Length > 60)
            return ActivityCommandResult.Failure("عنوان یا دسته‌بندی فعالیت معتبر نیست.");
        if (input.WeeklyHours is < 0.5m or > 168m)
            return ActivityCommandResult.Failure("هدف هفتگی باید بین نیم ساعت تا ۱۶۸ ساعت باشد.");
        var normalizedColor = input.ColorHex.ToUpperInvariant();
        if (!ActivityCatalog.Colors.ContainsKey(normalizedColor))
            return ActivityCommandResult.Failure("رنگ انتخاب‌شده معتبر نیست.");
        if (!ActivityCatalog.Icons.ContainsKey(input.IconKey))
            return ActivityCommandResult.Failure("نماد انتخاب‌شده معتبر نیست.");

        var duplicate = await db.Activities.AnyAsync(item =>
            item.UserSubject == subject && item.NormalizedTitle == normalizedTitle && item.Id != activityId,
            cancellationToken);
        if (duplicate) return ActivityCommandResult.Failure("فعالیتی با این عنوان از قبل وجود دارد.");

        var now = DateTimeOffset.UtcNow;
        LifeActivity activity;
        if (activityId is null)
        {
            activity = new LifeActivity
            {
                Id = Guid.NewGuid(),
                UserSubject = subject,
                CreatedAtUtc = now,
                IsActive = true
            };
            db.Activities.Add(activity);
        }
        else
        {
            var existingActivity = await db.Activities.Include(item => item.WeeklyGoal)
                .SingleOrDefaultAsync(item => item.Id == activityId && item.UserSubject == subject, cancellationToken);
            if (existingActivity is null) return ActivityCommandResult.Failure("فعالیت پیدا نشد.");
            activity = existingActivity;
        }

        activity.Title = title;
        activity.NormalizedTitle = normalizedTitle;
        activity.ColorHex = normalizedColor;
        activity.IconKey = input.IconKey;
        activity.Category = category;
        activity.UpdatedAtUtc = now;

        activity.WeeklyGoal ??= new WeeklyGoal
        {
            Id = Guid.NewGuid(),
            ActivityId = activity.Id,
            UserSubject = subject,
            CreatedAtUtc = now
        };
        activity.WeeklyGoal.TargetMinutes = HoursToMinutes(input.WeeklyHours);
        activity.WeeklyGoal.UpdatedAtUtc = now;

        await EnsureProfileBeforeSavingAsync(subject, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return ActivityCommandResult.Success(activityId is null ? "فعالیت تازه اضافه شد." : "تغییرات فعالیت ذخیره شد.");
    }

    public async Task<ActivityCommandResult> ToggleAsync(Guid activityId, CancellationToken cancellationToken = default)
    {
        var subject = GetSubject();
        var activity = await db.Activities.SingleOrDefaultAsync(
            item => item.Id == activityId && item.UserSubject == subject,
            cancellationToken);
        if (activity is null) return ActivityCommandResult.Failure("فعالیت پیدا نشد.");

        activity.IsActive = !activity.IsActive;
        activity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return ActivityCommandResult.Success(activity.IsActive ? "فعالیت دوباره فعال شد." : "فعالیت غیرفعال شد.");
    }

    public async Task<ActivityCommandResult> DeleteAsync(Guid activityId, CancellationToken cancellationToken = default)
    {
        var subject = GetSubject();
        var activity = await db.Activities.SingleOrDefaultAsync(
            item => item.Id == activityId && item.UserSubject == subject,
            cancellationToken);
        if (activity is null) return ActivityCommandResult.Failure("فعالیت پیدا نشد.");

        if (await db.ScheduleOccurrenceRecords.AnyAsync(item =>
                item.UserSubject == subject && item.Pattern.ActivityId == activityId,
                cancellationToken)
            || await db.ActualActivityEntries.AnyAsync(item =>
                item.UserSubject == subject && item.ActivityId == activityId,
                cancellationToken))
            return ActivityCommandResult.Failure("برای این فعالیت عملکرد واقعی ثبت شده است؛ برای حفظ سابقه آن را غیرفعال کنید.");

        db.Activities.Remove(activity);
        await db.SaveChangesAsync(cancellationToken);
        return ActivityCommandResult.Success("فعالیت حذف شد.");
    }

    public async Task<ActivityCommandResult> CompleteSetupAsync(
        IReadOnlyCollection<SetupActivityInput> suggestions,
        CancellationToken cancellationToken = default)
    {
        var selected = suggestions.Where(item => item.IsSelected).ToList();
        if (selected.Count == 0) return ActivityCommandResult.Failure("دست‌کم یک فعالیت را انتخاب کنید.");
        if (selected.Any(item => item.WeeklyHours is < 0.5m or > 168m))
            return ActivityCommandResult.Failure("هدف زمانی یکی از فعالیت‌ها معتبر نیست.");
        if (selected.Any(item => ActivityCatalog.FindTemplate(item.TemplateKey) is null))
            return ActivityCommandResult.Failure("یکی از فعالیت‌های پیشنهادی معتبر نیست.");

        var subject = GetSubject();
        var existingTitles = await db.Activities.Where(item => item.UserSubject == subject)
            .Select(item => item.NormalizedTitle)
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        await EnsureProfileBeforeSavingAsync(subject, now, cancellationToken);

        foreach (var item in selected)
        {
            var template = ActivityCatalog.FindTemplate(item.TemplateKey);
            if (template is null || existingTitles.Contains(NormalizeTitle(template.Title), StringComparer.Ordinal)) continue;

            var activity = new LifeActivity
            {
                Id = Guid.NewGuid(),
                UserSubject = subject,
                Title = template.Title,
                NormalizedTitle = NormalizeTitle(template.Title),
                ColorHex = template.ColorHex,
                IconKey = template.IconKey,
                Category = template.Category,
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            activity.WeeklyGoal = new WeeklyGoal
            {
                Id = Guid.NewGuid(),
                ActivityId = activity.Id,
                UserSubject = subject,
                TargetMinutes = HoursToMinutes(item.WeeklyHours),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.Activities.Add(activity);
        }

        await db.SaveChangesAsync(cancellationToken);
        return ActivityCommandResult.Success("شروع خوبی است؛ فعالیت‌ها و هدف‌های هفتگی آماده شدند.");
    }

    private async Task EnsureProfileBeforeSavingAsync(string subject, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await db.Profiles.AnyAsync(item => item.UserSubject == subject, cancellationToken)) return;
        db.Profiles.Add(new LifeProfile
        {
            Id = Guid.NewGuid(),
            UserSubject = subject,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
    }

    private string GetSubject() =>
        httpContextAccessor.HttpContext?.User.FindFirstValue("sub") is { Length: > 0 } subject
            ? subject
            : throw new InvalidOperationException("شناسهٔ حساب کاربر در نشست ورود وجود ندارد.");

    private static string NormalizeTitle(string value) => value.Trim()
        .Replace('ي', 'ی')
        .Replace('ك', 'ک')
        .ToUpperInvariant();
    private static int HoursToMinutes(decimal hours) => decimal.ToInt32(decimal.Round(hours * 60m, 0, MidpointRounding.AwayFromZero));
}
