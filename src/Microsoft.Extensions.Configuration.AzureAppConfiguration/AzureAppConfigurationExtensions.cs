// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Microsoft.Extensions.Configuration
{
    /// <summary>
    /// This type provides helper methods to make common use cases for Azure App Configuration easy.
    /// </summary>
    public static class AzureAppConfigurationExtensions
    {
        /// <summary>
        /// Adds key-value data from an Azure App Configuration store to a configuration builder.
        /// </summary>
        /// <param name="configurationBuilder">The configuration builder to add key-values to.</param>
        /// <param name="connectionString">The connection string used to connect to the configuration store.</param>
        /// <param name="optional">Determines the behavior of the App Configuration provider when an exception occurs. If false, the exception is thrown. If true, the exception is suppressed and no settings are populated from Azure App Configuration.</param>
        /// <returns>The provided configuration builder.</returns>
        public static IConfigurationBuilder AddAzureAppConfiguration(
            this IConfigurationBuilder configurationBuilder,
            string connectionString,
            bool optional = false)
        {
            return configurationBuilder.AddAzureAppConfiguration(options => options.Connect(connectionString), optional);
        }

        /// <summary>
        /// Adds key-value data from an Azure App Configuration store to a configuration builder.
        /// </summary>
        /// <param name="configurationBuilder">The configuration builder to add key-values to.</param>
        /// <param name="action">A callback used to configure Azure App Configuration options.</param>
        /// <param name="optional">Determines the behavior of the App Configuration provider when an exception occurs. If false, the exception is thrown. If true, the exception is suppressed and no settings are populated from Azure App Configuration.</param>
        /// <returns>The provided configuration builder.</returns>
        public static IConfigurationBuilder AddAzureAppConfiguration(
            this IConfigurationBuilder configurationBuilder,
            Action<AzureAppConfigurationOptions> action,
            bool optional = false)
        {
            return configurationBuilder.Add(new AzureAppConfigurationSource(action, optional));
        }

        /// <summary>
        /// Adds Azure App Configuration services to the specified <see cref="IServiceCollection"/>.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
        /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
        public static IServiceCollection AddAzureAppConfiguration(this IServiceCollection services)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services.AddLogging();
            services.AddSingleton<IConfigurationRefresherProvider, AzureAppConfigurationRefresherProvider>();
            return services;
        }
    }
}
