namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    using Microsoft.Azure.AppConfiguration.Azconfig;
    using Microsoft.Extensions.Configuration.AzureAppConfiguration.Constants;
    using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
    using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
    using Microsoft.Extensions.Configuration.AzureAppConfiguration.Models;
    using Newtonsoft.Json;
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
        private readonly AzconfigClient _client;
        private AzureAppConfigurationOptions _options;
        private ConcurrentDictionary<string, IKeyValue> _settings;

        public AzureAppConfigurationProvider(AzconfigClient client, AzureAppConfigurationOptions options, bool optional)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _optional = optional;

            // Initialize retry options.
            _client.RetryOptions.MaxRetries = MaxRetries;
            _client.RetryOptions.MaxRetryWaitTime = TimeSpan.FromMinutes(RetryWaitMinutes);

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

            LoadAll();

            // Mark all settings have loaded at startup.
            _isInitialLoadComplete = true;
        }

        public async Task Refresh()
        {
            await RefreshIndividualKeyValues().ConfigureAwait(false);
            await RefreshKeyValueCollections().ConfigureAwait(false);
        }

        private void LoadAll()
        {
            IDictionary<string, IKeyValue> data = new Dictionary<string, IKeyValue>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // Use default query if there are no key-values specified for use other than the feature flags
                bool useDefaultQuery = !_options.KeyValueSelectors.Any(selector => !selector.KeyFilter.StartsWith(FeatureManagementConstants.FeatureFlagMarker));

                if (useDefaultQuery)
                {
                    var options = new QueryKeyValueCollectionOptions
                    {
                        KeyFilter = KeyFilter.Any,
                        LabelFilter = LabelFilter.Null
                    };
                    
                    ConfigureRequestTracingOptions(options);

                    // Load all key-values with the null label
                    _client.GetKeyValues(options).ForEach(kv => { data[kv.Key] = kv; });
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

                    var queryKeyValueCollectionOptions = new QueryKeyValueCollectionOptions
                    {
                        KeyFilter = loadOption.KeyFilter,
                        LabelFilter = loadOption.LabelFilter,
                        PreferredDateTime = loadOption.PreferredDateTime
                    };

                    ConfigureRequestTracingOptions(queryKeyValueCollectionOptions);
                    _client.GetKeyValues(queryKeyValueCollectionOptions).ForEach(kv => { data[kv.Key] = kv; });

                    // Block current thread for the initial load of key-values registered for refresh that are not already loaded
                    LoadKeyValuesRegisteredForRefresh(data).ConfigureAwait(false).GetAwaiter().GetResult();
                }
            }
            catch (Exception exception) when (exception.InnerException is HttpRequestException ||
                                              exception.InnerException is OperationCanceledException ||
                                              exception.InnerException is UnauthorizedAccessException)
            {
                if (_options.OfflineCache != null)
                {
                    data = JsonConvert.DeserializeObject<IDictionary<string, IKeyValue>>(_options.OfflineCache.Import(_options), new KeyValueConverter());

                    if (data != null)
                    {
                        SetData(data);
                        return;
                    }
                }

                if (!_optional)
                {
                    throw;
                }

                return;
            }

            SetData(data);

            if (_options.OfflineCache != null)
            {
                _options.OfflineCache.Export(_options, JsonConvert.SerializeObject(data));
            }
        }

        private async Task LoadKeyValuesRegisteredForRefresh(IDictionary<string, IKeyValue> data)
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

                var options = new QueryKeyValueOptions { Label = watchedLabel };
                ConfigureRequestTracingOptions(options);

                // Send a request to retrieve key-value since it may be either not loaded or loaded with a different label
                IKeyValue watchedKv = await _client.GetKeyValue(watchedKey, options, CancellationToken.None).ConfigureAwait(false) ?? new KeyValue(watchedKey) { Label = watchedLabel };
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
                    IKeyValue watchedKv = null;

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

                        var options = new QueryKeyValueOptions { Label = watchedLabel };
                        ConfigureRequestTracingOptions(options);

                        // Send a request to retrieve key-value since it may be either not loaded or loaded with a different label
                        watchedKv = await _client.GetKeyValue(watchedKey, options, CancellationToken.None).ConfigureAwait(false) ?? new KeyValue(watchedKey) { Label = watchedLabel };
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
                            SetData(_settings);
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
                LoadAll();
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
                    IEnumerable<IKeyValue> currentKeyValues = _settings.Values.Where(kv =>
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
                        SetData(_settings);
                    }
                }
                finally
                {
                    changeWatcher.Semaphore.Release();
                }
            }
        }

        private void SetData(IDictionary<string, IKeyValue> data)
        {
            // Update cache of settings
            this._settings = data as ConcurrentDictionary<string, IKeyValue> ?? 
                new ConcurrentDictionary<string, IKeyValue>(data, StringComparer.OrdinalIgnoreCase);

            // Set the application data for the configuration provider
            var applicationData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, IKeyValue> kvp in data)
            {
                foreach (KeyValuePair<string, string> kv in ProcessAdapters(kvp.Value))
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
        
        private IEnumerable<KeyValuePair<string, string>> ProcessAdapters(IKeyValue keyValue)
        {
            List<KeyValuePair<string, string>> keyValues = null;

            foreach (IKeyValueAdapter adapter in _options.Adapters)
            {
                IEnumerable<KeyValuePair<string, string>> kvs = adapter.GetKeyValues(keyValue);

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
                    _settings.TryRemove(change.Key, out IKeyValue removed);
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
