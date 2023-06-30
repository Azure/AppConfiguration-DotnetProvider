// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using Microsoft.Azure.AppConfiguration.Functions.Worker;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;

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
            IServiceProvider serviceProvider = builder.Services.BuildServiceProvider();
            IConfigurationRefresherProvider refresherProvider = serviceProvider.GetService<IConfigurationRefresherProvider>();

            // Verify if AddAzureAppConfiguration was done before calling UseAzureAppConfiguration.
            // We use the IConfigurationRefresherProvider to make sure if the required services were added.
            if (refresherProvider == null)
            {
                throw new InvalidOperationException($"Unable to find the required services. Please add all the required services by calling '{nameof(IServiceCollection)}.{nameof(AzureAppConfigurationExtensions.AddAzureAppConfiguration)}()' in the application startup code.");
            }

            if (refresherProvider.Refreshers?.Count() != 0)
            {
                builder.UseMiddleware<AzureAppConfigurationRefreshMiddleware>();
            }

            return builder;
        }
    }
}
