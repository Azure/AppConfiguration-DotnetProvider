﻿// Copyright (c) Microsoft Corporation.
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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{

    internal class AzureAppConfigurationProvider : ConfigurationProvider, IConfigurationRefresher
    {
        private bool _optional;
        private bool _isInitialLoadComplete = false;
        private readonly bool _requestTracingEnabled;

        private readonly ConfigurationClient _client;
        private AzureAppConfigurationOptions _options;
        private Dictionary<string, ConfigurationSetting> _applicationSettings;
        private Dictionary<KeyValueIdentifier, ConfigurationSetting> _watchedSettings = new Dictionary<KeyValueIdentifier, ConfigurationSetting>();
        private RequestTracingOptions _requestTracingOptions;

        private readonly TimeSpan MinCacheExpirationInterval;

        // The most-recent time when the refresh operation attempted to load the initial configuration
        private DateTimeOffset InitializationCacheExpires = default;

        private static readonly TimeSpan MinDelayForUnhandledFailure = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan DefaultMaxSetDirtyDelay = TimeSpan.FromSeconds(30);
       
        // To avoid concurrent network operations, this flag is used to achieve synchronization between multiple threads.
        private int _networkOperationsInProgress = 0;

        public Uri AppConfigurationEndpoint
        {
            get
            {
                if (_options.Endpoint != null)
                {
                    return _options.Endpoint;
                }

                if (_options.ConnectionString != null)
                {
                    // Use try-catch block to avoid throwing exceptions from property getter.
                    // https://docs.microsoft.com/en-us/dotnet/standard/design-guidelines/property

                    try
                    {
                        return new Uri(ConnectionStringParser.Parse(_options.ConnectionString, "Endpoint"));
                    }
                    catch (ArgumentException) { }
                    catch (UriFormatException) { }
                }

                return null;
            }
        }

        public AzureAppConfigurationProvider(ConfigurationClient client, AzureAppConfigurationOptions options, bool optional)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _optional = optional;

            IEnumerable<KeyValueWatcher> watchers = options.ChangeWatchers.Union(options.MultiKeyWatchers);

            if (watchers.Any())
            {
                MinCacheExpirationInterval = watchers.Min(w => w.CacheExpirationInterval);
            }
            else
            {
                MinCacheExpirationInterval = AzureAppConfigurationRefreshOptions.DefaultCacheExpirationInterval;
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
                LoadAll(_optional).ConfigureAwait(false).GetAwaiter().GetResult();
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

        public async Task RefreshAsync()
        {
            // Ensure that concurrent threads do not simultaneously execute refresh operation. 
            if (Interlocked.Exchange(ref _networkOperationsInProgress, 1) == 0)
            {
                try
                {
                    // Check if initial configuration load had failed
                    if (_applicationSettings == null)
                    {
                        if (InitializationCacheExpires < DateTimeOffset.UtcNow)
                        {
                            InitializationCacheExpires = DateTimeOffset.UtcNow.Add(MinCacheExpirationInterval);
                            await LoadAll(ignoreFailures: false).ConfigureAwait(false);
                        }

                        return;
                    }

                    await RefreshIndividualKeyValues().ConfigureAwait(false);
                    await RefreshKeyValueCollections().ConfigureAwait(false);
                    await RefreshKeyValueAdapters().ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Exchange(ref _networkOperationsInProgress, 0);
                }
            }
        }

        public async Task<bool> TryRefreshAsync(ILogger logger)
        {
            try
            {
                await RefreshAsync().ConfigureAwait(false);
            }
            catch (RequestFailedException e)
            {
                if (e.Status == (int)HttpStatusCode.Unauthorized || e.Status == (int)HttpStatusCode.Forbidden)
                {
                    logger?.LogError(e, "Refresh operation failed due to an authentication error");
                }

                return false;
            }
            catch (AggregateException e) when (e?.InnerExceptions?.All(e => e is RequestFailedException) ?? false)
            {
                if (e.InnerExceptions.Any(exception => (exception is RequestFailedException ex) 
                                                        && (ex.Status == (int)HttpStatusCode.Unauthorized || ex.Status == (int)HttpStatusCode.Forbidden)))
                {
                    logger?.LogError(e, "Refresh operation failed due to an authentication error.");
                }

                return false;
            }
            catch (KeyVaultReferenceException e)
            {
                logger?.LogError(e, "Refresh operation failed while resolving a Key Vault reference.");
                return false;
            }
            catch (OperationCanceledException)
            {
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

        private async Task LoadAll(bool ignoreFailures)
        {
            IDictionary<string, ConfigurationSetting> data = null;
            string cachedData = null;

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
                        await foreach (ConfigurationSetting setting in _client.GetConfigurationSettingsAsync(selector, CancellationToken.None).ConfigureAwait(false))
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
                        await foreach (ConfigurationSetting setting in _client.GetConfigurationSettingsAsync(selector, CancellationToken.None).ConfigureAwait(false))
                        {
                            serverData[setting.Key] = setting;
                        }
                    }).ConfigureAwait(false);
                }

                // Block current thread for the initial load of key-values registered for refresh that are not already loaded
                await Task.Run(() => LoadKeyValuesRegisteredForRefresh(serverData).ConfigureAwait(false).GetAwaiter().GetResult()).ConfigureAwait(false);
                data = serverData;
            }
            catch (Exception exception) when (exception is RequestFailedException ||
                                              ((exception as AggregateException)?.InnerExceptions?.All(e => e is RequestFailedException) ?? false) ||
                                              exception is OperationCanceledException)
            {
                if (_options.OfflineCache != null)
                {
                    // During startup or refreshAll scenario, we'll try to populate config from offline cache, if available
                    cachedData = _options.OfflineCache.Import(_options);
                    
                    if (cachedData != null)
                    {
                        data = JsonSerializer.Deserialize<IDictionary<string, ConfigurationSetting>>(cachedData);
                    }
                }

                // If we're unable to load data from offline cache, check if we need to ignore or rethrow the exception 
                if (data == null && !ignoreFailures)
                {
                    throw;
                }
            }

            if (data != null)
            {
                // Invalidate all the cached KeyVault secrets
                foreach (IKeyValueAdapter adapter in _options.Adapters)
                {
                    adapter.InvalidateCache();
                }

                await SetData(data, ignoreFailures).ConfigureAwait(false);

                // Set the cache expiration time for all refresh registered settings
                var initialLoadTime = DateTimeOffset.UtcNow;
                
                foreach (KeyValueWatcher changeWatcher in _options.ChangeWatchers)
                {
                    changeWatcher.CacheExpires = initialLoadTime.Add(changeWatcher.CacheExpirationInterval);
                }

                foreach (KeyValueWatcher changeWatcher in _options.MultiKeyWatchers)
                {
                    changeWatcher.CacheExpires = initialLoadTime.Add(changeWatcher.CacheExpirationInterval);
                }

                if (_options.OfflineCache != null && cachedData == null)
                {
                    _options.OfflineCache.Export(_options, JsonSerializer.Serialize(data));
                }
            }
        }

        private async Task LoadKeyValuesRegisteredForRefresh(IDictionary<string, ConfigurationSetting> data)
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
                    await CallWithRequestTracing(async () => watchedKv = await _client.GetConfigurationSettingAsync(watchedKey, watchedLabel, CancellationToken.None)).ConfigureAwait(false);
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

        private async Task RefreshIndividualKeyValues()
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

                if (_watchedSettings.TryGetValue(watchedKeyLabel, out ConfigurationSetting watchedKv))
                {
                    KeyValueChange keyValueChange = default;
                    await TracingUtils.CallWithRequestTracing(_requestTracingEnabled, RequestType.Watch, _requestTracingOptions,
                        async () => keyValueChange = await _client.GetKeyValueChange(watchedKv, CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);

                    changeWatcher.CacheExpires = DateTimeOffset.UtcNow.Add(changeWatcher.CacheExpirationInterval);

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
                        await CallWithRequestTracing(async () => watchedKv = await _client.GetConfigurationSettingAsync(watchedKey, watchedLabel, CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);
                    }
                    catch (RequestFailedException e) when (e.Status == (int)HttpStatusCode.NotFound)
                    {
                        watchedKv = null;
                    }

                    changeWatcher.CacheExpires = DateTimeOffset.UtcNow.Add(changeWatcher.CacheExpirationInterval);

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

                if (hasChanged)
                {
                    await SetData(_applicationSettings).ConfigureAwait(false);
                }
            }

            // Trigger a single refresh-all operation if a change was detected in one or more key-values with refreshAll: true
            if (shouldRefreshAll)
            {
                await LoadAll(ignoreFailures: false).ConfigureAwait(false);
            }
        }

        private async Task RefreshKeyValueAdapters()
        {
            if (_options.Adapters.Any(adapter => adapter.NeedsRefresh()))
            {
                SetData(_applicationSettings);
            }
        }

        private async Task RefreshKeyValueCollections()
        {
            foreach (KeyValueWatcher changeWatcher in _options.MultiKeyWatchers)
            {
                // Skip the refresh for this key-prefix if the cached value has not expired
                if (DateTimeOffset.UtcNow < changeWatcher.CacheExpires)
                {
                    continue;
                }

                IEnumerable<ConfigurationSetting> currentKeyValues;

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

                IEnumerable<KeyValueChange> keyValueChanges = await _client.GetKeyValueChangeCollection(currentKeyValues, new GetKeyValueChangeCollectionOptions
                {
                    KeyFilter = changeWatcher.Key,
                    Label = changeWatcher.Label.NormalizeNull(),
                    RequestTracingEnabled = _requestTracingEnabled,
                    RequestTracingOptions = _requestTracingOptions
                }).ConfigureAwait(false);

                changeWatcher.CacheExpires = DateTimeOffset.UtcNow.Add(changeWatcher.CacheExpirationInterval);

                if (keyValueChanges?.Any() == true)
                {
                    ProcessChanges(keyValueChanges);

                    await SetData(_applicationSettings).ConfigureAwait(false);
                }
            }
        }

        private async Task SetData(IDictionary<string, ConfigurationSetting> data, bool ignoreFailures = false, CancellationToken cancellationToken = default)
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

        private async Task CallWithRequestTracing(Func<Task> clientCall)
        {
            var requestType = _isInitialLoadComplete ? RequestType.Watch : RequestType.Startup;
            await TracingUtils.CallWithRequestTracing(_requestTracingEnabled, requestType, _requestTracingOptions, clientCall).ConfigureAwait(false);
        }

        private void SetRequestTracingOptions()
        {
            _requestTracingOptions = new RequestTracingOptions
            {
                HostType = TracingUtils.GetHostType(),
                IsDevEnvironment = TracingUtils.IsDevEnvironment(),
                IsKeyVaultConfigured = _options.IsKeyVaultConfigured,
                IsKeyVaultRefreshConfigured = _options.IsKeyVaultRefreshConfigured,
                IsOfflineCacheConfigured = _options.IsOfflineCacheConfigured
            };
        }

        private DateTimeOffset AddRandomDelay(DateTimeOffset dt, TimeSpan maxDelay)
        {
            long randomTicks = (long)(maxDelay.Ticks * RandomGenerator.NextDouble());
            return dt.AddTicks(randomTicks);
        }
    }
}
