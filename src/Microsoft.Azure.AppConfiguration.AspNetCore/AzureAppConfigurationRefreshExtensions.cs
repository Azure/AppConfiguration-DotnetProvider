// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.Azure.AppConfiguration.AspNetCore;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;

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
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            IConfigurationRefresherProvider refresherProvider = (IConfigurationRefresherProvider)builder.ApplicationServices.GetService(typeof(IConfigurationRefresherProvider));

            // Verify if AddAzureAppConfiguration was done before calling UseAzureAppConfiguration.
            // We use the IConfigurationRefresherProvider to make sure if the required services were added.
            if (refresherProvider == null)
            {
                throw new InvalidOperationException($"Unable to find the required services. Please add all the required services by calling '{nameof(IServiceCollection)}.{nameof(Microsoft.Extensions.Configuration.AzureAppConfigurationExtensions.AddAzureAppConfiguration)}()' in the application startup code.");
            }

            if (refresherProvider.Refreshers?.Count() > 0)
            {
                builder.UseMiddleware<AzureAppConfigurationRefreshMiddleware>();
            }

            return builder;
        }
    }
}
