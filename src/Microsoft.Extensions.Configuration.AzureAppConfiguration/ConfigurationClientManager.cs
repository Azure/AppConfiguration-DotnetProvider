// Copyright (c) Microsoft Corporation.
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
using System.Diagnostics;
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
        private readonly IList<ConfigurationClientWrapper> _clients;
        private readonly Uri _endpoint;
        private readonly string _secret;
        private readonly string _id;
        private readonly TokenCredential _credential;
        private readonly ConfigurationClientOptions _clientOptions;
        private readonly bool _replicaDiscoveryEnabled;
        private readonly SrvLookupClient _srvLookupClient;
        private readonly string _validDomain;

        private IList<ConfigurationClientWrapper> _dynamicClients;
        private DateTimeOffset _lastFallbackClientRefresh = default;
        private DateTimeOffset _lastFallbackClientRefreshAttempt = default;
        private Logger _logger = new Logger();

        private static readonly TimeSpan FallbackClientRefreshExpireInterval = TimeSpan.FromHours(1);
        private static readonly TimeSpan MinimalClientRefreshInterval = TimeSpan.FromSeconds(30);
        private static readonly string[] TrustedDomainLabels = new[] { "azconfig", "appconfig" };

        public ConfigurationClientManager(
            IEnumerable<string> connectionStrings,
            ConfigurationClientOptions clientOptions,
            bool replicaDiscoveryEnabled)
        {
            if (connectionStrings == null || !connectionStrings.Any())
            {
                throw new ArgumentNullException(nameof(connectionStrings));
            }

            string connectionString = connectionStrings.First();
            _endpoint = new Uri(ConnectionStringUtils.Parse(connectionString, ConnectionStringUtils.EndpointSection));
            _secret = ConnectionStringUtils.Parse(connectionString, ConnectionStringUtils.SecretSection);
            _id = ConnectionStringUtils.Parse(connectionString, ConnectionStringUtils.IdSection);
            _clientOptions = clientOptions;
            _replicaDiscoveryEnabled = replicaDiscoveryEnabled;

            _validDomain = GetValidDomain(_endpoint);
            _srvLookupClient = new SrvLookupClient();

            _clients = connectionStrings
                .Select(cs =>
                {
                    var endpoint = new Uri(ConnectionStringUtils.Parse(cs, ConnectionStringUtils.EndpointSection));
                    return new ConfigurationClientWrapper(endpoint, new ConfigurationClient(cs, _clientOptions));
                })
                .ToList();
        }

        public ConfigurationClientManager(
            IEnumerable<Uri> endpoints,
            TokenCredential credential,
            ConfigurationClientOptions clientOptions,
            bool replicaDiscoveryEnabled)
        {
            if (endpoints == null || !endpoints.Any())
            {
                throw new ArgumentNullException(nameof(endpoints));
            }

            if (credential == null)
            {
                throw new ArgumentNullException(nameof(credential));
            }

            _endpoint = endpoints.First();
            _credential = credential;
            _clientOptions = clientOptions;
            _replicaDiscoveryEnabled = replicaDiscoveryEnabled;

            _validDomain = GetValidDomain(_endpoint);
            _srvLookupClient = new SrvLookupClient();

            _clients = endpoints
                .Select(endpoint => new ConfigurationClientWrapper(endpoint, new ConfigurationClient(endpoint, _credential, _clientOptions)))
                .ToList();
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
                now >= _lastFallbackClientRefreshAttempt + MinimalClientRefreshInterval &&
                (_dynamicClients == null ||
                now >= _lastFallbackClientRefresh + FallbackClientRefreshExpireInterval))
            {
                _lastFallbackClientRefreshAttempt = now;

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                cts.CancelAfter(MinimalClientRefreshInterval);

                try
                {
                    await RefreshFallbackClients(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Do nothing if origin cancellationToken is not cancelled.
                }
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

            if (_dynamicClients != null)
            {
                foreach (ConfigurationClientWrapper clientWrapper in _dynamicClients)
                {
                    if (clientWrapper.BackoffEndTime <= now)
                    {
                        yield return clientWrapper.Client;
                    }
                }
            }

            // All static and dynamic clients exhausted, check if previously unknown
            // dynamic client available
            if (now >= _lastFallbackClientRefreshAttempt + MinimalClientRefreshInterval)
            {
                _lastFallbackClientRefreshAttempt = now;

                await RefreshFallbackClients(cancellationToken).ConfigureAwait(false);
            }

            if (_dynamicClients != null)
            {
                foreach (ConfigurationClientWrapper clientWrapper in _dynamicClients)
                {
                    if (clientWrapper.BackoffEndTime <= now)
                    {
                        yield return clientWrapper.Client;
                    }
                }
            }
        }

        public void UpdateClientStatus(ConfigurationClient client, bool successful)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            ConfigurationClientWrapper clientWrapper = _clients.FirstOrDefault(c => c.Client == client);

            if (_dynamicClients != null && clientWrapper == null)
            {
                clientWrapper = _dynamicClients.FirstOrDefault(c => c.Client == client);
            }

            if (clientWrapper == null)
            {
                return;
            }

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

            ConfigurationClientWrapper clientWrapper = _clients.SingleOrDefault(c => new EndpointComparer().Equals(c.Endpoint, endpoint));

            if (_dynamicClients != null && clientWrapper == null)
            {
                clientWrapper = _dynamicClients.SingleOrDefault(c => new EndpointComparer().Equals(c.Endpoint, endpoint));
            }

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

            if (_dynamicClients != null && currentClient == null)
            {
                currentClient = _dynamicClients.FirstOrDefault(c => c.Client == client);
            }

            return currentClient?.Endpoint;
        }

        public void SetLogger(Logger logger)
        {
            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _logger = logger;
        }

        private async Task RefreshFallbackClients(CancellationToken cancellationToken)
        {
            IEnumerable<string> srvTargetHosts = Enumerable.Empty<string>();

            try
            {
                srvTargetHosts = await _srvLookupClient.QueryAsync(_endpoint.DnsSafeHost, cancellationToken).ConfigureAwait(false);
            }
            // Catch and log all exceptions thrown by srv lookup client to avoid new possible exceptions on app startup.
            catch (SocketException ex)
            {
                _logger.LogWarning(LogHelper.BuildFallbackClientLookupFailMessage(ex.Message));
                return;
            }
            catch (DnsResponseException ex)
            {
                _logger.LogWarning(LogHelper.BuildFallbackClientLookupFailMessage(ex.Message));
                return;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(LogHelper.BuildFallbackClientLookupFailMessage(ex.Message));
                return;
            }
            catch (DnsXidMismatchException ex)
            {
                _logger.LogWarning(LogHelper.BuildFallbackClientLookupFailMessage(ex.Message));
                return;
            }

            _dynamicClients ??= new List<ConfigurationClientWrapper>();

            // Shuffle the results to ensure hosts can be picked randomly.
            // Srv lookup does retrieve trailing dot in the host name, just trim it.
            IEnumerable<string> shuffledHosts = srvTargetHosts.Any() ?
                srvTargetHosts.ToList().Shuffle() :
                Enumerable.Empty<string>();

            // clean up clients in case the corresponding replicas are removed.
            foreach (ConfigurationClientWrapper client in _dynamicClients)
            {
                // Remove from dynamicClient if the replica no longer exists in SRV records.
                if (!shuffledHosts.Any(h => h.Equals(client.Endpoint.Host, StringComparison.OrdinalIgnoreCase)))
                {
                    _dynamicClients.Remove(client);
                }
            }

            foreach (string host in shuffledHosts)
            {
                if (!string.IsNullOrEmpty(host) &&
                    !_clients.Any(c => c.Endpoint.Host.Equals(host, StringComparison.OrdinalIgnoreCase)) &&
                    !_dynamicClients.Any(c => c.Endpoint.Host.Equals(host, StringComparison.OrdinalIgnoreCase)) &&
                    IsValidEndpoint(host))
                {
                    var targetEndpoint = new Uri($"https://{host}");

                    var configClient = _credential == null ?
                        new ConfigurationClient(ConnectionStringUtils.Build(targetEndpoint, _id, _secret), _clientOptions) :
                        new ConfigurationClient(targetEndpoint, _credential, _clientOptions);

                    _dynamicClients.Add(new ConfigurationClientWrapper(targetEndpoint, configClient));
                }
            }

            _lastFallbackClientRefresh = DateTime.UtcNow;
        }

        private string GetValidDomain(Uri endpoint)
        {
            Debug.Assert(endpoint != null);

            string hostName = endpoint.Host;

            foreach (string label in TrustedDomainLabels)
            {
                int index = hostName.LastIndexOf($".{label}.", StringComparison.OrdinalIgnoreCase);

                if (index > 0)
                {
                    return hostName.Substring(index);
                }
            }

            return string.Empty;
        }

        internal bool IsValidEndpoint(string hostName)
        {
            Debug.Assert(!string.IsNullOrEmpty(hostName));

            if (string.IsNullOrEmpty(_validDomain))
            {
                return false;
            }

            return hostName.EndsWith(_validDomain, StringComparison.OrdinalIgnoreCase);
        }
    }
}
