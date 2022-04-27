// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class AzureAppConfigurationProvider : ConfigurationProvider, IConfigurationRefresher
    {
        private bool _optional;
        private bool _isInitialLoadComplete = false;
        private readonly bool _requestTracingEnabled;
        private readonly IConfigurationClientProvider _configClientProvider;
        private AzureAppConfigurationOptions _options;
        private Dictionary<string, ConfigurationSetting> _applicationSettings;
        private Dictionary<KeyValueIdentifier, ConfigurationSetting> _watchedSettings = new Dictionary<KeyValueIdentifier, ConfigurationSetting>();
        private RequestTracingOptions _requestTracingOptions;

        private readonly TimeSpan MinCacheExpirationInterval;

        // This constant is necessary because HttpStatusCode.TooManyRequests is only available in netstandard2.1 and higher.
        private const int HttpStatusCodeRequestThrottled = 429;

        // The most-recent time when the refresh operation attempted to load the initial configuration
        private DateTimeOffset InitializationCacheExpires = default;

        private static readonly TimeSpan MinDelayForUnhandledFailure = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan DefaultMaxSetDirtyDelay = TimeSpan.FromSeconds(30);

        // To avoid concurrent network operations, this flag is used to achieve synchronization between multiple threads.
        private int _networkOperationsInProgress = 0;
        private ILogger _logger;
        private ILoggerFactory _loggerFactory;

        public Uri AppConfigurationEndpoint
        {
            get
            {
                if (_options.Endpoints != null)
                {
                    return _options.Endpoints.First();
                }

                if (_options.ConnectionString != null)
                {
                    // Use try-catch block to avoid throwing exceptions from property getter.
                    // https://docs.microsoft.com/en-us/dotnet/standard/design-guidelines/property

                    try
                    {
                        return new Uri(ConnectionStringParser.Parse(_options.ConnectionString, ConnectionStringParser.EndpointSection));
                    }
                    catch (FormatException) { }
                }

                return null;
            }
        }

        public ILoggerFactory LoggerFactory
        {
            get
            {
                return _loggerFactory;
            }
            set
            {
                _loggerFactory = value;
                _logger = _loggerFactory?.CreateLogger(LoggingConstants.AppConfigRefreshLogCategory);
            }
        }

        public AzureAppConfigurationProvider(IConfigurationClientProvider clientProvider, AzureAppConfigurationOptions options, bool optional)
        {
            _configClientProvider = clientProvider ?? throw new ArgumentNullException(nameof(clientProvider));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _optional = optional;

            IEnumerable<KeyValueWatcher> watchers = options.ChangeWatchers.Union(options.MultiKeyWatchers);

            if (watchers.Any())
            {
                MinCacheExpirationInterval = watchers.Min(w => w.CacheExpirationInterval);
            }
            else
            {
                MinCacheExpirationInterval = RefreshConstants.DefaultCacheExpirationInterval;
            }

            // Enable request tracing if not opt-out
            string requestTracingDisabled = null;
            try
            {
                requestTracingDisabled = Environment.GetEnvironmentVariable(RequestTracingConstants.RequestTracingDisabledEnvironmentVariable);
            }
            catch (SecurityException) { }
            _requestTracingEnabled = bool.TryParse(requestTracingDisabled, out bool tracingDisabled) ? !tracingDisabled : true;

            if (_requestTracingEnabled)
            {
                SetRequestTracingOptions();
            }
        }

        /// <summary>
        /// Loads (or reloads) the data for this provider.
        /// </summary>
        public override void Load()
        {
            var watch = Stopwatch.StartNew();

            try
            {
                // Load() is invoked only once during application startup. We don't need to check for concurrent network
                // operations here because there can't be any other startup or refresh operation in progress at this time.
                Task funcToExecute(ConfigurationClient client) => LoadAll(client, _optional, CancellationToken.None);
                ExecuteWithFailOverPolicyAsync(funcToExecute, CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch (ArgumentException)
            {
                // Instantly re-throw the exception
                throw;
            }
            catch
            {
                // AzureAppConfigurationProvider.Load() method is called in the application's startup code path.
                // Unhandled exceptions cause application crash which can result in crash loops as orchestrators attempt to restart the application.
                // Knowing the intended usage of the provider in startup code path, we mitigate back-to-back crash loops from overloading the server with requests by waiting a minimum time to propogate fatal errors.

                var waitInterval = MinDelayForUnhandledFailure.Subtract(watch.Elapsed);

                if (waitInterval.Ticks > 0)
                {
                    Task.Delay(waitInterval).ConfigureAwait(false).GetAwaiter().GetResult();
                }

                // Re-throw the exception after the additional delay (if required)
                throw;
            }
            finally
            {
                // Set the provider for AzureAppConfigurationRefresher instance after LoadAll has completed.
                // This stops applications from calling RefreshAsync until config has been initialized during startup.
                var refresher = (AzureAppConfigurationRefresher)_options.GetRefresher();
                refresher.SetProvider(this);
            }

            // Mark all settings have loaded at startup.
            _isInitialLoadComplete = true;
        }

        public async Task RefreshAsync(CancellationToken cancellationToken)
        {
            // Ensure that concurrent threads do not simultaneously execute refresh operation. 
            if (Interlocked.Exchange(ref _networkOperationsInProgress, 1) == 0)
            {
                try
                {
                    async Task funcToExecute(ConfigurationClient client) {

                        // Check if initial configuration load had failed
                        if (_applicationSettings == null)
                        {
                            if (InitializationCacheExpires < DateTimeOffset.UtcNow)
                            {
                                InitializationCacheExpires = DateTimeOffset.UtcNow.Add(MinCacheExpirationInterval);
                                await LoadAll(client, ignoreFailures: false, cancellationToken).ConfigureAwait(false);
                            }

                            return;
                        }

                        await RefreshIndividualKeyValues(client, cancellationToken).ConfigureAwait(false);
                        await RefreshKeyValueCollections(client, cancellationToken).ConfigureAwait(false);
                        await RefreshKeyValueAdapters(cancellationToken).ConfigureAwait(false);
                    };

                    await ExecuteWithFailOverPolicyAsync(funcToExecute, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Exchange(ref _networkOperationsInProgress, 0);
                }
            }
        }

        public async Task<bool> TryRefreshAsync(CancellationToken cancellationToken)
        {
            try
            {
                await RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (RequestFailedException e)
            {
                if (IsAuthenticationError(e))
                {
                    _logger?.LogWarning(e, LoggingConstants.RefreshFailedDueToAuthenticationError);
                }
                else
                {
                    _logger?.LogWarning(e, LoggingConstants.RefreshFailedError);
                }

                return false;
            }
            catch (AggregateException e) when (e?.InnerExceptions?.All(e => e is RequestFailedException) ?? false)
            {
                if (IsAuthenticationError(e))
                {
                    _logger?.LogWarning(e, LoggingConstants.RefreshFailedDueToAuthenticationError);
                }
                else
                {
                    _logger?.LogWarning(e, LoggingConstants.RefreshFailedError);
                }

                return false;
            }
            catch (KeyVaultReferenceException e)
            {
                _logger?.LogWarning(e, LoggingConstants.RefreshFailedDueToKeyVaultError);
                return false;
            }
            catch (OperationCanceledException)
            {
                _logger?.LogWarning(LoggingConstants.RefreshCanceledError);
                return false;
            }

            return true;
        }

        public void SetDirty(TimeSpan? maxDelay)
        {
            DateTimeOffset cacheExpires = AddRandomDelay(DateTimeOffset.UtcNow, maxDelay ?? DefaultMaxSetDirtyDelay);

            foreach (KeyValueWatcher changeWatcher in _options.ChangeWatchers)
            {
                changeWatcher.CacheExpires = cacheExpires;
            }

            foreach (KeyValueWatcher changeWatcher in _options.MultiKeyWatchers)
            {
                changeWatcher.CacheExpires = cacheExpires;
            }
        }

        public void ProcessPushNotification(PushNotification pushNotification, TimeSpan? maxDelay)
        {
            if (pushNotification == null)
            {
                throw new ArgumentNullException(nameof(pushNotification));
            }

            if (string.IsNullOrEmpty(pushNotification.SyncToken))
            {
                throw new ArgumentException(
                    "Sync token is required.",
                    $"{nameof(pushNotification)}.{nameof(pushNotification.SyncToken)}");
            }

            if (string.IsNullOrEmpty(pushNotification.EventType))
            {
                throw new ArgumentException(
                    "Event type is required.",
                    $"{nameof(pushNotification)}.{nameof(pushNotification.EventType)}");
            }

            if (pushNotification.ResourceUri == null)
            {
                throw new ArgumentException(
                    "Resource URI is required.",
                    $"{nameof(pushNotification)}.{nameof(pushNotification.ResourceUri)}");
            }

            if (string.IsNullOrWhiteSpace(pushNotification.Key))
            {
                throw new ArgumentException(
                    "Key is required.",
                    $"{nameof(pushNotification)}.{nameof(pushNotification.Key)}");
            }

            if (string.IsNullOrWhiteSpace(pushNotification.ETag))
            {
                throw new ArgumentException(
                    "ETag is required.",
                    $"{nameof(pushNotification)}.{nameof(pushNotification.ETag)}");
            }

            KeyValueWatcher pushNotificationKeyValue = new KeyValueWatcher
            {
                Key = pushNotification.Key,
                Label = pushNotification.Label.NormalizeNull()
            };

            if ((_configClientProvider.UpdateSyncToken(pushNotification.ResourceUri, pushNotification.SyncToken)) &&
                (_options.ChangeWatchers.Contains(pushNotificationKeyValue) ||
                 _options.MultiKeyWatchers.Contains(pushNotificationKeyValue)))
            {
                SetDirty(maxDelay);
            }
        }

        private async Task LoadAll(ConfigurationClient client, bool ignoreFailures, CancellationToken cancellationToken)
        {
            IDictionary<string, ConfigurationSetting> data = null;
            bool success = false;

            try
            {
                var serverData = new Dictionary<string, ConfigurationSetting>(StringComparer.OrdinalIgnoreCase);

                // Use default query if there are no key-values specified for use other than the feature flags
                bool useDefaultQuery = !_options.KeyValueSelectors.Any(selector => !selector.KeyFilter.StartsWith(FeatureManagementConstants.FeatureFlagMarker));

                if (useDefaultQuery)
                {
                    // Load all key-values with the null label.
                    var selector = new SettingSelector
                    {
                        KeyFilter = KeyFilter.Any,
                        LabelFilter = LabelFilter.Null
                    };

                    await CallWithRequestTracing(async () =>
                    {
                        await foreach (ConfigurationSetting setting in client.GetConfigurationSettingsAsync(selector, cancellationToken).ConfigureAwait(false))
                        {
                            serverData[setting.Key] = setting;
                        }
                    }).ConfigureAwait(false);
                }

                foreach (var loadOption in _options.KeyValueSelectors)
                {
                    if ((useDefaultQuery && LabelFilter.Null.Equals(loadOption.LabelFilter)) ||
                        _options.KeyValueSelectors.Any(s => s != loadOption &&
                           string.Equals(s.KeyFilter, KeyFilter.Any) &&
                           string.Equals(s.LabelFilter, loadOption.LabelFilter)))
                    {
                        // This selection was already encapsulated by a wildcard query
                        // Or would select kvs obtained by a different selector
                        // We skip it to prevent unnecessary requests
                        continue;
                    }

                    var selector = new SettingSelector
                    {
                        KeyFilter = loadOption.KeyFilter,
                        LabelFilter = loadOption.LabelFilter
                    };

                    await CallWithRequestTracing(async () =>
                    {
                        await foreach (ConfigurationSetting setting in client.GetConfigurationSettingsAsync(selector, cancellationToken).ConfigureAwait(false))
                        {
                            serverData[setting.Key] = setting;
                        }
                    }).ConfigureAwait(false);
                }

                // Block current thread for the initial load of key-values registered for refresh that are not already loaded
                await Task.Run(() => LoadKeyValuesRegisteredForRefresh(client, serverData, cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult()).ConfigureAwait(false);
                data = serverData;
                success = true;
            }
            catch (Exception exception) when (ignoreFailures &&
                                             (exception is RequestFailedException ||
                                             ((exception as AggregateException)?.InnerExceptions?.All(e => e is RequestFailedException) ?? false) ||
                                             exception is OperationCanceledException))
            { }
            finally
            {
                // Update the cache expiration time for all refresh registered settings and feature flags
                foreach (KeyValueWatcher changeWatcher in _options.ChangeWatchers.Concat(_options.MultiKeyWatchers))
                {
                    UpdateCacheExpirationTime(changeWatcher, success);
                }
            }

            if (data != null)
            {
                // Invalidate all the cached KeyVault secrets
                foreach (IKeyValueAdapter adapter in _options.Adapters)
                {
                    adapter.InvalidateCache();
                }

                await SetData(data, ignoreFailures, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task LoadKeyValuesRegisteredForRefresh(ConfigurationClient client, IDictionary<string, ConfigurationSetting> data, CancellationToken cancellationToken)
        {
            _watchedSettings.Clear();

            foreach (KeyValueWatcher changeWatcher in _options.ChangeWatchers)
            {
                string watchedKey = changeWatcher.Key;
                string watchedLabel = changeWatcher.Label;
                KeyValueIdentifier watchedKeyLabel = new KeyValueIdentifier(watchedKey, watchedLabel);

                // Skip the loading for the key-value in case it has already been loaded
                if (data.TryGetValue(watchedKey, out ConfigurationSetting loadedKv)
                    && watchedKeyLabel.Equals(new KeyValueIdentifier(loadedKv.Key, loadedKv.Label)))
                {
                    _watchedSettings[watchedKeyLabel] = loadedKv;
                    continue;
                }

                // Send a request to retrieve key-value since it may be either not loaded or loaded with a different label or different casing
                ConfigurationSetting watchedKv = null;
                try
                {
                    await CallWithRequestTracing(async () => watchedKv = await client.GetConfigurationSettingAsync(watchedKey, watchedLabel, cancellationToken)).ConfigureAwait(false);
                }
                catch (RequestFailedException e) when (e.Status == (int)HttpStatusCode.NotFound)
                {
                    watchedKv = null;
                }

                // If the key-value was found, store it for updating the settings
                if (watchedKv != null)
                {
                    data[watchedKey] = watchedKv;
                    _watchedSettings[watchedKeyLabel] = watchedKv;
                }
            }
        }

        private async Task RefreshIndividualKeyValues(ConfigurationClient client, CancellationToken cancellationToken)
        {
            bool shouldRefreshAll = false;

            foreach (KeyValueWatcher changeWatcher in _options.ChangeWatchers)
            {
                string watchedKey = changeWatcher.Key;
                string watchedLabel = changeWatcher.Label;

                // Skip the refresh for this key if the cached value has not expired
                if (DateTimeOffset.UtcNow < changeWatcher.CacheExpires)
                {
                    continue;
                }

                bool hasChanged = false;
                KeyValueIdentifier watchedKeyLabel = new KeyValueIdentifier(watchedKey, watchedLabel);
                bool success = false;

                try
                {
                    if (_watchedSettings.TryGetValue(watchedKeyLabel, out ConfigurationSetting watchedKv))
                    {
                        KeyValueChange keyValueChange = default;
                        await TracingUtils.CallWithRequestTracing(_requestTracingEnabled, RequestType.Watch, _requestTracingOptions,
                            async () => keyValueChange = await client.GetKeyValueChange(watchedKv, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

                        success = true;

                        // Check if a change has been detected in the key-value registered for refresh
                        if (keyValueChange.ChangeType != KeyValueChangeType.None)
                        {
                            if (changeWatcher.RefreshAll)
                            {
                                shouldRefreshAll = true;
                                break;
                            }

                            if (keyValueChange.ChangeType == KeyValueChangeType.Deleted)
                            {
                                _watchedSettings.Remove(watchedKeyLabel);
                            }
                            else if (keyValueChange.ChangeType == KeyValueChangeType.Modified)
                            {
                                _watchedSettings[watchedKeyLabel] = keyValueChange.Current;
                            }

                            hasChanged = true;
                            ProcessChanges(Enumerable.Repeat(keyValueChange, 1));
                        }
                    }
                    else
                    {
                        // Load the key-value in case the previous load attempts had failed
                        var options = new SettingSelector { LabelFilter = watchedLabel };

                        try
                        {
                            await CallWithRequestTracing(async () => watchedKv = await client.GetConfigurationSettingAsync(watchedKey, watchedLabel, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
                        }
                        catch (RequestFailedException e) when (e.Status == (int)HttpStatusCode.NotFound)
                        {
                            watchedKv = null;
                        }

                        success = true;

                        if (watchedKv != null)
                        {
                            if (changeWatcher.RefreshAll)
                            {
                                shouldRefreshAll = true;
                                break;
                            }

                            hasChanged = true;

                            // Add the key-value if it is not loaded, or update it if it was loaded with a different label
                            _applicationSettings[watchedKey] = watchedKv;
                            _watchedSettings[watchedKeyLabel] = watchedKv;

                            // Invalidate the cached Key Vault secret (if any) for this ConfigurationSetting
                            foreach (IKeyValueAdapter adapter in _options.Adapters)
                            {
                                adapter.InvalidateCache(watchedKv);
                            }
                        }
                    }
                }
                finally
                {
                    UpdateCacheExpirationTime(changeWatcher, success);
                }
                
                if (hasChanged)
                {
                    await SetData(_applicationSettings, false, cancellationToken).ConfigureAwait(false);
                }
            }

            // Trigger a single refresh-all operation if a change was detected in one or more key-values with refreshAll: true
            if (shouldRefreshAll)
            {
                await LoadAll(client, ignoreFailures: false, cancellationToken).ConfigureAwait(false);
            }
        }

        private Task RefreshKeyValueAdapters(CancellationToken cancellationToken)
        {
            if (_options.Adapters.Any(adapter => adapter.NeedsRefresh()))
            {
                return SetData(_applicationSettings, false, cancellationToken);
            }

            return Task.CompletedTask;
        }

        private async Task RefreshKeyValueCollections(ConfigurationClient client, CancellationToken cancellationToken)
        {
            foreach (KeyValueWatcher changeWatcher in _options.MultiKeyWatchers)
            {
                // Skip the refresh for this key-prefix if the cached value has not expired
                if (DateTimeOffset.UtcNow < changeWatcher.CacheExpires)
                {
                    continue;
                }

                IEnumerable<ConfigurationSetting> currentKeyValues;
                IEnumerable<KeyValueChange> keyValueChanges;
                bool success = false;

                try
                { 
                    if (changeWatcher.Key.EndsWith("*"))
                    {
                        // Get current application settings starting with changeWatcher.Key, excluding the last * character
                        var keyPrefix = changeWatcher.Key.Substring(0, changeWatcher.Key.Length - 1);
                        currentKeyValues = _applicationSettings.Values.Where(kv =>
                        {
                            return kv.Key.StartsWith(keyPrefix) && kv.Label == changeWatcher.Label.NormalizeNull();
                        });
                    }
                    else
                    {
                        currentKeyValues = _applicationSettings.Values.Where(kv =>
                        {
                            return kv.Key.Equals(changeWatcher.Key) && kv.Label == changeWatcher.Label.NormalizeNull();
                        });
                    }

                    keyValueChanges = await client.GetKeyValueChangeCollection(
                        currentKeyValues,
                        new GetKeyValueChangeCollectionOptions
                        {
                            KeyFilter = changeWatcher.Key,
                            Label = changeWatcher.Label.NormalizeNull(),
                            RequestTracingEnabled = _requestTracingEnabled,
                            RequestTracingOptions = _requestTracingOptions
                        },
                        cancellationToken).ConfigureAwait(false);

                    success = true;
                }
                finally
                {
                    UpdateCacheExpirationTime(changeWatcher, success);
                }

                if (keyValueChanges.Any())
                {
                    ProcessChanges(keyValueChanges);

                    await SetData(_applicationSettings, false, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private async Task SetData(IDictionary<string, ConfigurationSetting> data, bool ignoreFailures, CancellationToken cancellationToken)
        {
            // Update cache of settings
            this._applicationSettings = data as Dictionary<string, ConfigurationSetting> ??
                new Dictionary<string, ConfigurationSetting>(data, StringComparer.OrdinalIgnoreCase);

            // Set the application data for the configuration provider
            var applicationData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, ConfigurationSetting> kvp in data)
            {
                IEnumerable<KeyValuePair<string, string>> keyValuePairs = null;

                try
                {
                    keyValuePairs = await ProcessAdapters(kvp.Value, cancellationToken).ConfigureAwait(false);
                }
                catch (KeyVaultReferenceException)
                {
                    if (!ignoreFailures)
                    {
                        throw;
                    }

                    return;
                }

                foreach (KeyValuePair<string, string> kv in keyValuePairs)
                {
                    string key = kv.Key;
                    foreach (string prefix in _options.KeyPrefixes)
                    {
                        if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            key = key.Substring(prefix.Length);
                            break;
                        }
                    }

                    applicationData[key] = kv.Value;
                }
            }

            Data = applicationData;

            // Notify that the configuration has been updated
            OnReload();
        }

        private async Task<IEnumerable<KeyValuePair<string, string>>> ProcessAdapters(ConfigurationSetting setting, CancellationToken cancellationToken)
        {
            List<KeyValuePair<string, string>> keyValues = null;

            foreach (IKeyValueAdapter adapter in _options.Adapters)
            {
                if (!adapter.CanProcess(setting))
                {
                    continue;
                }

                IEnumerable<KeyValuePair<string, string>> kvs = await adapter.ProcessKeyValue(setting, cancellationToken).ConfigureAwait(false);

                if (kvs != null)
                {
                    keyValues = keyValues ?? new List<KeyValuePair<string, string>>();

                    keyValues.AddRange(kvs);
                }
            }

            return keyValues ?? Enumerable.Repeat(new KeyValuePair<string, string>(setting.Key, setting.Value), 1);
        }

        private void ProcessChanges(IEnumerable<KeyValueChange> changes)
        {
            foreach (KeyValueChange change in changes)
            {
                if (change.ChangeType == KeyValueChangeType.Deleted)
                {
                    _applicationSettings.Remove(change.Key);
                }
                else if (change.ChangeType == KeyValueChangeType.Modified)
                {
                    _applicationSettings[change.Key] = change.Current;
                }

                // Invalidate the cached Key Vault secret (if any) for this ConfigurationSetting
                foreach (IKeyValueAdapter adapter in _options.Adapters)
                {
                    adapter.InvalidateCache(change.Current);
                }
            }
        }

        private Task CallWithRequestTracing(Func<Task> clientCall)
        {
            var requestType = _isInitialLoadComplete ? RequestType.Watch : RequestType.Startup;
            return TracingUtils.CallWithRequestTracing(_requestTracingEnabled, requestType, _requestTracingOptions, clientCall);
        }

        private void SetRequestTracingOptions()
        {
            _requestTracingOptions = new RequestTracingOptions
            {
                HostType = TracingUtils.GetHostType(),
                IsDevEnvironment = TracingUtils.IsDevEnvironment(),
                IsKeyVaultConfigured = _options.IsKeyVaultConfigured,
                IsKeyVaultRefreshConfigured = _options.IsKeyVaultRefreshConfigured,
                FeatureManagementSchemaVersion = _options.FeatureManagementSchemaVersion
            };
        }

        private DateTimeOffset AddRandomDelay(DateTimeOffset dt, TimeSpan maxDelay)
        {
            long randomTicks = (long)(maxDelay.Ticks * RandomGenerator.NextDouble());
            return dt.AddTicks(randomTicks);
        }

        private bool IsAuthenticationError(Exception ex)
        {
            if (ex is RequestFailedException rfe)
            {
                return rfe.Status == (int)HttpStatusCode.Unauthorized || rfe.Status == (int)HttpStatusCode.Forbidden;
            }

            if (ex is AggregateException ae)
            {
                return ae.InnerExceptions?.Any(inner => IsAuthenticationError(inner)) ?? false;
            }

            return false;
        }

        private void UpdateCacheExpirationTime(KeyValueWatcher changeWatcher, bool success)
        {
            TimeSpan cacheExpirationTime;

            if (success)
            {
                changeWatcher.RefreshAttempts = 0;
                cacheExpirationTime = changeWatcher.CacheExpirationInterval;
            }
            else
            {
                if (changeWatcher.RefreshAttempts < int.MaxValue)
                {
                    changeWatcher.RefreshAttempts++;
                }

                cacheExpirationTime = changeWatcher.CacheExpirationInterval.CalculateBackoffTime(RefreshConstants.DefaultMinBackoff, RefreshConstants.DefaultMaxBackoff, changeWatcher.RefreshAttempts);
            }

            changeWatcher.CacheExpires = DateTimeOffset.UtcNow.Add(cacheExpirationTime);
        }

        private async Task ExecuteWithFailOverPolicyAsync(Func<ConfigurationClient, Task> funcToExecute, CancellationToken cancellationToken = default)
        {
            IEnumerator<ConfigurationClient> clientEnumerator = _configClientProvider.GetClientEnumerator();

            // Guaranteed to return at least 1 element
            Debug.Assert(clientEnumerator.MoveNext());

            while (true)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await funcToExecute(clientEnumerator.Current);

                    _configClientProvider.UpdateClientStatus(clientEnumerator.Current, successful: true);

                    break;
                }
                catch (AggregateException ae)
                {
                    if (!ShouldFailoverForException(ae))
                    {
                        throw;
                    }
                    else
                    {
                        _configClientProvider.UpdateClientStatus(clientEnumerator.Current, successful: false);

                        if (!clientEnumerator.MoveNext())
                        {
                            throw;
                        }
                    }
                }
                catch (RequestFailedException e)
                {
                    if (!ShouldFailoverForException(e))
                    {
                        throw;
                    }
                    else
                    {
                        _configClientProvider.UpdateClientStatus(clientEnumerator.Current, successful: false);

                        if (!clientEnumerator.MoveNext())
                        {
                            throw;
                        }
                    }
                }
            }
        }

        private bool ShouldFailoverForException(Exception ex)
        {
            int statusCode = 0;

            if (ex is AggregateException aggregateException)
            {
                IReadOnlyCollection<Exception> innerExceptions = aggregateException.Flatten().InnerExceptions;

                if (innerExceptions.Count > 0 && innerExceptions.All(ex => ex is RequestFailedException))
                {
                    statusCode = (innerExceptions.Last() as RequestFailedException).Status;
                }
            }
            else if (ex is RequestFailedException rfe)
            {
                statusCode = rfe.Status;
            }

            return statusCode == HttpStatusCodeRequestThrottled || statusCode == (int)HttpStatusCode.RequestTimeout || statusCode >= (int)HttpStatusCode.InternalServerError;
        }
    }
}
