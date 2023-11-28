// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.AzureAppConfiguration
{
    internal class MockedConfigurationClientManager : IConfigurationClientManager
    {
        IList<ConfigurationClientWrapper> _clients;
        IList<ConfigurationClientWrapper> _autoFailoverClients;

        internal int UpdateSyncTokenCalled { get; set; } = 0;

        public bool HasAvailableClients => _clients.Any(client => client.BackoffEndTime <= DateTime.UtcNow);

        public MockedConfigurationClientManager(IEnumerable<ConfigurationClientWrapper> clients)
        {
            _clients = clients.ToList();
            _autoFailoverClients = new List<ConfigurationClientWrapper>();
        }

        public IEnumerable<ConfigurationClient> GetAvailableClients(DateTimeOffset time) => GetAllClients();

        public IEnumerable<ConfigurationClient> GetAllClients()
        {
            return _clients.Select(cw => cw.Client);
        }

        public MockedConfigurationClientManager(IEnumerable<ConfigurationClientWrapper> clients, IEnumerable<ConfigurationClientWrapper> autoFailoverClients)
        {
            _autoFailoverClients = autoFailoverClients.ToList();
            _clients = clients.ToList();
        }

        public async IAsyncEnumerable<ConfigurationClient> GetAvailableClients([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(0);

            foreach (var client in _clients)
            {
                yield return client.Client;
            }

            foreach (var client in _autoFailoverClients)
            {
                yield return client.Client;
            }
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

            return currentClient?.Endpoint;
        }

        public async IAsyncEnumerable<ConfigurationClient> GetAllClients([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(0);

            foreach (var client in _clients)
            {
                yield return client.Client;
            }

            foreach (var client in _autoFailoverClients)
            {
                yield return client.Client;
            }
        }
    }
}
