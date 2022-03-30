// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using Azure;
using Azure.Core;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Constants;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.ConfigurationClients
{
    internal class FailOverSupportedConfigurationClient : IConfigurationClient
    {
        private const int HttpStatusCodeRequestThrottled = 429;

        private readonly IEnumerable<ConfigurationClientState> _clients;

        private readonly TimeSpan _parallelRetryInterval;

        private class ConfigurationClientState
        {
            public int FailedAttempts;

            public ConfigurationClientState(Uri endpoint, ConfigurationClient configurationClient)
            {
                Endpoint = endpoint;
                Client = configurationClient;
                BackoffEndTime = DateTimeOffset.UtcNow;
                FailedAttempts = 0;
            }

            public ConfigurationClient Client { get; private set; }
            public Uri Endpoint { get; private set; }
            public DateTimeOffset BackoffEndTime { get; set; }
        }

        public FailOverSupportedConfigurationClient(string connectionString, AzureAppConfigurationOptions options)
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentNullException(nameof(connectionString));
            }

            var endpoint = new Uri(ConnectionStringParser.Parse(connectionString, ConnectionStringParser.EndpointSection));
            this._clients = new List<ConfigurationClientState>{ new ConfigurationClientState(endpoint, new ConfigurationClient(connectionString, options.ClientOptions)) };
            this._parallelRetryInterval = options.ParallelRetryInterval;
        }

        public FailOverSupportedConfigurationClient(IEnumerable<Uri> endpoints, TokenCredential credential, AzureAppConfigurationOptions options)
        {
            if (endpoints == null || endpoints.Count() < 1)
            {
                throw new ArgumentNullException(nameof(endpoints));
            }

            if (credential == null)
            {
                throw new NullReferenceException(nameof(credential));
            }

            this._clients = endpoints.Select(endpoint => new ConfigurationClientState(endpoint, new ConfigurationClient(endpoint, credential, options.ClientOptions))).ToList();
            this._parallelRetryInterval = options.ParallelRetryInterval;
        }

        public Task<Response<ConfigurationSetting>> GetConfigurationSettingAsync(string key, string label = null, CancellationToken cancellationToken = default)
        {
            Task<Response<ConfigurationSetting>> func(ConfigurationClient client)
            {
                return client.GetConfigurationSettingAsync(key, label, cancellationToken);
            }

            return ExecuteWithFailOverPolicyAsync(func, cancellationToken);
        }

        public Task<Response<ConfigurationSetting>> GetConfigurationSettingAsync(ConfigurationSetting setting, bool onlyIfChanged = false, CancellationToken cancellationToken = default)
        {
            Task<Response<ConfigurationSetting>> func(ConfigurationClient client)
            {
                return client.GetConfigurationSettingAsync(setting, onlyIfChanged, cancellationToken);
            }

            return ExecuteWithFailOverPolicyAsync(func, cancellationToken);
        }

        public async Task<IEnumerable<ConfigurationSetting>> GetConfigurationSettingsAsync(SettingSelector selector, CancellationToken cancellationToken = default)
        {
            Page<ConfigurationSetting> settingsPage = null;
            string continuationToken = null;
            var result = new List<ConfigurationSetting>();

            async Task<Page<ConfigurationSetting>> func(ConfigurationClient client)
            {
                IAsyncEnumerator<Page<ConfigurationSetting>> enumerator = client.GetConfigurationSettingsAsync(selector, cancellationToken)
                                                                                .AsPages(continuationToken: continuationToken)
                                                                                .GetAsyncEnumerator();

                Page<ConfigurationSetting> page = enumerator != null && await enumerator.MoveNextAsync() ? enumerator.Current : null;
                return page;
            }

            do
            {
                settingsPage = await ExecuteWithFailOverPolicyAsync(func, cancellationToken);
                if (settingsPage != null)
                {
                    result.AddRange(settingsPage.Values);
                    continuationToken = settingsPage.ContinuationToken;
                }
            }
            while (settingsPage != null && settingsPage.ContinuationToken != null);

            return result;
        }

        public void UpdateSyncToken(Uri endpoint, string syncToken)
        {
            this._clients.Single(clientAndState => clientAndState.Endpoint.Host.Equals(endpoint.Host)).Client.UpdateSyncToken(syncToken);
        }

        private async Task<T> ExecuteWithFailOverPolicyAsync<T>(Func<ConfigurationClient, Task<T>> funcToExecute, CancellationToken cancellationToken = default)
        {
            var tasks = new List<Task>();
            Exception lastException = null;
            IEnumerable<ConfigurationClientState> clients = GetPrioritizedConfigurationClientList();

            using (var attemptsCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                foreach (ConfigurationClientState client in clients)
                {
                    var parallelAwait = Task.Delay(this._parallelRetryInterval, attemptsCancellation.Token);
                    Task<T> funcTask = funcToExecute(client.Client);
                    tasks.Add(funcTask);
                    tasks.Add(parallelAwait);

                    var completedTask = await Task.WhenAny(tasks);
                    if (!completedTask.Equals(parallelAwait))
                    {
                        if (completedTask.Status == TaskStatus.RanToCompletion)
                        {
                            client.BackoffEndTime = DateTimeOffset.UtcNow;
                            client.FailedAttempts = 0;

                            // Safe because the task is completed.
                            return funcTask.Result;
                        }
                        else if (completedTask.Status == TaskStatus.Faulted)
                        {
                            tasks.Remove(completedTask);
                            tasks.Remove(parallelAwait);

                            if (IsRetryableException(completedTask.Exception))
                            {
                                lastException = completedTask.Exception;
                                Interlocked.Increment(ref client.FailedAttempts);
                                TimeSpan backoffInterval = BackoffIntervalConstants.MinBackoffInterval.CalculateBackoffInterval(BackoffIntervalConstants.MaxBackoffInterval, client.FailedAttempts);
                                client.BackoffEndTime = DateTimeOffset.UtcNow.Add(backoffInterval);
                            }
                            else
                            {
                                completedTask.Exception.Handle(e =>
                                {
                                    // propagate the original exception.
                                    throw e;
                                });
                            }
                        }
                        else if (completedTask.Status == TaskStatus.Canceled)
                        {
                            // Throw expected OperationCanceledException
                            completedTask.GetAwaiter().GetResult();
                        }
                    }
                    else
                    {
                        tasks.Remove(parallelAwait);
                    }
                }
            }

            throw lastException;
        }

        private bool IsRetryableException(AggregateException ex)
        {
            int statusCode = 0;

            IReadOnlyCollection<Exception> innerExceptions = ex.Flatten().InnerExceptions;

            if (innerExceptions.Count > 0 && innerExceptions.All(ex => ex is RequestFailedException))
            {
                statusCode = (innerExceptions.Last() as RequestFailedException).Status;
            }

            switch (statusCode)
            {
                case (int)HttpStatusCode.RequestTimeout:       // 408
                case HttpStatusCodeRequestThrottled:           // 429
                case (int)HttpStatusCode.InternalServerError:  // 500
                case (int)HttpStatusCode.BadGateway:           // 502
                case (int)HttpStatusCode.ServiceUnavailable:   // 503
                case (int)HttpStatusCode.GatewayTimeout:       // 504
                    return true;
                default:
                    return false;
            }
        }

        private IEnumerable<ConfigurationClientState> GetPrioritizedConfigurationClientList()
        {
            var startIndex = -1;
            var clients = new List<ConfigurationClientState>();
            var i = 0;

            foreach (ConfigurationClientState client in _clients)
            {
                if (DateTimeOffset.UtcNow >= client.BackoffEndTime)
                {
                    clients.Add(client);
                    if (startIndex == -1)
                    {
                        startIndex = i;
                    }
                }
                ++i;
            }

            // All configuration clients are in the failed state, so we try all clients regardless.
            if (startIndex == -1)
            {
                clients.AddRange(_clients);
            }
            // We have put the available configuration clients in the list first, and populating the rest of the clients even though they might be in the failed state.
            else if (clients.Count() != _clients.Count())
            {
                i = 0;
                foreach (ConfigurationClientState client in _clients)
                {
                    clients.Add(client);

                    if (++i == startIndex)
                    {
                        break;
                    }
                }
            }

            return clients;
        }
    }
}
