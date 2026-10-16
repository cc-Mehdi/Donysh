namespace DonyshMobile.UI;

public sealed class DonyshButton : Button
{
    public static readonly BindableProperty GlyphProperty = BindableProperty.Create(
        nameof(Glyph), typeof(string), typeof(DonyshButton), string.Empty,
        propertyChanged: static (bindable, _, value) =>
            ((DonyshButton)bindable)._icon.Glyph = value as string ?? string.Empty);

    private readonly FontImageSource _icon;

    public DonyshButton()
    {
        _icon = new FontImageSource
        {
            FontFamily = "MaterialIconsRound",
            Glyph = string.Empty,
            Size = 19
        };
        _icon.SetBinding(FontImageSource.ColorProperty, new Binding(nameof(TextColor), source: this));
        ImageSource = _icon;
        ContentLayout = new Button.ButtonContentLayout(Button.ButtonContentLayout.ImagePosition.Right, 8);
    }

    public string Glyph
    {
        get => GetValue(GlyphProperty) as string ?? string.Empty;
        set => SetValue(GlyphProperty, value ?? string.Empty);
    }
}
