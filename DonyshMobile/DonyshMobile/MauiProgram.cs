using Microsoft.Extensions.Logging;

namespace DonyshMobile
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("YekanBakh-Regular.ttf", "YekanBakh");
                    fonts.AddFont("YekanBakh-SemiBold.ttf", "YekanBakhMedium");
                    fonts.AddFont("YekanBakh-Bold.ttf", "YekanBakhBold");
                    fonts.AddFont("YekanBakh-ExtraBold.ttf", "YekanBakhDisplay");
                });

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
