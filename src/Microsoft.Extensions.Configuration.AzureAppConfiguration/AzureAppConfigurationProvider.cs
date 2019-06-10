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

    class AzureAppConfigurationProvider : ConfigurationProvider
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
        private IDictionary<string, DateTimeOffset> _changeWatcherTimeMap;
        private IDictionary<string, DateTimeOffset> _multiKeyWatcherTimeMap;

        public AzureAppConfigurationProvider(AzconfigClient client, AzureAppConfigurationOptions options, bool optional)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _optional = optional;

            // Initialize retry options.
            _client.RetryOptions.MaxRetries = MaxRetries;
            _client.RetryOptions.MaxRetryWaitTime = TimeSpan.FromMinutes(RetryWaitMinutes);

            _changeWatcherTimeMap = new Dictionary<string, DateTimeOffset>();
            _multiKeyWatcherTimeMap = new Dictionary<string, DateTimeOffset>();

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

        public override void Load()
        {
            var refresher = (AzureAppConfigurationRefresher)_options.GetRefresher();
            refresher.SetProvider(this);

            LoadAll();

            // Mark all settings have loaded at startup.
            _isInitialLoadComplete = true;
        }

        internal async Task RefreshKeyValues()
        {
            await RefreshKeyValuesWithSpecificKey();
            await RefreshKeyValuesWithKeyPrefix();
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
                    LoadKeyValuesRegisteredForRefresh(data).Wait();
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

                // Update the last refresh time since we plan to refresh the key-value with the server
                _changeWatcherTimeMap[watchedKey] = DateTimeOffset.UtcNow;

                var options = new QueryKeyValueOptions { Label = watchedLabel };
                ConfigureRequestTracingOptions(options);

                // Send a request to retrieve key-value since it may be either not loaded or loaded with a different label
                var watchedKv = await _client.GetKeyValue(watchedKey, options, CancellationToken.None) ?? new KeyValue(watchedKey) { Label = watchedLabel };
                data[watchedKey] = watchedKv;
            }
        }

        private async Task RefreshKeyValuesWithSpecificKey()
        {
            bool shouldRefreshAll = false;

            foreach (KeyValueWatcher changeWatcher in _options.ChangeWatchers)
            {
                string watchedKey = changeWatcher.Key;
                string watchedLabel = changeWatcher.Label;
                bool hasLastRefreshTime = _changeWatcherTimeMap.TryGetValue(watchedKey, out var lastRefreshTime);

                // If the cache for the key hasn't expired, skip the refresh for this key
                if (hasLastRefreshTime && DateTimeOffset.UtcNow - lastRefreshTime < changeWatcher.CacheExpirationTime)
                {
                    continue;
                }

                // Update the last refresh time since we plan to refresh the key-value with the server
                _changeWatcherTimeMap[watchedKey] = DateTimeOffset.UtcNow;

                bool hasChanged = false;
                IKeyValue watchedKv = null;

                if (_settings.ContainsKey(watchedKey) && _settings[watchedKey].Label == watchedLabel.NormalizeNull())
                {
                    watchedKv = _settings[watchedKey];
                    var keyValueChange = await _client.GetKeyValueChange(watchedKv, CancellationToken.None, _requestTracingEnabled, _hostType);

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
                    watchedKv = await _client.GetKeyValue(watchedKey, options, CancellationToken.None) ?? new KeyValue(watchedKey) { Label = watchedLabel };

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

            // Trigger a single refresh-all operation if a change was detected in one or more key-values with refreshAll: true
            if (shouldRefreshAll)
            {
                LoadAll();
            }
        }

        private async Task RefreshKeyValuesWithKeyPrefix()
        {
            foreach (KeyValueWatcher changeWatcher in _options.MultiKeyWatchers)
            {
                bool hasLastRefreshTime = _multiKeyWatcherTimeMap.TryGetValue(changeWatcher.Key, out var lastRefreshTime);

                // If the cache for the key-prefix hasn't expired, skip the refresh for this key-prefix
                if (hasLastRefreshTime && DateTimeOffset.UtcNow - lastRefreshTime < changeWatcher.CacheExpirationTime)
                {
                    continue;
                }

                // If we reach here, update the last refresh time since we plan to refresh the key-values with the server
                _multiKeyWatcherTimeMap[changeWatcher.Key] = DateTimeOffset.UtcNow;

                IEnumerable<IKeyValue> currentKeyValues = _settings.Values.Where(kv =>
                {
                    return kv.Key.StartsWith(changeWatcher.Key) && kv.Label == changeWatcher.Label.NormalizeNull();
                });

                var keyValueChanges = await _client.GetKeyValueChangeCollection(new GetKeyValueChangeCollectionOptions
                {
                    Prefix = changeWatcher.Key,
                    Label = changeWatcher.Label.NormalizeNull()
                }, currentKeyValues, _requestTracingEnabled, _hostType);

                if (keyValueChanges?.Any() == true)
                {
                    ProcessChanges(keyValueChanges);
                    SetData(_settings);
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
            options.ConfigureRequestTracingOptions(_requestTracingEnabled, _isInitialLoadComplete, _hostType);
        }
    }
}
