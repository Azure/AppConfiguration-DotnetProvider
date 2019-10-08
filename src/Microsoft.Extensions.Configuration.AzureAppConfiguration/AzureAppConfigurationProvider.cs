using Azure;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Constants;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
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

        private const int MaxRetries = 12;
        private const int RetryWaitMinutes = 1;

        private readonly HostType _hostType;
        private readonly ConfigurationClient _client;
        private AzureAppConfigurationOptions _options;
        private ConcurrentDictionary<string, ConfigurationSetting> _settings;

        private static readonly TimeSpan MinDelayForUnhandledFailure = TimeSpan.FromSeconds(5);

        public AzureAppConfigurationProvider(ConfigurationClient client, AzureAppConfigurationOptions options, bool optional)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _optional = optional;

            string requestTracingDisabled = null;
            try
            {
                requestTracingDisabled = Environment.GetEnvironmentVariable(RequestTracingConstants.RequestTracingDisabledEnvironmentVariable);
                _hostType = Environment.GetEnvironmentVariable(RequestTracingConstants.AzureFunctionEnvironmentVariable) != null
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
            var watch = Stopwatch.StartNew();
            var refresher = (AzureAppConfigurationRefresher)_options.GetRefresher();
            refresher.SetProvider(this);

            try
            {
                LoadAll().ConfigureAwait(false).GetAwaiter().GetResult();
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

                var waitTime = MinDelayForUnhandledFailure.Subtract(watch.Elapsed);

                if (waitTime.Ticks > 0)
                {
                    Task.Delay(waitTime).ConfigureAwait(false).GetAwaiter().GetResult();
                }

                // Re-throw the exception after the additional delay (if required)
                throw;
            }

            // Mark all settings have loaded at startup.
            _isInitialLoadComplete = true;
        }

        public async Task Refresh()
        {
            await RefreshIndividualKeyValues().ConfigureAwait(false);
            await RefreshKeyValueCollections().ConfigureAwait(false);
        }

        internal static ConfigurationClientOptions GetClientOptions()
        {
            ConfigurationClientOptions clientOptions = new ConfigurationClientOptions(ConfigurationClientOptions.ServiceVersion.V1_0);
            clientOptions.Retry.MaxRetries = MaxRetries;
            clientOptions.Retry.MaxDelay = TimeSpan.FromMinutes(RetryWaitMinutes);

            clientOptions.Diagnostics.ApplicationId = TracingUtils.GenerateUserAgent();

            return clientOptions;
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
                    // Load all key-values with the null label.
                    var selector = new SettingSelector(KeyFilter.Any, LabelFilter.Null);

                    await CallWithRequestTracing(async () =>
                    {
                        await foreach (ConfigurationSetting setting in _client.GetSettingsAsync(selector))
                        {
                            data[setting.Key] = setting;
                        }
                    }).ConfigureAwait(false);
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
                        // Or would select kvs obtained by a different selector
                        // We skip it to prevent unnecessary requests
                        continue;
                    }

                    var selector = new SettingSelector(loadOption.KeyFilter, loadOption.LabelFilter)
                    {
                        AsOf = loadOption.PreferredDateTime
                    };

                    // Load all key-values with the null label.
                    await CallWithRequestTracing(async () =>
                    {
                        await foreach (ConfigurationSetting setting in _client.GetSettingsAsync(selector))
                        {
                            data[setting.Key] = setting;
                        }
                    }).ConfigureAwait(false);

                    // Block current thread for the initial load of key-values registered for refresh that are not already loaded
                    await Task.Run(() => LoadKeyValuesRegisteredForRefresh(data).ConfigureAwait(false).GetAwaiter().GetResult());
                }
            }
            catch (Exception exception) when (// TODO: exception is Azure.RequestFailedException ||
                                              exception.InnerException is HttpRequestException ||
                                              exception.InnerException is OperationCanceledException ||
                                              exception.InnerException is UnauthorizedAccessException)
            {
                if (_options.OfflineCache != null)
                {
                    data = JsonConvert.DeserializeObject<IDictionary<string, ConfigurationSetting>>(_options.OfflineCache.Import(_options), new KeyValueConverter());

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
                _options.OfflineCache.Export(_options, JsonConvert.SerializeObject(data));
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
                    continue;
                }

                // Send a request to retrieve key-value since it may be either not loaded or loaded with a different label
                ConfigurationSetting watchedKv = null;
                try
                {
                    await CallWithRequestTracing(async () => watchedKv = await _client.GetAsync(watchedKey, watchedLabel, CancellationToken.None)).ConfigureAwait(false);
                }
                catch (RequestFailedException e) when (e.Status == (int)HttpStatusCode.NotFound)
                {
                    watchedKv = new ConfigurationSetting(watchedKey, null) { Label = watchedLabel };
                }

                changeWatcher.LastRefreshTime = DateTimeOffset.UtcNow;

                // If the key-value was found, store it for updating the settings
                if (watchedKv != null)
                {
                    data[watchedKey] = watchedKv;
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

                        KeyValueChange keyValueChange = default;
                        await TracingUtils.CallWithRequestTracing(_requestTracingEnabled, RequestType.Watch, _hostType,
                            async () => keyValueChange = await _client.GetKeyValueChange(watchedKv, CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);

                        changeWatcher.LastRefreshTime = DateTimeOffset.UtcNow;

                        // Check if a change has been detected in the key-value registered for refresh
                        if (keyValueChange.ChangeType != KeyValueChangeType.None)
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

                        try
                        {
                            await CallWithRequestTracing(async () => watchedKv = await _client.GetAsync(watchedKey, watchedLabel, CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);
                        }
                        catch (RequestFailedException e) when (e.Status == 404)
                        {
                            watchedKv = new ConfigurationSetting(watchedKey, null, watchedLabel);
                        }

                        changeWatcher.LastRefreshTime = DateTimeOffset.UtcNow;

                        if (watchedKv != null)
                        {
                            // Add the key-value if it is not loaded, or update it if it was loaded with a different label
                            _settings[watchedKey] = watchedKv;
                            hasChanged = true;
                        }
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

        private async Task SetData(IDictionary<string, ConfigurationSetting> data, CancellationToken cancellationToken = default(CancellationToken))
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
    }
}
