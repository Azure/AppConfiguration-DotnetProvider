// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Azure;
using System;
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Afd
{
    internal class AfdClientManager : IClientManager
    {
        private readonly ClientWrapper _clientWrapper;

        public AfdClientManager(
            IAzureClientFactory<ConfigurationClient> configurationClientFactory,
            IAzureClientFactory<FeatureFlagClient> featureFlagClientFactory,
            Uri endpoint)
        {
            if (configurationClientFactory == null)
            {
                throw new ArgumentNullException(nameof(configurationClientFactory));
            }

            if (featureFlagClientFactory == null)
            {
                throw new ArgumentNullException(nameof(featureFlagClientFactory));
            }

            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            _clientWrapper = new ClientWrapper(
                endpoint,
                configurationClientFactory.CreateClient(endpoint.AbsoluteUri),
                featureFlagClientFactory.CreateClient(endpoint.AbsoluteUri));
        }

        public IEnumerable<ClientWrapper> GetClients()
        {
            return new List<ClientWrapper> { _clientWrapper };
        }

        public void RefreshClients()
        {
            return;
        }

        public bool UpdateSyncToken(Uri endpoint, string syncToken)
        {
            return false;
        }
    }
}
