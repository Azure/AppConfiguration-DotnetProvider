using Microsoft.Azure.AppConfiguration.AspNetCore;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.DependencyInjection;
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
        public static IApplicationBuilder UseAzureAppConfiguration(this IApplicationBuilder builder)
        {
            // Verify if AddAzureAppConfiguration was done before calling UseAzureAppConfiguration.
            // We use the IConfigurationRefresher to make sure if the required services were added.
            var refresher = builder.ApplicationServices.GetService(typeof(IConfigurationRefresher));

            if (refresher == null)
            {
                throw new InvalidOperationException($"Unable to find the required services. Please add all the required services by calling '{nameof(IServiceCollection)}.AddAzureAppConfiguration'.");
            }

            return builder.UseMiddleware<AzureAppConfigurationRefreshMiddleware>();
        }
    }
}
