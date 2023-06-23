// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.Azure.AppConfiguration.AspNetCore;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Constants;
using System;
using System.Security;

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

            bool providerDisabled = false;

            try
            {
                providerDisabled = bool.TryParse(Environment.GetEnvironmentVariable(ConditionalProviderConstants.DisableProviderEnvironmentVariable), out bool disabled) ? disabled : false;
            }
            catch (SecurityException) { }

            if (!providerDisabled)
            {
                // Verify if AddAzureAppConfiguration was done before calling UseAzureAppConfiguration.
                // We use the IConfigurationRefresherProvider to make sure if the required services were added.
                if (builder.ApplicationServices.GetService(typeof(IConfigurationRefresherProvider)) == null)
                {
                    throw new InvalidOperationException("Unable to find the required services. Please add all the required services by calling 'IServiceCollection.AddAzureAppConfiguration' inside the call to 'ConfigureServices(...)' in the application startup code.");
                }

                builder.UseMiddleware<AzureAppConfigurationRefreshMiddleware>();
            }

            return builder;
        }
    }
}
