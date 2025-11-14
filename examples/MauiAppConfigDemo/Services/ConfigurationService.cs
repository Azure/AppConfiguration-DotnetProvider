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
