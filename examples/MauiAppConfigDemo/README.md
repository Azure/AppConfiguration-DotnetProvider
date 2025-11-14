# MAUI App with Azure App Configuration via Azure Front Door

This project demonstrates how to integrate Azure App Configuration with a .NET MAUI application using Azure Front Door for global distribution and anonymous access.

## Project Overview

A simple travel booking app that demonstrates:
- **Hybrid Configuration**: Snapshot (stable settings) + dynamic key-values
- **Feature Flags**: Server-controlled feature toggles
- **Configuration Refresh**: Automatic background refresh every 1 minute
- **Cross-Platform**: Runs on Android, iOS, macOS Catalyst, and Windows

## Prerequisites

- .NET 9.0 SDK
- Visual Studio 2022 or Visual Studio Code with .NET MAUI workload
- Azure subscription with App Configuration resource
- Azure Front Door instance configured for your App Configuration

---

## Step-by-Step Implementation

### 1. Create New MAUI App

Start with the default .NET MAUI template:

```bash
dotnet new maui -n MauiAppConfigDemo
cd MauiAppConfigDemo
```

### 2. Add NuGet Packages

Add the required packages to `MauiAppConfigDemo.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.Configuration.AzureAppConfiguration" Version="8.5.0-preview" />
  <PackageReference Include="Microsoft.FeatureManagement" Version="4.3.0" />
</ItemGroup>
```

Run:
```bash
dotnet restore
```

### 3. Create Models Folder and AppSettings Class

**Create:** `Models/AppSettings.cs`

```csharp
namespace MauiAppConfigDemo.Models;

/// <summary>
/// Application settings loaded from Azure App Configuration.
/// </summary>
public class AppSettings
{
    public string ApiUrl { get; set; } = "Unable to load API URL from Azure App Configuration";
    public string AppVersion { get; set; } = "Unable to load App Version from Azure App Configuration";
    public string WelcomeMessage { get; set; } = "Travel Booking configuration failed to load from Azure App Configuration.";
    public string PromotionText { get; set; } = string.Empty;
}
```

### 4. Create Services Folder and ConfigurationService

**Create:** `Services/ConfigurationService.cs`

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.FeatureManagement;
using MauiAppConfigDemo.Models;

namespace MauiAppConfigDemo.Services;

/// <summary>
/// Provides strongly-typed access to Azure App Configuration.
/// Handles both snapshot (stable) and dynamic configuration.
/// Feature flags are managed via IFeatureManager.
/// </summary>
public class ConfigurationService
{
    private readonly IConfiguration _configuration;
    private readonly IFeatureManager _featureManager;
    private readonly IEnumerable<IConfigurationRefresher> _refreshers;

    public ConfigurationService(IConfiguration configuration, IFeatureManager featureManager, IConfigurationRefresherProvider refresherProvider)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _featureManager = featureManager ?? throw new ArgumentNullException(nameof(featureManager));
        _refreshers = refresherProvider?.Refreshers ?? throw new ArgumentNullException(nameof(refresherProvider));
    }

    /// <summary>
    /// Gets app settings from Azure App Configuration.
    /// </summary>
    public AppSettings GetSettings()
    {
        var settings = new AppSettings();
        _configuration.GetSection("TravelApp").Bind(settings);
        return settings;
    }

    /// <summary>
    /// Checks if hotel booking feature is enabled.
    /// </summary>
    public async Task<bool> IsHotelBookingEnabledAsync()
    {
        return await _featureManager.IsEnabledAsync("TravelApp.HotelBooking");
    }

    /// <summary>
    /// Checks if promotional banner is enabled.
    /// </summary>
    public async Task<bool> IsPromotionEnabledAsync()
    {
        return await _featureManager.IsEnabledAsync("TravelApp.ShowPromotion");
    }

    /// <summary>
    /// Triggers a configuration refresh on demand.
    /// </summary>
    public async Task RefreshConfigurationAsync()
    {
        foreach (var refresher in _refreshers)
        {
            _ = refresher.TryRefreshAsync();
        }
    }
}
```

### 5. Update MauiProgram.cs

**Replace** the default `MauiProgram.cs` with:

```csharp
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

        // Configure Azure App Configuration via Azure Front Door
        var afdEndpoint = new Uri("https://YOUR-AFD-ENDPOINT.azurefd.net");

        builder.Configuration.AddAzureAppConfiguration(options =>
        {
            options.ConnectAzureFrontDoor(afdEndpoint)
                   .SelectSnapshot("TravelAppSnapshot")  // Load snapshot
                   .Select("TravelApp:*")                // Load key-values
                   .UseFeatureFlags(featureFlagOptions =>
                   {
                       featureFlagOptions.Select("TravelApp.*")     // Load feature flags
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
```

**Important:** Replace `YOUR-AFD-ENDPOINT.azurefd.net` with your actual Azure Front Door endpoint.

### 6. Update MainPage.xaml

**Replace** the default `MainPage.xaml` with:

```xaml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             x:Class="MauiAppConfigDemo.MainPage">

    <RefreshView x:Name="RefreshView"
                 Refreshing="OnRefreshing">
        <ScrollView>
            <VerticalStackLayout Padding="20" Spacing="20">

            <!-- App Header -->
            <Label x:Name="WelcomeLabel"
                   Text="Welcome to Travel Booking!"
                   FontSize="24"
                   FontAttributes="Bold"
                   HorizontalTextAlignment="Center"
                   Margin="0,20,0,10"/>

            <!-- Promotional Banner -->
            <Border x:Name="PromotionalBanner"
                    IsVisible="False"
                    BackgroundColor="#E3F2FD"
                    Stroke="#1E88E5"
                    StrokeThickness="2"
                    Padding="15">
                <Border.StrokeShape>
                    <RoundRectangle CornerRadius="12"/>
                </Border.StrokeShape>
                <Label x:Name="PromotionText"
                       Text="Special Offer!"
                       FontSize="16"
                       FontAttributes="Bold"
                       TextColor="#1565C0"
                       HorizontalTextAlignment="Center"/>
            </Border>

            <!-- Hotel Booking -->
            <Border x:Name="HotelBookingCard"
                    IsVisible="False"
                    BackgroundColor="White"
                    Stroke="#DDDDDD"
                    StrokeThickness="1"
                    Padding="20">
                <Border.StrokeShape>
                    <RoundRectangle CornerRadius="10"/>
                </Border.StrokeShape>
                <Border.GestureRecognizers>
                    <TapGestureRecognizer Tapped="OnHotelBookingTapped"/>
                </Border.GestureRecognizers>
                <HorizontalStackLayout Spacing="15">
                    <Label Text="🏨"
                           FontSize="40"
                           VerticalOptions="Center"/>
                    <VerticalStackLayout VerticalOptions="Center" HorizontalOptions="Fill">
                        <Label Text="Hotel Booking"
                               FontSize="18"
                               FontAttributes="Bold"/>
                        <Label Text="Find the perfect stay for your trip"
                               FontSize="14"
                               TextColor="#666666"/>
                    </VerticalStackLayout>
                    <Label Text="›"
                           FontSize="30"
                           TextColor="#1E88E5"
                           VerticalOptions="Center"/>
                </HorizontalStackLayout>
            </Border>

            <!-- App Info -->
            <Label x:Name="AppInfoLabel"
                   Text=""
                   FontSize="12"
                   TextColor="#999999"
                   HorizontalTextAlignment="Center"
                   Margin="0,10,0,20"/>

        </VerticalStackLayout>
        </ScrollView>
    </RefreshView>

</ContentPage>
```

### 7. Update MainPage.xaml.cs

**Replace** the default `MainPage.xaml.cs` with:

```csharp
using MauiAppConfigDemo.Services;

namespace MauiAppConfigDemo;

public partial class MainPage : ContentPage
{
    private readonly ConfigurationService _configService;

    public MainPage(ConfigurationService configService)
    {
        InitializeComponent();
        _configService = configService;
        LoadConfiguration();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Trigger configuration refresh when page appears
        Task.Run(async () =>
        {
            await _configService.RefreshConfigurationAsync();
            MainThread.BeginInvokeOnMainThread(() => LoadConfiguration());
        });
    }

    private async void LoadConfiguration()
    {
        var settings = _configService.GetSettings();

        var isPromotionEnabled = await _configService.IsPromotionEnabledAsync();
        var isHotelEnabled = await _configService.IsHotelBookingEnabledAsync();

        WelcomeLabel.Text = settings.WelcomeMessage;

        PromotionalBanner.IsVisible = isPromotionEnabled && !string.IsNullOrEmpty(settings.PromotionText);
        if (PromotionalBanner.IsVisible)
        {
            PromotionText.Text = settings.PromotionText;
        }

        HotelBookingCard.IsVisible = isHotelEnabled;

        AppInfoLabel.Text = $"v{settings.AppVersion} | {settings.ApiUrl}";
    }

    private async void OnHotelBookingTapped(object? sender, EventArgs e)
    {
        await DisplayAlert("Hotel Booking", "Hotel booking feature - powered by Azure App Configuration!", "OK");
    }

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await _configService.RefreshConfigurationAsync();
        LoadConfiguration();
        RefreshView.IsRefreshing = false;
    }
}
```

### 8. Update AppShell.xaml (Optional)

Simplify to single page shell:

```xaml
<?xml version="1.0" encoding="UTF-8" ?>
<Shell
    x:Class="MauiAppConfigDemo.AppShell"
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:local="clr-namespace:MauiAppConfigDemo"
    Shell.FlyoutBehavior="Disabled"
    Title="MauiAppConfigDemo">

    <ShellContent
        Title="Home"
        ContentTemplate="{DataTemplate local:MainPage}"
        Route="MainPage" />

</Shell>
```

---

## Azure App Configuration Setup

### Create a Configuration Snapshot

Create a snapshot named `TravelAppSnapshot` which should have the following key-values:

| Key | Value | Label |
|-----|-------|--------------|
| `TravelApp:ApiUrl` | `https://api.travelapp.com/v1` | (No Label) |
| `TravelApp:AppVersion` | `1.0.0` | (No Label) |

### Create Key-Values

Add the following key-values directly (not in snapshot):

| Key | Value | Label |
|-----|-------|-------|
| `TravelApp:WelcomeMessage` | `Welcome to Travel Booking powered by Azure App Configuration!` | (No Label) |
| `TravelApp:PromotionText` | `Book now and save 20% on your first hotel!` | (No Label) |

### Create Feature Flags

Add the following feature flags:

| Feature Flag Name | State |
|-------------------|-------|
| `TravelApp.HotelBooking` | On |
| `TravelApp.ShowPromotion` | On |

### Configure Azure Front Door

1. Configure your App Configuration store to expose the required key-values through Azure Front Door. Follow instructions at `https://aka.ms/appconfig/afdsetup`.
2. Update `MauiProgram.cs` with your AFD endpoint (e.g., `https://xxxxx.azurefd.net`)

## Troubleshooting

### Configuration doesn't load
- Verify Azure Front Door endpoint URL is correct
- Check for AFD configuration warnings in AppConfig portal and fix the issues if any.
- Make sure the correct scoping filters are set when configuring the AFD endpoint. These filters (for key-values, snapshots, and feature flags) define the regex rules that block requests that don't match specified filters. If your app can’t access its configuration, review AFD rules to find any blocking regex patterns. Update the rule with the right filter or create a new AFD endpoint from the App Configuration portal.

### Configuration doesn't refresh
- Azure Front Door manages caching behavior, so updates from App Configuration aren’t immediately available to the app. Even if your app checks for changes every minute, AFD may serve cached data until its own cache expires. For example, if AFD caches for 10 minutes, your app won’t see updates for at least 10 minutes, even though it keeps requesting every minute. This design ensures eventual consistency, not real-time updates, which is expected for any CDN-based solutions. Learn more about (caching with Azure Front Door)[https://learn.microsoft.com/en-us/azure/frontdoor/front-door-caching].

---

## Additional Resources

- [Azure App Configuration Documentation](https://learn.microsoft.com/azure/azure-app-configuration/)
- [.NET MAUI Documentation](https://learn.microsoft.com/dotnet/maui/)
- [Feature Management Documentation](https://learn.microsoft.com/azure/azure-app-configuration/feature-management)
- [Azure Front Door Documentation](https://learn.microsoft.com/azure/frontdoor/)

---
