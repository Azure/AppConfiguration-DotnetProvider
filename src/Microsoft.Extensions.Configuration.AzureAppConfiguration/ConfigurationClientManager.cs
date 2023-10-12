// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using Azure.Core;
using Azure.Data.AppConfiguration;
using DnsClient;
using DnsClient.Protocol;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Constants;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Exceptions;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
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
        private IList<ConfigurationClientWrapper> _dynamicClients;
        private ConfigurationClientOptions _clientOptions;
        private Lazy<SrvLookupClient> _srvLookupClient;
        private DateTimeOffset _lastFallbackClientRefresh = default;

        private readonly Uri _endpoint;
        private readonly string _secret;
        private readonly string _id;
        private readonly TokenCredential _credential;
        private readonly bool _replicaDiscoveryEnabled;

        private static readonly TimeSpan FallbackClientRefreshExpireInterval = TimeSpan.FromHours(1);
        private static readonly TimeSpan MinimalClientRefreshInterval = TimeSpan.FromSeconds(30);

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

        public async IAsyncEnumerable<ConfigurationClient> GetAvailableClients([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;

            // Principle of refreshing fallback clients:
            // 1.Perform initial refresh attempt to query available clients on app start-up.
            // 2.Refreshing when cached fallback clients have expired, FallbackClientRefreshExpireInterval is due.
            // 3.At least wait MinimalClientRefreshInterval between two attempt to ensure not perform refreshing too often.

            if (_replicaDiscoveryEnabled &&
                now >= _lastFallbackClientRefresh + MinimalClientRefreshInterval && 
                (_dynamicClients == null ||
                now >= _lastFallbackClientRefresh + FallbackClientRefreshExpireInterval))
            {
                await RefreshFallbackClients(cancellationToken).ConfigureAwait(false);

                _lastFallbackClientRefresh = now;
            }

            foreach (ConfigurationClientWrapper clientWrapper in _clients)
            {
                if (clientWrapper.BackoffEndTime <= now)
                {
                    yield return clientWrapper.Client;
                }
            }

            // No need to continue if replica discovery is not enabled.
            if (!_replicaDiscoveryEnabled)
            {
                yield break;
            }

            foreach (ConfigurationClientWrapper clientWrapper in _dynamicClients)
            {
                if (clientWrapper.BackoffEndTime <= now)
                {
                    yield return clientWrapper.Client;
                }
            }

            // All static and dynamic clients exhausted, refresh fallback clients if
            // minimal client refresh interval is due.
            if (now >= _lastFallbackClientRefresh + MinimalClientRefreshInterval)
            {
                await RefreshFallbackClients(cancellationToken).ConfigureAwait(false);

                _lastFallbackClientRefresh = now;
            }

            foreach (ConfigurationClientWrapper clientWrapper in _dynamicClients)
            {
                if (clientWrapper.BackoffEndTime <= now)
                {
                    yield return clientWrapper.Client;
                }
            }
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
            // Catch and rethrow all exceptions thrown by srv record lookup to avoid new possible exceptions on app startup.
            catch (SocketException ex)
            {
                throw new FallbackClientLookupException(ex);
            }
            catch (DnsResponseException ex)
            {
                throw new FallbackClientLookupException(ex);
            }
            catch (InvalidOperationException ex)
            {
                throw new FallbackClientLookupException(ex);
            }
            catch (DnsXidMismatchException ex)
            {
                throw new FallbackClientLookupException(ex);
            }

            // Shuffle the results to ensure hosts can be picked randomly.
            // Srv lookup does retrieve trailing dot in the host name, just trim it.
            IEnumerable<string> srvTargetHosts = results.ToList().Shuffle().Select(r => $"{r.Target.Value.TrimEnd('.')}");

            _dynamicClients ??= new List<ConfigurationClientWrapper>();

            if (!srvTargetHosts.Any())
            {
                return;
            }

            // clean up clients in case the corresponding replicas are removed.
            foreach (ConfigurationClientWrapper client in _dynamicClients)
            {
                // Remove from dynamicClient if the replica no longer exists in SRV records.
                if (!srvTargetHosts.Any(h => h.Equals(client.Endpoint.Host, StringComparison.OrdinalIgnoreCase)))
                {
                    _dynamicClients.Remove(client);
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

                    _dynamicClients.Add(new ConfigurationClientWrapper(targetEndpoint, configClient));
                }
            }
        }
    }
}
