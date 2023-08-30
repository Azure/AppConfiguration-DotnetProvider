// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using Azure.Core;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Constants;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.DnsClient;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
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

        private IEnumerable<string> _connectionStrings;
        private IEnumerable<Uri> _endpoints;
        private TokenCredential _credential;
        private ConfigurationClientOptions _clientOptions;

        public ConfigurationClientManager(AzureAppConfigurationOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (options.ClientOptions == null)
            {
                throw new ArgumentNullException(nameof(options.ClientOptions));
            }

            _connectionStrings = options.ConnectionStrings;
            _endpoints = options.Endpoints;
            _credential = options.Credential;
            _clientOptions = options.ClientOptions;

            if (_connectionStrings != null && _connectionStrings.Any())
            {
                _clients = _connectionStrings
                    .Select(connectionString =>
                        {
                            var endpoint = new Uri(ConnectionStringUtils.Parse(connectionString, ConnectionStringUtils.EndpointSection));
                            return new ConfigurationClientWrapper(endpoint, new ConfigurationClient(connectionString, _clientOptions));
                        })
                    .ToList();
            }
            else if (_endpoints != null && _endpoints.Any())
            {
                if (_credential == null)
                {
                    throw new ArgumentNullException(nameof(options.Credential));
                }

                _clients = _endpoints
                    .Select(endpoint => new ConfigurationClientWrapper(endpoint, new ConfigurationClient(endpoint, _credential, _clientOptions)))
                    .ToList();
            }
            else
            {
                throw new ArgumentNullException(nameof(_clients));
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

        public IEnumerable<ConfigurationClient> GetAvailableClients(DateTimeOffset time)
        {
            return _clients.Where(client => client.BackoffEndTime <= time).Select(c => c.Client).ToList();
        }

        public async Task<IEnumerable<ConfigurationClient>> GetAutoFailoverClients(Logger logger, CancellationToken cancellationToken)
        {
            var isUsingConnectionString = _connectionStrings != null && _connectionStrings.Any();

            Uri endpoint = null;
            var secret = string.Empty;
            var id = string.Empty;

            if (isUsingConnectionString)
            {
                var connectionString = _connectionStrings.First();

                endpoint = new Uri(ConnectionStringUtils.Parse(connectionString, ConnectionStringUtils.EndpointSection));
                secret = ConnectionStringUtils.Parse(connectionString, ConnectionStringUtils.SecretSection);
                id = ConnectionStringUtils.Parse(connectionString, ConnectionStringUtils.IdSection);
            }
            else
            {
                endpoint = _endpoints.First();
            }

            var lookup = new SrvLookupClient(logger);

            IReadOnlyCollection<SrvRecord> results = await lookup.QueryAsync(endpoint.DnsSafeHost, cancellationToken).ConfigureAwait(false);
            
            var autoFailoverClients = new List<ConfigurationClient>();

            // shuffle the results to ensure hosts can be picked randomly.
            IEnumerable<string> srvTargetHosts = results.Select(r => $"{r.Target}").Shuffle().ToList();

            foreach (string host in srvTargetHosts)
            {
                if (!_clients.Any(c => c.Endpoint.Host.Equals(host, StringComparison.OrdinalIgnoreCase)))
                {
                    var targetEndpoint = new Uri($"https://{host}");

                    var configClient = isUsingConnectionString? 
                        new ConfigurationClient(ConnectionStringUtils.Build(targetEndpoint, id, secret), _clientOptions):
                        new ConfigurationClient(targetEndpoint, _credential, _clientOptions);

                    _clients.Add(new ConfigurationClientWrapper(targetEndpoint, configClient));

                    autoFailoverClients.Add(configClient);
                }
            }

            // clean up clients in case the corresponding replicas are removed.
            foreach (var client in _clients)
            {
                if (IsEligibleToRemove(srvTargetHosts, client))
                {
                    _clients.Remove(client);
                }
            }

            return autoFailoverClients;
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

            ConfigurationClientWrapper clientWrapper = _clients.SingleOrDefault(c => new EndpointComparer().Equals(c.Endpoint, endpoint));

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


        // Only remove the client if it is not in the user passed connection string or endpoints, as well as not in returned SRV records.
        private bool IsEligibleToRemove(IEnumerable<string> srvEndpointHosts, ConfigurationClientWrapper client)
        {
            if (_connectionStrings != null && _connectionStrings.Any(c => GetHostFromConnectionString(c).Equals(client.Endpoint.Host, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            if (_endpoints != null && _endpoints.Any(e => e.Host.Equals(client.Endpoint.Host, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            if (srvEndpointHosts.Any(h => h.Equals(client.Endpoint.Host, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            return true;
        }

        private string GetHostFromConnectionString(string connectionString)
        {
            var endpointString = ConnectionStringUtils.Parse(connectionString, ConnectionStringUtils.EndpointSection);
            var endpoint = new Uri(endpointString);

            return endpoint.Host;
        }
    }
}
