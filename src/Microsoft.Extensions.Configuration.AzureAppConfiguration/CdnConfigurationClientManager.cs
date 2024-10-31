using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Azure;
using System;
using System.Collections.Generic;
namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class CdnConfigurationClientManager : IConfigurationClientManager
    {
        private readonly ConfigurationClient _client;
        private readonly Uri _endpoint;

        public CdnConfigurationClientManager(
            IAzureClientFactory<ConfigurationClient> clientFactory,
            Uri endpoint)
        {
            if (clientFactory == null)
            {
                throw new ArgumentNullException(nameof(clientFactory));
            }
            _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));

            _client = clientFactory.CreateClient(_endpoint.AbsoluteUri);
        }

        public IEnumerable<ConfigurationClient> GetClients()
        {
            return new List<ConfigurationClient> { _client };
        }

        public void RefreshClients()
        {
            return;
        }

        public bool UpdateSyncToken(Uri endpoint, string syncToken)
        {
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            if (string.IsNullOrWhiteSpace(syncToken))
            {
                throw new ArgumentNullException(nameof(syncToken));
            }

            if (new EndpointComparer().Equals(endpoint, _endpoint))
            {
                _client.UpdateSyncToken(syncToken);

                return true;
            }

            return false;
        }

        public Uri GetEndpointForClient(ConfigurationClient client)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            if (client == _client)
            {
                return _endpoint;
            }

            return null;
        }
    }
}
