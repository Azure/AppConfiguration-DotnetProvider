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
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
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
        private readonly IConfigurationClientManager _configClientManager;
        private AzureAppConfigurationOptions _options;
        private Dictionary<string, ConfigurationSetting> _serverData;
        private Dictionary<string, ConfigurationSetting> _mappedData;
        private Dictionary<KeyValueIdentifier, ConfigurationSetting> _watchedSettings = new Dictionary<KeyValueIdentifier, ConfigurationSetting>();
        private RequestTracingOptions _requestTracingOptions;

        private readonly TimeSpan MinCacheExpirationInterval;

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

        public AzureAppConfigurationProvider(IConfigurationClientManager clientManager, AzureAppConfigurationOptions options, bool optional)
        {
            _configClientManager = clientManager ?? throw new ArgumentNullException(nameof(clientManager));
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

            var loadStartTime = DateTimeOffset.UtcNow;

            // Guaranteed to have atleast one available client since it is a application startup path.
            IEnumerable<ConfigurationClient> availableClients = _configClientManager.GetAvailableClients(loadStartTime);

            try
            {
                // Load() is invoked only once during application startup. We don't need to check for concurrent network
                // operations here because there can't be any other startup or refresh operation in progress at this time.
                InitializeAsync(_optional, availableClients, CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
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
                    var utcNow = DateTimeOffset.UtcNow;
                    IEnumerable<KeyValueWatcher> cacheExpiredWatchers = _options.ChangeWatchers.Where(changeWatcher => utcNow >= changeWatcher.CacheExpires);
                    IEnumerable<KeyValueWatcher> cacheExpiredMultiKeyWatchers = _options.MultiKeyWatchers.Where(changeWatcher => utcNow >= changeWatcher.CacheExpires);

                    // Skip refresh if serverData is loaded, but none of the watchers or adapters cache is expired.
                    if (_serverData != null &&
                        !cacheExpiredWatchers.Any() &&
                        !cacheExpiredMultiKeyWatchers.Any() &&
                        !_options.Adapters.Any(adapter => adapter.NeedsRefresh()))
                    {
                        return;
                    }

                    IEnumerable<ConfigurationClient> availableClients = _configClientManager.GetAvailableClients(utcNow);
                    if (!availableClients.Any())
                    {
                        return;
                    }

                    // Check if initial configuration load had failed
                    if (_serverData == null)
                    {
                        if (InitializationCacheExpires < utcNow)
                        {
                            InitializationCacheExpires = utcNow.Add(MinCacheExpirationInterval);
                            await InitializeAsync(ignoreFailures: false, availableClients, cancellationToken).ConfigureAwait(false);
                        }

                        return;
                    }

                    //
                    // Avoid instance state modification
                    Dictionary<string, ConfigurationSetting> serverData = null;
                    Dictionary<KeyValueWatcher, KeyValueChange> keyValueChanges = null;
                    List<KeyValueChange> changedKeyValuesCollection = null;
                    Dictionary<string, ConfigurationSetting> mappedData = new Dictionary<string, ConfigurationSetting>(_mappedData);
                    bool refreshAll = false;

                    await ExecuteWithFailOverPolicyAsync(availableClients, async (client) =>
                        {
                            serverData = null;
                            keyValueChanges = new Dictionary<KeyValueWatcher, KeyValueChange>();
                            changedKeyValuesCollection = null;
                            refreshAll = false;

                            foreach (KeyValueWatcher changeWatcher in cacheExpiredWatchers)
                            {
                                string watchedKey = changeWatcher.Key;
                                string watchedLabel = changeWatcher.Label;

                                KeyValueIdentifier watchedKeyLabel = new KeyValueIdentifier(watchedKey, watchedLabel);

                                KeyValueChange change = default;

                                //
                                // Find if there is a change associated with watcher
                                if (_watchedSettings.TryGetValue(watchedKeyLabel, out ConfigurationSetting watchedKv))
                                {
                                    await TracingUtils.CallWithRequestTracing(_requestTracingEnabled, RequestType.Watch, _requestTracingOptions,
                                        async () => change = await client.GetKeyValueChange(watchedKv, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
                                }
                                else
                                {
                                    // Load the key-value in case the previous load attempts had failed

                                    try
                                    {
                                        await CallWithRequestTracing(
                                            async () => watchedKv = await client.GetConfigurationSettingAsync(watchedKey, watchedLabel, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
                                    }
                                    catch (RequestFailedException e) when (e.Status == (int)HttpStatusCode.NotFound)
                                    {
                                        watchedKv = null;
                                    }

                                    if (watchedKv != null)
                                    {
                                        change = new KeyValueChange()
                                        {
                                            Key = watchedKv.Key,
                                            Label = watchedKv.Label.NormalizeNull(),
                                            Current = watchedKv,
                                            ChangeType = KeyValueChangeType.Modified
                                        };
                                    }
                                }

                                // Check if a change has been detected in the key-value registered for refresh
                                if (change.ChangeType != KeyValueChangeType.None)
                                {
                                    keyValueChanges[changeWatcher] = change;

                                    if (changeWatcher.RefreshAll)
                                    {
                                        refreshAll = true;
                                        break;
                                    }
                                }
                            }

                            if (refreshAll)
                            {
                                // Trigger a single load-all operation if a change was detected in one or more key-values with refreshAll: true
                                // Preserve reference to loaded data with deep copy into serverData before calling mapper functions
                                Dictionary<string, ConfigurationSetting> data = await LoadAll(client, cancellationToken).ConfigureAwait(false);
                                serverData = data.ToDictionary(kvp => kvp.Key, kvp => new ConfigurationSetting(kvp.Value.Key, kvp.Value.Value, kvp.Value.Label, kvp.Value.ETag));
                                mappedData = await MapConfigurationData(data).ConfigureAwait(false);
                                return;
                            }

                            changedKeyValuesCollection = await GetRefreshedKeyValueCollections(cacheExpiredMultiKeyWatchers, client, cancellationToken).ConfigureAwait(false);
                        },
                        cancellationToken)
                        .ConfigureAwait(false);

                    if (!refreshAll)
                    {
                        serverData = new Dictionary<string, ConfigurationSetting>(_serverData, StringComparer.OrdinalIgnoreCase);

                        foreach (KeyValueChange change in keyValueChanges.Values.Concat(changedKeyValuesCollection))
                        {
                            if (change.ChangeType == KeyValueChangeType.Deleted)
                            {
                                serverData.Remove(change.Key);
                                mappedData.Remove(change.Key);
                            }
                            else if (change.ChangeType == KeyValueChangeType.Modified)
                            {
                                ConfigurationSetting setting = change.Current;
                                ConfigurationSetting settingCopy = new ConfigurationSetting(setting.Key, setting.Value, setting.Label, setting.ETag);
                                foreach (Func<ConfigurationSetting, ValueTask<ConfigurationSetting>> func in _options.UserDefinedMappers)
                                {
                                    setting = await func(setting).ConfigureAwait(false);
                                }
                                mappedData[change.Key] = setting;
                                serverData[change.Key] = settingCopy;
                            }

                            // Invalidate the cached Key Vault secret (if any) for this ConfigurationSetting
                            foreach (IKeyValueAdapter adapter in _options.Adapters)
                            {
                                adapter.InvalidateCache(change.Current);
                            }
                        }
                    }
                    else
                    {
                        // Invalidate all the cached KeyVault secrets
                        foreach (IKeyValueAdapter adapter in _options.Adapters)
                        {
                            adapter.InvalidateCache();
                        }

                        // Update the cache expiration time for all refresh registered settings and feature flags
                        foreach (KeyValueWatcher changeWatcher in _options.ChangeWatchers.Concat(_options.MultiKeyWatchers))
                        {
                            UpdateCacheExpirationTime(changeWatcher);
                        }
                    }

                    if (_options.Adapters.Any(adapter => adapter.NeedsRefresh()) || changedKeyValuesCollection?.Any() == true || keyValueChanges.Any())
                    {
                        _serverData = serverData;

                        foreach (KeyValuePair<KeyValueWatcher, KeyValueChange> kvp in keyValueChanges)
                        {
                            KeyValueChange keyValueChange = kvp.Value;
                            KeyValueWatcher changeWatcher = kvp.Key;
                            KeyValueIdentifier kvIdentifier = new KeyValueIdentifier(changeWatcher.Key, changeWatcher.Label);

                            if (keyValueChange.ChangeType == KeyValueChangeType.Modified)
                            {
                                _watchedSettings[kvIdentifier] = keyValueChange.Current;
                            }
                            else if (keyValueChange.ChangeType == KeyValueChangeType.Deleted)
                            {
                                _watchedSettings.Remove(kvIdentifier);
                            }

                            // Already updated cache expiration time if refreshAll is true.
                            if (!refreshAll)
                            {
                                UpdateCacheExpirationTime(kvp.Key);
                            }
                        }

                        // PrepareData makes calls to KeyVault and may throw exceptions. But, we still update watchers before
                        // SetData because repeating appconfig calls (by not updating watchers) won't help anything for keyvault calls.
                        // As long as adapter.NeedsRefresh is true, we will attempt to update keyvault again the next time RefreshAsync is called.
                        SetData(await PrepareData(mappedData, cancellationToken).ConfigureAwait(false));
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _networkOperationsInProgress, 0);
                }
            }
        }

        private async Task<Dictionary<string, string>> PrepareData(Dictionary<string, ConfigurationSetting> data, CancellationToken cancellationToken = default)
        {
            var applicationData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Reset old filter telemetry in order to track the filter types present in the current response from server.
            _options.FeatureFilterTelemetry.ResetFeatureFilterTelemetry();

            foreach (KeyValuePair<string, ConfigurationSetting> kvp in data)
            {
                IEnumerable<KeyValuePair<string, string>> keyValuePairs = null;
                keyValuePairs = await ProcessAdapters(kvp.Value, cancellationToken).ConfigureAwait(false);

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

            return applicationData;
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

            if (_configClientManager.UpdateSyncToken(pushNotification.ResourceUri, pushNotification.SyncToken))
            {
                SetDirty(maxDelay);
            }
            else
            {
                _logger.LogWarning($"Ignoring the push notification received for the unregistered endpoint '{pushNotification.ResourceUri}'");
            }
        }

        private async Task InitializeAsync(bool ignoreFailures, IEnumerable<ConfigurationClient> availableClients, CancellationToken cancellationToken = default)
        {
            Dictionary<string, ConfigurationSetting> data = null;
            
            try
            {
                data = await ExecuteWithFailOverPolicyAsync(availableClients, (client) => LoadAll(client, cancellationToken), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (ignoreFailures &&
                                             (exception is RequestFailedException ||
                                             ((exception as AggregateException)?.InnerExceptions?.All(e => e is RequestFailedException) ?? false) ||
                                             exception is OperationCanceledException))
            { }

            // Update the cache expiration time for all refresh registered settings and feature flags
            foreach (KeyValueWatcher changeWatcher in _options.ChangeWatchers.Concat(_options.MultiKeyWatchers))
            {
                UpdateCacheExpirationTime(changeWatcher);
            }

            if (data != null)
            {
                // Invalidate all the cached KeyVault secrets
                foreach (IKeyValueAdapter adapter in _options.Adapters)
                {
                    adapter.InvalidateCache();
                }

<<<<<<< HEAD

                try
                {
                    _serverData = data.ToDictionary(kvp => kvp.Key, kvp => new ConfigurationSetting(kvp.Value.Key, kvp.Value.Value, kvp.Value.Label, kvp.Value.ETag));
                    Dictionary<string, ConfigurationSetting> mappedData = await MapConfigurationData(data).ConfigureAwait(false);
=======
                try
                {
                    _applicationSettings = data.ToDictionary(kvp => kvp.Key, kvp => new ConfigurationSetting(kvp.Value.Key, kvp.Value.Value, kvp.Value.Label, kvp.Value.ETag));
                    Dictionary<string, ConfigurationSetting> mappedData = await MapConfigurationData(data);
>>>>>>> 9f50c356208526e24cc5987c0564a3cf04cb9eb0
                    _mappedData = mappedData;
                    SetData(await PrepareData(mappedData, cancellationToken).ConfigureAwait(false));
                }
                catch (KeyVaultReferenceException) when (ignoreFailures)
                {
                    // ignore failures
                }
            }
        }

        private async Task<Dictionary<string, ConfigurationSetting>> LoadAll(ConfigurationClient client, CancellationToken cancellationToken)
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

            // Load key-values registered for refresh that are not already loaded
            await LoadKeyValuesRegisteredForRefresh(client, serverData, cancellationToken).ConfigureAwait(false);
            return serverData;
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

        private async Task<List<KeyValueChange>> GetRefreshedKeyValueCollections(IEnumerable<KeyValueWatcher> multiKeyWatchers, ConfigurationClient client, CancellationToken cancellationToken)
        {
            var keyValueChanges = new List<KeyValueChange>();

            foreach (KeyValueWatcher changeWatcher in multiKeyWatchers)
            {
                IEnumerable<ConfigurationSetting> currentKeyValues;

                if (changeWatcher.Key.EndsWith("*"))
                {
                    // Get current application settings starting with changeWatcher.Key, excluding the last * character
                    var keyPrefix = changeWatcher.Key.Substring(0, changeWatcher.Key.Length - 1);
                    currentKeyValues = _serverData.Values.Where(kv =>
                    {
                        return kv.Key.StartsWith(keyPrefix) && kv.Label == changeWatcher.Label.NormalizeNull();
                    });
                }
                else
                {
                    currentKeyValues = _serverData.Values.Where(kv =>
                    {
                        return kv.Key.Equals(changeWatcher.Key) && kv.Label == changeWatcher.Label.NormalizeNull();
                    });
                }

                keyValueChanges.AddRange(
                    await client.GetKeyValueChangeCollection(
                        currentKeyValues,
                        new GetKeyValueChangeCollectionOptions
                        {
                            KeyFilter = changeWatcher.Key,
                            Label = changeWatcher.Label.NormalizeNull(),
                            RequestTracingEnabled = _requestTracingEnabled,
                            RequestTracingOptions = _requestTracingOptions
                        },
                        cancellationToken)
                    .ConfigureAwait(false));

            }

            return keyValueChanges;
        }

        private void SetData(IDictionary<string, string> data)
        {
            // Set the application data for the configuration provider
            Data = data;

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
                FeatureManagementSchemaVersion = _options.FeatureManagementSchemaVersion,
                ReplicaCount = _options.Endpoints?.Count() - 1 ?? 0,
                FilterTelemetry = _options.FeatureFilterTelemetry
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

        private void UpdateCacheExpirationTime(KeyValueWatcher changeWatcher)
        {
            TimeSpan cacheExpirationTime = changeWatcher.CacheExpirationInterval;
            changeWatcher.CacheExpires = DateTimeOffset.UtcNow.Add(cacheExpirationTime);
        }

        private async Task<T> ExecuteWithFailOverPolicyAsync<T>(IEnumerable<ConfigurationClient> clients, Func<ConfigurationClient, Task<T>> funcToExecute, CancellationToken cancellationToken = default)
        {
            using IEnumerator<ConfigurationClient> clientEnumerator = clients.GetEnumerator();

            clientEnumerator.MoveNext();

            ConfigurationClient currentClient;

            while (true)
            {
                bool success = false;
                bool backoffAllClients = false;

                cancellationToken.ThrowIfCancellationRequested();
                currentClient = clientEnumerator.Current;

                try
                {
                    T result = await funcToExecute(currentClient).ConfigureAwait(false);
                    success = true;

                    return result;
                }
                catch (AggregateException ae)
                {
                    if (!IsFailOverable(ae) || !clientEnumerator.MoveNext())
                    {
                        backoffAllClients = true;

                        throw;
                    }
                }
                catch (RequestFailedException rfe)
                {
                    if (!IsFailOverable(rfe) || !clientEnumerator.MoveNext())
                    {
                        backoffAllClients = true;

                        throw;
                    }
                }
                finally
                {
                    if (!success && backoffAllClients)
                    {
                        do
                        {
                            _configClientManager.UpdateClientStatus(currentClient, success);
                            clientEnumerator.MoveNext();
                            currentClient = clientEnumerator.Current;
                        }
                        while (currentClient != null);
                    }
                    else
                    {
                        _configClientManager.UpdateClientStatus(currentClient, success);
                    }
                }
            }
        }

        private async Task ExecuteWithFailOverPolicyAsync(IEnumerable<ConfigurationClient> clients, Func<ConfigurationClient, Task> funcToExecute, CancellationToken cancellationToken = default)
        {
            await ExecuteWithFailOverPolicyAsync<object>(clients, async (client) =>
            {
                await funcToExecute(client);
                return null;

            }, cancellationToken);
        }

        private bool IsFailOverable(AggregateException ex)
        {
            IReadOnlyCollection<Exception> innerExceptions = ex.InnerExceptions;

            if (innerExceptions != null && innerExceptions.Any() && innerExceptions.All(ex => ex is RequestFailedException))
            {
                return IsFailOverable((RequestFailedException)innerExceptions.Last());
            }

            return false;
        }

        private bool IsFailOverable(RequestFailedException rfe)
        {

            // The InnerException could be SocketException or WebException when endpoint is invalid and IOException if it is network issue.
            if (rfe.InnerException != null && rfe.InnerException is HttpRequestException hre && hre.InnerException != null)
            {
                return hre.InnerException is WebException ||
                       hre.InnerException is SocketException ||
                       hre.InnerException is IOException;
            }

            return rfe.Status == HttpStatusCodes.TooManyRequests ||
                   rfe.Status == (int)HttpStatusCode.RequestTimeout ||
                   rfe.Status >= (int)HttpStatusCode.InternalServerError;
        }

        private async Task<Dictionary<string, ConfigurationSetting>> MapConfigurationData(Dictionary<string, ConfigurationSetting> data)
        {
            Dictionary<string, ConfigurationSetting> mappedData = new Dictionary<string, ConfigurationSetting>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, ConfigurationSetting> kvp in data)
            {
                ConfigurationSetting setting = kvp.Value;
                foreach (Func<ConfigurationSetting, ValueTask<ConfigurationSetting>> func in _options.UserDefinedMappers)
                {
                    setting = await func(setting).ConfigureAwait(false);
                }
                mappedData[kvp.Key] = setting;
            }

            return mappedData;
        }
    }
}
