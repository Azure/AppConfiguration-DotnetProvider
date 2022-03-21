// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using Azure;
using Azure.Core;
using Azure.Data.AppConfiguration;
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

        private readonly IEnumerable<(ConfigurationClient, ConfigurationClientState)> _configurationClientAndStates;

        private readonly TimeSpan _parallelRetryInterval;

        public FailOverSupportedConfigurationClient(string connectionString, AzureAppConfigurationOptions options)
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentNullException(nameof(connectionString));
            }

            var endpoint = new Uri(ConnectionStringParser.Parse(connectionString, ConnectionStringParser.EndpointSection));
            this._configurationClientAndStates = new List<(ConfigurationClient, ConfigurationClientState)>{ (new ConfigurationClient(connectionString, options.ClientOptions), new ConfigurationClientState(endpoint)) };
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

            this._configurationClientAndStates = endpoints.Select(endpoint => (new ConfigurationClient(endpoint, credential, options.ClientOptions), new ConfigurationClientState(endpoint)));
            this._parallelRetryInterval = options.ParallelRetryInterval;
        }

        public Task<Response<ConfigurationSetting>> GetConfigurationSettingAsync(string key, string label = null, CancellationToken cancellationToken = default)
        {
            Task<Response<ConfigurationSetting>> func(ConfigurationClient client)
            {
                return client.GetConfigurationSettingAsync(key, label, cancellationToken);
            }

            return ExecuteWithFailOverPolicyAsync(func, forceTryFailedReplicas: false, cancellationToken);
        }

        public Task<Response<ConfigurationSetting>> GetConfigurationSettingAsync(ConfigurationSetting setting, bool onlyIfChanged = false, CancellationToken cancellationToken = default)
        {
            Task<Response<ConfigurationSetting>> func(ConfigurationClient client)
            {
                return client.GetConfigurationSettingAsync(setting, onlyIfChanged, cancellationToken);
            }

            return ExecuteWithFailOverPolicyAsync(func, forceTryFailedReplicas: false, cancellationToken);
        }

        public async Task<IEnumerable<ConfigurationSetting>> GetConfigurationSettingsAsync(SettingSelector selector, CancellationToken cancellationToken = default)
        {
            Page<ConfigurationSetting> settingsPage = null;
            string continuationToken = null;
            IAsyncEnumerator<Page<ConfigurationSetting>> enumerator;
            var result = new List<ConfigurationSetting>();

            IAsyncEnumerator<Page<ConfigurationSetting>> func(ConfigurationClient client)
            {
                return client.GetConfigurationSettingsAsync(selector, cancellationToken)
                             .AsPages(continuationToken: continuationToken)
                             .GetAsyncEnumerator();
            }

            do
            {
                enumerator = await ExecuteWithFailOverPolicyAsync(func, forceTryFailedReplicas: false, cancellationToken);
                settingsPage = enumerator != null && await enumerator.MoveNextAsync() ? enumerator.Current : null;

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
            this._configurationClientAndStates.Single(clientAndState => clientAndState.Item2.Endpoint.Host.Equals(endpoint.Host)).Item1.UpdateSyncToken(syncToken);
        }

        private async Task<Response<T>> ExecuteWithFailOverPolicyAsync<T>(Func<ConfigurationClient, Task<Response<T>>> funcToExecute, bool forceTryFailedReplicas = false, CancellationToken cancellationToken = default)
        {
            var tasks = new List<Task>();
            var attemptsCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            int replicasTried = 0;
            Exception lastException = null;

            for(int i = 0; i < _configurationClientAndStates.Count(); i++)
            {
                ConfigurationClient client = _configurationClientAndStates.ElementAt(i).Item1;
                ConfigurationClientState clientState = _configurationClientAndStates.ElementAt(i).Item2;

                if (forceTryFailedReplicas || clientState.IsAvailable())
                {
                    var parallelAwait = Task.Delay(this._parallelRetryInterval, attemptsCancellation.Token);
                    Task<Response<T>> funcTask = funcToExecute(client);
                    tasks.Add(funcTask);
                    tasks.Add(parallelAwait);
                    replicasTried++;

                    var completedTask = await Task.WhenAny(tasks);
                    if (!completedTask.Equals(parallelAwait))
                    {
                        if (completedTask.Status == TaskStatus.RanToCompletion)
                        {
                            clientState.UpdateConfigurationStoreStatus(requestSuccessful: true);

                            // Safe because the task is completed.
                            return funcTask.Result;
                        }
                        else if (completedTask.Status == TaskStatus.Faulted)
                        {
                            tasks.Clear();

                            if (IsRetryableException(completedTask.Exception))
                            {
                                lastException = completedTask.Exception;
                                clientState.UpdateConfigurationStoreStatus(requestSuccessful: false);
                            }
                            else
                            {
                                throw completedTask.Exception;
                            }
                        }
                    }
                    else
                    {
                        tasks.Remove(parallelAwait);
                    }
                }
            }

            if (replicasTried == 0)
            {
                return await ExecuteWithFailOverPolicyAsync(funcToExecute, forceTryFailedReplicas: true, cancellationToken);
            }

            throw lastException;
        }

        private async Task<T> ExecuteWithFailOverPolicyAsync<T>(Func<ConfigurationClient, T> funcToExecute, bool forceTryFailedReplicas = false, CancellationToken cancellationToken = default)
        {
            var tasks = new List<Task>();
            var attemptsCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            int replicasTried = 0;
            Exception lastException = null;

            for (int i = 0; i < _configurationClientAndStates.Count(); i++)
            {
                ConfigurationClient client = _configurationClientAndStates.ElementAt(i).Item1;
                ConfigurationClientState clientState = _configurationClientAndStates.ElementAt(i).Item2;

                if (forceTryFailedReplicas || clientState.IsAvailable())
                {
                    var parallelAwait = Task.Delay(this._parallelRetryInterval, attemptsCancellation.Token);
                    Task<T> funcTask = Task.Run(() => funcToExecute(client), cancellationToken);
                    tasks.Add(funcTask);
                    tasks.Add(parallelAwait);
                    replicasTried++;

                    var completedTask = await Task.WhenAny(tasks);
                    if (!completedTask.Equals(parallelAwait))
                    {
                        if (completedTask.Status == TaskStatus.RanToCompletion)
                        {
                            clientState.UpdateConfigurationStoreStatus(requestSuccessful: true);

                            // Safe because the task is completed.
                            return funcTask.Result;
                        }
                        else if (completedTask.Status == TaskStatus.Faulted)
                        {
                            tasks.Clear();

                            if (IsRetryableException(completedTask.Exception))
                            {
                                lastException = completedTask.Exception;
                                clientState.UpdateConfigurationStoreStatus(requestSuccessful: false);
                            }
                            else
                            {
                                throw completedTask.Exception;
                            }
                        }
                    }
                    else
                    {
                        tasks.Remove(parallelAwait);
                    }
                }
            }

            if (replicasTried == 0)
            {
                return await ExecuteWithFailOverPolicyAsync<T>(funcToExecute, forceTryFailedReplicas: true, cancellationToken);
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
    }
}
