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
    internal class MockedConfigurationClientProvider : IConfigurationClientProvider
    {
        IList<ConfigurationClientStatus> _clients;

        internal int UpdateSyncTokenCalled { get; set; } = 0;

        public MockedConfigurationClientProvider(IEnumerable<ConfigurationClientStatus> clients)
        {
            this._clients = clients.ToList();
        }

        public IEnumerable<ConfigurationClient> GetClients()
        {
            return this._clients.Select(cw => cw.Client);
        }

        public void UpdateClientStatus(ConfigurationClient client, bool successful)
        {
            return;
        }

        public bool UpdateSyncToken(Uri endpoint, string syncToken)
        {
            this.UpdateSyncTokenCalled++;
            var client = _clients.SingleOrDefault(c => c.Endpoint.Host.ToLowerInvariant().Equals(endpoint.Host.ToLowerInvariant()));
            client?.Client?.UpdateSyncToken(syncToken);
            return true;
        }
    }
}
