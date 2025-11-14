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
        // Get all configuration settings
        var settings = _configService.GetSettings();

        // Check feature flags
        var isPromotionEnabled = await _configService.IsPromotionEnabledAsync();
        var isHotelEnabled = await _configService.IsHotelBookingEnabledAsync();

        // Display welcome message
        WelcomeLabel.Text = settings.WelcomeMessage;

        // Configure promotional banner (using feature flag)
        PromotionalBanner.IsVisible = isPromotionEnabled && !string.IsNullOrEmpty(settings.PromotionText);
        if (PromotionalBanner.IsVisible)
        {
            PromotionText.Text = settings.PromotionText;
        }

        // Show/hide hotel booking based on feature flag
        HotelBookingCard.IsVisible = isHotelEnabled;

        // Show app info
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
