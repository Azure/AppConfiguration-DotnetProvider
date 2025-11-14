using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;
using MauiAppConfigDemo.Services;

namespace MauiAppConfigDemo;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        var afdEndpoint = new Uri("https://travelapp-aqg7cwewa6fcdyhs.b01.azurefd.net");

        builder.Configuration.AddAzureAppConfiguration(options =>
        {
            options.ConnectAzureFrontDoor(afdEndpoint)
                .SelectSnapshot("TravelAppSnapshot")
                .Select("TravelApp:*")
                .UseFeatureFlags(featureFlagOptions =>
                {
                    featureFlagOptions.Select("TravelApp.*")
                        .SetRefreshInterval(TimeSpan.FromMinutes(1));
                })
                .ConfigureRefresh(refreshOptions =>
                {
                    refreshOptions.RegisterAll()
                        .SetRefreshInterval(TimeSpan.FromMinutes(1));
                });
        });

        builder.Services.AddAzureAppConfiguration();
        builder.Services.AddSingleton<ConfigurationService>();
        builder.Services.AddFeatureManagement();
        builder.Services.AddTransient<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
