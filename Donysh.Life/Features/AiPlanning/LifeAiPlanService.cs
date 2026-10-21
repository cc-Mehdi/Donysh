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

    public static readonly string Prompt = """
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
        روزها فقط Saturday, Sunday, Monday, Tuesday, Wednesday, Thursday, Friday باشند. ساعت با قالب 24 ساعته HH:mm و روی نیم‌ساعت (00 یا 30) باشد. مدت هر بازه مضرب 30 دقیقه و حداقل 30 دقیقه باشد. بازه‌ها از نیمه‌شب عبور نکنند و با هم تداخل نداشته باشند. برای هر فعالیت حداکثر 10 بازه، در مجموع حداکثر 20 فعالیت و 80 بازه بساز. weeklyHours بین 0.5 و 168 باشد. رنگ را فقط از #0D9488, #2563EB, #7C3AED, #EA580C, #DB2777, #475569 و نماد را فقط از circle, briefcase, book, graduation, activity, spark, moon, music, heart انتخاب کن. هر فعالیت عنوان یکتا داشته باشد. اگر اطلاعات زمان‌بندی کافی ندارم، پیش از تولید JSON از من بپرس؛ زمان یا تعهدی را حدس نزن. خروجی را به صورت فایل .json قابل دانلود بده.
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
            var slotCount = 0;
            foreach (var activity in plan.Activities)
            {
                if (activity is null) return (null, "یکی از فعالیت‌های JSON معتبر نیست.");
                var title = activity.Title?.Trim() ?? "";
                var category = activity.Category?.Trim();
                if (title.Length is < 2 or > 80 || category?.Length > 60 || !names.Add(NormalizeTitle(title)))
                    return (null, "عنوان یا دسته‌بندی فعالیت نامعتبر یا تکراری است.");
                if (activity.WeeklyHours is < 0.5m or > 168m || (activity.WeeklyHours * 2) % 1 != 0)
                    return (null, $"هدف هفتگی «{title}» باید بین نیم تا ۱۶۸ ساعت و مضرب نیم‌ساعت باشد.");
                if (activity.Color is null || !ActivityCatalog.Colors.ContainsKey(activity.Color.ToUpperInvariant())
                    || activity.Icon is null || !ActivityCatalog.Icons.ContainsKey(activity.Icon))
                    return (null, $"رنگ یا نماد «{title}» معتبر نیست.");
                if (activity.Slots is not { Count: > 0 and <= 10 })
                    return (null, $"برای «{title}» دست‌کم یک و حداکثر ۱۰ زمان مشخص کنید.");
                slotCount += activity.Slots!.Count;
                if (slotCount > MaxSlots) return (null, "برنامه بیش از ۸۰ بازهٔ زمانی دارد.");
                foreach (var slot in activity.Slots)
                {
                    if (slot is null) return (null, $"یکی از زمان‌های «{title}» معتبر نیست.");
                    if (!TrySlot(slot, out _, out _)) return (null, $"روز، ساعت یا مدت «{title}» معتبر نیست.");
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
        var chosen = new List<(AiActivity Activity, List<(DayOfWeek Day, int Start, int Duration)> Slots)>();
        var proposedSlots = new HashSet<(DayOfWeek, int)>();
        foreach (var (activity, index) in plan.Activities.Select((value, index) => (value, index)))
        {
            var selection = selections[index];
            if (selection.Slots.Count != activity.Slots!.Count) return "پیش‌نمایش تغییر کرده است؛ فایل را دوباره بارگذاری کنید.";
            if (!selection.Included) continue;
            var slots = new List<(DayOfWeek, int, int)>();
            foreach (var edit in selection.Slots.Where(x => x.Included))
            {
                if (!TrySlot(new AiSlot { Day = edit.Day, Start = edit.Start, DurationMinutes = edit.DurationMinutes }, out var day, out var start))
                    return $"روز، ساعت یا مدت «{activity.Title}» معتبر نیست.";
                for (var minute = start; minute < start + edit.DurationMinutes; minute += 30)
                    if (!proposedSlots.Add((day, minute))) return "زمان‌های انتخاب‌شده با هم تداخل دارند؛ آن‌ها را ویرایش کنید.";
                slots.Add((day, start, edit.DurationMinutes));
            }
            chosen.Add((activity, slots));
        }
        if (chosen.Count == 0) return "دست‌کم یک فعالیت را انتخاب کنید.";

        var titles = chosen.Select(x => NormalizeTitle(x.Activity.Title!)).ToList();
        var existingTitles = await db.Activities.AsNoTracking().Where(x => x.UserSubject == subject && titles.Contains(x.NormalizedTitle))
            .Select(x => x.Title).ToListAsync(ct);
        if (existingTitles.Count > 0) return $"فعالیت «{existingTitles[0]}» از قبل وجود دارد. آن را از انتخاب خارج کنید یا در فعالیت‌ها ویرایش کنید.";
        var days = chosen.SelectMany(x => x.Slots).Select(x => x.Day).Distinct().ToList();
        var existing = await db.SchedulePatterns.AsNoTracking().Where(x => x.UserSubject == subject && x.IsActive && days.Contains(x.DayOfWeek))
            .Select(x => new { x.DayOfWeek, x.StartMinute, x.DurationMinutes, x.Activity.Title }).ToListAsync(ct);
        foreach (var row in chosen.SelectMany(x => x.Slots))
        {
            var conflict = existing.FirstOrDefault(x => x.DayOfWeek == row.Day && x.StartMinute < row.Start + row.Duration
                && row.Start < x.StartMinute + x.DurationMinutes);
            if (conflict is not null) return $"زمان انتخاب‌شده با «{conflict.Title}» تداخل دارد؛ روز یا ساعت را تغییر دهید.";
        }

        var now = DateTimeOffset.UtcNow;
        if (!await db.Profiles.AnyAsync(x => x.UserSubject == subject, ct))
            db.Profiles.Add(new LifeProfile { Id = Guid.NewGuid(), UserSubject = subject, CreatedAtUtc = now, UpdatedAtUtc = now });
        foreach (var (source, slots) in chosen)
        {
            var activity = new LifeActivity
            {
                Id = Guid.NewGuid(), UserSubject = subject, Title = source.Title!.Trim(), NormalizedTitle = NormalizeTitle(source.Title!),
                Category = source.Category?.Trim(), ColorHex = source.Color!.ToUpperInvariant(), IconKey = source.Icon!,
                IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now
            };
            activity.WeeklyGoal = new WeeklyGoal
            {
                Id = Guid.NewGuid(), ActivityId = activity.Id, UserSubject = subject,
                TargetMinutes = decimal.ToInt32(source.WeeklyHours * 60m), CreatedAtUtc = now, UpdatedAtUtc = now
            };
            foreach (var (day, start, duration) in slots)
            {
                var pattern = new RecurringSchedulePattern
                {
                    Id = Guid.NewGuid(), UserSubject = subject, ActivityId = activity.Id, DayOfWeek = day,
                    StartMinute = start, DurationMinutes = duration, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now
                };
                for (var minute = start; minute < start + duration; minute += 30)
                    pattern.Slots.Add(new RecurringScheduleSlot { PatternId = pattern.Id, UserSubject = subject, DayOfWeek = day, SlotIndex = minute / 30 });
                activity.SchedulePatterns.Add(pattern);
            }
            db.Activities.Add(activity);
        }
        try { await db.SaveChangesAsync(ct); return null; }
        catch (DbUpdateException) { return "برنامه هم‌زمان تغییر کرده یا یک فعالیت/زمان تکراری است؛ صفحه را تازه کنید و دوباره تلاش کنید."; }
    }

    public static bool TrySlot(AiSlot slot, out DayOfWeek day, out int start)
    {
        day = default; start = 0;
        if (!Enum.TryParse(slot.Day, false, out day) || !Enum.IsDefined(day) || slot.Day != day.ToString()
            || !TimeOnly.TryParseExact(slot.Start, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) return false;
        start = time.Hour * 60 + time.Minute;
        return start % 30 == 0 && slot.DurationMinutes is >= 30 and <= 1440
            && slot.DurationMinutes % 30 == 0 && start + slot.DurationMinutes <= 1440;
    }

    private static string NormalizeTitle(string value) => value.Trim().Replace('ي', 'ی').Replace('ك', 'ک').ToUpperInvariant();
}

public sealed class AiPlan { public int Version { get; set; } public List<AiActivity>? Activities { get; set; } }
public sealed class AiActivity
{
    public string? Title { get; set; }
    public string? Category { get; set; }
    public string? Color { get; set; }
    public string? Icon { get; set; }
    public decimal WeeklyHours { get; set; }
    public List<AiSlot>? Slots { get; set; }
}
public sealed class AiSlot { public string? Day { get; set; } public string? Start { get; set; } public int DurationMinutes { get; set; } }
public sealed class AiActivitySelection { public bool Included { get; set; } = true; public List<AiSlotSelection> Slots { get; set; } = []; }
public sealed class AiSlotSelection { public bool Included { get; set; } = true; public string? Day { get; set; } public string? Start { get; set; } public int DurationMinutes { get; set; } }
