namespace Microsoft.Extensions.Configuration.Azconfig
{
    using Microsoft.Azconfig.Client;
    using System;

    public static class AzconfigConfigurationExtensions
    {
        public static IConfigurationBuilder AddRemoteAppConfiguration(
            this IConfigurationBuilder configurationBuilder,
            string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentNullException(nameof(connectionString));
            }

            return AddRemoteAppConfiguration(configurationBuilder, new RemoteConfigurationOptions(), new AzconfigClient(connectionString));
        }

        public static IConfigurationBuilder AddRemoteAppConfiguration(
            this IConfigurationBuilder configurationBuilder,
            Action<RemoteConfigurationOptions> action)
        {
            RemoteConfigurationOptions options = new RemoteConfigurationOptions();
            action(options);

            string connectionString = options.ConnectionString;

            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentNullException("Connection string is null. Please invoke connection() to pass in.", nameof(connectionString));
            }

            return AddRemoteAppConfiguration(configurationBuilder, 
                                             options,
                                             new AzconfigClient(connectionString));
        }

        public static IConfigurationBuilder AddRemoteAppConfiguration(
            this IConfigurationBuilder configurationBuilder,
            RemoteConfigurationOptions options)
        {
            string connectionString = options.ConnectionString;

            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentNullException(nameof(connectionString));
            }

            return AddRemoteAppConfiguration(configurationBuilder, options, new AzconfigClient(connectionString));
        }

        public static IConfigurationBuilder AddRemoteAppConfiguration(
            this IConfigurationBuilder configurationBuilder,
            RemoteConfigurationOptions options,
            AzconfigClient client)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            configurationBuilder.Add(
                new AzconfigConfigurationSource()
                {
                    Client = client,
                    Options = options
                }
            );

            return configurationBuilder;
        }
    }
}
