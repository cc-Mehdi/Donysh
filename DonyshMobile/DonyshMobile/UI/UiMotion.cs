namespace DonyshMobile.UI;

public static class UiMotion
{
    public static readonly BindableProperty AnimatePressProperty = BindableProperty.CreateAttached(
        "AnimatePress", typeof(bool), typeof(UiMotion), false, propertyChanged: OnAnimatePressChanged);

    public static bool GetAnimatePress(BindableObject view) => (bool)view.GetValue(AnimatePressProperty);
    public static void SetAnimatePress(BindableObject view, bool value) => view.SetValue(AnimatePressProperty, value);

    private static bool CanAnimate
    {
        get
        {
            if (!UiPreferences.Motion) return false;
            try
            {
#if ANDROID
                return global::Android.Provider.Settings.Global.GetFloat(
                    global::Android.App.Application.Context.ContentResolver,
                    global::Android.Provider.Settings.Global.AnimatorDurationScale, 1) > 0;
#elif IOS || MACCATALYST
                return !UIKit.UIAccessibility.IsReduceMotionEnabled;
#elif WINDOWS
                return new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
#else
                return true;
#endif
            }
            catch (Exception) { return false; }
        }
    }

    public static async Task EnterAsync(VisualElement view)
    {
        if (!CanAnimate) return;
        view.Opacity = 0.65;
        view.TranslationY = 10;
        await Task.WhenAll(view.FadeTo(1, 180, Easing.CubicOut), view.TranslateTo(0, 0, 180, Easing.CubicOut));
    }

    public static async Task PressAsync(VisualElement view)
    {
        if (!CanAnimate) return;
        await view.ScaleTo(0.97, 65, Easing.CubicOut);
        await view.ScaleTo(1, 100, Easing.CubicOut);
    }

    public static void Feedback()
    {
        if (!UiPreferences.Haptics) return;
        try
        {
            if (HapticFeedback.Default.IsSupported) HapticFeedback.Default.Perform(HapticFeedbackType.Click);
        }
        // Optional device feedback must never interrupt a successful save.
        catch (Exception) { }
    }

    private static void OnAnimatePressChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not Button button) return;
        button.Pressed -= OnPressed;
        button.Released -= OnReleased;
        if ((bool)newValue)
        {
            button.Pressed += OnPressed;
            button.Released += OnReleased;
        }
    }

    private static async void OnPressed(object? sender, EventArgs e)
    {
        if (sender is Button button && CanAnimate) await button.ScaleTo(0.97, 65, Easing.CubicOut);
    }

    private static async void OnReleased(object? sender, EventArgs e)
    {
        if (sender is Button button) await button.ScaleTo(1, CanAnimate ? 100u : 1u, Easing.CubicOut);
    }
}
