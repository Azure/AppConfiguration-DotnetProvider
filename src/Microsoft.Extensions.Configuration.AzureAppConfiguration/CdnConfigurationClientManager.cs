using Azure.Data.AppConfiguration;
using System;
using System.Collections.Generic;
using System.Linq;
namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class CdnConfigurationClientManager : IConfigurationClientManager
    {
        private readonly IList<ConfigurationClientWrapper> _clients;

        public CdnConfigurationClientManager(
            IEnumerable<Uri> endpoints,
            ConfigurationClientOptions clientOptions)
        {
            if (endpoints == null || !endpoints.Any())
            {
                throw new ArgumentNullException(nameof(endpoints));
            }

            if (clientOptions == null)
            {
                throw new ArgumentNullException(nameof(clientOptions));
            }

            _clients = endpoints
                .Select(endpoint => new ConfigurationClientWrapper(endpoint, new ConfigurationClient(endpoint, new EmptyTokenCredential(), clientOptions)))
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
