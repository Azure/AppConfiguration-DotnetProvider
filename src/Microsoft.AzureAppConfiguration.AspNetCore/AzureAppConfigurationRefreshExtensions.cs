using Microsoft.AspNetCore.Builder;

namespace Microsoft.AzureAppConfiguration.AspNetCore
{
    /// <summary>
    /// Extension methods for refresh-related scenarios in Azure App Configuration.
    /// </summary>
    public static class AzureAppConfigurationRefreshExtensions
    {
        /// <summary>
        /// Configures a middleware for Azure App Configuration to use activity-based refresh for data configured in the provider.
        /// </summary>
        /// <param name="builder">An instance of <see cref="IApplicationBuilder"/></param>
        public static IApplicationBuilder UseAzureAppConfigurationRefresh(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<AzureAppConfigurationRefreshMiddleware>();
        }
    }
}
