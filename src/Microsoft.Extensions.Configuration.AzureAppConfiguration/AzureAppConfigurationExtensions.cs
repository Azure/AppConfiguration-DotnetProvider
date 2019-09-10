namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    using Microsoft.Extensions.DependencyInjection;
    using System;
    using System.Collections.Generic;
    using System.Linq;

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
        /// <param name="optional">If true, this method will not throw an exception if the configuration store cannot be accessed.</param>
        /// <returns>The provided configuration builder.</returns>
        public static IConfigurationBuilder AddAzureAppConfiguration(
            this IConfigurationBuilder configurationBuilder,
            string connectionString,
            bool optional = false)
        {
            return configurationBuilder.AddAzureAppConfiguration(new AzureAppConfigurationOptions().Connect(connectionString), optional);
        }

        /// <summary>
        /// Adds key-value data from an Azure App Configuration store to a configuration builder.
        /// </summary>
        /// <param name="configurationBuilder">The configuration builder to add key-values to.</param>
        /// <param name="action">A callback used to configure Azure App Configuration options.</param>
        /// <param name="optional">If true, this method will not throw an exception if the configuration store cannot be accessed.</param>
        /// <returns>The provided configuration builder.</returns>
        public static IConfigurationBuilder AddAzureAppConfiguration(
            this IConfigurationBuilder configurationBuilder,
            Action<AzureAppConfigurationOptions> action,
            bool optional = false)
        {
            return configurationBuilder.Add(new AzureAppConfigurationSource(action, optional));
        }

        /// <summary>
        /// Adds key-value data from an Azure App Configuration store to a configuration builder.
        /// </summary>
        /// <param name="configurationBuilder">The configuration builder to add key-values to</param>
        /// <param name="options">Options used to configure the behavior of the Azure App Configuration provider.</param>
        /// <param name="optional">If true, this method will not throw an exception if the configuration store cannot be accessed.</param>
        /// <returns>The provided configuration builder.</returns>
        public static IConfigurationBuilder AddAzureAppConfiguration(
            this IConfigurationBuilder configurationBuilder,
            AzureAppConfigurationOptions options,
            bool optional = false)
        {
            return configurationBuilder.Add(new AzureAppConfigurationSource(options, optional));
        }

        /// <summary>
        /// Adds Azure App Configuration services to the specified <see cref="IServiceCollection"/>.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
        /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
        public static IServiceCollection AddAzureAppConfiguration(this IServiceCollection services)
        {
            var configuration = services
                .Where(s => s.ServiceType == typeof(IConfiguration) && s.ImplementationInstance != null)
                .Select(s => s.ImplementationInstance)
                .FirstOrDefault();

            var configurationRoot = configuration as IConfigurationRoot;
            var refreshers = new List<IConfigurationRefresher>();

            foreach (var provider in configurationRoot?.Providers)
            {
                if (provider is IConfigurationRefresher refresher)
                {
                    refreshers.Add(refresher);
                }
            }

            if (!refreshers.Any())
            {
                throw new InvalidOperationException($"Unable to find Azure App Configuration provider. Please ensure that it has been configured correctly.");
            }

            var globalRefresher = new AzureAppConfigurationRefresher();
            refreshers.ForEach(r => globalRefresher.Register(r));
            services.AddSingleton<IConfigurationRefresher>(globalRefresher);
            return services;
        }
    }
}
