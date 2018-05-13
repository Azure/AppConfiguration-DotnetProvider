namespace Microsoft.Extensions.Configuration.AppConfig
{
    using System;

    public static class AppConfigConfigurationExtensions
    {
        public static IConfigurationBuilder AddRemoteAppConfiguration
        (
            this IConfigurationBuilder configurationBuilder,
            string appConfigUri,
            string secretId,
            string secretValue
        )
        {
            return AddRemoteAppConfiguration(configurationBuilder, appConfigUri, secretId, secretValue, new RemoteConfigurationOptions());
        }

        public static IConfigurationBuilder AddRemoteAppConfiguration
        (
            this IConfigurationBuilder configurationBuilder,
            string appConfigUri,
            string secretId,
            string secretValue,
            RemoteConfigurationOptions options
        )
        {
            return AddRemoteAppConfiguration(configurationBuilder, options, new AppConfigClient(appConfigUri, secretId, secretValue, options));
        }

        public static IConfigurationBuilder AddRemoteAppConfiguration
        (
            this IConfigurationBuilder configurationBuilder,
            RemoteConfigurationOptions options,
            IAppConfigClient client
        )
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
                new AppConfigConfigurationSource()
                {
                    Client = client,
                    Options = options
                }
            );

            return configurationBuilder;
        }
    }
}
