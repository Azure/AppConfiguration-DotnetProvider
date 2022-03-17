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
    internal class LocalConfigurationClient : IConfigurationClient
    {
        private const int HttpStatusRequestThrottled = 429;

        private readonly IEnumerable<(ConfigurationClient, ConfigurationClientState)> _configurationClientAndStates;

        private readonly TimeSpan _parallelRetryTimeout;

        public LocalConfigurationClient(string connectionString, AzureAppConfigurationOptions options)
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentNullException(nameof(connectionString));
            }

            var endpoint = new Uri(ConnectionStringParser.Parse(connectionString, "Endpoint"));
            this._configurationClientAndStates = new List<(ConfigurationClient, ConfigurationClientState)>{ (new ConfigurationClient(connectionString, options.ClientOptions), new ConfigurationClientState(endpoint)) };
            this._parallelRetryTimeout = options.RetryTimeoutBetweenReplicas;
        }

        public LocalConfigurationClient(IEnumerable<Uri> endpoints, TokenCredential credential, AzureAppConfigurationOptions options)
        {
            if (endpoints == null || endpoints.Count() == 0)
            {
                throw new ArgumentNullException(nameof(endpoints));
            }

            if (credential == null)
            {
                throw new NullReferenceException(nameof(credential));
            }

            this._configurationClientAndStates = endpoints.Select(endpoint => (new ConfigurationClient(endpoint, credential, options.ClientOptions), new ConfigurationClientState(endpoint)));
            this._parallelRetryTimeout = options.RetryTimeoutBetweenReplicas;
        }

        public Task<Response<ConfigurationSetting>> GetConfigurationSettingAsync(string key, string label = null, CancellationToken cancellationToken = default)
        {
            Task<Response<ConfigurationSetting>> func(ConfigurationClient client)
            {
                return client.GetConfigurationSettingAsync(key, label, cancellationToken);
            }

            return ExecuteWithFailOverPolicyAsync<ConfigurationSetting>(func, forceTryFailedReplicas: false, cancellationToken);
        }

        public Task<Response<ConfigurationSetting>> GetConfigurationSettingAsync(ConfigurationSetting setting, bool onlyIfChanged = false, CancellationToken cancellationToken = default)
        {
            Task<Response<ConfigurationSetting>> func(ConfigurationClient client)
            {
                return client.GetConfigurationSettingAsync(setting, onlyIfChanged, cancellationToken);
            }

            return ExecuteWithFailOverPolicyAsync<ConfigurationSetting>(func, forceTryFailedReplicas: false, cancellationToken);
        }

        public async Task<IEnumerable<ConfigurationSetting>> GetConfigurationSettingsAsync(SettingSelector selector, CancellationToken cancellationToken = default)
        {
            Page<ConfigurationSetting> settingsPage = null;
            string continuationToken = null;
            IAsyncEnumerator<Page<ConfigurationSetting>> enumerator;
            List<ConfigurationSetting> result = new List<ConfigurationSetting>();

            IAsyncEnumerator<Page<ConfigurationSetting>> func(ConfigurationClient client)
            {
                return client.GetConfigurationSettingsAsync(selector, cancellationToken)
                             .AsPages(continuationToken: continuationToken)
                             .GetAsyncEnumerator();
            }

            do
            {
                enumerator = await ExecuteWithFailOverPolicyAsync<IAsyncEnumerator<Page<ConfigurationSetting>>>(func, forceTryFailedReplicas: false, cancellationToken);
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
            List<Task> tasks = new List<Task>();
            var attemptsCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            int replicasTried = 0;
            Exception lastException = null;

            for(int i = 0; i < _configurationClientAndStates.Count(); i++)
            {
                ConfigurationClient client = _configurationClientAndStates.ElementAt(i).Item1;
                ConfigurationClientState clientState = _configurationClientAndStates.ElementAt(i).Item2;

                if (forceTryFailedReplicas || clientState.IsAvailable())
                {
                    Task parallelAwait = Task.Delay(this._parallelRetryTimeout, attemptsCancellation.Token);
                    Task<Response<T>> funcTask = funcToExecute(client);
                    tasks.Add(funcTask);
                    tasks.Add(parallelAwait);
                    replicasTried++;

                    Task completed = await Task.WhenAny(tasks);
                    if (!completed.Equals(parallelAwait))
                    {
                        if (completed.Status == TaskStatus.RanToCompletion)
                        {
                            clientState.UpdateConfigurationStoreStatus(requestSuccessful: true);

                            // Safe because the task is completed.
                            return funcTask.Result;
                        }
                        else if (completed.Status == TaskStatus.Faulted)
                        {
                            tasks.Clear();

                            if (IsRetryable(completed.Exception))
                            {
                                lastException = completed.Exception;
                                clientState.UpdateConfigurationStoreStatus(requestSuccessful: false);
                            }
                            else
                            {
                                throw completed.Exception;
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

        private async Task<T> ExecuteWithFailOverPolicyAsync<T>(Func<ConfigurationClient, T> funcToExecute, bool forceTryFailedReplicas = false, CancellationToken cancellationToken = default)
        {
            List<Task> tasks = new List<Task>();
            var attemptsCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            int replicasTried = 0;
            Exception lastException = null;

            for (int i = 0; i < _configurationClientAndStates.Count(); i++)
            {
                ConfigurationClient client = _configurationClientAndStates.ElementAt(i).Item1;
                ConfigurationClientState clientState = _configurationClientAndStates.ElementAt(i).Item2;

                if (forceTryFailedReplicas || clientState.IsAvailable())
                {
                    Task parallelAwait = Task.Delay(this._parallelRetryTimeout, attemptsCancellation.Token);
                    Task<T> funcTask = Task.Run(() => funcToExecute(client), cancellationToken);
                    tasks.Add(funcTask);
                    tasks.Add(parallelAwait);
                    replicasTried++;

                    Task completed = await Task.WhenAny(tasks);
                    if (!completed.Equals(parallelAwait))
                    {
                        if (completed.Status == TaskStatus.RanToCompletion)
                        {
                            clientState.UpdateConfigurationStoreStatus(requestSuccessful: true);

                            // Safe because the task is completed.
                            return funcTask.Result;
                        }
                        else if (completed.Status == TaskStatus.Faulted)
                        {
                            tasks.Clear();

                            if (IsRetryable(completed.Exception))
                            {
                                lastException = completed.Exception;
                                clientState.UpdateConfigurationStoreStatus(requestSuccessful: false);
                            }
                            else
                            {
                                throw completed.Exception;
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

        private bool IsRetryable(Exception ex)
        {
            int statusCode = 0;

            if (ex is RequestFailedException e)
            {
                statusCode = e.Status;
            }
            else if (ex is AggregateException aggregateException && aggregateException.InnerExceptions?.All(ex => ex is RequestFailedException) == true)
            {
                if (aggregateException.InnerExceptions.LastOrDefault() is RequestFailedException lastException)
                {
                    statusCode = lastException.Status;
                }
            }

            switch (statusCode)
            {
                case (int)HttpStatusCode.RequestTimeout:       // 408
                case HttpStatusRequestThrottled:               // 429
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
