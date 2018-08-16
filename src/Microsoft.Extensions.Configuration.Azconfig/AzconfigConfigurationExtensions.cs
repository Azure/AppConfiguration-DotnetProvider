namespace Microsoft.Extensions.Configuration.Azconfig
{
    using Microsoft.Azconfig.Client;
    using Microsoft.Extensions.Configuration.Azconfig.Models;
    using System;

    public static class AzconfigConfigurationExtensions
    {
        private const string EndPointSegmentId = "Endpoint=";
        private const string CredentialSegmentId = "Id=";
        private const string SecretSegmentId = "Secret=";

        public static IConfigurationBuilder AddRemoteAppConfiguration
        (
            this IConfigurationBuilder configurationBuilder,
            string azconfigUri,
            string secretId,
            string secretValue,
            RemoteConfigurationOptions options
        )
        {
            string connectionString = EndPointSegmentId + azconfigUri + ";";
            connectionString += CredentialSegmentId + secretId + ";";
            connectionString += SecretSegmentId + secretValue;

            return AddRemoteAppConfiguration(configurationBuilder, connectionString, options);
        }

        public static IConfigurationBuilder AddRemoteAppConfiguration
        (
            this IConfigurationBuilder configurationBuilder,
            string azconfigUri,
            string secretId,
            string secretValue
        )
        {
            return AddRemoteAppConfiguration(configurationBuilder, azconfigUri, secretId, secretValue, new RemoteConfigurationOptions());
        }

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

            return AddRemoteAppConfiguration(configurationBuilder, options, new AzconfigClient(connectionString));
        }

        public static IConfigurationBuilder AddRemoteAppConfiguration
        (
            this IConfigurationBuilder configurationBuilder,
            RemoteConfigurationOptions options,
            AzconfigClient client
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
