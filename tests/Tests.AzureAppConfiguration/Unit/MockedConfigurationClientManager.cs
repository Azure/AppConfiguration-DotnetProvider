// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Tests.AzureAppConfiguration
{
    internal class MockedConfigurationClientManager : IConfigurationClientManager
    {
        IList<ConfigurationClientWrapper> _clients;
        IList<ConfigurationClientWrapper> _autoFailoverClients;

        internal int UpdateSyncTokenCalled { get; set; } = 0;

        public MockedConfigurationClientManager(IEnumerable<ConfigurationClientWrapper> clients)
        {
            _clients = clients.ToList();
            _autoFailoverClients = new List<ConfigurationClientWrapper>();
        }

        public MockedConfigurationClientManager(IEnumerable<ConfigurationClientWrapper> clients, IEnumerable<ConfigurationClientWrapper> autoFailoverClients)
        {
            _autoFailoverClients = autoFailoverClients.ToList();
            _clients = clients.ToList();
        }

        public void UpdateClientStatus(ConfigurationClient client, bool successful)
        {
            return;
        }

        public bool UpdateSyncToken(Uri endpoint, string syncToken)
        {
            this.UpdateSyncTokenCalled++;
            var client = _clients.SingleOrDefault(c => string.Equals(c.Endpoint.Host, endpoint.Host, StringComparison.OrdinalIgnoreCase));
            client?.Client?.UpdateSyncToken(syncToken);
            return true;
        }

        public Uri GetEndpointForClient(ConfigurationClient client)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            ConfigurationClientWrapper currentClient = _clients.FirstOrDefault(c => c.Client == client);

            if (currentClient == null)
            {
                currentClient = _autoFailoverClients.FirstOrDefault(c => c.Client == client);
            }

            return currentClient?.Endpoint;
        }

        public IEnumerable<ConfigurationClient> GetClients()
        {
            var result = new List<ConfigurationClient>();

            foreach (var client in _clients)
            {
                result.Add(client.Client);
            }

            foreach (var client in _autoFailoverClients)
            {
                result.Add(client.Client);
            }

            return result;
        }

        public void RefreshClients()
        {
            return;
        }
    }
}
