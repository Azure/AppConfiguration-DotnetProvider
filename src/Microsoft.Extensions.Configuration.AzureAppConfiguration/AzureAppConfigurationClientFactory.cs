using System;
using Azure;
using Azure.Data.AppConfiguration;
using Azure.Core;
using Microsoft.Extensions.Azure;
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class AzureAppConfigurationClientFactory : IAzureClientFactory<ConfigurationClient>
    {
        private readonly ConfigurationClientOptions _clientOptions;

        private readonly TokenCredential _credential;

        private readonly string _secret;
        private readonly string _id;
        private IDictionary<string, ConfigurationClient> _clients = new Dictionary<string, ConfigurationClient>();

        public AzureAppConfigurationClientFactory(
            string connectionString,
            ConfigurationClientOptions clientOptions)
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentNullException(nameof(connectionString));
            }

            _clientOptions = clientOptions ?? throw new ArgumentNullException(nameof(clientOptions));

            _secret = ConnectionStringUtils.Parse(connectionString, ConnectionStringUtils.SecretSection);
            _id = ConnectionStringUtils.Parse(connectionString, ConnectionStringUtils.IdSection);
        }

        public AzureAppConfigurationClientFactory(
            TokenCredential credential,
            ConfigurationClientOptions clientOptions)
        {
            _credential = credential ?? throw new ArgumentNullException(nameof(credential));
            _clientOptions = clientOptions ?? throw new ArgumentNullException(nameof(clientOptions));
        }

        public ConfigurationClient CreateClient(string endpoint)
        {
            if (string.IsNullOrEmpty(endpoint))
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri uriResult) || !(uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
            {
                throw new ArgumentException("Invalid host URI.");
            }

            if (!_clients.TryGetValue(endpoint, out ConfigurationClient configClient))
            {
                configClient = _credential == null
                        ? new ConfigurationClient(ConnectionStringUtils.Build(new Uri(endpoint), _id, _secret), _clientOptions)
                        : new ConfigurationClient(new Uri(endpoint), _credential, _clientOptions);

                _clients[endpoint] = configClient;
            }

            return configClient;
        }
    }
}
