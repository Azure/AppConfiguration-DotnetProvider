﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
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
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class AzureAppConfigurationProvider : ConfigurationProvider, IConfigurationRefresher, IDisposable
    {
        private bool _optional;
        private bool _isInitialLoadComplete = false;
        private bool _isAssemblyInspected;
        private readonly bool _requestTracingEnabled;
        private readonly IConfigurationClientManager _configClientManager;
        private Uri _lastSuccessfulEndpoint;
        private AzureAppConfigurationOptions _options;
        private Dictionary<string, ConfigurationSetting> _mappedData;
        private Dictionary<KeyValueIdentifier, ConfigurationSetting> _watchedIndividualKvs = new Dictionary<KeyValueIdentifier, ConfigurationSetting>();
        private Dictionary<KeyValueSelector, IEnumerable<MatchConditions>> _watchedKvEtags = new Dictionary<KeyValueSelector, IEnumerable<MatchConditions>>();
        private Dictionary<KeyValueSelector, IEnumerable<MatchConditions>> _watchedFfEtags = new Dictionary<KeyValueSelector, IEnumerable<MatchConditions>>();
        private RequestTracingOptions _requestTracingOptions;
        private Dictionary<Uri, ConfigurationClientBackoffStatus> _configClientBackoffs = new Dictionary<Uri, ConfigurationClientBackoffStatus>();
        private DateTimeOffset _registerAllNextRefreshTime;

        private readonly TimeSpan MinRefreshInterval;

        // The most-recent time when the refresh operation attempted to load the initial configuration
        private DateTimeOffset InitializationCacheExpires = default;

        private static readonly TimeSpan MinDelayForUnhandledFailure = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan DefaultMaxSetDirtyDelay = TimeSpan.FromSeconds(30);

        // To avoid concurrent network operations, this flag is used to achieve synchronization between multiple threads.
        private int _networkOperationsInProgress = 0;
        private Logger _logger = new Logger();
        private ILoggerFactory _loggerFactory;

        private class ConfigurationClientBackoffStatus
        {
            public int FailedAttempts { get; set; }
            public DateTimeOffset BackoffEndTime { get; set; }
        }

        public Uri AppConfigurationEndpoint
        {
            get
            {
                if (_options.Endpoints != null)
                {
                    return _options.Endpoints.First();
                }

                if (_options.ConnectionStrings != null && _options.ConnectionStrings.Any() && _options.ConnectionStrings.First() != null)
                {
                    // Use try-catch block to avoid throwing exceptions from property getter.
                    // https://docs.microsoft.com/en-us/dotnet/standard/design-guidelines/property

                    try
                    {
                        return new Uri(ConnectionStringUtils.Parse(_options.ConnectionStrings.First(), ConnectionStringUtils.EndpointSection));
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

                if (_loggerFactory != null)
                {
                    _logger = new Logger(_loggerFactory.CreateLogger(LoggingConstants.AppConfigRefreshLogCategory));

                    if (_configClientManager is ConfigurationClientManager clientManager)
                    {
                        clientManager.SetLogger(_logger);
                    }
                }
            }
        }

        public AzureAppConfigurationProvider(IConfigurationClientManager configClientManager, AzureAppConfigurationOptions options, bool optional)
        {
            _configClientManager = configClientManager ?? throw new ArgumentNullException(nameof(configClientManager));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _optional = optional;

            IEnumerable<KeyValueWatcher> watchers = options.IndividualKvWatchers.Union(options.FeatureFlagWatchers);

            bool hasWatchers = watchers.Any();
            TimeSpan minWatcherRefreshInterval = hasWatchers ? watchers.Min(w => w.RefreshInterval) : TimeSpan.MaxValue;

            if (options.RegisterAllEnabled)
            {
                MinRefreshInterval = TimeSpan.FromTicks(Math.Min(minWatcherRefreshInterval.Ticks, options.KvCollectionRefreshInterval.Ticks));
            }
            else
            {
                MinRefreshInterval = hasWatchers ? minWatcherRefreshInterval : RefreshConstants.DefaultRefreshInterval;
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
                using var startupCancellationTokenSource = new CancellationTokenSource(_options.Startup.Timeout);

                // Load() is invoked only once during application startup. We don't need to check for concurrent network
                // operations here because there can't be any other startup or refresh operation in progress at this time.
                LoadAsync(_optional, startupCancellationTokenSource.Token).ConfigureAwait(false).GetAwaiter().GetResult();
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
                    // FeatureManagement assemblies may not be loaded on provider startup, so version information is gathered upon first refresh for tracing
                    EnsureAssemblyInspected();

                    var utcNow = DateTimeOffset.UtcNow;
                    IEnumerable<KeyValueWatcher> refreshableIndividualKvWatchers = _options.IndividualKvWatchers.Where(kvWatcher => utcNow >= kvWatcher.NextRefreshTime);
                    IEnumerable<KeyValueWatcher> refreshableFfWatchers = _options.FeatureFlagWatchers.Where(ffWatcher => utcNow >= ffWatcher.NextRefreshTime);
                    bool registerAllIsRefreshable = utcNow >= _registerAllNextRefreshTime;

                    // Skip refresh if mappedData is loaded, but none of the watchers or adapters are refreshable.
                    if (_mappedData != null &&
                        !refreshableIndividualKvWatchers.Any() &&
                        !refreshableFfWatchers.Any() &&
                        !registerAllIsRefreshable &&
                        !_options.Adapters.Any(adapter => adapter.NeedsRefresh()))
                    {
                        return;
                    }

                    IEnumerable<ConfigurationClient> clients = _configClientManager.GetClients();

                    //
                    // Filter clients based on their backoff status
                    clients = clients.Where(client =>
                    {
                        Uri endpoint = _configClientManager.GetEndpointForClient(client);

                        if (!_configClientBackoffs.TryGetValue(endpoint, out ConfigurationClientBackoffStatus clientBackoffStatus))
                        {
                            clientBackoffStatus = new ConfigurationClientBackoffStatus();

                            _configClientBackoffs[endpoint] = clientBackoffStatus;
                        }

                        return clientBackoffStatus.BackoffEndTime <= utcNow;
                    }
                    );

                    if (!clients.Any())
                    {
                        _configClientManager.RefreshClients();

                        _logger.LogDebug(LogHelper.BuildRefreshSkippedNoClientAvailableMessage());

                        return;
                    }

                    // Check if initial configuration load had failed
                    if (_mappedData == null)
                    {
                        if (InitializationCacheExpires < utcNow)
                        {
                            InitializationCacheExpires = utcNow.Add(MinRefreshInterval);

                            await InitializeAsync(clients, cancellationToken).ConfigureAwait(false);
                        }

                        return;
                    }

                    //
                    // Avoid instance state modification
                    Dictionary<KeyValueSelector, IEnumerable<MatchConditions>> watchedFfEtags = null;
                    Dictionary<KeyValueSelector, IEnumerable<MatchConditions>> watchedKvEtags = null;
                    Dictionary<KeyValueIdentifier, ConfigurationSetting> watchedIndividualKvs = null;
                    List<KeyValueChange> keyValueChanges = null;
                    Dictionary<string, ConfigurationSetting> data = null;
                    Dictionary<string, ConfigurationSetting> ffCollectionData = null;
                    bool kvCollectionUpdated = false;
                    bool ffCollectionUpdated = false;
                    bool refreshAll = false;
                    StringBuilder logInfoBuilder = new StringBuilder();
                    StringBuilder logDebugBuilder = new StringBuilder();

                    await ExecuteWithFailOverPolicyAsync(clients, async (client) =>
                        {
                            data = null;
                            ffCollectionData = null;
                            watchedIndividualKvs = null;
                            watchedKvEtags = null;
                            watchedFfEtags = null;
                            keyValueChanges = new List<KeyValueChange>();
                            refreshAll = false;
                            Uri endpoint = _configClientManager.GetEndpointForClient(client);
                            logDebugBuilder.Clear();
                            logInfoBuilder.Clear();

                            if (!_options.RegisterAllEnabled)
                            {
                                refreshAll = await RefreshIndividualKvWatchers(
                                    client,
                                    keyValueChanges,
                                    refreshableIndividualKvWatchers,
                                    endpoint,
                                    logDebugBuilder,
                                    logInfoBuilder,
                                    cancellationToken).ConfigureAwait(false);

                                if (!refreshAll)
                                {
                                    // Get feature flag changes
                                    watchedFfEtags = new Dictionary<KeyValueSelector, IEnumerable<MatchConditions>>(_watchedFfEtags);

                                    ffCollectionData = new Dictionary<string, ConfigurationSetting>();

                                    ffCollectionUpdated = await RefreshFfCollections(
                                        client,
                                        ffCollectionData,
                                        refreshableFfWatchers,
                                        watchedFfEtags,
                                        endpoint,
                                        logDebugBuilder,
                                        logInfoBuilder,
                                        cancellationToken).ConfigureAwait(false);
                                }
                                else
                                {
                                    // Trigger a single load-all operation if a change was detected in one or more key-values with refreshAll: true
                                    ffCollectionData = new Dictionary<string, ConfigurationSetting>();
                                    data = new Dictionary<string, ConfigurationSetting>();

                                    watchedKvEtags = await LoadSelected(client, data, _options.Selectors, cancellationToken).ConfigureAwait(false);
                                    watchedIndividualKvs = await LoadKeyValuesRegisteredForRefresh(client, data, cancellationToken).ConfigureAwait(false);
                                    logInfoBuilder.AppendLine(LogHelper.BuildConfigurationUpdatedMessage());
                                    return;
                                }
                            }
                            else
                            {
                                // Get key value collection changes if RegisterAll was called
                                watchedKvEtags = new Dictionary<KeyValueSelector, IEnumerable<MatchConditions>>(_watchedKvEtags);

                                if (registerAllIsRefreshable)
                                {
                                    kvCollectionData = new Dictionary<string, ConfigurationSetting>();

                                    kvCollectionUpdated = await RefreshKvCollections(
                                        client,
                                        kvCollectionData,
                                        watchedKvEtags,
                                        endpoint,
                                        logDebugBuilder,
                                        logInfoBuilder,
                                        cancellationToken).ConfigureAwait(false);
                                }

                                // Get feature flag changes
                                watchedFfEtags = new Dictionary<KeyValueSelector, IEnumerable<MatchConditions>>(_watchedFfEtags);

                                ffCollectionData = new Dictionary<string, ConfigurationSetting>();

                                ffCollectionUpdated = await RefreshFfCollections(
                                    client,
                                    ffCollectionData,
                                    refreshableFfWatchers,
                                    watchedFfEtags,
                                    endpoint,
                                    logDebugBuilder,
                                    logInfoBuilder,
                                    cancellationToken).ConfigureAwait(false);
                            }
                        },
                        cancellationToken)
                        .ConfigureAwait(false);

                    if (!refreshAll)
                    {
                        watchedIndividualKvs = new Dictionary<KeyValueIdentifier, ConfigurationSetting>(_watchedIndividualKvs);

                        foreach (KeyValueWatcher changeWatcher in refreshableIndividualKvWatchers.Concat(refreshableFfWatchers))
                        {
                            UpdateNextRefreshTime(changeWatcher);
                        }

                        foreach (KeyValueChange change in keyValueChanges)
                        {
                            KeyValueIdentifier changeIdentifier = new KeyValueIdentifier(change.Key, change.Label);

                            if (change.ChangeType == KeyValueChangeType.Modified)
                            {
                                ConfigurationSetting setting = change.Current;
                                ConfigurationSetting settingCopy = new ConfigurationSetting(setting.Key, setting.Value, setting.Label, setting.ETag);

                                watchedIndividualKvs[changeIdentifier] = settingCopy;

                                foreach (Func<ConfigurationSetting, ValueTask<ConfigurationSetting>> func in _options.Mappers)
                                {
                                    setting = await func(setting).ConfigureAwait(false);
                                }

                                if (setting == null)
                                {
                                    _individualKvData.Remove(change.Key);
                                }
                                else
                                {
                                    _individualKvData[change.Key] = setting;
                                }
                            }
                            else if (change.ChangeType == KeyValueChangeType.Deleted)
                            {
                                _individualKvData.Remove(change.Key);

                                watchedIndividualKvs.Remove(changeIdentifier);
                            }

                            // Invalidate the cached Key Vault secret (if any) for this ConfigurationSetting
                            foreach (IKeyValueAdapter adapter in _options.Adapters)
                            {
                                // If the current setting is null, try to pass the previous setting instead
                                if (change.Current != null)
                                {
                                    adapter.OnChangeDetected(change.Current);
                                }
                                else if (change.Previous != null)
                                {
                                    adapter.OnChangeDetected(change.Previous);
                                }
                            }
                        }
                    }

                    if (refreshAll)
                    {
                        _kvCollectionData = await MapConfigurationSettings(kvCollectionData).ConfigureAwait(false);

                        _individualKvData = await MapConfigurationSettings(individualKvData).ConfigureAwait(false);

                        _ffCollectionData = await MapConfigurationSettings(ffCollectionData).ConfigureAwait(false);

                        // Invalidate all the cached KeyVault secrets
                        foreach (IKeyValueAdapter adapter in _options.Adapters)
                        {
                            adapter.OnChangeDetected();
                        }

                        // Update the next refresh time for all refresh registered settings and feature flags
                        foreach (KeyValueWatcher changeWatcher in _options.IndividualKvWatchers.Concat(_options.FeatureFlagWatchers))
                        {
                            UpdateNextRefreshTime(changeWatcher);
                        }
                    }
                    else
                    {
                        if (_options.RegisterAllEnabled)
                        {
                            if (registerAllIsRefreshable)
                            {
                                _registerAllNextRefreshTime = DateTimeOffset.UtcNow.Add(_options.KvCollectionRefreshInterval);
                            }

                            if (kvCollectionUpdated)
                            {
                                _kvCollectionData = await MapConfigurationSettings(kvCollectionData).ConfigureAwait(false);
                            }
                        }

                        if (ffCollectionUpdated)
                        {
                            _ffCollectionData = await MapConfigurationSettings(ffCollectionData).ConfigureAwait(false);
                        }
                    }

                    if (_options.Adapters.Any(adapter => adapter.NeedsRefresh()) || keyValueChanges.Any() || kvCollectionUpdated || ffCollectionUpdated)
                    {
                        _watchedIndividualKvs = watchedIndividualKvs;

                        _watchedFfEtags = watchedFfEtags;

                        _watchedKvEtags = watchedKvEtags;

                        if (logDebugBuilder.Length > 0)
                        {
                            _logger.LogDebug(logDebugBuilder.ToString().Trim());
                        }

                        if (logInfoBuilder.Length > 0)
                        {
                            _logger.LogInformation(logInfoBuilder.ToString().Trim());
                        }

                        Dictionary<string, ConfigurationSetting> combinedData = GetCombinedData(_kvCollectionData, _individualKvData, _ffCollectionData);

                        // PrepareData makes calls to KeyVault and may throw exceptions. But, we still update watchers before
                        // SetData because repeating appconfig calls (by not updating watchers) won't help anything for keyvault calls.
                        // As long as adapter.NeedsRefresh is true, we will attempt to update keyvault again the next time RefreshAsync is called.
                        SetData(await PrepareData(combinedData, cancellationToken).ConfigureAwait(false));
                    }
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
            catch (RequestFailedException rfe)
            {
                if (IsAuthenticationError(rfe))
                {
                    _logger.LogWarning(LogHelper.BuildRefreshFailedDueToAuthenticationErrorMessage(rfe.Message));
                }
                else
                {
                    _logger.LogWarning(LogHelper.BuildRefreshFailedErrorMessage(rfe.Message));
                }

                return false;
            }
            catch (KeyVaultReferenceException kvre)
            {
                _logger.LogWarning(LogHelper.BuildRefreshFailedDueToKeyVaultErrorMessage(kvre.Message));
                return false;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning(LogHelper.BuildRefreshCanceledErrorMessage());
                return false;
            }
            catch (InvalidOperationException e)
            {
                _logger.LogWarning(LogHelper.BuildRefreshFailedErrorMessage(e.Message));
                return false;
            }
            catch (AggregateException ae)
            {
                if (ae.InnerExceptions?.Any(e => e is RequestFailedException) ?? false)
                {
                    if (IsAuthenticationError(ae))
                    {
                        _logger.LogWarning(LogHelper.BuildRefreshFailedDueToAuthenticationErrorMessage(ae.Message));
                    }
                    else
                    {
                        _logger.LogWarning(LogHelper.BuildRefreshFailedErrorMessage(ae.Message));
                    }
                }
                else if (ae.InnerExceptions?.Any(e => e is OperationCanceledException) ?? false)
                {
                    _logger.LogWarning(LogHelper.BuildRefreshCanceledErrorMessage());
                }
                else
                {
                    throw;
                }

                return false;
            }
            catch (FormatException fe)
            {
                _logger.LogWarning(LogHelper.BuildRefreshFailedDueToFormattingErrorMessage(fe.Message));

                return false;
            }

            return true;
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
                _logger.LogWarning(LogHelper.BuildPushNotificationUnregisteredEndpointMessage(pushNotification.ResourceUri.ToString()));
            }
        }

        private void SetDirty(TimeSpan? maxDelay)
        {
            DateTimeOffset nextRefreshTime = AddRandomDelay(DateTimeOffset.UtcNow, maxDelay ?? DefaultMaxSetDirtyDelay);

            if (_options.RegisterAllEnabled)
            {
                _registerAllNextRefreshTime = nextRefreshTime;
            }
            else
            {
                foreach (KeyValueWatcher kvWatcher in _options.IndividualKvWatchers)
                {
                    kvWatcher.NextRefreshTime = nextRefreshTime;
                }
            }

            foreach (KeyValueWatcher featureFlagWatcher in _options.FeatureFlagWatchers)
            {
                featureFlagWatcher.NextRefreshTime = nextRefreshTime;
            }
        }

        private async Task<Dictionary<string, string>> PrepareData(Dictionary<string, ConfigurationSetting> data, CancellationToken cancellationToken = default)
        {
            var applicationData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Reset old feature flag tracing in order to track the information present in the current response from server.
            _options.FeatureFlagTracing.ResetFeatureFlagTracing();

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

        private async Task LoadAsync(bool ignoreFailures, CancellationToken cancellationToken)
        {
            var startupStopwatch = Stopwatch.StartNew();

            int postFixedWindowAttempts = 0;

            var startupExceptions = new List<Exception>();

            try
            {
                while (true)
                {
                    IEnumerable<ConfigurationClient> clients = _configClientManager.GetClients();

                    if (await TryInitializeAsync(clients, startupExceptions, cancellationToken).ConfigureAwait(false))
                    {
                        break;
                    }

                    TimeSpan delay;

                    if (startupStopwatch.Elapsed.TryGetFixedBackoff(out TimeSpan backoff))
                    {
                        delay = backoff;
                    }
                    else
                    {
                        postFixedWindowAttempts++;

                        delay = FailOverConstants.MinStartupBackoffDuration.CalculateBackoffDuration(
                            FailOverConstants.MaxBackoffDuration,
                            postFixedWindowAttempts);
                    }

                    try
                    {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw new TimeoutException(
                            $"The provider timed out while attempting to load.",
                            new AggregateException(startupExceptions));
                    }
                }
            }
            catch (Exception exception) when (
                ignoreFailures &&
                (exception is RequestFailedException ||
                exception is KeyVaultReferenceException ||
                exception is TimeoutException ||
                exception is OperationCanceledException ||
                exception is InvalidOperationException ||
                exception is FormatException ||
                ((exception as AggregateException)?.InnerExceptions?.Any(e =>
                    e is RequestFailedException ||
                    e is OperationCanceledException) ?? false)))
            { }
        }

        private async Task<bool> TryInitializeAsync(IEnumerable<ConfigurationClient> clients, List<Exception> startupExceptions, CancellationToken cancellationToken = default)
        {
            try
            {
                await InitializeAsync(clients, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch (RequestFailedException exception)
            {
                if (IsFailOverable(exception))
                {
                    startupExceptions.Add(exception);

                    return false;
                }

                throw;
            }
            catch (AggregateException exception)
            {
                if (exception.InnerExceptions?.Any(e => e is OperationCanceledException) ?? false)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        startupExceptions.Add(exception);
                    }

                    return false;
                }

                if (IsFailOverable(exception))
                {
                    startupExceptions.Add(exception);

                    return false;
                }

                throw;
            }

            return true;
        }

        private async Task InitializeAsync(IEnumerable<ConfigurationClient> clients, CancellationToken cancellationToken = default)
        {
            Dictionary<string, ConfigurationSetting> kvCollectionData = new Dictionary<string, ConfigurationSetting>();
            Dictionary<string, ConfigurationSetting> ffCollectionData = new Dictionary<string, ConfigurationSetting>();
            Dictionary<string, ConfigurationSetting> individualKvData = new Dictionary<string, ConfigurationSetting>();
            Dictionary<KeyValueSelector, IEnumerable<MatchConditions>> watchedKvEtags = null;
            Dictionary<KeyValueSelector, IEnumerable<MatchConditions>> watchedFfEtags = null;
            Dictionary<KeyValueIdentifier, ConfigurationSetting> watchedIndividualKvs = null;

            await ExecuteWithFailOverPolicyAsync(
                clients,
                async (client) =>
                {
                    watchedKvEtags = await LoadSelected(
                        client,
                        kvCollectionData,
                        _options.KeyValueSelectors,
                        cancellationToken)
                        .ConfigureAwait(false);

                    watchedFfEtags = await LoadSelected(
                        client,
                        ffCollectionData,
                        _options.FeatureFlagSelectors,
                        cancellationToken)
                        .ConfigureAwait(false);

                    watchedIndividualKvs = await LoadKeyValuesRegisteredForRefresh(
                        client,
                        kvCollectionData,
                        individualKvData,
                        cancellationToken)
                        .ConfigureAwait(false);
                },
                cancellationToken)
                .ConfigureAwait(false);

            // Update the next refresh time for all refresh registered settings and feature flags
            foreach (KeyValueWatcher changeWatcher in _options.IndividualKvWatchers.Concat(_options.FeatureFlagWatchers))
            {
                UpdateNextRefreshTime(changeWatcher);
            }

            if (_options.RegisterAllEnabled)
            {
                _registerAllNextRefreshTime = DateTimeOffset.UtcNow.Add(_options.KvCollectionRefreshInterval);
            }

            if (watchedKvEtags != null && watchedFfEtags != null && watchedIndividualKvs != null)
            {
                // Invalidate all the cached KeyVault secrets
                foreach (IKeyValueAdapter adapter in _options.Adapters)
                {
                    adapter.OnChangeDetected();
                }

                kvCollectionData = await MapConfigurationSettings(kvCollectionData).ConfigureAwait(false);

                individualKvData = await MapConfigurationSettings(individualKvData).ConfigureAwait(false);

                ffCollectionData = await MapConfigurationSettings(ffCollectionData).ConfigureAwait(false);

                Dictionary<string, ConfigurationSetting> combinedData = GetCombinedData(kvCollectionData, individualKvData, ffCollectionData);

                SetData(await PrepareData(combinedData, cancellationToken).ConfigureAwait(false));

                _watchedIndividualKvs = watchedIndividualKvs;
                _watchedKvEtags = watchedKvEtags;
                _watchedFfEtags = watchedFfEtags;
                _kvCollectionData = kvCollectionData;
                _individualKvData = individualKvData;
                _ffCollectionData = ffCollectionData;
            }
        }

        private async Task<Dictionary<KeyValueSelector, IEnumerable<MatchConditions>>> LoadSelected(
            ConfigurationClient client,
            Dictionary<string, ConfigurationSetting> existingData,
            IEnumerable<KeyValueSelector> selectors,
            CancellationToken cancellationToken)
        {
            var watchedEtags = new Dictionary<KeyValueSelector, IEnumerable<MatchConditions>>();

            foreach (KeyValueSelector loadOption in selectors)
            {
                if (string.IsNullOrEmpty(loadOption.SnapshotName))
                {
                    var selector = new SettingSelector()
                    {
                        KeyFilter = loadOption.KeyFilter,
                        LabelFilter = loadOption.LabelFilter
                    };

                    var matchConditions = new List<MatchConditions>();

                    await CallWithRequestTracing(async () =>
                    {
                        AsyncPageable<ConfigurationSetting> pageableSettings = client.GetConfigurationSettingsAsync(selector, cancellationToken);

                        await foreach (Page<ConfigurationSetting> page in _options.PageableManager.GetPages(pageableSettings).ConfigureAwait(false))
                        {
                            // Server etag will never be null or empty because non-conditional List key-values API always returns 200
                            ETag serverEtag = (ETag)page?.GetRawResponse()?.Headers.ETag;

                            if (serverEtag == null || page.Values == null)
                            {
                                throw new RequestFailedException(ErrorMessages.InvalidConfigurationSettingPage);
                            }

                            foreach (ConfigurationSetting setting in page.Values)
                            {
                                existingData[setting.Key] = setting;
                            }

                            matchConditions.Add(new MatchConditions { IfNoneMatch = serverEtag });
                        }
                    }).ConfigureAwait(false);

                    watchedEtags[loadOption] = matchConditions;
                }
                else
                {
                    ConfigurationSnapshot snapshot;

                    try
                    {
                        snapshot = await client.GetSnapshotAsync(loadOption.SnapshotName).ConfigureAwait(false);
                    }
                    catch (RequestFailedException rfe) when (rfe.Status == (int)HttpStatusCode.NotFound)
                    {
                        throw new InvalidOperationException($"Could not find snapshot with name '{loadOption.SnapshotName}'.", rfe);
                    }

                    if (snapshot.SnapshotComposition != SnapshotComposition.Key)
                    {
                        throw new InvalidOperationException($"{nameof(snapshot.SnapshotComposition)} for the selected snapshot with name '{snapshot.Name}' must be 'key', found '{snapshot.SnapshotComposition}'.");
                    }

                    IAsyncEnumerable<ConfigurationSetting> settingsEnumerable = client.GetConfigurationSettingsForSnapshotAsync(
                        loadOption.SnapshotName,
                        cancellationToken);

                    await CallWithRequestTracing(async () =>
                    {
                        await foreach (ConfigurationSetting setting in settingsEnumerable.ConfigureAwait(false))
                        {
                            existingData[setting.Key] = setting;
                        }
                    }).ConfigureAwait(false);
                }
            }

            return watchedEtags;
        }

        private async Task<Dictionary<KeyValueIdentifier, ConfigurationSetting>> LoadKeyValuesRegisteredForRefresh(
            ConfigurationClient client,
            IDictionary<string, ConfigurationSetting> existingSettings,
            CancellationToken cancellationToken)
        {
            var watchedIndividualKvs = new Dictionary<KeyValueIdentifier, ConfigurationSetting>();

            foreach (KeyValueWatcher kvWatcher in _options.IndividualKvWatchers)
            {
                string watchedKey = kvWatcher.Key;
                string watchedLabel = kvWatcher.Label;

                KeyValueIdentifier watchedKeyLabel = new KeyValueIdentifier(watchedKey, watchedLabel);

                // Skip the loading for the key-value in case it has already been loaded
                if (existingSettings.TryGetValue(watchedKey, out ConfigurationSetting loadedKv)
                    && watchedKeyLabel.Equals(new KeyValueIdentifier(loadedKv.Key, loadedKv.Label)))
                {
                    watchedIndividualKvs[watchedKeyLabel] = new ConfigurationSetting(loadedKv.Key, loadedKv.Value, loadedKv.Label, loadedKv.ETag);
                    continue;
                }

                // Send a request to retrieve key-value since it may be either not loaded or loaded with a different label or different casing
                ConfigurationSetting watchedKv = null;
                try
                {
                    await CallWithRequestTracing(async () => watchedKv = await client.GetConfigurationSettingAsync(watchedKey, watchedLabel, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
                }
                catch (RequestFailedException e) when (e.Status == (int)HttpStatusCode.NotFound)
                {
                    watchedKv = null;
                }

                // If the key-value was found, store it for updating the settings
                if (watchedKv != null)
                {
                    watchedIndividualKvs[watchedKeyLabel] = new ConfigurationSetting(watchedKv.Key, watchedKv.Value, watchedKv.Label, watchedKv.ETag);
                    existingSettings[watchedKey] = watchedKv;
                }
            }

            return watchedIndividualKvs;
        }

        private async Task<bool> RefreshIndividualKvWatchers(
            ConfigurationClient client,
            List<KeyValueChange> keyValueChanges,
            IEnumerable<KeyValueWatcher> refreshableIndividualKvWatchers,
            Uri endpoint,
            StringBuilder logDebugBuilder,
            StringBuilder logInfoBuilder,
            CancellationToken cancellationToken)
        {
            foreach (KeyValueWatcher kvWatcher in refreshableIndividualKvWatchers)
            {
                string watchedKey = kvWatcher.Key;
                string watchedLabel = kvWatcher.Label;

                KeyValueIdentifier watchedKeyLabel = new KeyValueIdentifier(watchedKey, watchedLabel);

                KeyValueChange change = default;

                //
                // Find if there is a change associated with watcher
                if (_watchedIndividualKvs.TryGetValue(watchedKeyLabel, out ConfigurationSetting watchedKv))
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
                    logDebugBuilder.AppendLine(LogHelper.BuildKeyValueReadMessage(change.ChangeType, change.Key, change.Label, endpoint.ToString()));
                    logInfoBuilder.AppendLine(LogHelper.BuildKeyValueSettingUpdatedMessage(change.Key));
                    keyValueChanges.Add(change);

                    if (kvWatcher.RefreshAll)
                    {
                        return true;
                    }
                }
                else
                {
                    logDebugBuilder.AppendLine(LogHelper.BuildKeyValueReadMessage(change.ChangeType, change.Key, change.Label, endpoint.ToString()));
                }
            }

            return false;
        }

        private async Task<bool> RefreshFfCollections(
            ConfigurationClient client,
            Dictionary<string, ConfigurationSetting> data,
            IEnumerable<KeyValueWatcher> refreshableFfWatchers,
            Dictionary<KeyValueSelector, IEnumerable<MatchConditions>> watchedFfEtags,
            Uri endpoint,
            StringBuilder logDebugBuilder,
            StringBuilder logInfoBuilder,
            CancellationToken cancellationToken)
        {
            bool ffCollectionsChanged = await UpdateWatchedCollections(
                refreshableFfWatchers.Select(watcher => new KeyValueSelector { KeyFilter = watcher.Key, LabelFilter = watcher.Label }),
                watchedFfEtags,
                client,
                cancellationToken).ConfigureAwait(false);

            if (!ffCollectionsChanged)
            {
                logDebugBuilder.AppendLine(LogHelper.BuildFeatureFlagsUnchangedMessage(endpoint.ToString()));
            }
            else
            {
                watchedFfEtags = await LoadSelected(client, data, _options.FeatureFlagSelectors, cancellationToken).ConfigureAwait(false);

                logInfoBuilder.Append(LogHelper.BuildFeatureFlagsUpdatedMessage());
            }

            return ffCollectionsChanged;
        }

        private async Task<bool> RefreshKvCollections(
            ConfigurationClient client,
            Dictionary<string, ConfigurationSetting> data,
            Dictionary<KeyValueSelector, IEnumerable<MatchConditions>> watchedKvEtags,
            Uri endpoint,
            StringBuilder logDebugBuilder,
            StringBuilder logInfoBuilder,
            CancellationToken cancellationToken)
        {
            bool kvCollectionsChanged = await UpdateWatchedCollections(_options.KeyValueSelectors, watchedKvEtags, client, cancellationToken).ConfigureAwait(false);

            if (!kvCollectionsChanged)
            {
                logDebugBuilder.AppendLine(LogHelper.BuildSelectedKeyValueCollectionsUnchangedMessage(endpoint.ToString()));
            }
            else
            {
                watchedKvEtags = await LoadSelected(client, data, _options.KeyValueSelectors, cancellationToken).ConfigureAwait(false);

                logInfoBuilder.Append(LogHelper.BuildSelectedKeyValueCollectionsUpdatedMessage());
            }

            return kvCollectionsChanged;
        }

        private void SetData(IDictionary<string, string> data)
        {
            // Set the application data for the configuration provider
            Data = data;

            foreach (IKeyValueAdapter adapter in _options.Adapters)
            {
                adapter.OnConfigUpdated();
            }

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

                IEnumerable<KeyValuePair<string, string>> kvs = await adapter.ProcessKeyValue(setting, AppConfigurationEndpoint, _logger, cancellationToken).ConfigureAwait(false);

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
                ReplicaCount = _options.Endpoints?.Count() - 1 ?? _options.ConnectionStrings?.Count() - 1 ?? 0,
                FeatureFlagTracing = _options.FeatureFlagTracing,
                IsLoadBalancingEnabled = _options.LoadBalancingEnabled
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

        private void UpdateNextRefreshTime(KeyValueWatcher changeWatcher)
        {
            changeWatcher.NextRefreshTime = DateTimeOffset.UtcNow.Add(changeWatcher.RefreshInterval);
        }

        private async Task<T> ExecuteWithFailOverPolicyAsync<T>(
            IEnumerable<ConfigurationClient> clients,
            Func<ConfigurationClient, Task<T>> funcToExecute,
            CancellationToken cancellationToken = default)
        {
            if (_requestTracingEnabled && _requestTracingOptions != null)
            {
                _requestTracingOptions.IsFailoverRequest = false;
            }

            if (_options.LoadBalancingEnabled && _lastSuccessfulEndpoint != null && clients.Count() > 1)
            {
                int nextClientIndex = 0;

                foreach (ConfigurationClient client in clients)
                {
                    nextClientIndex++;

                    if (_configClientManager.GetEndpointForClient(client) == _lastSuccessfulEndpoint)
                    {
                        break;
                    }
                }

                // If we found the last successful client, we'll rotate the list so that the next client is at the beginning
                if (nextClientIndex < clients.Count())
                {
                    clients = clients.Skip(nextClientIndex).Concat(clients.Take(nextClientIndex));
                }
            }

            using IEnumerator<ConfigurationClient> clientEnumerator = clients.GetEnumerator();

            clientEnumerator.MoveNext();

            Uri previousEndpoint = _configClientManager.GetEndpointForClient(clientEnumerator.Current);
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

                    _lastSuccessfulEndpoint = _configClientManager.GetEndpointForClient(currentClient);

                    return result;
                }
                catch (RequestFailedException rfe)
                {
                    if (!IsFailOverable(rfe) || !clientEnumerator.MoveNext())
                    {
                        backoffAllClients = true;

                        throw;
                    }
                }
                catch (AggregateException ae)
                {
                    if (!IsFailOverable(ae) || !clientEnumerator.MoveNext())
                    {
                        backoffAllClients = true;

                        throw;
                    }
                }
                finally
                {
                    if (!success && backoffAllClients)
                    {
                        _logger.LogWarning(LogHelper.BuildLastEndpointFailedMessage(previousEndpoint?.ToString()));

                        do
                        {
                            UpdateClientBackoffStatus(previousEndpoint, success);

                            clientEnumerator.MoveNext();

                            currentClient = clientEnumerator.Current;
                        }
                        while (currentClient != null);
                    }
                    else
                    {
                        UpdateClientBackoffStatus(previousEndpoint, success);
                    }
                }

                Uri currentEndpoint = _configClientManager.GetEndpointForClient(clientEnumerator.Current);

                if (previousEndpoint != currentEndpoint)
                {
                    _logger.LogWarning(LogHelper.BuildFailoverMessage(previousEndpoint?.ToString(), currentEndpoint?.ToString()));
                }

                previousEndpoint = currentEndpoint;

                if (_requestTracingEnabled && _requestTracingOptions != null)
                {
                    _requestTracingOptions.IsFailoverRequest = true;
                }
            }
        }

        private async Task ExecuteWithFailOverPolicyAsync(
            IEnumerable<ConfigurationClient> clients,
            Func<ConfigurationClient, Task> funcToExecute,
            CancellationToken cancellationToken = default)
        {
            await ExecuteWithFailOverPolicyAsync<object>(clients, async (client) =>
            {
                await funcToExecute(client).ConfigureAwait(false);
                return null;

            }, cancellationToken).ConfigureAwait(false);
        }

        private bool IsFailOverable(AggregateException ex)
        {
            RequestFailedException rfe = ex.InnerExceptions?.LastOrDefault(e => e is RequestFailedException) as RequestFailedException;

            return rfe != null ? IsFailOverable(rfe) : false;
        }

        private bool IsFailOverable(RequestFailedException rfe)
        {
            if (rfe.Status == HttpStatusCodes.TooManyRequests ||
                rfe.Status == (int)HttpStatusCode.RequestTimeout ||
                rfe.Status >= (int)HttpStatusCode.InternalServerError ||
                rfe.Status == (int)HttpStatusCode.Forbidden ||
                rfe.Status == (int)HttpStatusCode.Unauthorized)
            {
                return true;
            }

            Exception innerException;

            if (rfe.InnerException is HttpRequestException hre)
            {
                innerException = hre.InnerException;
            }
            else
            {
                innerException = rfe.InnerException;
            }

            // The InnerException could be SocketException or WebException when an endpoint is invalid and IOException if it's a network issue.
            return innerException is WebException ||
                   innerException is SocketException ||
                   innerException is IOException;
        }

        private IEnumerable<string> GetExistingKeys(string key, IEnumerable<string> existingKeys)
        {
            IEnumerable<string> currentKeyValues;

            if (key.EndsWith("*"))
            {
                // Get current application settings starting with changeWatcher.Key, excluding the last * character
                string keyPrefix = key.Substring(0, key.Length - 1);
                currentKeyValues = existingKeys.Where(val =>
                {
                    return val.StartsWith(keyPrefix);
                });
            }
            else
            {
                currentKeyValues = existingKeys.Where(val =>
                {
                    return val.Equals(key);
                });
            }

            return currentKeyValues;
        }

        private async Task<Dictionary<string, ConfigurationSetting>> MapConfigurationSettings(Dictionary<string, ConfigurationSetting> data)
        {
            Dictionary<string, ConfigurationSetting> mappedData = new Dictionary<string, ConfigurationSetting>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, ConfigurationSetting> kvp in data)
            {
                ConfigurationSetting setting = kvp.Value;

                foreach (Func<ConfigurationSetting, ValueTask<ConfigurationSetting>> func in _options.Mappers)
                {
                    setting = await func(setting).ConfigureAwait(false);
                }

                if (setting != null)
                {
                    mappedData[kvp.Key] = setting;
                }
            }

            return mappedData;
        }

        private void EnsureAssemblyInspected()
        {
            if (!_isAssemblyInspected)
            {
                _isAssemblyInspected = true;

                if (_requestTracingEnabled && _requestTracingOptions != null)
                {
                    _requestTracingOptions.FeatureManagementVersion = TracingUtils.GetAssemblyVersion(RequestTracingConstants.FeatureManagementAssemblyName);

                    _requestTracingOptions.FeatureManagementAspNetCoreVersion = TracingUtils.GetAssemblyVersion(RequestTracingConstants.FeatureManagementAspNetCoreAssemblyName);

                    if (TracingUtils.GetAssemblyVersion(RequestTracingConstants.SignalRAssemblyName) != null)
                    {
                        _requestTracingOptions.IsSignalRUsed = true;
                    }
                }
            }
        }

        private void UpdateClientBackoffStatus(Uri endpoint, bool successful)
        {
            if (!_configClientBackoffs.TryGetValue(endpoint, out ConfigurationClientBackoffStatus clientBackoffStatus))
            {
                clientBackoffStatus = new ConfigurationClientBackoffStatus();
            }

            if (successful)
            {
                clientBackoffStatus.BackoffEndTime = DateTimeOffset.UtcNow;

                clientBackoffStatus.FailedAttempts = 0;
            }
            else
            {
                clientBackoffStatus.FailedAttempts++;

                TimeSpan backoffDuration = _options.MinBackoffDuration.CalculateBackoffDuration(FailOverConstants.MaxBackoffDuration, clientBackoffStatus.FailedAttempts);

                clientBackoffStatus.BackoffEndTime = DateTimeOffset.UtcNow.Add(backoffDuration);
            }

            _configClientBackoffs[endpoint] = clientBackoffStatus;
        }

        private async Task<bool> UpdateWatchedCollections(IEnumerable<KeyValueSelector> selectors, Dictionary<KeyValueSelector, IEnumerable<MatchConditions>> watchedEtags, ConfigurationClient client, CancellationToken cancellationToken)
        {
            bool watchedCollectionsChanged = false;

            foreach (KeyValueSelector selector in selectors)
            {
                if (watchedEtags.TryGetValue(selector, out IEnumerable<MatchConditions> matchConditions))
                {
                    await TracingUtils.CallWithRequestTracing(_requestTracingEnabled, RequestType.Watch, _requestTracingOptions,
                        async () => watchedCollectionsChanged = await client.HasWatchedCollectionsChanged(selector, matchConditions, _options.PageableManager, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
                }

                if (watchedCollectionsChanged)
                {
                    return watchedCollectionsChanged;
                }
            }

            return watchedCollectionsChanged;
        }

        private Dictionary<string, ConfigurationSetting> GetCombinedData(
            Dictionary<string, ConfigurationSetting> kvCollectionData,
            Dictionary<string, ConfigurationSetting> individualKvData,
            Dictionary<string, ConfigurationSetting> ffCollectionData)
        {
            // Merge all key-value and feature flag data
            Dictionary<string, ConfigurationSetting> combinedData = new Dictionary<string, ConfigurationSetting>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, ConfigurationSetting> kvp in kvCollectionData)
            {
                combinedData[kvp.Key] = kvp.Value;
            }

            foreach (KeyValuePair<string, ConfigurationSetting> kvp in individualKvData)
            {
                combinedData[kvp.Key] = kvp.Value;
            }

            foreach (KeyValuePair<string, ConfigurationSetting> kvp in ffCollectionData)
            {
                combinedData[kvp.Key] = kvp.Value;
            }

            return combinedData;
        }

        public void Dispose()
        {
            (_configClientManager as ConfigurationClientManager)?.Dispose();
        }
    }
}
