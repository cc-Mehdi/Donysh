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
            var window = new Window(new AppShell());
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
    }
}
