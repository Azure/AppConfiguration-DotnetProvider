namespace Microsoft.Extensions.Configuration.AppConfig
{
    using System;

    public static class AppConfigConfigurationExtensions
    {
        public static IConfigurationBuilder AddRemoteAppConfiguration
        (
            this IConfigurationBuilder configurationBuilder,
            string appConfigUri
        )
        {
            return AddRemoteAppConfiguration(configurationBuilder, appConfigUri, new RemoteConfigurationOptions());
        }

        public static IConfigurationBuilder AddRemoteAppConfiguration
        (
            this IConfigurationBuilder configurationBuilder,
            string appConfigUri,
            RemoteConfigurationOptions options
        )
        {
            return AddRemoteAppConfiguration(configurationBuilder, appConfigUri, options, new AppConfigClient(options));
        }

        public static IConfigurationBuilder AddRemoteAppConfiguration
        (
            this IConfigurationBuilder configurationBuilder,
            string appConfigUri,
            RemoteConfigurationOptions options,
            IAppConfigClient client
        )
        {
            if (appConfigUri == null)
            {
                throw new ArgumentNullException(nameof(appConfigUri));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            configurationBuilder.Add(
                new AppConfigConfigurationSource()
                {
                    AppConfigUri = appConfigUri,
                    Client = client,
                    Options = options
                }
            );

            return configurationBuilder;
        }
    }
}
