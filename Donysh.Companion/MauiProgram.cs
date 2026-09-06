using Microsoft.Maui.Controls.Hosting;

namespace Donysh.Companion;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp() => MauiApp.CreateBuilder().UseMauiApp<App>().Build();
}

public sealed class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new NavigationPage(new MainPage()));
#if WINDOWS
        window.Title = "همراه دونیش";
        window.Width = 740;
        window.Height = 900;
        window.MinimumWidth = 460;
        window.MinimumHeight = 560;
#endif
        return window;
    }
}
