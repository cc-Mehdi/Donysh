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
        try
        {
            view.Opacity = 0.65;
            view.TranslationY = 10;
            await Task.WhenAll(view.FadeTo(1, 180, Easing.CubicOut), view.TranslateTo(0, 0, 180, Easing.CubicOut));
        }
        catch (Exception)
        {
            // A detached native view must not turn an optional transition into a crash.
            view.Opacity = 1;
            view.TranslationY = 0;
        }
    }

    public static async Task PressAsync(VisualElement view)
    {
        if (!CanAnimate) return;
        await ScaleSafelyAsync(view, 0.97, 65);
        await ScaleSafelyAsync(view, 1, 100);
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
        if (sender is Button button && CanAnimate) await ScaleSafelyAsync(button, 0.97, 65);
    }

    private static async void OnReleased(object? sender, EventArgs e)
    {
        if (sender is Button button) await ScaleSafelyAsync(button, 1, CanAnimate ? 100u : 1u);
    }

    private static async Task ScaleSafelyAsync(VisualElement view, double scale, uint duration)
    {
        try { await view.ScaleTo(scale, duration, Easing.CubicOut); }
        catch (Exception) { view.Scale = 1; }
    }
}
