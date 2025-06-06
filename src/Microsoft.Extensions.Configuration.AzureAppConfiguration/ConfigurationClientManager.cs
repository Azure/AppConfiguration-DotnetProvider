﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using Azure.Data.AppConfiguration;
using DnsClient;
using DnsClient.Protocol;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    internal class ConfigurationClientManager : IConfigurationClientManager, IDisposable
    {
        private readonly IAzureClientFactory<ConfigurationClient> _clientFactory;
        private readonly IList<ConfigurationClientWrapper> _clients;

        private readonly Uri _endpoint;

        private readonly bool _replicaDiscoveryEnabled;
        private readonly SrvLookupClient _srvLookupClient;
        private readonly string _validDomain;

        private IList<ConfigurationClientWrapper> _dynamicClients;
        private DateTimeOffset _lastFallbackClientRefresh = default;
        private DateTimeOffset _lastFallbackClientRefreshAttempt = default;
        private Logger _logger = new Logger();
        private bool _isDisposed = false;

        private static readonly TimeSpan FallbackClientRefreshExpireInterval = TimeSpan.FromHours(1);
        private static readonly TimeSpan MinimalClientRefreshInterval = TimeSpan.FromSeconds(30);
        private static readonly string[] TrustedDomainLabels = new[] { "azconfig", "appconfig" };

        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        // Only used for unit testing
        internal int RefreshClientsCalled { get; set; } = 0;

        public ConfigurationClientManager(
            IAzureClientFactory<ConfigurationClient> clientFactory,
            IEnumerable<Uri> endpoints,
            bool replicaDiscoveryEnabled,
            bool loadBalancingEnabled)
        {
            _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));

            if (endpoints == null || !endpoints.Any())
            {
                throw new ArgumentNullException(nameof(endpoints));
            }

            _endpoint = endpoints.First();

            _replicaDiscoveryEnabled = replicaDiscoveryEnabled;

            // If load balancing is enabled, shuffle the passed in endpoints to randomize the endpoint used on startup
            if (loadBalancingEnabled)
            {
                endpoints = endpoints.ToList().Shuffle();
            }

            _validDomain = GetValidDomain(_endpoint);
            _srvLookupClient = new SrvLookupClient();

            _clients = endpoints
                .Select(endpoint => new ConfigurationClientWrapper(endpoint, clientFactory.CreateClient(endpoint.AbsoluteUri)))
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

        public IEnumerable<ConfigurationClient> GetClients()
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;

            if (_replicaDiscoveryEnabled &&
                now >= _lastFallbackClientRefreshAttempt + MinimalClientRefreshInterval &&
                (_dynamicClients == null ||
                    now >= _lastFallbackClientRefresh + FallbackClientRefreshExpireInterval))
            {
                _lastFallbackClientRefreshAttempt = now;

                _ = DiscoverFallbackClients();
            }

            // Treat the passed in endpoints as the highest priority clients
            IEnumerable<ConfigurationClient> clients = _clients.Select(c => c.Client);

            if (_dynamicClients != null && _dynamicClients.Any())
            {
                clients = clients.Concat(_dynamicClients.Select(c => c.Client));
            }

            return clients;
        }

        public void RefreshClients()
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;

            if (_replicaDiscoveryEnabled &&
                now >= _lastFallbackClientRefreshAttempt + MinimalClientRefreshInterval)
            {
                _lastFallbackClientRefreshAttempt = now;

                _ = DiscoverFallbackClients();
            }

            RefreshClientsCalled++;
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

        private async Task DiscoverFallbackClients()
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token);

            cts.CancelAfter(MinimalClientRefreshInterval);

            try
            {
                await RefreshFallbackClients(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException e) when (!_cancellationTokenSource.IsCancellationRequested)
            {
                _logger.LogWarning(LogHelper.BuildFallbackClientLookupFailMessage(e.Message));
            }
        }

        private async Task RefreshFallbackClients(CancellationToken cancellationToken)
        {
            IEnumerable<SrvRecord> srvTargetHosts = Enumerable.Empty<SrvRecord>();

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

            var newDynamicClients = new List<ConfigurationClientWrapper>();

            // Honor with the DNS based service discovery protocol, but shuffle the results first to ensure hosts can be picked randomly,
            // Srv lookup does retrieve trailing dot in the host name, just trim it.
            IEnumerable<string> OrderedHosts = srvTargetHosts.Any()
                ? srvTargetHosts.ToList().Shuffle().SortSrvRecords().Select(r => $"{r.Target.Value.TrimEnd('.')}")
                : Enumerable.Empty<string>();

            foreach (string host in OrderedHosts)
            {
                if (!string.IsNullOrEmpty(host) &&
                    !_clients.Any(c => c.Endpoint.Host.Equals(host, StringComparison.OrdinalIgnoreCase)) &&
                    IsValidEndpoint(host))
                {
                    var targetEndpoint = new Uri($"https://{host}");

                    ConfigurationClient configClient = _clientFactory.CreateClient(targetEndpoint.AbsoluteUri);

                    newDynamicClients.Add(new ConfigurationClientWrapper(targetEndpoint, configClient));
                }
            }

            _dynamicClients = newDynamicClients;

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

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _cancellationTokenSource.Cancel();

                _cancellationTokenSource.Dispose();

                _isDisposed = true;
            }
        }
    }
}
