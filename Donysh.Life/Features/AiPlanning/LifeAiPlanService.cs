using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Donysh.Life.Data;
using Donysh.Life.Domain;
using Donysh.Life.Features.Activities;
using Microsoft.EntityFrameworkCore;

namespace Donysh.Life.Features.AiPlanning;

public sealed class LifeAiPlanService(LifeDbContext db, IHttpContextAccessor context)
{
    public const int MaxJsonLength = 48_000;
    public const int MaxActivities = 20;
    public const int MaxSlots = 80;

    public static readonly string Prompt = $$"""
        تو دستیار برنامه‌ریزی زندگی من هستی. روتینی را که در همین گفتگو برایت توضیح داده‌ام به فعالیت‌ها و برنامهٔ هفتگی واقع‌بینانه تبدیل کن. فقط یک فایل JSON معتبر، بدون Markdown یا توضیح اضافی، با ساختار زیر بساز:
        {
          "version": 1,
          "activities": [
            {
              "title": "مطالعه",
              "category": "یادگیری",
              "color": "#2563EB",
              "icon": "book",
              "weeklyHours": 5,
              "slots": [
                { "day": "Saturday", "start": "09:00", "durationMinutes": 60 }
              ]
            }
          ]
        }
        روزها فقط Saturday, Sunday, Monday, Tuesday, Wednesday, Thursday, Friday باشند. ساعت با قالب 24 ساعته HH:mm و روی نیم‌ساعت (00 یا 30) باشد. مدت هر بازه مضرب 30 دقیقه و بین 30 دقیقه تا 24 ساعت باشد. عبور از نیمه‌شب و هم‌زمانی فعالیت‌ها مجاز است؛ خواب شبانه را یک بازه بساز. برای هر فعالیت حداکثر 10 بازه، در مجموع حداکثر 20 فعالیت و 80 بازه بساز. weeklyHours بین 0.5 و 168 باشد. رنگ را فقط از {{string.Join(", ", ActivityCatalog.Colors.Keys)}} و نماد را فقط از {{string.Join(", ", ActivityCatalog.Icons.Keys)}} انتخاب کن. هر فعالیت عنوان یکتا داشته باشد. اگر اطلاعات زمان‌بندی کافی ندارم، پیش از تولید JSON از من بپرس؛ زمان یا تعهدی را حدس نزن. خروجی را به صورت فایل .json قابل دانلود بده.
        """;

    public static readonly string ImprovementPrompt = $$"""
        تو دستیار بهبود برنامهٔ زندگی من هستی. فایل JSON «وضعیت فعلی دُنیش» را به‌عنوان زمینه بخوان: activities الگوهای تکرارشونده‌اند، currentWeek تغییرهای همین هفته را نشان می‌دهد و progress عملکرد ثبت‌شدهٔ هفتهٔ جاری است. از من بپرس چه چیزی را می‌خواهم بهتر کنم؛ با توجه به عملکرد واقعی، پیشنهادهای کوچک و قابل اجرا بده و زمان یا تعهدی را حدس نزن.

        پس از توافق، فقط یک فایل JSON معتبر و قابل دانلود، بدون Markdown یا توضیح اضافی، با قالب version: 1 و activities بساز. فقط فعالیت‌های تغییرکرده یا تازه را در activities بیاور؛ فعالیت‌های جاافتاده بدون تغییر می‌مانند. برای هر فعالیت موجود، existingActivityId را دقیقاً از فایل وضعیت فعلی کپی کن و عنوانش را نگه دار. برای فعالیت تازه، existingActivityId را حذف کن. نمونه:
        {
          "version": 1,
          "activities": [
            {
              "existingActivityId": "شناسهٔ واقعی فعالیت از فایل وضعیت فعلی",
              "title": "مطالعه",
              "category": "یادگیری",
              "color": "#2563EB",
              "icon": "book",
              "weeklyHours": 5,
              "slots": [{ "day": "Saturday", "start": "09:00", "durationMinutes": 60, "reminderMinutesBefore": null }]
            }
          ]
        }
        هر فعالیت موجودِ انتخاب‌شده با هدف، ظاهر و همهٔ بازه‌های slots پیشنهادی‌اش به‌روزرسانی می‌شود؛ فعالیت‌های دیگر و سابقهٔ عملکرد حذف نمی‌شوند. روزها فقط Saturday, Sunday, Monday, Tuesday, Wednesday, Thursday, Friday باشند. ساعت HH:mm و روی نیم‌ساعت باشد؛ مدت مضرب 30 دقیقه و بین 30 تا 1440 دقیقه باشد. عبور از نیمه‌شب و هم‌زمانی مجاز است. reminderMinutesBefore اختیاری و بین 0 تا 120 دقیقه است. weeklyHours بین 0.5 و 168 و مضرب نیم‌ساعت باشد. برای هر فعالیت حداکثر 10 بازه، در مجموع حداکثر 20 فعالیت و 80 بازه بیاور. رنگ فقط از {{string.Join(", ", ActivityCatalog.Colors.Keys)}} و نماد فقط از {{string.Join(", ", ActivityCatalog.Icons.Keys)}} باشد. فایل وضعیت فعلی را به‌عنوان خروجی پیشنهادی برنگردان.
        """;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, MaxDepth = 12 };

    public static (AiPlan? Plan, string? Error) Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (null, "فایل یا متن JSON را وارد کنید.");
        if (json.Length > MaxJsonLength) return (null, "حجم JSON بیشتر از حد مجاز است.");
        try
        {
            var plan = JsonSerializer.Deserialize<AiPlan>(json, JsonOptions);
            if (plan?.Version != 1 || plan.Activities is not { Count: > 0 and <= MaxActivities })
                return (null, "ساختار برنامه معتبر نیست؛ نسخه باید ۱ و تعداد فعالیت‌ها بین ۱ تا ۲۰ باشد.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            var existingIds = new HashSet<Guid>();
            var slotCount = 0;
            foreach (var activity in plan.Activities)
            {
                if (activity is null) return (null, "یکی از فعالیت‌های JSON معتبر نیست.");
                var title = activity.Title?.Trim() ?? "";
                var category = activity.Category?.Trim();
                if (title.Length is < 2 or > 80 || category?.Length > 60 || !names.Add(NormalizeTitle(title)))
                    return (null, "عنوان یا دسته‌بندی فعالیت نامعتبر یا تکراری است.");
                if (activity.ExistingActivityId == Guid.Empty
                    || (activity.ExistingActivityId is Guid existingId && !existingIds.Add(existingId)))
                    return (null, "شناسهٔ فعالیت موجود نامعتبر یا تکراری است.");
                if (activity.WeeklyHours is < 0.5m or > 168m || (activity.WeeklyHours * 2) % 1 != 0)
                    return (null, $"هدف هفتگی «{title}» باید بین نیم تا ۱۶۸ ساعت و مضرب نیم‌ساعت باشد.");
                if (activity.Color is null || !ActivityCatalog.Colors.ContainsKey(activity.Color.ToUpperInvariant())
                    || activity.Icon is null || !ActivityCatalog.Icons.ContainsKey(activity.Icon))
                    return (null, $"رنگ یا نماد «{title}» معتبر نیست.");
                if (activity.Slots is null || activity.Slots.Count > 10
                    || (activity.ExistingActivityId is null && activity.Slots.Count == 0))
                    return (null, activity.ExistingActivityId is null
                        ? $"برای «{title}» دست‌کم یک و حداکثر ۱۰ زمان مشخص کنید."
                        : $"برای «{title}» حداکثر ۱۰ زمان مشخص کنید.");
                slotCount += activity.Slots!.Count;
                if (slotCount > MaxSlots) return (null, "برنامه بیش از ۸۰ بازهٔ زمانی دارد.");
                var uniqueSlots = new HashSet<(string? Day, string? Start, int Duration)>();
                foreach (var slot in activity.Slots)
                {
                    if (slot is null) return (null, $"یکی از زمان‌های «{title}» معتبر نیست.");
                    if (!TrySlot(slot, out _, out _)) return (null, $"روز، ساعت یا مدت «{title}» معتبر نیست.");
                    if (!uniqueSlots.Add((slot.Day, slot.Start, slot.DurationMinutes)))
                        return (null, $"یک زمان برای «{title}» تکرار شده است.");
                }
            }
            return (plan, null);
        }
        catch (JsonException) { return (null, "JSON قابل خواندن نیست. فقط محتوای فایل JSON را وارد کنید."); }
    }

    public async Task<string?> ValidateAndApplyAsync(AiPlan plan, IReadOnlyList<AiActivitySelection> selections, CancellationToken ct)
    {
        if (selections.Count != plan.Activities!.Count) return "پیش‌نمایش تغییر کرده است؛ فایل را دوباره بارگذاری کنید.";
        var subject = context.HttpContext?.User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(subject)) return "نشست ورود معتبر نیست؛ دوباره وارد شوید.";
        var chosen = new List<(AiActivity Activity, List<(DayOfWeek Day, int Start, int Duration, int? Reminder)> Slots)>();
        foreach (var (activity, index) in plan.Activities.Select((value, index) => (value, index)))
        {
            var selection = selections[index];
            if (selection.Slots.Count != activity.Slots!.Count) return "پیش‌نمایش تغییر کرده است؛ فایل را دوباره بارگذاری کنید.";
            if (!selection.Included) continue;
            var slots = new List<(DayOfWeek, int, int, int?)>();
            foreach (var edit in selection.Slots.Where(x => x.Included))
            {
                if (!TrySlot(new AiSlot { Day = edit.Day, Start = edit.Start, DurationMinutes = edit.DurationMinutes,
                        ReminderMinutesBefore = edit.ReminderMinutesBefore }, out var day, out var start))
                    return $"روز، ساعت یا مدت «{activity.Title}» معتبر نیست.";
                slots.Add((day, start, edit.DurationMinutes, edit.ReminderMinutesBefore));
            }
            chosen.Add((activity, slots));
        }
        if (chosen.Count == 0) return "دست‌کم یک فعالیت را انتخاب کنید.";
        var referenceError = await ValidateExistingTargetsAsync(plan, ct);
        if (referenceError is not null) return referenceError;
        var updateIds = chosen.Where(x => x.Activity.ExistingActivityId.HasValue)
            .Select(x => x.Activity.ExistingActivityId!.Value).ToList();
        var existing = await db.Activities.Where(x => x.UserSubject == subject && updateIds.Contains(x.Id))
            .Include(x => x.WeeklyGoal).Include(x => x.SchedulePatterns).ThenInclude(x => x.Slots)
            .ToDictionaryAsync(x => x.Id, ct);
        var newTitles = chosen.Where(x => x.Activity.ExistingActivityId is null)
            .Select(x => NormalizeTitle(x.Activity.Title!)).ToList();
        var duplicate = await db.Activities.AsNoTracking()
            .Where(x => x.UserSubject == subject && newTitles.Contains(x.NormalizedTitle))
            .Select(x => x.Title).FirstOrDefaultAsync(ct);
        if (duplicate is not null)
            return $"فعالیت «{duplicate}» از قبل وجود دارد. آن را از انتخاب خارج کنید یا در فعالیت‌ها ویرایش کنید.";
        foreach (var (source, _) in chosen.Where(x => x.Activity.ExistingActivityId.HasValue))
            if (!existing.TryGetValue(source.ExistingActivityId!.Value, out var current)
                || !current.IsActive || current.NormalizedTitle != NormalizeTitle(source.Title!))
                return $"عنوان یا شناسهٔ «{source.Title}» با فعالیت موجود هماهنگ نیست؛ فایل وضعیت فعلی را دوباره دریافت کنید.";

        var oldPatternIds = existing.Values.SelectMany(x => x.SchedulePatterns.Where(pattern => pattern.IsActive))
            .Select(pattern => pattern.Id).ToList();
        var protectedPatternIds = (await db.ScheduleOccurrenceRecords.AsNoTracking()
                .Where(x => x.UserSubject == subject && oldPatternIds.Contains(x.PatternId))
                .Select(x => x.PatternId).ToListAsync(ct))
            .Concat(await db.ScheduleOccurrenceOverrides.AsNoTracking()
                .Where(x => x.UserSubject == subject && oldPatternIds.Contains(x.PatternId))
                .Select(x => x.PatternId).ToListAsync(ct)).ToHashSet();
        var now = DateTimeOffset.UtcNow;
        if (!await db.Profiles.AnyAsync(x => x.UserSubject == subject, ct))
            db.Profiles.Add(new LifeProfile { Id = Guid.NewGuid(), UserSubject = subject, CreatedAtUtc = now, UpdatedAtUtc = now });
        foreach (var (source, slots) in chosen)
        {
            LifeActivity activity;
            if (source.ExistingActivityId is Guid id)
            {
                activity = existing[id];
                var desired = slots.ToList();
                foreach (var pattern in activity.SchedulePatterns.Where(pattern => pattern.IsActive).ToList())
                {
                    var match = desired.FindIndex(slot => slot.Day == pattern.DayOfWeek
                        && slot.Start == pattern.StartMinute && slot.Duration == pattern.DurationMinutes);
                    if (match >= 0)
                    {
                        pattern.ReminderMinutesBefore = desired[match].Reminder;
                        pattern.UpdatedAtUtc = now;
                        desired.RemoveAt(match);
                    }
                    else if (protectedPatternIds.Contains(pattern.Id))
                    {
                        pattern.IsActive = false;
                        pattern.UpdatedAtUtc = now;
                        db.ScheduleSlots.RemoveRange(pattern.Slots);
                        pattern.Slots.Clear();
                    }
                    else db.SchedulePatterns.Remove(pattern);
                }
                AddPatterns(activity, subject, desired, now);
            }
            else
            {
                activity = new LifeActivity
                {
                    Id = Guid.NewGuid(), UserSubject = subject, Title = source.Title!.Trim(),
                    NormalizedTitle = NormalizeTitle(source.Title!), IsActive = true,
                    CreatedAtUtc = now, UpdatedAtUtc = now
                };
                db.Activities.Add(activity);
                AddPatterns(activity, subject, slots, now);
            }
            activity.Category = source.Category?.Trim();
            activity.ColorHex = source.Color!.ToUpperInvariant();
            activity.IconKey = source.Icon!;
            activity.UpdatedAtUtc = now;
            activity.WeeklyGoal ??= new WeeklyGoal
            {
                Id = Guid.NewGuid(), ActivityId = activity.Id, UserSubject = subject, CreatedAtUtc = now
            };
            activity.WeeklyGoal.TargetMinutes = decimal.ToInt32(source.WeeklyHours * 60m);
            activity.WeeklyGoal.UpdatedAtUtc = now;
        }
        try { await db.SaveChangesAsync(ct); return null; }
        catch (DbUpdateException) { return "برنامه هم‌زمان تغییر کرده یا یک فعالیت تکراری است؛ صفحه را تازه کنید و دوباره تلاش کنید."; }
    }

    public async Task<string?> ValidateExistingTargetsAsync(AiPlan plan, CancellationToken ct)
    {
        var subject = context.HttpContext?.User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(subject)) return "نشست ورود معتبر نیست؛ دوباره وارد شوید.";
        var ids = plan.Activities!.Where(item => item.ExistingActivityId.HasValue)
            .Select(item => item.ExistingActivityId!.Value).ToList();
        if (ids.Count == 0) return null;
        var owned = await db.Activities.AsNoTracking()
            .Where(item => item.UserSubject == subject && item.IsActive && ids.Contains(item.Id))
            .Select(item => item.Id).ToListAsync(ct);
        return owned.Count == ids.Count ? null : "یک فعالیت موجود به حساب شما تعلق ندارد یا غیرفعال شده است؛ فایل وضعیت فعلی را دوباره دریافت کنید.";
    }

    private void AddPatterns(LifeActivity activity, string subject,
        IEnumerable<(DayOfWeek Day, int Start, int Duration, int? Reminder)> slots, DateTimeOffset now)
    {
        foreach (var (day, start, duration, reminder) in slots)
            db.SchedulePatterns.Add(new RecurringSchedulePattern
            {
                Id = Guid.NewGuid(), UserSubject = subject, ActivityId = activity.Id,
                DayOfWeek = day, StartMinute = start, DurationMinutes = duration,
                ReminderMinutesBefore = reminder, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now
            });
    }

    public static bool TrySlot(AiSlot slot, out DayOfWeek day, out int start)
    {
        day = default; start = 0;
        if (!Enum.TryParse(slot.Day, false, out day) || !Enum.IsDefined(day) || slot.Day != day.ToString()
            || !TimeOnly.TryParseExact(slot.Start, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) return false;
        start = time.Hour * 60 + time.Minute;
        return start % 30 == 0 && slot.DurationMinutes is >= 30 and <= 1440
            && slot.DurationMinutes % 30 == 0
            && slot.ReminderMinutesBefore is null or >= 0 and <= 120;
    }

    private static string NormalizeTitle(string value) => value.Trim().Replace('ي', 'ی').Replace('ك', 'ک').ToUpperInvariant();
}

public sealed class AiPlan { public int Version { get; set; } public List<AiActivity>? Activities { get; set; } }
public sealed class AiActivity
{
    public Guid? ExistingActivityId { get; set; }
    public string? Title { get; set; }
    public string? Category { get; set; }
    public string? Color { get; set; }
    public string? Icon { get; set; }
    public decimal WeeklyHours { get; set; }
    public List<AiSlot>? Slots { get; set; }
}
public sealed class AiSlot { public string? Day { get; set; } public string? Start { get; set; } public int DurationMinutes { get; set; } public int? ReminderMinutesBefore { get; set; } }
public sealed class AiActivitySelection { public bool Included { get; set; } = true; public List<AiSlotSelection> Slots { get; set; } = []; }
public sealed class AiSlotSelection { public bool Included { get; set; } = true; public string? Day { get; set; } public string? Start { get; set; } public int DurationMinutes { get; set; } public int? ReminderMinutesBefore { get; set; } }
