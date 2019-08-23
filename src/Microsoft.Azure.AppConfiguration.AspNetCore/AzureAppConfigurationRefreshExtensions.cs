using Microsoft.Azure.AppConfiguration.AspNetCore;
using System;

namespace Microsoft.AspNetCore.Builder
{
    /// <summary>
    /// Extension methods for Azure App Configuration.
    /// </summary>
    public static class AzureAppConfigurationExtensions
    {
        /// <summary>
        /// Configures a middleware for Azure App Configuration to use activity-based refresh for data configured in the provider.
        /// </summary>
        /// <param name="builder">An instance of <see cref="IApplicationBuilder"/></param>
        /// <param name="options">A callback used to configure Azure App Configuration middleware options.</param>
        public static IApplicationBuilder UseAzureAppConfiguration(this IApplicationBuilder builder, Action<AzureAppConfigurationMiddlewareOptions> options = null)
        {
            return builder.UseMiddleware<AzureAppConfigurationRefreshMiddleware>(options);
        }
    }
}
