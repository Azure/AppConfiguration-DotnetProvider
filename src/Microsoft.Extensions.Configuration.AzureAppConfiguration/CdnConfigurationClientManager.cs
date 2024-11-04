using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Azure;
using System;
using System.Collections.Generic;
using System.Linq;
namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class CdnConfigurationClientManager : IConfigurationClientManager
    {
        private readonly IList<ConfigurationClientWrapper> _clients;

        public CdnConfigurationClientManager(
            IAzureClientFactory<ConfigurationClient> clientFactory,
            IEnumerable<Uri> endpoints)
        {
            if (clientFactory == null)
            {
                throw new ArgumentNullException(nameof(clientFactory));
            }

            _clients = endpoints
                .Select(endpoint => new ConfigurationClientWrapper(endpoint, clientFactory.CreateClient(endpoint.AbsoluteUri)))
                .ToList();
        }

        public IEnumerable<ConfigurationClient> GetClients()
        {
            return _clients.Select(c => c.Client);
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

            ConfigurationClientWrapper clientWrapper = _clients.SingleOrDefault(c => new EndpointComparer().Equals(c.Endpoint, endpoint));

            if (clientWrapper != null)
            {
                clientWrapper.Client.UpdateSyncToken(syncToken);

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

            ConfigurationClientWrapper currentClient = _clients.FirstOrDefault(c => c.Client == client);

            return currentClient?.Endpoint;
        }
    }
}
