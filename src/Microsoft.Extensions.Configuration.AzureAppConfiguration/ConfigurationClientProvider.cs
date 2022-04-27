// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using Azure.Core;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Constants;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// A configuration client provider which maintains state of configuration clients and provides set of clients to use when requested.
    /// </summary>
    /// <remarks>
    /// This class is not thread-safe. Since config provider does not allow multiple network requests at the same time,
    /// there won't be multiple threads calling this client at the same time.
    /// </remarks>
    internal class ConfigurationClientProvider : IConfigurationClientProvider
    {
        private readonly IList<ConfigurationClientWrapper> _clients;
        private readonly TokenCredential _tokenCredential;

        public ConfigurationClientProvider(string connectionString, ConfigurationClientOptions clientOptions)
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentNullException(nameof(connectionString));
            }

            var endpoint = new Uri(ConnectionStringParser.Parse(connectionString, ConnectionStringParser.EndpointSection));
            _clients = new List<ConfigurationClientWrapper> { new ConfigurationClientWrapper(endpoint, new ConfigurationClient(connectionString, clientOptions)) };
        }

        public ConfigurationClientProvider(IEnumerable<Uri> endpoints, TokenCredential credential, ConfigurationClientOptions clientOptions)
        {
            if (endpoints == null || endpoints.Count() < 1)
            {
                throw new ArgumentNullException(nameof(endpoints));
            }

            _tokenCredential = credential ?? throw new NullReferenceException(nameof(credential));

            _clients = endpoints.Select(endpoint => new ConfigurationClientWrapper(endpoint, new ConfigurationClient(endpoint, credential, clientOptions))).ToList();
        }

        /// <summary>
        /// Internal constructor; Only used for unit testing.
        /// </summary>
        /// <param name="clients"></param>
        internal ConfigurationClientProvider(IList<ConfigurationClientWrapper> clients)
        {
            _clients = clients;
        }

        public IEnumerator<ConfigurationClient> GetClientEnumerator()
        {
            IList<ConfigurationClient> clients = new List<ConfigurationClient>();

            foreach(ConfigurationClientWrapper configurationClient in _clients)
            {
                if (configurationClient.BackoffEndTime <= DateTimeOffset.UtcNow)
                {
                    clients.Add(configurationClient.Client);
                }
            }

            // If all clients are in the back-off state, try all clients anyways.
            if (clients.Count == 0)
            {
                clients = _clients.Select(c => c.Client).ToList();
            }

            return clients.GetEnumerator();
        }

        public void UpdateClientStatus(ConfigurationClient client, bool successful)
        {
            ConfigurationClientWrapper clientWrapper = _clients.First(c => c.Client.Equals(client));

            if (successful)
            {
                clientWrapper.BackoffEndTime = DateTimeOffset.UtcNow;
                clientWrapper.FailedAttempts = 0;
            }
            else
            {
                clientWrapper.FailedAttempts++;
                TimeSpan backoffInterval = FailOverConstants.MinBackoffInterval.CalculateBackoffInterval(FailOverConstants.MaxBackoffInterval, clientWrapper.FailedAttempts);
                clientWrapper.BackoffEndTime = DateTimeOffset.UtcNow.Add(backoffInterval);
            }
        }

        public bool UpdateSyncToken(Uri endpoint, string syncToken)
        {
            ConfigurationClientWrapper clientWrapper = this._clients.SingleOrDefault(c => c.Endpoint.Host.ToLowerInvariant().Equals(endpoint.Host.ToLowerInvariant()));

            if (clientWrapper != null)
            {
                clientWrapper.Client.UpdateSyncToken(syncToken);
                return true;
            }

            // If the endpoint is not present in the list, but we have the token credential, then try to create a new client and put it in the list first.
            if (_tokenCredential != null)
            {
                ConfigurationClient newClient = new ConfigurationClient(endpoint, _tokenCredential);
                newClient.UpdateSyncToken(syncToken);
                var newClientWrapper = new ConfigurationClientWrapper(endpoint, newClient);
                this._clients.Insert(0, newClientWrapper);

                return true;
            }

            return false;
        }
    }
}
