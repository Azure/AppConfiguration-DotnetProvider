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

        private IList<ConfigurationClientWrapper> _dynamicClients;
        private Lazy<SrvLookupClient> _srvLookupClient;
        private DateTimeOffset _lastFallbackClientRefresh = default;
        private DateTimeOffset _lastFallbackClientRefreshAttempt = default;
        private Logger _logger = new Logger();
        private volatile int _counterLock = 0;

        private static readonly TimeSpan FallbackClientRefreshExpireInterval = TimeSpan.FromHours(1);
        private static readonly TimeSpan MinimalClientRefreshInterval = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan SrvLookupTimeout = TimeSpan.FromSeconds(30);

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

            _srvLookupClient = new Lazy<SrvLookupClient>();

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

            _srvLookupClient = new Lazy<SrvLookupClient>();

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
            DateTimeOffset minimalRefreshTime = _lastFallbackClientRefreshAttempt + MinimalClientRefreshInterval;
            Task task = null;

            // Principle of refreshing fallback clients:
            // 1.Perform initial refresh attempt to query available clients on app start-up.
            // 2.Refreshing when cached fallback clients have expired, FallbackClientRefreshExpireInterval is due.
            // 3.At least wait MinimalClientRefreshInterval between two attempt to ensure not perform refreshing too often.

            if (_replicaDiscoveryEnabled &&
                now >= minimalRefreshTime && 
                (_dynamicClients == null ||
                now >= _lastFallbackClientRefresh + FallbackClientRefreshExpireInterval))
            {
                _lastFallbackClientRefreshAttempt = now;

                var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(SrvLookupTimeout);

                task = RefreshFallbackClients(cts.Token);

                // Observe cancellation if it occurs
                _ = task.ObserveCancellation(_logger);

                _ = task.ContinueWith(t =>
                {
                    cts.Dispose();
                });
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
            if (now >= minimalRefreshTime)
            {
                if (task != null)
                {
                    await task.ConfigureAwait(false);
                }
                else
                {
                    _lastFallbackClientRefreshAttempt = now;

                    await RefreshFallbackClients(cancellationToken).ConfigureAwait(false);
                }
            }

            foreach (ConfigurationClientWrapper clientWrapper in _dynamicClients)
            {
                if (clientWrapper.BackoffEndTime <= now)
                {
                    yield return clientWrapper.Client;
                }
            }
        }

        private async Task RefreshFallbackClients(CancellationToken cancellationToken)
        {
            IEnumerable<SrvRecord> results = await _srvLookupClient.Value.QueryAsync(_endpoint.DnsSafeHost, cancellationToken).ConfigureAwait(false);

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

                    var configClient = _isUsingConnetionString ? 
                        new ConfigurationClient(ConnectionStringUtils.Build(targetEndpoint, _id, _secret), _clientOptions):
                        new ConfigurationClient(targetEndpoint, _credential, _clientOptions);

                    _clients.Add(new ConfigurationClientWrapper(targetEndpoint, configClient, true));
                }
            }
        }

        public IEnumerable<ConfigurationClient> GetAllClients()
        {
            return _clients.Select(c => c.Client).ToList();
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
            IEnumerable<SrvRecord> results = Enumerable.Empty<SrvRecord>();

            try
            {
                results = await _srvLookupClient.Value.QueryAsync(_endpoint.DnsSafeHost, cancellationToken).ConfigureAwait(false);
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

            // Shuffle the results to ensure hosts can be picked randomly.
            // Srv lookup does retrieve trailing dot in the host name, just trim it.
            IEnumerable<string> srvTargetHosts = results.ToList().Shuffle().Select(r => $"{r.Target.Value.TrimEnd('.')}");

            List<ConfigurationClientWrapper> newDynamicClients = new List<ConfigurationClientWrapper>();

            foreach (string host in srvTargetHosts)
            {
                if (!_clients.Any(c => c.Endpoint.Host.Equals(host, StringComparison.OrdinalIgnoreCase)))
                {
                    var targetEndpoint = new Uri($"https://{host}");

                    var configClient = _credential == null ?
                        new ConfigurationClient(ConnectionStringUtils.Build(targetEndpoint, _id, _secret), _clientOptions) :
                        new ConfigurationClient(targetEndpoint, _credential, _clientOptions);

                    newDynamicClients.Add(new ConfigurationClientWrapper(targetEndpoint, configClient));
                }
            }

            if (Interlocked.CompareExchange(ref _counterLock, 1, 0) == 0)
            {
                _dynamicClients = newDynamicClients;

                _lastFallbackClientRefresh = DateTime.UtcNow;

                Interlocked.Exchange(ref _counterLock, 0);
            }
        }
    }
}
