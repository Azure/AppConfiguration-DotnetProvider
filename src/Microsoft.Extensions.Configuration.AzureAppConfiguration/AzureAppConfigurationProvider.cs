// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Models;
using System;
using System.Collections.Concurrent;
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

        private readonly HostType _hostType;
        private readonly ConfigurationClient _client;
        private AzureAppConfigurationOptions _options;
        private ConcurrentDictionary<string, ConfigurationSetting> _settings;
        private ConcurrentDictionary<KeyLabelIdentifier, ConfigurationSetting> _refreshRegisteredSettings = new ConcurrentDictionary<KeyLabelIdentifier, ConfigurationSetting>();

        private readonly TimeSpan MinCacheExpirationInterval;
        private readonly SemaphoreSlim InitializationSemaphore = new SemaphoreSlim(1);

        // The most-recent time when the refresh operation attempted to load the initial configuration
        private DateTimeOffset InitializationCacheExpires = default;

        private static readonly TimeSpan MinDelayForUnhandledFailure = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan DefaultMaxSetDirtyDelay = TimeSpan.FromSeconds(30);

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
                _hostType = TracingUtils.GetHostType();
            }
        }

        /// <summary>
        /// Loads (or reloads) the data for this provider.
        /// </summary>
        public override void Load()
        {
            var watch = Stopwatch.StartNew();
            var refresher = (AzureAppConfigurationRefresher)_options.GetRefresher();
            refresher.SetProvider(this);

            try
            {
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

            // Mark all settings have loaded at startup.
            _isInitialLoadComplete = true;
        }

        public async Task RefreshAsync()
        {
            // Check if initial configuration load had failed
            if (_settings == null)
            {
                await RefreshInitialConfiguration().ConfigureAwait(false);
                return;
            }

            await RefreshIndividualKeyValues().ConfigureAwait(false);
            await RefreshKeyValueCollections().ConfigureAwait(false);
        }

        public async Task<bool> TryRefreshAsync()
        {
            try
            {
                await RefreshAsync().ConfigureAwait(false);
            }
            catch (Exception e) when (
                e is KeyVaultReferenceException ||
                e is RequestFailedException ||
                ((e as AggregateException)?.InnerExceptions?.All(e => e is RequestFailedException) ?? false) ||
                e is OperationCanceledException)
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

        private async Task RefreshInitialConfiguration()
        {
            if (DateTimeOffset.UtcNow < InitializationCacheExpires || !InitializationSemaphore.Wait(0))
            {
                return;
            }

            InitializationCacheExpires = DateTimeOffset.UtcNow.Add(MinCacheExpirationInterval);

            try
            {
                await LoadAll(ignoreFailures: false).ConfigureAwait(false);
            }
            finally
            {
                InitializationSemaphore.Release();
            }
        }

        private async Task LoadKeyValuesRegisteredForRefresh(IDictionary<string, ConfigurationSetting> data)
        {
            foreach (KeyValueWatcher changeWatcher in _options.ChangeWatchers)
            {
                string watchedKey = changeWatcher.Key;
                string watchedLabel = changeWatcher.Label;
                KeyLabelIdentifier watchedKeyLabel = new KeyLabelIdentifier(watchedKey, watchedLabel);

                // Skip the loading for the key-value in case it has already been loaded
                if (data.ContainsKey(watchedKey) && data[watchedKey].Label == watchedLabel.NormalizeNull())
                {
                    _refreshRegisteredSettings[watchedKeyLabel] = data[watchedKey];
                    continue;
                }

                // Send a request to retrieve key-value since it may be either not loaded or loaded with a different label
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
                    _refreshRegisteredSettings[watchedKeyLabel] = watchedKv;
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

                // Skip the refresh for this key if the cached value has not expired or a refresh operation is in progress
                if (DateTimeOffset.UtcNow < changeWatcher.CacheExpires || !changeWatcher.Semaphore.Wait(0))
                {
                    continue;
                }

                try
                {
                    bool hasChanged = false;
                    ConfigurationSetting watchedKv = null;
                    KeyLabelIdentifier watchedKeyLabel = new KeyLabelIdentifier(watchedKey, watchedLabel);

                    if (_refreshRegisteredSettings.ContainsKey(watchedKeyLabel))
                    {
                        watchedKv = _refreshRegisteredSettings[watchedKeyLabel];

                        KeyValueChange keyValueChange = default;
                        await TracingUtils.CallWithRequestTracing(_requestTracingEnabled, RequestType.Watch, _hostType,
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

                            // If this key-value exists in _settings cache, it needs to be updated.
                            if (_settings.ContainsKey(watchedKey) && _settings[watchedKey].Label == watchedLabel.NormalizeNull())
                            {
                                hasChanged = true;
                                ProcessChanges(Enumerable.Repeat(keyValueChange, 1));
                            }
                            else
                            {
                                // This is only a refresh registered setting with 'RefreshAll=false'.
                                // Change in this key-value will not affect any other key-values in IConfiguration.
                                _refreshRegisteredSettings[watchedKeyLabel] = keyValueChange.Current;
                            }
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
                            _refreshRegisteredSettings[watchedKeyLabel] = watchedKv;

                            // Add the key-value if it is not loaded, or update it if it was loaded with a different label
                            _settings[watchedKey] = watchedKv;
                        }
                    }

                    if (hasChanged)
                    {
                        await SetData(_settings).ConfigureAwait(false);
                    }
                }
                finally
                {
                    changeWatcher.Semaphore.Release();
                }
            }

            // Trigger a single refresh-all operation if a change was detected in one or more key-values with refreshAll: true
            if (shouldRefreshAll)
            {
                await LoadAll(ignoreFailures: false).ConfigureAwait(false);
            }
        }

        private async Task RefreshKeyValueCollections()
        {
            foreach (KeyValueWatcher changeWatcher in _options.MultiKeyWatchers)
            {
                // Skip the refresh for this key-prefix if the cached value has not expired or a refresh operation is in progress
                if (DateTimeOffset.UtcNow < changeWatcher.CacheExpires || !changeWatcher.Semaphore.Wait(0))
                {
                    continue;
                }

                try
                {
                    IEnumerable<ConfigurationSetting> currentKeyValues = _settings.Values.Where(kv =>
                    {
                        return kv.Key.StartsWith(changeWatcher.Key) && kv.Label == changeWatcher.Label.NormalizeNull();
                    });

                    IEnumerable<KeyValueChange> keyValueChanges = await _client.GetKeyValueChangeCollection(currentKeyValues, new GetKeyValueChangeCollectionOptions
                    {
                        Prefix = changeWatcher.Key,
                        Label = changeWatcher.Label.NormalizeNull(),
                        RequestTracingEnabled = _requestTracingEnabled,
                        HostType = _hostType
                    }).ConfigureAwait(false);

                    changeWatcher.CacheExpires = DateTimeOffset.UtcNow.Add(changeWatcher.CacheExpirationInterval);

                    if (keyValueChanges?.Any() == true)
                    {
                        ProcessChanges(keyValueChanges);

                        await SetData(_settings).ConfigureAwait(false);
                    }
                }
                finally
                {
                    changeWatcher.Semaphore.Release();
                }
            }
        }

        private async Task SetData(IDictionary<string, ConfigurationSetting> data, bool ignoreFailures = false, CancellationToken cancellationToken = default)
        {
            // Update cache of settings
            this._settings = data as ConcurrentDictionary<string, ConfigurationSetting> ??
                new ConcurrentDictionary<string, ConfigurationSetting>(data, StringComparer.OrdinalIgnoreCase);

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
                    _settings.TryRemove(change.Key, out ConfigurationSetting removed);
                }
                else if (change.ChangeType == KeyValueChangeType.Modified)
                {
                    _settings[change.Key] = change.Current;
                }
            }
        }

        private async Task CallWithRequestTracing(Func<Task> clientCall)
        {
            var requestType = _isInitialLoadComplete ? RequestType.Watch : RequestType.Startup;
            await TracingUtils.CallWithRequestTracing(_requestTracingEnabled, requestType, _hostType, clientCall).ConfigureAwait(false);
        }

        private DateTimeOffset AddRandomDelay(DateTimeOffset dt, TimeSpan maxDelay)
        {
            long randomTicks = (long)(maxDelay.Ticks * RandomGenerator.NextDouble());
            return dt.AddTicks(randomTicks);
        }
    }
}
