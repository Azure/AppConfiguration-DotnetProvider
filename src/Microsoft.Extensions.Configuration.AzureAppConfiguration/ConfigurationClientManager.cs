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
    internal class ConfigurationClientManager : IConfigurationClientManager
    {
        private readonly IList<ConfigurationClientStatus> _clients;
        private readonly EndpointUriComparer _endpointComparer = new EndpointUriComparer();

        public ConfigurationClientManager(string connectionString, ConfigurationClientOptions clientOptions)
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentNullException(nameof(connectionString));
            }

            var endpoint = new Uri(ConnectionStringParser.Parse(connectionString, ConnectionStringParser.EndpointSection));
            var configurationClientStatus = new ConfigurationClientStatus(endpoint, new ConfigurationClient(connectionString, clientOptions));
            _clients = new List<ConfigurationClientStatus> { configurationClientStatus };
        }

        public ConfigurationClientManager(IEnumerable<Uri> endpoints, TokenCredential credential, ConfigurationClientOptions clientOptions)
        {
            if (endpoints == null || !endpoints.Any())
            {
                throw new ArgumentNullException(nameof(endpoints));
            }

            _clients = endpoints.Select(endpoint => new ConfigurationClientStatus(endpoint, new ConfigurationClient(endpoint, credential, clientOptions))).ToList();
        }

        /// <summary>
        /// Internal constructor; Only used for unit testing.
        /// </summary>
        /// <param name="clients"></param>
        internal ConfigurationClientManager(IList<ConfigurationClientStatus> clients)
        {
            _clients = clients;
        }

        public IEnumerable<ConfigurationClient> GetAvailableClients()
        {
            IEnumerable<ConfigurationClient> clients = Enumerable.Empty<ConfigurationClient>();

            clients = _clients.Where(client => client.BackoffEndTime <= DateTimeOffset.UtcNow).Select(c => c.Client);

            // If all clients are in the back-off state, try all clients anyways.
            if (!clients.Any())
            {
                clients = _clients.Select(c => c.Client);
            }

            return clients;
        }

        public void UpdateClientStatus(ConfigurationClient client, bool successful)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            ConfigurationClientStatus clientWrapper = _clients.First(c => c.Client.Equals(client));

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
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            if (string.IsNullOrWhiteSpace(syncToken))
            {
                throw new ArgumentNullException(nameof(syncToken));
            }

            ConfigurationClientStatus clientWrapper = this._clients.SingleOrDefault(c => this._endpointComparer.Equals(c.Endpoint, endpoint));

            if (clientWrapper != null)
            {
                clientWrapper.Client.UpdateSyncToken(syncToken);
                return true;
            }

            return false;
        }
    }
}
