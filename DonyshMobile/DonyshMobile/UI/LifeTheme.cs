namespace DonyshMobile.UI;

internal readonly record struct LifeColor(Color Light, Color Dark);

internal static class LifeTheme
{
    internal static readonly LifeColor Canvas = Pair("#F4F7F6", "#0B1928");
    internal static readonly LifeColor Surface = Pair("#FFFFFF", "#17283B");
    internal static readonly LifeColor SurfaceBorder = Pair("#DCE8E5", "#304456");
    internal static readonly LifeColor Ink = Pair("#102A3A", "#EDF6F7");
    internal static readonly LifeColor Muted = Pair("#476468", "#B3C7CE");
    internal static readonly LifeColor AccentText = Pair("#087E78", "#8AF0DF");
    internal static readonly LifeColor Primary = Pair("#087E78", "#0B726C");
    internal static readonly LifeColor OnPrimary = Pair("#FFFFFF", "#FFFFFF");
    internal static readonly LifeColor Focus = Pair("#E4FAF2", "#173F42");
    internal static readonly LifeColor SoftTeal = Pair("#E4F4F0", "#194B4B");
    internal static readonly LifeColor Finance = Pair("#E9E7F9", "#30294F");
    internal static readonly LifeColor FinanceText = Pair("#473592", "#D8CCFF");
    internal static readonly LifeColor Field = Pair("#FFFFFF", "#20384A");
    internal static readonly LifeColor Complete = Pair("#DDF8EA", "#174637");
    internal static readonly LifeColor CompleteText = Pair("#116C4E", "#9CF2CE");
    internal static readonly LifeColor Partial = Pair("#FFF1D9", "#4A3C28");
    internal static readonly LifeColor PartialText = Pair("#8F6108", "#FFDF9C");
    internal static readonly LifeColor Missed = Pair("#FDEBE8", "#4D2B30");
    internal static readonly LifeColor MissedText = Pair("#9A4136", "#FFB7AB");
    internal static readonly LifeColor WarningText = Pair("#8F4C16", "#FFCA8A");
    internal static readonly LifeColor Quiet = Pair("#EFF4F3", "#20384A");
    internal static readonly LifeColor Today = Pair("#AAF0E2", "#69DFCA");
    internal static readonly LifeColor TodayText = Pair("#075D57", "#092A32");

    internal static void Set(BindableObject target, BindableProperty property, LifeColor color) =>
        target.SetAppThemeColor(property, color.Light, color.Dark);

    private static LifeColor Pair(string light, string dark) =>
        new(Color.FromArgb(light), Color.FromArgb(dark));
}
