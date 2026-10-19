namespace Donysh.Life.Features.Activities;

public static class ActivityCatalog
{
    public static readonly IReadOnlyList<ActivityTemplate> Templates =
    [
        new("work", "کار", "briefcase", "#0D9488", "شغلی", 20),
        new("study", "مطالعه", "book", "#2563EB", "یادگیری", 7),
        new("university", "دانشگاه", "graduation", "#7C3AED", "یادگیری", 25),
        new("exercise", "ورزش", "activity", "#EA580C", "سلامت", 4),
        new("personal", "پروژهٔ شخصی", "spark", "#DB2777", "شخصی", 6),
        new("rest", "استراحت هدفمند", "moon", "#475569", "تندرستی", 7)
    ];

    public static readonly IReadOnlyDictionary<string, string> Icons = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["circle"] = "●",
        ["briefcase"] = "▣",
        ["book"] = "▤",
        ["graduation"] = "◇",
        ["activity"] = "↗",
        ["spark"] = "✦",
        ["moon"] = "◒",
        ["music"] = "♫",
        ["heart"] = "♡"
    };

    public static readonly IReadOnlyDictionary<string, string> IconLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["circle"] = "ساده",
        ["briefcase"] = "کار",
        ["book"] = "کتاب",
        ["graduation"] = "آموزش",
        ["activity"] = "تحرک",
        ["spark"] = "درخشش",
        ["moon"] = "استراحت",
        ["music"] = "موسیقی",
        ["heart"] = "سلامت"
    };

    public static readonly IReadOnlyDictionary<string, string> Colors = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["#0D9488"] = "سبزآبی",
        ["#2563EB"] = "آبی",
        ["#7C3AED"] = "بنفش",
        ["#EA580C"] = "نارنجی",
        ["#DB2777"] = "صورتی",
        ["#475569"] = "خاکستری"
    };

    public static ActivityTemplate? FindTemplate(string key) =>
        Templates.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal));

    public static string IconGlyph(string key) => Icons.GetValueOrDefault(key, Icons["circle"]);
    public static string IconLabel(string key) => IconLabels.GetValueOrDefault(key, IconLabels["circle"]);
    public static string ColorClass(string colorHex) => colorHex.ToUpperInvariant() switch
    {
        "#2563EB" => "activity-blue",
        "#7C3AED" => "activity-purple",
        "#EA580C" => "activity-orange",
        "#DB2777" => "activity-pink",
        "#475569" => "activity-slate",
        _ => "activity-teal"
    };
}

public sealed record ActivityTemplate(
    string Key,
    string Title,
    string IconKey,
    string ColorHex,
    string Category,
    decimal SuggestedHours);
