using Azure.Data.AppConfiguration;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    using Microsoft.Extensions.Configuration.AzureAppConfiguration.Constants;
    using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
    using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
    using Microsoft.Extensions.Configuration.AzureAppConfiguration.Models;
    using System.Text.Json;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Security;
    using System.Threading;
    using System.Threading.Tasks;

    internal class AzureAppConfigurationProvider : ConfigurationProvider, IConfigurationRefresher
    {
        private bool _optional;
        private bool _isInitialLoadComplete = false;
        private readonly bool _requestTracingEnabled;

        private const int MaxRetries = 12;
        private const int RetryWaitMinutes = 1;

        private readonly HostType _hostType;
        private readonly ConfigurationClient _client;
        private AzureAppConfigurationOptions _options;
        private ConcurrentDictionary<string, ConfigurationSetting> _settings;

        public AzureAppConfigurationProvider(AzureAppConfigurationOptions options, bool optional)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _optional = optional;
            
            ConfigurationClientOptions clientOptions = new ConfigurationClientOptions(ConfigurationClientOptions.ServiceVersion.Default);
            clientOptions.Retry.MaxRetries = MaxRetries;
            clientOptions.Retry.MaxDelay = TimeSpan.FromMinutes(RetryWaitMinutes);

            // TODO: what is the scenario where we could have already have a client populated - should we support this?
            // Answer: MSI auth, and tests

            // TODO: what is the requirement here?
            _client.UserAgent = TracingUtils.GenerateUserAgent(client.UserAgent);

            string requestTracingDisabled = null;
            try
            {
                requestTracingDisabled = Environment.GetEnvironmentVariable(RequestTracingConstants.RequestTracingDisabledEnvironmentVariable);
                _hostType =  Environment.GetEnvironmentVariable(RequestTracingConstants.AzureFunctionEnvironmentVariable) != null
                    ? HostType.AzureFunction
                    : Environment.GetEnvironmentVariable(RequestTracingConstants.AzureWebAppEnvironmentVariable) != null
                        ? HostType.AzureWebApp
                        : HostType.None;
            }
            catch (SecurityException) { }

            // Enable request tracing by default (if no valid environmental variable option is specified).
            _requestTracingEnabled = bool.TryParse(requestTracingDisabled, out bool tracingDisabled) ? !tracingDisabled : true;
        }

        /// <summary>
        /// Loads (or reloads) the data for this provider.
        /// </summary>
        public override void Load()
        {
            var refresher = (AzureAppConfigurationRefresher)_options.GetRefresher();
            refresher.SetProvider(this);

            LoadAll().ConfigureAwait(false).GetAwaiter().GetResult();

            // Mark all settings have loaded at startup.
            _isInitialLoadComplete = true;
        }

        public async Task Refresh()
        {
            await RefreshIndividualKeyValues().ConfigureAwait(false);
            await RefreshKeyValueCollections().ConfigureAwait(false);
        }

        private async Task LoadAll()
        {
            IDictionary<string, ConfigurationSetting> data = new Dictionary<string, ConfigurationSetting>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // Use default query if there are no key-values specified for use other than the feature flags
                bool useDefaultQuery = !_options.KeyValueSelectors.Any(selector => !selector.KeyFilter.StartsWith(FeatureManagementConstants.FeatureFlagMarker));

                if (useDefaultQuery)
                {
                    ConfigureRequestTracingOptions(_options);

                    // TODO: does this set Key to Any? // If not, use SettingSelector(null)
                    var selector = new SettingSelector();


                    // TODO: IAsyncEnumerable isn't supported till netstandard2.1
                    //
                    // Load all key-values with the null label.
                    var collection = _client.GetSettingsAsync(selector);// .ConfigureAwait(false);
                    
                    await foreach (var response in collection)
                    {
                        data[response.Value.Key] = response.Value;
                    }
                    .ConfigureAwait(false);
                }

                foreach (var loadOption in _options.KeyValueSelectors)
                {
                    if ((useDefaultQuery && LabelFilter.Null.Equals(loadOption.LabelFilter)) ||
                        _options.KeyValueSelectors.Any(s => s != loadOption && 
                           string.Equals(s.KeyFilter, KeyFilter.Any) && 
                           string.Equals(s.LabelFilter, loadOption.LabelFilter) && 
                           Nullable<DateTimeOffset>.Equals(s.PreferredDateTime, loadOption.PreferredDateTime)))
                    {
                        // This selection was already encapsulated by a wildcard query
                        // We skip it to prevent unnecessary requests
                        continue;
                    }

                    var options = new SettingSelector(loadOption.KeyFilter, loadOption.LabelFilter)
                    {
                        AsOf = loadOption.PreferredDateTime
                    };

                    ConfigureRequestTracingOptions(options);

                    // TODO: Get AsyncCollection
                    await _client.GetSettingsAsync(options).ForEachAsync(kv => data[kv.Key] = kv).ConfigureAwait(false);

                    // Block current thread for the initial load of key-values registered for refresh that are not already loaded
                    await Task.Run(() => LoadKeyValuesRegisteredForRefresh(data).ConfigureAwait(false).GetAwaiter().GetResult());
                }
            }
            catch (Exception exception) when (exception.InnerException is HttpRequestException ||
                                              exception.InnerException is OperationCanceledException ||
                                              exception.InnerException is UnauthorizedAccessException)
            {
                if (_options.OfflineCache != null)
                {
                    // TODO: ?
                    var cache = _options.OfflineCache.Import(_options);
                    // TODO: does this do the right thing?
                    data = JsonSerializer.Deserialize<IDictionary<string, ConfigurationSetting>>(cache);

                    if (data != null)
                    {
                        await SetData(data).ConfigureAwait(false);
                        return;
                    }
                }

                if (!_optional)
                {
                    throw;
                }

                return;
            }

            await SetData(data).ConfigureAwait(false);

            if (_options.OfflineCache != null)
            {
                _options.OfflineCache.Export(_options, JsonSerializer.Serialize(data));
            }
        }

        private async Task LoadKeyValuesRegisteredForRefresh(IDictionary<string, ConfigurationSetting> data)
        {
            foreach (KeyValueWatcher changeWatcher in _options.ChangeWatchers)
            {
                string watchedKey = changeWatcher.Key;
                string watchedLabel = changeWatcher.Label;

                // Skip the loading for the key-value in case it has already been loaded
                if (data.ContainsKey(watchedKey) && data[watchedKey].Label == watchedLabel.NormalizeNull())
                {
                    return;
                }

                var options = new SettingSelector(watchedKey, watchedLabel);

                ConfigureRequestTracingOptions(options);

                // Send a request to retrieve key-value since it may be either not loaded or loaded with a different label
                // TODO: How do we request from service with SettingSelector?
                ConfigurationSetting watchedKv = await _client.GetSettingsAsync(options, CancellationToken.None).ConfigureAwait(false) ?? new ConfigurationSetting(watchedKey, null) { Label = watchedLabel };
                changeWatcher.LastRefreshTime = DateTimeOffset.UtcNow;
                data[watchedKey] = watchedKv;
            }
        }

        private async Task RefreshIndividualKeyValues()
        {
            bool shouldRefreshAll = false;

            foreach (KeyValueWatcher changeWatcher in _options.ChangeWatchers)
            {
                string watchedKey = changeWatcher.Key;
                string watchedLabel = changeWatcher.Label;
                var timeElapsedSinceLastRefresh = DateTimeOffset.UtcNow - changeWatcher.LastRefreshTime;

                // Skip the refresh for this key if the cached value has not expired or a refresh operation is in progress
                if (timeElapsedSinceLastRefresh < changeWatcher.CacheExpirationTime || !changeWatcher.Semaphore.Wait(0))
                {
                    continue;
                }

                try
                {
                    bool hasChanged = false;
                    ConfigurationSetting watchedKv = null;

                    if (_settings.ContainsKey(watchedKey) && _settings[watchedKey].Label == watchedLabel.NormalizeNull())
                    {
                        watchedKv = _settings[watchedKey];
                        var options = _requestTracingEnabled ? new RequestOptionsBase() : null;
                        options.ConfigureRequestTracing(_requestTracingEnabled, RequestType.Watch, _hostType);

                        KeyValueChange keyValueChange = await _client.GetKeyValueChange(watchedKv, options, CancellationToken.None).ConfigureAwait(false);
                        changeWatcher.LastRefreshTime = DateTimeOffset.UtcNow;

                        // Check if a change has been detected in the key-value registered for refresh
                        if (keyValueChange != null)
                        {
                            ProcessChanges(Enumerable.Repeat(keyValueChange, 1));
                            hasChanged = true;
                        }
                    }
                    else
                    {
                        // Load the key-value in case the previous load attempts had failed

                        var options = new SettingSelector();
                        options.Labels.Add(watchedLabel);
                        ConfigureRequestTracingOptions(options);

                        // Send a request to retrieve key-value since it may be either not loaded or loaded with a different label
                        // TODO: Get with Selector
                        // TODO: is it valid to pass a null value?
                        watchedKv = await _client.Get(watchedKey, options, CancellationToken.None).ConfigureAwait(false) ?? new ConfigurationSetting(watchedKey, null) { Label = watchedLabel };
                        changeWatcher.LastRefreshTime = DateTimeOffset.UtcNow;

                        // Add the key-value if it is not loaded, or update it if it was loaded with a different label
                        _settings[watchedKey] = watchedKv;
                        hasChanged = true;
                    }

                    if (hasChanged)
                    {
                        if (changeWatcher.RefreshAll)
                        {
                            shouldRefreshAll = true;

                            // Skip refresh for other key-values since refreshAll will populate configuration from scratch
                            break;
                        }
                        else
                        {
                            await SetData(_settings).ConfigureAwait(false);
                        }
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
                await LoadAll().ConfigureAwait(false);
            }
        }

        private async Task RefreshKeyValueCollections()
        {
            foreach (KeyValueWatcher changeWatcher in _options.MultiKeyWatchers)
            {
                var timeElapsedSinceLastRefresh = DateTimeOffset.UtcNow - changeWatcher.LastRefreshTime;

                // Skip the refresh for this key-prefix if the cached value has not expired or a refresh operation is in progress
                if (timeElapsedSinceLastRefresh < changeWatcher.CacheExpirationTime || !changeWatcher.Semaphore.Wait(0))
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

                    changeWatcher.LastRefreshTime = DateTimeOffset.UtcNow;

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

        private async Task SetData(IDictionary<string, ConfigurationSetting> data)
        {
            // Update cache of settings
            this._settings = data as ConcurrentDictionary<string, ConfigurationSetting> ?? 
                new ConcurrentDictionary<string, ConfigurationSetting>(data, StringComparer.OrdinalIgnoreCase);

            // Set the application data for the configuration provider
            var applicationData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, ConfigurationSetting> kvp in data)
            {
                foreach (KeyValuePair<string, string> kv in await ProcessAdapters(kvp.Value, cancellationToken).ConfigureAwait(false))
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
        
        private  async Task<IEnumerable<KeyValuePair<string, string>>> ProcessAdapters(IKeyValue keyValue)
        {
            List<KeyValuePair<string, string>> keyValues = null;

            foreach (IKeyValueAdapter adapter in _options.Adapters)
            {
                if (!adapter.CanProcess(keyValue))
                {
                    continue;
                }

                IEnumerable<KeyValuePair<string, string>> kvs = await adapter.ProcessKeyValue(keyValue, cancellationToken).ConfigureAwait(false);

                if (kvs != null)
                {
                    keyValues = keyValues ?? new List<KeyValuePair<string, string>>();

                    keyValues.AddRange(kvs);
                }
            }

            return keyValues ?? Enumerable.Repeat(new KeyValuePair<string, string>(keyValue.Key, keyValue.Value), 1);
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

        private void ConfigureRequestTracingOptions(IRequestOptions options)
        {
            var requestType = _isInitialLoadComplete ? RequestType.Watch : RequestType.Startup;
            options.ConfigureRequestTracing(_requestTracingEnabled, requestType, _hostType);
        }
    }
}
