// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Core;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using System.Collections.Generic;
using System.Security;

namespace Microsoft.Extensions.Configuration
{
    /// <summary>
    /// This type provides helper methods to make common use cases for Azure App Configuration easy.
    /// </summary>
    public static class AzureAppConfigurationExtensions
    {
        private const string DisableProviderEnvironmentVariable = "AZURE_APP_CONFIGURATION_PROVIDER_DISABLED";
        private static readonly bool _isProviderDisabled = IsProviderDisabled();

        private static bool IsProviderDisabled()
        {
            try
            {
                return bool.TryParse(Environment.GetEnvironmentVariable(DisableProviderEnvironmentVariable), out bool disabled) ? disabled : false;
            }
            catch (SecurityException) { }

            return false;
        }

        /// <summary>
        /// Adds key-value data from an Azure App Configuration store to a configuration builder using its connection string.
        /// This is a simplified overload that loads all key-values with no label. For advanced scenarios such as selecting specific keys, 
        /// filtering by labels, configuring refresh, using feature flags, or resolving Key Vault references, 
        /// use the overload that accepts an <see cref="Action{AzureAppConfigurationOptions}"/> parameter with options.Connect().
        /// </summary>
        /// <param name="configurationBuilder">The configuration builder to add key-values to.</param>
        /// <param name="connectionString">The connection string used to connect to the configuration store.</param>
        /// <param name="optional">Determines the behavior of the App Configuration provider when an exception occurs while loading data from server. If false, the exception is thrown. If true, the exception is suppressed and no settings are populated from Azure App Configuration.
        /// <exception cref="ArgumentException"/> will always be thrown when the caller gives an invalid input configuration (connection strings, endpoints, key/label filters...etc).
        /// </param>
        /// <returns>The provided configuration builder.</returns>
        public static IConfigurationBuilder AddAzureAppConfiguration(
            this IConfigurationBuilder configurationBuilder,
            string connectionString,
            bool optional = false)
        {
            return configurationBuilder.AddAzureAppConfiguration(options => options.Connect(connectionString), optional);
        }

        /// <summary>
        /// Adds key-value data from a primary Azure App Configuration store and one or more replica stores to a configuration builder using connection strings.
        /// This is a simplified overload that loads all key-values with no label. For advanced scenarios such as selecting specific keys, 
        /// filtering by labels, configuring refresh, using feature flags, or resolving Key Vault references, 
        /// use the overload that accepts an <see cref="Action{AzureAppConfigurationOptions}"/> parameter with options.Connect().
        /// </summary>
        /// <param name="configurationBuilder">The configuration builder to add key-values to.</param>
        /// <param name="connectionStrings">The list of connection strings used to connect to the configuration store and its replicas.</param>
        /// <param name="optional">Determines the behavior of the App Configuration provider when an exception occurs while loading data from server. If false, the exception is thrown. If true, the exception is suppressed and no settings are populated from Azure App Configuration.
        /// <exception cref="ArgumentException"/> will always be thrown when the caller gives an invalid input configuration (connection strings, endpoints, key/label filters...etc).
        /// </param>
        /// <returns>The provided configuration builder.</returns>
        public static IConfigurationBuilder AddAzureAppConfiguration(
            this IConfigurationBuilder configurationBuilder,
            IEnumerable<string> connectionStrings,
            bool optional = false)
        {
            return configurationBuilder.AddAzureAppConfiguration(options => options.Connect(connectionStrings), optional);
        }

        /// <summary>
        /// Adds key-value data from an Azure App Configuration store to a configuration builder using endpoint with AAD authentication.
        /// This is a simplified overload that loads all key-values with no label. For advanced scenarios such as selecting specific keys, 
        /// filtering by labels, configuring refresh, using feature flags, or resolving Key Vault references, 
        /// use the overload that accepts an <see cref="Action{AzureAppConfigurationOptions}"/> parameter with options.Connect().
        /// </summary>
        /// <param name="configurationBuilder">The configuration builder to add key-values to.</param>
        /// <param name="endpoint">The endpoint used to connect to the configuration store.</param>
        /// <param name="credential">The token credential used to authenticate requests to the configuration store.</param>
        /// <param name="optional">Determines the behavior of the App Configuration provider when an exception occurs while loading data from server. If false, the exception is thrown. If true, the exception is suppressed and no settings are populated from Azure App Configuration.
        /// <exception cref="ArgumentException"/> will always be thrown when the caller gives an invalid input configuration (connection strings, endpoints, key/label filters...etc).
        /// </param>
        /// <returns>The provided configuration builder.</returns>
        public static IConfigurationBuilder AddAzureAppConfiguration(
            this IConfigurationBuilder configurationBuilder,
            Uri endpoint,
            TokenCredential credential,
            bool optional = false)
        {
            return configurationBuilder.AddAzureAppConfiguration(options => options.Connect(endpoint, credential), optional);
        }

        /// <summary>
        /// Adds key-value data from an Azure App Configuration store to a configuration builder using a fully configurable <see cref="AzureAppConfigurationOptions"/> callback for advanced scenarios.
        /// Use this overload when you need to: select keys by prefix, filter by labels, configure dynamic refresh, use feature flags, resolve Key Vault references, etc.
        /// </summary>
        /// <param name="configurationBuilder">The configuration builder to add key-values to.</param>
        /// <param name="action">A callback used to configure Azure App Configuration options.</param>
        /// <param name="optional">Determines the behavior of the App Configuration provider when an exception occurs while loading data from server. If false, the exception is thrown. If true, the exception is suppressed and no settings are populated from Azure App Configuration.
        /// <exception cref="ArgumentException"/> will always be thrown when the caller gives an invalid input configuration (connection strings, endpoints, key/label filters...etc).
        /// </param>
        /// <returns>The provided configuration builder.</returns>
        public static IConfigurationBuilder AddAzureAppConfiguration(
            this IConfigurationBuilder configurationBuilder,
            Action<AzureAppConfigurationOptions> action,
            bool optional = false)
        {
            if (!_isProviderDisabled)
            {
                configurationBuilder.Add(new AzureAppConfigurationSource(action, optional));
            }

            return configurationBuilder;
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

            if (!_isProviderDisabled)
            {
                services.AddLogging();
                services.TryAddSingleton<IConfigurationRefresherProvider, AzureAppConfigurationRefresherProvider>();
            }
            else
            {
                services.TryAddSingleton<IConfigurationRefresherProvider, EmptyConfigurationRefresherProvider>();
            }

            return services;
        }
    }
}
