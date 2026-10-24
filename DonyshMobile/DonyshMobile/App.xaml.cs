namespace DonyshMobile
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            UI.UiPreferences.ApplyTheme();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
#if DEBUG && ANDROID
            if (Diagnostics.TemplateFlowSmokeCheck.IsRequested) return Diagnostics.TemplateFlowSmokeCheck.CreateWindow();
#endif
#if DEBUG && WINDOWS
            var initialService = WinUI.StartupSmokeCheck.IsEnabled
                ? Core.DonyshService.Finance : Core.ServicePreference.Default;
#else
            var initialService = Core.ServicePreference.Default;
#endif
            var window = new Window(new AppShell(initialService));
#if WINDOWS
            window.Width = 520;
            window.Height = 900;
            window.MinimumWidth = 360;
            window.MinimumHeight = 600;
#endif
#if DEBUG && WINDOWS
            WinUI.StartupSmokeCheck.Attach(window);
#endif
            return window;
        }

        public static void OpenService(Core.DonyshService? service)
        {
            if (Current?.Windows.FirstOrDefault() is { } window)
                window.Page = new AppShell(service);
        }
    }
}
