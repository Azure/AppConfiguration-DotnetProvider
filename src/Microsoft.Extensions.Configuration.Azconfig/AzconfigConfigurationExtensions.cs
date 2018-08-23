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
                throw new ArgumentNullException($"No connection has been specified. Use '{nameof(options.Connect)}()' to provide connection details.");
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
