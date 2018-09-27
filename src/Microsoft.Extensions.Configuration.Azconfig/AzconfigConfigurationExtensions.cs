namespace Microsoft.Extensions.Configuration.Azconfig
{
    using Microsoft.Azconfig.Client;
    using System;

    public static class AzconfigConfigurationExtensions
    {
        public static IConfigurationBuilder AddRemoteAppConfiguration(
            this IConfigurationBuilder configurationBuilder,
            string connectionString,
            bool optional = false) => AddRemoteAppConfiguration(configurationBuilder, 
                                                                new RemoteConfigurationOptions(), 
                                                                GetAzconfigClient(connectionString, optional), 
                                                                optional);

        public static IConfigurationBuilder AddRemoteAppConfiguration(
            this IConfigurationBuilder configurationBuilder,
            Action<RemoteConfigurationOptions> action,
            bool optional = false)
        {
            RemoteConfigurationOptions options = new RemoteConfigurationOptions();
            action(options);
            return AddRemoteAppConfiguration(configurationBuilder,
                                             options,
                                             GetAzconfigClient(options.ConnectionString, optional),
                                             optional);
        }

        public static IConfigurationBuilder AddRemoteAppConfiguration(
            this IConfigurationBuilder configurationBuilder,
            RemoteConfigurationOptions options,
            bool optional = false) => AddRemoteAppConfiguration(configurationBuilder, 
                                                                new RemoteConfigurationOptions(), 
                                                                GetAzconfigClient(options.ConnectionString, optional), 
                                                                optional);


        public static IConfigurationBuilder AddRemoteAppConfiguration(
            this IConfigurationBuilder configurationBuilder,
            RemoteConfigurationOptions options,
            AzconfigClient client,
            bool optional = false)
        {
            try
            {
                configurationBuilder.Add(
                    new AzconfigConfigurationSource()
                    {
                        Client = client ?? throw new ArgumentNullException(nameof(client)),
                        Options = options ?? throw new ArgumentNullException(nameof(options))
                    }
                );

                options.Optional = optional;
                return configurationBuilder;
            }
            catch(ArgumentNullException exception)
            {
                if (optional)
                {
                    return configurationBuilder;
                }

                throw exception;
            }
        }

        private static AzconfigClient GetAzconfigClient(string connectionString, bool optional)
        {
            try
            {
                return new AzconfigClient(connectionString);
            }
            catch
            {
                if (optional)
                {
                    return null;
                }

                throw;
            }
        }
    }
}
