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
        IList<AppConfigurationClient> _clients;
        IList<AppConfigurationClient> _autoFailoverClients;

        internal int UpdateSyncTokenCalled { get; set; } = 0;

        public MockedConfigurationClientManager(IEnumerable<AppConfigurationClient> clients)
        {
            _clients = clients.ToList();
            _autoFailoverClients = new List<AppConfigurationClient>();
        }

        public MockedConfigurationClientManager(IEnumerable<AppConfigurationClient> clients, IEnumerable<AppConfigurationClient> autoFailoverClients)
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
            client?.UpdateSyncToken(syncToken);
            return true;
        }

        public IEnumerable<IAppConfigurationClient> GetClients()
        {
            var result = new List<IAppConfigurationClient>();

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
