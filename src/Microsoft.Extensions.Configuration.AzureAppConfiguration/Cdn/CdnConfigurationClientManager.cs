// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Azure;
using System;
using System.Collections.Generic;
using System.Linq;
namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Cdn
{
    internal class CdnConfigurationClientManager : IConfigurationClientManager
    {
        private readonly ConfigurationClientWrapper _clientWrapper;

        public CdnConfigurationClientManager(
            IAzureClientFactory<ConfigurationClient> clientFactory,
            Uri endpoint)
        {
            if (clientFactory == null)
            {
                throw new ArgumentNullException(nameof(clientFactory));
            }

            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            _clientWrapper = new ConfigurationClientWrapper(endpoint, clientFactory.CreateClient(endpoint.AbsoluteUri));
        }

        public IEnumerable<ConfigurationClient> GetClients()
        {
            return new[] { _clientWrapper.Client };
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

            if (new EndpointComparer().Equals(_clientWrapper.Endpoint, endpoint))
            {
                _clientWrapper.Client.UpdateSyncToken(syncToken);
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

            return _clientWrapper.Client == client ? _clientWrapper.Endpoint : null;
        }
    }
}
