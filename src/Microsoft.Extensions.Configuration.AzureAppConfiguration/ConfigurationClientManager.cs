﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using Azure.Core;
using Azure.Data.AppConfiguration;
using DnsClient;
using DnsClient.Protocol;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Constants;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// A configuration client manager which maintains state of configuration clients and provides set of clients to use when requested.
    /// </summary>
    /// <remarks>
    /// This class is not thread-safe. Since config provider does not allow multiple network requests at the same time,
    /// there won't be multiple threads calling this client at the same time.
    /// </remarks>
    internal class ConfigurationClientManager : IConfigurationClientManager
    {
        private IList<ConfigurationClientWrapper> _clients;
        private ConfigurationClientOptions _clientOptions;
        private Lazy<SrvLookupClient> _srvLookupClient;
        private DateTimeOffset _lastFallbackClientRefresh = default;
        private bool _allClientFailed = false;

        private readonly Uri _endpoint;
        private readonly string _secret;
        private readonly string _id;
        private readonly TokenCredential _credential;
        private readonly bool _replicaDiscoveryEnabled;

        private readonly static TimeSpan _fallbackClientRefreshInterval = TimeSpan.FromSeconds(30);

        public ConfigurationClientManager(AzureAppConfigurationOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (options.ClientOptions == null)
            {
                throw new ArgumentException(nameof(options.ClientOptions));
            }

            IEnumerable<string> connectionStrings = options.ConnectionStrings;
            IEnumerable<Uri> endpoints = options.Endpoints;
            _credential = options.Credential;
            _clientOptions = options.ClientOptions;
            _replicaDiscoveryEnabled = options.ReplicaDiscoveryEnabled;

            _srvLookupClient = new Lazy<SrvLookupClient>();

            if (connectionStrings != null && connectionStrings.Any())
            {
                string connectionString = connectionStrings.First();
                _endpoint = new Uri(ConnectionStringUtils.Parse(connectionString, ConnectionStringUtils.EndpointSection));
                _secret = ConnectionStringUtils.Parse(connectionString, ConnectionStringUtils.SecretSection);
                _id = ConnectionStringUtils.Parse(connectionString, ConnectionStringUtils.IdSection);

                _clients = connectionStrings
                    .Select(cs =>
                        {
                            var endpoint = new Uri(ConnectionStringUtils.Parse(cs, ConnectionStringUtils.EndpointSection));
                            return new ConfigurationClientWrapper(endpoint, new ConfigurationClient(cs, _clientOptions));
                        })
                    .ToList();
            }
            else if (endpoints != null && endpoints.Any())
            {
                if (_credential == null)
                {
                    throw new ArgumentException(nameof(options.Credential));
                }

                _endpoint = endpoints.First();

                _clients = endpoints
                    .Select(endpoint => new ConfigurationClientWrapper(endpoint, new ConfigurationClient(endpoint, _credential, _clientOptions)))
                    .ToList();
            }
            else
            {
                throw new ArgumentException($"Neither {nameof(options.ConnectionStrings)} nor {nameof(options.Endpoints)} is specified.");
            }
        }

        /// <summary>
        /// Internal constructor; Only used for unit testing.
        /// </summary>
        /// <param name="clients"></param>
        internal ConfigurationClientManager(IList<ConfigurationClientWrapper> clients)
        {
            _clients = clients;
        }

        public async Task<IEnumerable<ConfigurationClient>> GetAvailableClients(CancellationToken cancellationToken)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;

            if (_replicaDiscoveryEnabled)
            {
                // The source of truth whether there's available client in the list.
                int successfulClientsCount = _clients.Where(client => client.FailedAttempts == 0).Count();

                // Refresh fallback clients if:
                // 1.It's the initial attempt to query available clients on app start-up.
                // 2.At least one client is available in the last attempt, it's the first attempt that all static and dynamic clients are backed off.
                // 3.It's not the first attempt that all clients have backed off, as well as it's due time to refresh fallback clients.

                if ((_lastFallbackClientRefresh == default ||
                    successfulClientsCount == 0) &&
                    !_allClientFailed ||
                    now >= _lastFallbackClientRefresh + _fallbackClientRefreshInterval)
                {
                    await RefreshFallbackClients(cancellationToken).ConfigureAwait(false);

                    _lastFallbackClientRefresh = now;
                }

                _allClientFailed = successfulClientsCount == 0;
            }

            return _clients.Where(client => client.BackoffEndTime <= now).Select(c => c.Client).ToList();
        }

        public void UpdateClientStatus(ConfigurationClient client, bool successful)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            ConfigurationClientWrapper clientWrapper = _clients.First(c => c.Client.Equals(client));

            if (successful)
            {
                clientWrapper.BackoffEndTime = DateTimeOffset.UtcNow;
                clientWrapper.FailedAttempts = 0;
            }
            else
            {
                clientWrapper.FailedAttempts++;
                TimeSpan backoffDuration = FailOverConstants.MinBackoffDuration.CalculateBackoffDuration(FailOverConstants.MaxBackoffDuration, clientWrapper.FailedAttempts);
                clientWrapper.BackoffEndTime = DateTimeOffset.UtcNow.Add(backoffDuration);
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

            ConfigurationClientWrapper clientWrapper = this._clients.SingleOrDefault(c => new EndpointComparer().Equals(c.Endpoint, endpoint));

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

        private async Task RefreshFallbackClients(CancellationToken cancellationToken)
        {
            IEnumerable<SrvRecord> results = Enumerable.Empty<SrvRecord>();

            try
            {
                results = await _srvLookupClient.Value.QueryAsync(_endpoint.DnsSafeHost, cancellationToken).ConfigureAwait(false);
            }
            catch (SocketException)
            {
                // If the DNS lookup fails due to network, just return.
                return;
            }
            catch (DnsResponseException ex) when (ex.Code == DnsResponseCode.ConnectionTimeout)
            {
                // If the DNS lookup times out, just return.
                return;
            }

            // Shuffle the results to ensure hosts can be picked randomly.
            // Srv lookup may retrieve trailing dot in the host name, just trim it.
            IEnumerable<string> srvTargetHosts = results.Shuffle().Select(r => $"{r.Target.Value.Trim('.')}").ToList();

            // clean up clients in case the corresponding replicas are removed.
            foreach (ConfigurationClientWrapper client in _clients)
            {
                if (IsEligibleToRemove(srvTargetHosts, client))
                {
                    _clients.Remove(client);
                }
            }

            foreach (string host in srvTargetHosts)
            {
                if (!_clients.Any(c => c.Endpoint.Host.Equals(host, StringComparison.OrdinalIgnoreCase)))
                {
                    var targetEndpoint = new Uri($"https://{host}");

                    var configClient = _credential == null ?
                        new ConfigurationClient(ConnectionStringUtils.Build(targetEndpoint, _id, _secret), _clientOptions) :
                        new ConfigurationClient(targetEndpoint, _credential, _clientOptions);

                    _clients.Add(new ConfigurationClientWrapper(targetEndpoint, configClient, true));
                }
            }
        }

        private bool IsEligibleToRemove(IEnumerable<string> srvEndpointHosts, ConfigurationClientWrapper client)
        {
            // Only remove the client if it is discovered, as well as not in returned SRV records.
            if (!client.IsDiscoveredClient)
            {
                return false;
            }

            if (srvEndpointHosts.Any(h => h.Equals(client.Endpoint.Host, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            return true;
        }
    }
}
