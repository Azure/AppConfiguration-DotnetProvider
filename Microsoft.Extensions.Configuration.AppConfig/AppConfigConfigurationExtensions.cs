namespace Microsoft.Extensions.Configuration.AppConfig
{
    using System;

    public static class AppConfigConfigurationExtensions
    {
        private const string EndPointSegmentId = "EndPoint=";
        private const string CredentialSegmentId = "Credential=";
        private const string SecretSegmentId = "Secret=";

        public static IConfigurationBuilder AddRemoteAppConfiguration
        (
            this IConfigurationBuilder configurationBuilder,
            string connectionString
        )
        {
            return AddRemoteAppConfiguration(configurationBuilder, connectionString, new RemoteConfigurationOptions());
        }

        public static IConfigurationBuilder AddRemoteAppConfiguration
        (
            this IConfigurationBuilder configurationBuilder,
            string connectionString,
            RemoteConfigurationOptions options
        )
        {
            if (String.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentNullException(nameof(connectionString));
            }

            string appConfigUri=null;
            string secretId=null;
            string secretValue=null;

            foreach (var entry in connectionString.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var segment = entry.Trim();
                if (segment.StartsWith(EndPointSegmentId, StringComparison.OrdinalIgnoreCase))
                {
                    appConfigUri = segment.Substring(EndPointSegmentId.Length);
                }
                else if (segment.StartsWith(CredentialSegmentId, StringComparison.OrdinalIgnoreCase))
                {
                    secretId = segment.Substring(CredentialSegmentId.Length);
                }
                else if (segment.StartsWith(SecretSegmentId, StringComparison.OrdinalIgnoreCase))
                {
                    secretValue = segment.Substring(SecretSegmentId.Length);
                }
            }

            return AddRemoteAppConfiguration(configurationBuilder, appConfigUri, secretId, secretValue, options);
        }

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
