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
