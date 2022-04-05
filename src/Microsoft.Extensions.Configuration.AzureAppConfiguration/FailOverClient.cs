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

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// A configuration client with fail-over capabilities.
    /// </summary>
    /// <remarks>
    /// This class is not thread-safe. Since config provider does not allow multiple network requests at the same time,
    /// there won't be multiple threads calling this client at the same time.
    /// </remarks>
    internal class FailOverClient
    {
        // This constant is necessary because HttpStatusCode.TooManyRequests is only available in netstandard2.1 and higher.
        private const int HttpStatusCodeRequestThrottled = 429;

        private readonly IEnumerable<ConfigurationClientWrapper> _clients;

        private readonly TimeSpan _parallelRetryInterval;

        private class ConfigurationClientWrapper
        {
            public int FailedAttempts;

            public DateTimeOffset BackoffEndTime;

            public ConfigurationClientWrapper(Uri endpoint, ConfigurationClient configurationClient)
            {
                Endpoint = endpoint;
                Client = configurationClient;
                BackoffEndTime = DateTimeOffset.UtcNow;
                FailedAttempts = 0;
            }

            public ConfigurationClient Client { get; private set; }
            public Uri Endpoint { get; private set; }
        }

        // internal constructor to allow mocking.
        internal FailOverClient() { }

        public FailOverClient(string connectionString, AzureAppConfigurationOptions options)
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentNullException(nameof(connectionString));
            }

            var endpoint = new Uri(ConnectionStringParser.Parse(connectionString, ConnectionStringParser.EndpointSection));
            _clients = new List<ConfigurationClientWrapper> { new ConfigurationClientWrapper(endpoint, new ConfigurationClient(connectionString, options.ClientOptions)) };
            _parallelRetryInterval = options.ParallelFailOverInterval;
        }

        public FailOverClient(IEnumerable<Uri> endpoints, TokenCredential credential, AzureAppConfigurationOptions options)
        {
            if (endpoints == null || endpoints.Count() < 1)
            {
                throw new ArgumentNullException(nameof(endpoints));
            }

            if (credential == null)
            {
                throw new NullReferenceException(nameof(credential));
            }

            _clients = endpoints.Select(endpoint => new ConfigurationClientWrapper(endpoint, new ConfigurationClient(endpoint, credential, options.ClientOptions))).ToList();
            _parallelRetryInterval = options.ParallelFailOverInterval;
        }

        public virtual Task<Response<ConfigurationSetting>> GetConfigurationSettingAsync(string key, string label = null, CancellationToken cancellationToken = default)
        {
            Task<Response<ConfigurationSetting>> func(ConfigurationClient client)
            {
                return client.GetConfigurationSettingAsync(key, label, cancellationToken);
            }

            return ExecuteWithFailOverPolicyAsync(func, cancellationToken);
        }

        public virtual Task<Response<ConfigurationSetting>> GetConfigurationSettingAsync(ConfigurationSetting setting, bool onlyIfChanged = false, CancellationToken cancellationToken = default)
        {
            Task<Response<ConfigurationSetting>> func(ConfigurationClient client)
            {
                return client.GetConfigurationSettingAsync(setting, onlyIfChanged, cancellationToken);
            }

            return ExecuteWithFailOverPolicyAsync(func, cancellationToken);
        }

        public virtual async Task<IEnumerable<ConfigurationSetting>> GetConfigurationSettingsAsync(SettingSelector selector, CancellationToken cancellationToken = default)
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

        public virtual void UpdateSyncToken(Uri endpoint, string syncToken)
        {
            _clients.Single(clientAndState => clientAndState.Endpoint.Host.Equals(endpoint.Host)).Client.UpdateSyncToken(syncToken);
        }

        public virtual async Task<KeyValueChange> GetKeyValueChange(ConfigurationSetting setting, CancellationToken cancellationToken)
        {
            if (setting == null)
            {
                throw new ArgumentNullException(nameof(setting));
            }

            if (string.IsNullOrEmpty(setting.Key))
            {
                throw new ArgumentNullException($"{nameof(setting)}.{nameof(setting.Key)}");
            }

            try
            {
                Response<ConfigurationSetting> response = await GetConfigurationSettingAsync(setting, onlyIfChanged: true, cancellationToken).ConfigureAwait(false);
                if (response.GetRawResponse().Status == (int)HttpStatusCode.OK)
                {
                    return new KeyValueChange
                    {
                        ChangeType = KeyValueChangeType.Modified,
                        Current = response.Value,
                        Key = setting.Key,
                        Label = setting.Label
                    };
                }
            }
            catch (RequestFailedException e) when (e.Status == (int)HttpStatusCode.NotFound && setting.ETag != default)
            {
                return new KeyValueChange
                {
                    ChangeType = KeyValueChangeType.Deleted,
                    Current = null,
                    Key = setting.Key,
                    Label = setting.Label
                };
            }

            return new KeyValueChange
            {
                ChangeType = KeyValueChangeType.None,
                Current = setting,
                Key = setting.Key,
                Label = setting.Label
            };
        }

        public virtual async Task<IEnumerable<KeyValueChange>> GetKeyValueChangeCollection(IEnumerable<ConfigurationSetting> keyValues, GetKeyValueChangeCollectionOptions options, CancellationToken cancellationToken)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (keyValues == null)
            {
                keyValues = Enumerable.Empty<ConfigurationSetting>();
            }

            if (options.KeyFilter == null)
            {
                options.KeyFilter = string.Empty;
            }

            if (keyValues.Any(k => string.IsNullOrEmpty(k.Key)))
            {
                throw new ArgumentNullException($"{nameof(keyValues)}[].{nameof(ConfigurationSetting.Key)}");
            }

            if (keyValues.Any(k => !string.Equals(k.Label.NormalizeNull(), options.Label.NormalizeNull())))
            {
                throw new ArgumentException("All key-values registered for refresh must use the same label.", $"{nameof(keyValues)}[].{nameof(ConfigurationSetting.Label)}");
            }

            if (keyValues.Any(k => k.Label != null && k.Label.Contains("*")))
            {
                throw new ArgumentException("The label filter cannot contain '*'", $"{nameof(options)}.{nameof(options.Label)}");
            }

            var hasKeyValueCollectionChanged = false;
            var selector = new SettingSelector
            {
                KeyFilter = options.KeyFilter,
                LabelFilter = string.IsNullOrEmpty(options.Label) ? LabelFilter.Null : options.Label,
                Fields = SettingFields.ETag | SettingFields.Key
            };

            // Dictionary of eTags that we write to and use for comparison
            var eTagMap = keyValues.ToDictionary(kv => kv.Key, kv => kv.ETag);

            // Fetch e-tags for prefixed key-values that can be used to detect changes
            await TracingUtils.CallWithRequestTracing(options.RequestTracingEnabled, RequestType.Watch, options.RequestTracingOptions,
                async () =>
                {
                    foreach (ConfigurationSetting setting in await GetConfigurationSettingsAsync(selector, cancellationToken).ConfigureAwait(false))
                    {
                        if (!eTagMap.TryGetValue(setting.Key, out ETag etag) || !etag.Equals(setting.ETag))
                        {
                            hasKeyValueCollectionChanged = true;
                            break;
                        }

                        eTagMap.Remove(setting.Key);
                    }
                }).ConfigureAwait(false);

            // Check for any deletions
            if (eTagMap.Any())
            {
                hasKeyValueCollectionChanged = true;
            }

            var changes = new List<KeyValueChange>();

            // If changes have been observed, refresh prefixed key-values
            if (hasKeyValueCollectionChanged)
            {
                selector = new SettingSelector
                {
                    KeyFilter = options.KeyFilter,
                    LabelFilter = string.IsNullOrEmpty(options.Label) ? LabelFilter.Null : options.Label
                };

                eTagMap = keyValues.ToDictionary(kv => kv.Key, kv => kv.ETag);
                await TracingUtils.CallWithRequestTracing(options.RequestTracingEnabled, RequestType.Watch, options.RequestTracingOptions,
                    async () =>
                    {
                        foreach (ConfigurationSetting setting in await GetConfigurationSettingsAsync(selector, cancellationToken).ConfigureAwait(false))
                        {
                            if (!eTagMap.TryGetValue(setting.Key, out ETag etag) || !etag.Equals(setting.ETag))
                            {
                                changes.Add(new KeyValueChange
                                {
                                    ChangeType = KeyValueChangeType.Modified,
                                    Key = setting.Key,
                                    Label = options.Label.NormalizeNull(),
                                    Current = setting
                                });
                            }

                            eTagMap.Remove(setting.Key);
                        }
                    }).ConfigureAwait(false);

                foreach (var kvp in eTagMap)
                {
                    changes.Add(new KeyValueChange
                    {
                        ChangeType = KeyValueChangeType.Deleted,
                        Key = kvp.Key,
                        Label = options.Label.NormalizeNull(),
                        Current = null
                    });
                }
            }

            return changes;
        }

        private async Task<T> ExecuteWithFailOverPolicyAsync<T>(Func<ConfigurationClient, Task<T>> funcToExecute, CancellationToken cancellationToken = default)
        {
            if (_clients.Count() < 2)
            {
                return await funcToExecute(_clients.Single().Client);
            }

            var tasks = new List<Task>();
            Exception lastException = null;
            IEnumerable<ConfigurationClientWrapper> clients = GetPrioritizedConfigurationClientList();
            var tasksToClients = new Dictionary<Task, ConfigurationClientWrapper>();

            using (CancellationTokenSource attemptsCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                foreach (ConfigurationClientWrapper client in clients)
                {
                    var parallelAwait = Task.Delay(_parallelRetryInterval, attemptsCancellation.Token);
                    Task<T> funcTask = funcToExecute(client.Client);
                    tasks.Add(funcTask);
                    tasks.Add(parallelAwait);
                    tasksToClients.Add(funcTask, client);

                    var completedTask = await Task.WhenAny(tasks);
                    if (!completedTask.Equals(parallelAwait))
                    {
                        tasksToClients.TryGetValue(completedTask, out ConfigurationClientWrapper completedTaskClient);

                        if (completedTask.Status == TaskStatus.RanToCompletion)
                        {
                            if (completedTaskClient != null)
                            {
                                completedTaskClient.BackoffEndTime = DateTimeOffset.UtcNow;
                                completedTaskClient.FailedAttempts = 0;
                            }

                            // Safe because the task is completed.
                            return funcTask.Result;
                        }
                        else if (completedTask.Status == TaskStatus.Faulted)
                        {
                            tasks.Remove(completedTask);
                            tasks.Remove(parallelAwait);

                            if (ShouldFailoverForException(completedTask.Exception))
                            {
                                lastException = completedTask.Exception;
                                if (completedTaskClient != null)
                                {
                                    completedTaskClient.FailedAttempts++;
                                    TimeSpan backoffInterval = FailOverConstants.MinBackoffInterval.CalculateBackoffInterval(FailOverConstants.MaxBackoffInterval, completedTaskClient.FailedAttempts);
                                    completedTaskClient.BackoffEndTime = DateTimeOffset.UtcNow.Add(backoffInterval);
                                }
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

        private bool ShouldFailoverForException(AggregateException ex)
        {
            int statusCode = 0;

            IReadOnlyCollection<Exception> innerExceptions = ex.Flatten().InnerExceptions;

            if (innerExceptions.Count > 0 && innerExceptions.All(ex => ex is RequestFailedException))
            {
                statusCode = (innerExceptions.Last() as RequestFailedException).Status;
            }

            return statusCode == HttpStatusCodeRequestThrottled || statusCode == (int)HttpStatusCode.RequestTimeout || statusCode >= (int)HttpStatusCode.InternalServerError;
        }

        private IEnumerable<ConfigurationClientWrapper> GetPrioritizedConfigurationClientList()
        {
            var clients = new List<ConfigurationClientWrapper>();

            foreach (ConfigurationClientWrapper client in _clients)
            {
                if (DateTimeOffset.UtcNow >= client.BackoffEndTime)
                {
                    clients.Add(client);
                }
            }

            if (!clients.Any())
            {
                // All configuration clients are in the 'backed-off' state, so we try all clients regardless.
                clients.AddRange(_clients);
            }

            return clients;
        }
    }
}
