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
    internal class MockedConfigurationClientManager : IClientManager
    {
        IList<ClientWrapper> _clients;
        IList<ClientWrapper> _autoFailoverClients;

        internal int UpdateSyncTokenCalled { get; set; } = 0;

        public MockedConfigurationClientManager(IEnumerable<ClientWrapper> clients)
        {
            _clients = clients.ToList();
            _autoFailoverClients = new List<ClientWrapper>();
        }

        public MockedConfigurationClientManager(IEnumerable<ClientWrapper> clients, IEnumerable<ClientWrapper> autoFailoverClients)
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
            client?.ConfigurationClient?.UpdateSyncToken(syncToken);
            return true;
        }

        public IEnumerable<ClientWrapper> GetClients()
        {
            var result = new List<ClientWrapper>();

            foreach (var client in _clients)
            {
                result.Add(client);
            }

            foreach (var client in _autoFailoverClients)
            {
                result.Add(client);
            }

            return result;
        }

        public void RefreshClients()
        {
            return;
        }
    }
}
