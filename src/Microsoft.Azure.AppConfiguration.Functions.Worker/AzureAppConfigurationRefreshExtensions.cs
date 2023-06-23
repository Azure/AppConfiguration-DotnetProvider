// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using Microsoft.Azure.AppConfiguration.Functions.Worker;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Constants;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Security;

namespace Microsoft.Extensions.Hosting
{
    /// <summary>
    /// Extension methods for Azure App Configuration.
    /// </summary>
    public static class AzureAppConfigurationRefreshExtensions
    {
        /// <summary>
        /// Configures a middleware for Azure App Configuration to use activity-based refresh for data configured in the provider.
        /// </summary>
        /// <param name="builder">An instance of <see cref="IFunctionsWorkerApplicationBuilder"/></param>
        public static IFunctionsWorkerApplicationBuilder UseAzureAppConfiguration(this IFunctionsWorkerApplicationBuilder builder)
        {
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
                if (!builder.Services.Any(service => service.ServiceType == typeof(IConfigurationRefresherProvider)))
                {
                    throw new InvalidOperationException($"Unable to find the required services. Please add all the required services by calling '{nameof(IServiceCollection)}.{nameof(AzureAppConfigurationExtensions.AddAzureAppConfiguration)}()' inside the call to 'ConfigureServices(...)' in the application startup code.");
                }

                builder.UseMiddleware<AzureAppConfigurationRefreshMiddleware>();
            }

            return builder;
        }
    }
}
