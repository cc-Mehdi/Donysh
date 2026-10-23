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
        ["heart"] = "♡",
        ["home"] = "⌂",
        ["coffee"] = "☕",
        ["fitness"] = "◆",
        ["code"] = "⌘",
        ["art"] = "✎",
        ["food"] = "◈",
        ["travel"] = "✈",
        ["people"] = "♧",
        ["goal"] = "◎",
        ["nature"] = "❧",
        ["sun"] = "☼",
        ["calendar"] = "▦",
        ["idea"] = "✧",
        ["computer"] = "▣",
        ["cycling"] = "◇",
        ["photo"] = "▧",
        ["writing"] = "✍",
        ["finance"] = "◉",
        ["game"] = "♠"
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
        ["heart"] = "سلامت",
        ["home"] = "خانه",
        ["coffee"] = "استراحت",
        ["fitness"] = "ورزش",
        ["code"] = "برنامه‌نویسی",
        ["art"] = "هنر",
        ["food"] = "خوراک",
        ["travel"] = "سفر",
        ["people"] = "جمع دوستان",
        ["goal"] = "هدف",
        ["nature"] = "طبیعت",
        ["sun"] = "صبح",
        ["calendar"] = "برنامه",
        ["idea"] = "ایده",
        ["computer"] = "رایانه",
        ["cycling"] = "دوچرخه",
        ["photo"] = "عکاسی",
        ["writing"] = "نوشتن",
        ["finance"] = "مالی",
        ["game"] = "بازی"
    };

    public static readonly IReadOnlyDictionary<string, string> IconSymbols = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["circle"] = "circle", ["briefcase"] = "briefcase", ["book"] = "book-open",
        ["graduation"] = "graduation-cap", ["activity"] = "activity", ["spark"] = "sparkles",
        ["moon"] = "moon", ["music"] = "music", ["heart"] = "heart",
        ["home"] = "house", ["coffee"] = "coffee", ["fitness"] = "dumbbell",
        ["code"] = "code-xml", ["art"] = "palette", ["food"] = "utensils",
        ["travel"] = "plane", ["people"] = "users", ["goal"] = "target",
        ["nature"] = "leaf", ["sun"] = "sun", ["calendar"] = "calendar-days",
        ["idea"] = "lightbulb", ["computer"] = "laptop", ["cycling"] = "bike",
        ["photo"] = "camera", ["writing"] = "pen-tool", ["finance"] = "wallet",
        ["game"] = "gamepad-2"
    };

    public static readonly IReadOnlyDictionary<string, string> Colors = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["#0D9488"] = "سبزآبی",
        ["#2563EB"] = "آبی",
        ["#7C3AED"] = "بنفش",
        ["#EA580C"] = "نارنجی",
        ["#DB2777"] = "صورتی",
        ["#475569"] = "خاکستری",
        ["#059669"] = "سبز زمردی",
        ["#65A30D"] = "سبز روشن",
        ["#0891B2"] = "فیروزه‌ای",
        ["#0284C7"] = "آبی آسمانی",
        ["#4F46E5"] = "نیلی",
        ["#C026D3"] = "ارغوانی",
        ["#DC2626"] = "قرمز",
        ["#D97706"] = "کهربایی",
        ["#BE123C"] = "سرخابی",
        ["#0F766E"] = "سبز تیره"
    };

    public static ActivityTemplate? FindTemplate(string key) =>
        Templates.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal));

    public static string IconGlyph(string key) => Icons.GetValueOrDefault(key, Icons["circle"]);
    public static string IconLabel(string key) => IconLabels.GetValueOrDefault(key, IconLabels["circle"]);
    public static string IconSymbol(string key) => IconSymbols.GetValueOrDefault(key, IconSymbols["circle"]);
    public static string ColorClass(string colorHex) => colorHex.ToUpperInvariant() switch
    {
        "#2563EB" => "activity-blue",
        "#7C3AED" => "activity-purple",
        "#EA580C" => "activity-orange",
        "#DB2777" => "activity-pink",
        "#475569" => "activity-slate",
        "#059669" => "activity-emerald",
        "#65A30D" => "activity-lime",
        "#0891B2" => "activity-cyan",
        "#0284C7" => "activity-sky",
        "#4F46E5" => "activity-indigo",
        "#C026D3" => "activity-fuchsia",
        "#DC2626" => "activity-red",
        "#D97706" => "activity-amber",
        "#BE123C" => "activity-rose",
        "#0F766E" => "activity-deep-teal",
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
