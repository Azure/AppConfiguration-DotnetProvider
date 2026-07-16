// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.SnapshotReference;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Models;
using FeatureFlagSelector = Microsoft.Extensions.Configuration.AzureAppConfiguration.Models.FeatureFlagSelector;
using AppConfigFeatureFlagSelector = Azure.Data.AppConfiguration.FeatureFlagSelector;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class AzureAppConfigurationProvider : ConfigurationProvider, IConfigurationRefresher, IHealthCheck, IDisposable
    {
        private readonly ActivitySource _activitySource;
        private bool _optional;
        private bool _isInitialLoadComplete = false;
        private bool _isAssemblyInspected;
        private readonly bool _requestTracingEnabled;
        private readonly bool _fmSchemaCompatibilityDisabled;
        private readonly IAppConfigurationClientManager _clientManager;
        private Uri _lastSuccessfulEndpoint;
        private AzureAppConfigurationOptions _options;
        private Dictionary<string, ConfigurationSetting> _mappedData;
        private Dictionary<KeyValueIdentifier, ConfigurationSetting> _watchedIndividualKvs = new Dictionary<KeyValueIdentifier, ConfigurationSetting>();
        private HashSet<string> _ffKeys = new HashSet<string>();
        private IEnumerable<FeatureFlag> _featureFlags = Enumerable.Empty<FeatureFlag>();
        private Dictionary<KeyValueSelector, IEnumerable<WatchedPage>> _watchedKvPages = new Dictionary<KeyValueSelector, IEnumerable<WatchedPage>>();
        private Dictionary<FeatureFlagSelector, IEnumerable<WatchedPage>> _watchedClassicFeatureFlagPages = new Dictionary<FeatureFlagSelector, IEnumerable<WatchedPage>>();
        private Dictionary<FeatureFlagSelector, IEnumerable<WatchedPage>> _watchedFeatureFlagPages = new Dictionary<FeatureFlagSelector, IEnumerable<WatchedPage>>();
        private RequestTracingOptions _requestTracingOptions;
        private Dictionary<Uri, ClientBackoffStatus> _clientBackoffs = new Dictionary<Uri, ClientBackoffStatus>();
        private DateTimeOffset _nextCollectionRefreshTime;

        private readonly TimeSpan MinRefreshInterval;

        // The most-recent time when the refresh operation attempted to load the initial configuration
        private DateTimeOffset InitializationCacheExpires = default;

        private static readonly TimeSpan MinDelayForUnhandledFailure = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan DefaultMaxSetDirtyDelay = TimeSpan.FromSeconds(30);

        // To avoid concurrent network operations, this flag is used to achieve synchronization between multiple threads.
        private int _networkOperationsInProgress = 0;
        private Logger _logger = new Logger();
        private ILoggerFactory _loggerFactory;

        // For health check
        private DateTimeOffset? _lastSuccessfulAttempt = null;
        private DateTimeOffset? _lastFailedAttempt = null;

        private class ClientBackoffStatus
        {
            public int FailedAttempts { get; set; }
            public DateTimeOffset BackoffEndTime { get; set; }
        }

        private class FeatureFlagLoadResult
        {
            public IEnumerable<FeatureFlag> FeatureFlags { get; set; }
            public Dictionary<FeatureFlagSelector, IEnumerable<WatchedPage>> Pages { get; set; }
        }

        class ClassicFeatureFlagLoadResult
        {
            public IEnumerable<ConfigurationSetting> ClassicFeatureFlags { get; set; }
            public Dictionary<FeatureFlagSelector, IEnumerable<WatchedPage>> Pages { get; set; }
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

                    if (_clientManager is AppConfigurationClientManager clientManager)
                    {
                        clientManager.SetLogger(_logger);
                    }
                }
            }
        }

        public AzureAppConfigurationProvider(IAppConfigurationClientManager clientManager, AzureAppConfigurationOptions options, bool optional)
        {
            _clientManager = clientManager ?? throw new ArgumentNullException(nameof(clientManager));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _optional = optional;

            IEnumerable<KeyValueWatcher> watchers = options.IndividualKvWatchers.Union(options.FeatureFlagWatchers);

            bool hasWatchers = watchers.Any();
            TimeSpan minWatcherRefreshInterval = hasWatchers ? watchers.Min(w => w.RefreshInterval) : TimeSpan.MaxValue;

            if (options.RegisterAllEnabled)
            {
                if (options.KvCollectionRefreshInterval <= TimeSpan.Zero)
                {
                    throw new ArgumentException(
                        $"{nameof(options.KvCollectionRefreshInterval)} must be greater than zero seconds when using RegisterAll for refresh",
                        nameof(options));
                }

                MinRefreshInterval = TimeSpan.FromTicks(Math.Min(minWatcherRefreshInterval.Ticks, options.KvCollectionRefreshInterval.Ticks));
            }
            else if (hasWatchers)
            {
                MinRefreshInterval = minWatcherRefreshInterval;
            }
            else
            {
                MinRefreshInterval = RefreshConstants.DefaultRefreshInterval;
            }

            _requestTracingEnabled = !EnvironmentVariableHelper.GetBoolOrDefault(EnvironmentVariableNames.RequestTracingDisabled);

            _fmSchemaCompatibilityDisabled = EnvironmentVariableHelper.GetBoolOrDefault(EnvironmentVariableNames.FmSchemacompatibilityDisabled);

            if (_requestTracingEnabled)
            {
                SetRequestTracingOptions();
            }

            _activitySource = new ActivitySource(options.ActivitySourceName ?? ActivityNames.AzureAppConfigurationActivitySource);
        }

        /// <summary>
        /// Loads (or reloads) the data for this provider.
        /// </summary>
        public override void Load()
        {
            var watch = Stopwatch.StartNew();
            using Activity activity = _activitySource?.StartActivity(ActivityNames.Load);
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
                    bool isRefreshDue = _options.RegisterAllEnabled && utcNow >= _nextCollectionRefreshTime;

                    // Skip refresh if mappedData is loaded, but none of the watchers or adapters are refreshable.
                    if (_mappedData != null &&
                        !refreshableIndividualKvWatchers.Any() &&
                        !refreshableFfWatchers.Any() &&
                        !isRefreshDue &&
                        !_options.Adapters.Any(adapter => adapter.NeedsRefresh()))
                    {
                        return;
                    }

                    IEnumerable<IAppConfigurationClient> clients = _clientManager.GetClients();

                    if (_requestTracingOptions != null)
                    {
                        _requestTracingOptions.ReplicaCount = clients.Count() - 1;
                    }

                    //
                    // Filter clients based on their backoff status
                    clients = clients.Where(client =>
                    {
                        Uri endpoint = client.Endpoint;

                        if (!_clientBackoffs.TryGetValue(endpoint, out ClientBackoffStatus clientBackoffStatus))
                        {
                            clientBackoffStatus = new ClientBackoffStatus();

                            _clientBackoffs[endpoint] = clientBackoffStatus;
                        }

                        return clientBackoffStatus.BackoffEndTime <= utcNow;
                    }
                    );

                    if (!clients.Any())
                    {
                        _clientManager.RefreshClients();

                        _logger.LogDebug(LogHelper.BuildRefreshSkippedNoClientAvailableMessage());

                        _lastFailedAttempt = DateTime.UtcNow;

                        return;
                    }

                    using Activity activity = _activitySource?.StartActivity(ActivityNames.Refresh);
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
                    Dictionary<KeyValueSelector, IEnumerable<WatchedPage>> kvEtags = null;
                    HashSet<string> ffKeys = null;
                    Dictionary<KeyValueIdentifier, ConfigurationSetting> watchedIndividualKvs = null;
                    List<KeyValueChange> watchedIndividualKvChanges = null;
                    Dictionary<string, ConfigurationSetting> data = null;
                    ClassicFeatureFlagLoadResult classicFeatureFlagLoadResult = null;
                    FeatureFlagLoadResult featureFlagLoadResult = null;
                    bool refreshFeatureFlag = false;
                    bool refreshAll = false;
                    StringBuilder logInfoBuilder = new StringBuilder();
                    StringBuilder logDebugBuilder = new StringBuilder();

                    await ExecuteWithFailOverPolicyAsync(clients, async (appConfigClient) =>
                    {
                        kvEtags = null;
                        ffKeys = null;
                        watchedIndividualKvs = null;
                        watchedIndividualKvChanges = new List<KeyValueChange>();
                        data = null;
                        refreshFeatureFlag = false;
                        refreshAll = false;
                        logDebugBuilder.Clear();
                        logInfoBuilder.Clear();
                        Uri endpoint = appConfigClient.Endpoint;

                        if (_options.RegisterAllEnabled)
                        {
                            if (isRefreshDue)
                            {
                                refreshAll = await HaveCollectionsChanged(
                                    _options.KeyValueSelectors,
                                    _watchedKvPages,
                                    appConfigClient,
                                    cancellationToken).ConfigureAwait(false);
                            }
                        }
                        else
                        {
                            refreshAll = await RefreshIndividualKvWatchers(
                                appConfigClient,
                                watchedIndividualKvChanges,
                                refreshableIndividualKvWatchers,
                                endpoint,
                                logDebugBuilder,
                                logInfoBuilder,
                                cancellationToken).ConfigureAwait(false);
                        }

                        if (refreshAll)
                        {
                            // Trigger a single load-all operation if a change was detected in one or more key-values with refreshAll: true,
                            // or if any key-value collection change was detected.
                            kvEtags = new Dictionary<KeyValueSelector, IEnumerable<WatchedPage>>();

                            data = await LoadKeyValues(appConfigClient, kvEtags, _options.KeyValueSelectors, cancellationToken).ConfigureAwait(false);

                            classicFeatureFlagLoadResult = await LoadClassicFeatureFlags(
                                appConfigClient,
                                _options.FeatureFlagSelectors,
                                cancellationToken).ConfigureAwait(false);

                            featureFlagLoadResult = await LoadFeatureFlags(
                                appConfigClient,
                                _options.FeatureFlagSelectors,
                                cancellationToken).ConfigureAwait(false);

                            ffKeys = new HashSet<string>(
                                classicFeatureFlagLoadResult.ClassicFeatureFlags.Select(ff => ff.Key)
                                .Concat(featureFlagLoadResult.FeatureFlags.Select(ff => FeatureManagementConstants.FeatureFlagMarker + ff.Name))
                            );

                            watchedIndividualKvs = await LoadIndividualWatchedSettings(appConfigClient, data, cancellationToken).ConfigureAwait(false);

                            logInfoBuilder.AppendLine(LogHelper.BuildConfigurationUpdatedMessage());

                            return;
                        }

                        // Get feature flag changes
                        refreshFeatureFlag = await HaveClassicFeatureFlagsChanged(_options.FeatureFlagSelectors, _watchedClassicFeatureFlagPages, appConfigClient, cancellationToken).ConfigureAwait(false)
                            || await HaveFeatureFlagsChanged(_options.FeatureFlagSelectors, _watchedFeatureFlagPages, appConfigClient, cancellationToken).ConfigureAwait(false);

                        if (refreshFeatureFlag)
                        {
                            classicFeatureFlagLoadResult = await LoadClassicFeatureFlags(
                                appConfigClient,
                                _options.FeatureFlagSelectors,
                                cancellationToken).ConfigureAwait(false);

                            featureFlagLoadResult = await LoadFeatureFlags(
                                appConfigClient,
                                _options.FeatureFlagSelectors,
                                cancellationToken).ConfigureAwait(false);

                            ffKeys = new HashSet<string>(
                                classicFeatureFlagLoadResult.ClassicFeatureFlags.Select(ff => ff.Key)
                                .Concat(featureFlagLoadResult.FeatureFlags.Select(ff => FeatureManagementConstants.FeatureFlagMarker + ff.Name))
                            );

                            logInfoBuilder.Append(LogHelper.BuildFeatureFlagsUpdatedMessage());
                        }
                        else
                        {
                            logDebugBuilder.AppendLine(LogHelper.BuildFeatureFlagsUnchangedMessage(endpoint.ToString()));
                        }
                    },
                    cancellationToken)
                    .ConfigureAwait(false);

                    int classicFeatureFlagCount = 0;

                    if (refreshAll)
                    {
                        // Merges the classic feature flags loaded from the ".appconfig.featureflag/" key-value namespace into the key-value data,
                        // excluding any that are superseded by a standalone feature flag with the same name.
                        var featureFlagKeys = new HashSet<string>(
                            featureFlagLoadResult.FeatureFlags.Select(ff => FeatureManagementConstants.FeatureFlagMarker + ff.Name));

                        List<ConfigurationSetting> classicFeatureFlags = classicFeatureFlagLoadResult.ClassicFeatureFlags
                            .Where(setting => !featureFlagKeys.Contains(setting.Key))
                            .ToList();

                        foreach (ConfigurationSetting setting in classicFeatureFlags)
                        {
                            data[setting.Key] = setting;
                        }

                        classicFeatureFlagCount = classicFeatureFlags.Count;

                        _mappedData = await MapConfigurationSettings(data).ConfigureAwait(false);
                        _featureFlags = featureFlagLoadResult.FeatureFlags;

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
                        watchedIndividualKvs = new Dictionary<KeyValueIdentifier, ConfigurationSetting>(_watchedIndividualKvs);

                        await ProcessKeyValueChangesAsync(watchedIndividualKvChanges, _mappedData, watchedIndividualKvs).ConfigureAwait(false);

                        if (refreshFeatureFlag)
                        {
                            // Exclude any classic feature flags that are superseded by a standalone feature flag with the same name.
                            var featureFlagKeys = new HashSet<string>(
                                featureFlagLoadResult.FeatureFlags.Select(ff => FeatureManagementConstants.FeatureFlagMarker + ff.Name));

                            List<ConfigurationSetting> classicFeatureFlags = classicFeatureFlagLoadResult.ClassicFeatureFlags
                                .Where(setting => !featureFlagKeys.Contains(setting.Key))
                                .ToList();

                            classicFeatureFlagCount = classicFeatureFlags.Count;

                            // Remove all feature flag keys that are not present in the latest loading of feature flags, but were loaded previously
                            foreach (string key in _ffKeys.Except(ffKeys))
                            {
                                _mappedData.Remove(key);
                            }

                            // Remove any classic feature flags that are now superseded by a standalone feature flag with the same name.
                            foreach (FeatureFlag featureFlag in featureFlagLoadResult.FeatureFlags)
                            {
                                _mappedData.Remove(FeatureManagementConstants.FeatureFlagMarker + featureFlag.Name);
                            }

                            Dictionary<string, ConfigurationSetting> mappedFfData = await MapConfigurationSettings(classicFeatureFlags.ToDictionary(x => x.Key, x => x)).ConfigureAwait(false);

                            foreach (KeyValuePair<string, ConfigurationSetting> kvp in mappedFfData)
                            {
                                _mappedData[kvp.Key] = kvp.Value;
                            }

                            _featureFlags = featureFlagLoadResult.FeatureFlags;
                        }

                        //
                        // update the next refresh time for all refresh registered settings and feature flags
                        foreach (KeyValueWatcher changeWatcher in refreshableIndividualKvWatchers.Concat(refreshableFfWatchers))
                        {
                            UpdateNextRefreshTime(changeWatcher);
                        }
                    }

                    if (_options.RegisterAllEnabled && isRefreshDue)
                    {
                        _nextCollectionRefreshTime = DateTimeOffset.UtcNow.Add(_options.KvCollectionRefreshInterval);
                    }

                    if (_options.Adapters.Any(adapter => adapter.NeedsRefresh()) || watchedIndividualKvChanges.Any() || refreshAll || refreshFeatureFlag)
                    {
                        _watchedIndividualKvs = watchedIndividualKvs ?? _watchedIndividualKvs;

                        _watchedClassicFeatureFlagPages = classicFeatureFlagLoadResult?.Pages ?? _watchedClassicFeatureFlagPages;

                        _watchedFeatureFlagPages = featureFlagLoadResult?.Pages ?? _watchedFeatureFlagPages;

                        _watchedKvPages = kvEtags ?? _watchedKvPages;

                        _ffKeys = ffKeys ?? _ffKeys;

                        if (logDebugBuilder.Length > 0)
                        {
                            _logger.LogDebug(logDebugBuilder.ToString().Trim());
                        }

                        if (logInfoBuilder.Length > 0)
                        {
                            _logger.LogInformation(logInfoBuilder.ToString().Trim());
                        }

                        // PrepareData makes calls to KeyVault and may throw exceptions. But, we still update watchers before
                        // SetData because repeating appconfig calls (by not updating watchers) won't help anything for keyvault calls.
                        // As long as adapter.NeedsRefresh is true, we will attempt to update keyvault again the next time RefreshAsync is called.
                        Dictionary<string, string> preparedData = await PrepareData(_mappedData, cancellationToken).ConfigureAwait(false);

                        IEnumerable<KeyValuePair<string, string>> processedFeatureFlags = ProcessFeatureFlags(_featureFlags, classicFeatureFlagCount);

                        foreach (KeyValuePair<string, string> kv in processedFeatureFlags)
                        {
                            preparedData[kv.Key] = kv.Value;
                        }

                        SetData(preparedData);
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

            if (_clientManager.UpdateSyncToken(pushNotification.ResourceUri, pushNotification.SyncToken))
            {
                if (_requestTracingEnabled && _requestTracingOptions != null)
                {
                    _requestTracingOptions.IsPushRefreshUsed = true;
                }

                SetDirty(maxDelay);
            }
            else
            {
                _logger.LogWarning(LogHelper.BuildPushNotificationUnregisteredEndpointMessage(pushNotification.ResourceUri.ToString()));
            }
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            HealthStatus failureStatus = context.Registration?.FailureStatus ?? HealthStatus.Unhealthy;

            if (!_lastSuccessfulAttempt.HasValue)
            {
                return new HealthCheckResult(status: failureStatus, description: HealthCheckConstants.LoadNotCompletedMessage);
            }

            if (_lastFailedAttempt.HasValue &&
                _lastSuccessfulAttempt.Value < _lastFailedAttempt.Value)
            {
                return new HealthCheckResult(status: failureStatus, description: HealthCheckConstants.RefreshFailedMessage);
            }

            return HealthCheckResult.Healthy();
        }

        private void SetDirty(TimeSpan? maxDelay)
        {
            DateTimeOffset nextRefreshTime = AddRandomDelay(DateTimeOffset.UtcNow, maxDelay ?? DefaultMaxSetDirtyDelay);

            if (_options.RegisterAllEnabled)
            {
                _nextCollectionRefreshTime = nextRefreshTime;
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

        private async Task<Dictionary<string, string>> PrepareData(
            Dictionary<string, ConfigurationSetting> data,
            CancellationToken cancellationToken = default)
        {
            var applicationData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Reset old feature flag tracing in order to track the information present in the current response from server.
            _options.FeatureFlagTracing.ResetFeatureFlagTracing();

            // Reset old request tracing values for content type
            if (_requestTracingEnabled && _requestTracingOptions != null)
            {
                _requestTracingOptions.ResetAiConfigurationTracing();
            }

            // The running index into the "feature_management:feature_flags" array. Classic feature flags emitted
            // using the Microsoft schema advance this index;
            int featureFlagIndex = 0;

            foreach (KeyValuePair<string, ConfigurationSetting> kvp in data)
            {
                IEnumerable<KeyValuePair<string, string>> keyValuePairs;

                if (_requestTracingEnabled && _requestTracingOptions != null)
                {
                    _requestTracingOptions.UpdateAiConfigurationTracing(kvp.Value.ContentType);
                }

                if (ClassicFeatureFlagConverter.IsClassicFeatureFlag(kvp.Value))
                {
                    ClassicFeatureFlag classicFeatureFlag = ClassicFeatureFlagConverter.Parse(kvp.Value);

                    _options.FeatureFlagTracing.Update(classicFeatureFlag);

                    var metadata = new FeatureFlagMetadata(kvp.Value.Key, kvp.Value.Label, kvp.Value.ETag);

                    keyValuePairs = ClassicFeatureFlagConverter.ToConfiguration(
                        classicFeatureFlag,
                        metadata,
                        AppConfigurationEndpoint,
                        _fmSchemaCompatibilityDisabled,
                        featureFlagIndex);

                    featureFlagIndex++;
                }
                else
                {
                    keyValuePairs = await ProcessAdapters(kvp.Value, cancellationToken).ConfigureAwait(false);
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

            return applicationData;
        }

        // Produces the feature-management configuration key-values for standalone feature flags directly from the SDK FeatureFlag model.
        private IEnumerable<KeyValuePair<string, string>> ProcessFeatureFlags(IEnumerable<FeatureFlag> featureFlags, int featureFlagIndex)
        {
            var processedFeatureFlags = new List<KeyValuePair<string, string>>();

            if (featureFlags == null)
            {
                return processedFeatureFlags;
            }

            foreach (FeatureFlag featureFlag in featureFlags)
            {
                _options.FeatureFlagTracing.Update(featureFlag);

                foreach (KeyValuePair<string, string> kv in FeatureFlagConverter.ToConfiguration(featureFlag, AppConfigurationEndpoint, featureFlagIndex))
                {
                    processedFeatureFlags.Add(new KeyValuePair<string, string>(kv.Key, kv.Value));
                }

                featureFlagIndex++;
            }

            return processedFeatureFlags;
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
                    IEnumerable<IAppConfigurationClient> clients = _clientManager.GetClients();

                    if (_requestTracingEnabled && _requestTracingOptions != null)
                    {
                        _requestTracingOptions.ReplicaCount = clients.Count() - 1;
                    }

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

        private async Task<bool> TryInitializeAsync(IEnumerable<IAppConfigurationClient> clients, List<Exception> startupExceptions, CancellationToken cancellationToken = default)
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

        private async Task InitializeAsync(IEnumerable<IAppConfigurationClient> clients, CancellationToken cancellationToken = default)
        {
            Dictionary<string, ConfigurationSetting> data = null;
            Dictionary<KeyValueSelector, IEnumerable<WatchedPage>> kvEtags = new Dictionary<KeyValueSelector, IEnumerable<WatchedPage>>();
            Dictionary<KeyValueIdentifier, ConfigurationSetting> watchedIndividualKvs = null;
            ClassicFeatureFlagLoadResult classicFeatureFlagLoadResult = null;
            FeatureFlagLoadResult featureFlagLoadResult = null;

            await ExecuteWithFailOverPolicyAsync(
                clients,
                async (appConfigClient) =>
                {
                    data = await LoadKeyValues(
                        appConfigClient,
                        kvEtags,
                        _options.KeyValueSelectors,
                        cancellationToken)
                        .ConfigureAwait(false);

                    watchedIndividualKvs = await LoadIndividualWatchedSettings(
                        appConfigClient,
                        data,
                        cancellationToken)
                        .ConfigureAwait(false);

                    classicFeatureFlagLoadResult = await LoadClassicFeatureFlags(
                        appConfigClient,
                        _options.FeatureFlagSelectors,
                        cancellationToken)
                        .ConfigureAwait(false);

                    featureFlagLoadResult = await LoadFeatureFlags(
                        appConfigClient,
                        _options.FeatureFlagSelectors,
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
                _nextCollectionRefreshTime = DateTimeOffset.UtcNow.Add(_options.KvCollectionRefreshInterval);
            }

            // Invalidate all the cached KeyVault secrets
            foreach (IKeyValueAdapter adapter in _options.Adapters)
            {
                adapter.OnChangeDetected();
            }

            // Merges the classic feature flags loaded from the ".appconfig.featureflag/" key-value namespace into the key-value data,
            // excluding any that are superseded by a standalone feature flag with the same name.
            var featureFlagKeys = new HashSet<string>(
                featureFlagLoadResult.FeatureFlags.Select(ff => FeatureManagementConstants.FeatureFlagMarker + ff.Name));

            List<ConfigurationSetting> classicFeatureFlags = classicFeatureFlagLoadResult.ClassicFeatureFlags
                .Where(setting => !featureFlagKeys.Contains(setting.Key))
                .ToList();

            foreach (ConfigurationSetting setting in classicFeatureFlags)
            {
                data[setting.Key] = setting;
            }

            Dictionary<string, ConfigurationSetting> mappedData = await MapConfigurationSettings(data).ConfigureAwait(false);

            Dictionary<string, string> preparedData = await PrepareData(mappedData, cancellationToken).ConfigureAwait(false);

            foreach (KeyValuePair<string, string> kv in ProcessFeatureFlags(featureFlagLoadResult.FeatureFlags, classicFeatureFlags.Count))
            {
                preparedData[kv.Key] = kv.Value;
            }

            SetData(preparedData);

            _mappedData = mappedData;
            _watchedKvPages = kvEtags;
            _watchedClassicFeatureFlagPages = classicFeatureFlagLoadResult.Pages;
            _watchedFeatureFlagPages = featureFlagLoadResult.Pages;
            _watchedIndividualKvs = watchedIndividualKvs;
            _featureFlags = featureFlagLoadResult.FeatureFlags;
            _ffKeys = new HashSet<string>(
                classicFeatureFlags.Select(ff => ff.Key).Concat(featureFlagKeys)
            );
        }

        private async Task<Dictionary<string, ConfigurationSetting>> LoadKeyValues(
            IAppConfigurationClient client,
            Dictionary<KeyValueSelector, IEnumerable<WatchedPage>> kvPageWatchers,
            IEnumerable<KeyValueSelector> selectors,
            CancellationToken cancellationToken)
        {
            Dictionary<string, ConfigurationSetting> data = new Dictionary<string, ConfigurationSetting>();

            foreach (KeyValueSelector loadOption in selectors)
            {
                if (string.IsNullOrEmpty(loadOption.SnapshotName))
                {
                    var selector = new SettingSelector()
                    {
                        KeyFilter = loadOption.KeyFilter,
                        LabelFilter = loadOption.LabelFilter
                    };

                    if (loadOption.TagFilters != null)
                    {
                        foreach (string tagFilter in loadOption.TagFilters)
                        {
                            selector.TagsFilter.Add(tagFilter);
                        }
                    }

                    var pageWatchers = new List<WatchedPage>();

                    await CallWithRequestTracing(async () =>
                    {
                        AsyncPageable<ConfigurationSetting> pageableSettings = client.GetConfigurationSettingsAsync(selector, cancellationToken);

                        await foreach (Page<ConfigurationSetting> page in pageableSettings.AsPages(_options.ConfigurationSettingPageIterator).ConfigureAwait(false))
                        {
                            using Response rawResponse = page.GetRawResponse();
                            DateTimeOffset serverResponseTime = rawResponse.GetMsDate();

                            foreach (ConfigurationSetting setting in page.Values)
                            {
                                if (setting.ContentType == SnapshotReferenceConstants.ContentType)
                                {
                                    // Track snapshot reference usage for telemetry
                                    if (_requestTracingEnabled && _requestTracingOptions != null)
                                    {
                                        _requestTracingOptions.UsesSnapshotReference = true;
                                    }

                                    SnapshotReference.SnapshotReference snapshotReference = SnapshotReferenceParser.Parse(setting);

                                    Dictionary<string, ConfigurationSetting> resolvedSettings = await LoadSnapshotData(snapshotReference.SnapshotName, client, cancellationToken).ConfigureAwait(false);

                                    if (_requestTracingEnabled && _requestTracingOptions != null)
                                    {
                                        _requestTracingOptions.UsesSnapshotReference = false;
                                    }

                                    foreach (KeyValuePair<string, ConfigurationSetting> resolvedSetting in resolvedSettings)
                                    {
                                        data[resolvedSetting.Key] = resolvedSetting.Value;
                                    }

                                    continue;
                                }

                                data[setting.Key] = setting;
                            }

                            // The ETag will never be null here because it's not a conditional request
                            // Each successful response should have 200 status code and an ETag
                            pageWatchers.Add(new WatchedPage()
                            {
                                MatchConditions = new MatchConditions { IfNoneMatch = rawResponse.Headers.ETag },
                                LastServerResponseTime = serverResponseTime
                            });
                        }
                    }).ConfigureAwait(false);

                    kvPageWatchers[loadOption] = pageWatchers;
                }
                else
                {
                    Dictionary<string, ConfigurationSetting> resolvedSettings = await LoadSnapshotData(loadOption.SnapshotName, client, cancellationToken).ConfigureAwait(false);

                    foreach (KeyValuePair<string, ConfigurationSetting> resolvedSetting in resolvedSettings)
                    {
                        data[resolvedSetting.Key] = resolvedSetting.Value;
                    }
                }
            }

            if (_options.ExcludeClassicFeatureFlags)
            {
                foreach (string key in data.Keys.ToList())
                {
                    if (ClassicFeatureFlagConverter.IsClassicFeatureFlag(data[key]))
                    {
                        data.Remove(key);
                    }
                }
            }

            return data;
        }

        // Loads classic feature flags (from the ".appconfig.featureflag/" key-value namespace) into `data`
        // as configuration settings. Returns the watched pages per selector.
        private async Task<ClassicFeatureFlagLoadResult> LoadClassicFeatureFlags(
            IAppConfigurationClient client,
            IEnumerable<FeatureFlagSelector> featureFlagSelectors,
            CancellationToken cancellationToken)
        {
            var classicFeatureFlags = new Dictionary<string, ConfigurationSetting>();

            var pages = new Dictionary<FeatureFlagSelector, IEnumerable<WatchedPage>>();

            if (_options.ExcludeClassicFeatureFlags)
            {
                return new ClassicFeatureFlagLoadResult
                {
                    ClassicFeatureFlags = classicFeatureFlags.Values,
                    Pages = pages
                };
            }

            foreach (FeatureFlagSelector ffSelector in featureFlagSelectors)
            {
                var selector = new SettingSelector()
                {
                    KeyFilter = FeatureManagementConstants.FeatureFlagMarker + ffSelector.NameFilter,
                    LabelFilter = ffSelector.LabelFilter
                };

                if (ffSelector.TagFilters != null)
                {
                    foreach (string tagFilter in ffSelector.TagFilters)
                    {
                        selector.TagsFilter.Add(tagFilter);
                    }
                }

                var pageWatchers = new List<WatchedPage>();

                await CallWithRequestTracing(async () =>
                {
                    AsyncPageable<ConfigurationSetting> pageableSettings = client.GetConfigurationSettingsAsync(selector, cancellationToken);

                    await foreach (Page<ConfigurationSetting> page in pageableSettings.AsPages(_options.ConfigurationSettingPageIterator).ConfigureAwait(false))
                    {
                        using Response rawResponse = page.GetRawResponse();
                        DateTimeOffset serverResponseTime = rawResponse.GetMsDate();

                        foreach (ConfigurationSetting setting in page.Values)
                        {
                            classicFeatureFlags[setting.Key] = setting;
                        }

                        // The ETag will never be null here because it's not a conditional request
                        // Each successful response should have 200 status code and an ETag
                        pageWatchers.Add(new WatchedPage()
                        {
                            MatchConditions = new MatchConditions { IfNoneMatch = rawResponse.Headers.ETag },
                            LastServerResponseTime = serverResponseTime
                        });
                    }
                }).ConfigureAwait(false);

                pages[ffSelector] = pageWatchers;
            }

            return new ClassicFeatureFlagLoadResult
            {
                ClassicFeatureFlags = classicFeatureFlags.Values,
                Pages = pages
            };
        }

        // Loads standalone feature flags from the feature-flag endpoint into `featureFlags`.
        // Returns the watched pages per selector.
        private async Task<FeatureFlagLoadResult> LoadFeatureFlags(
            IAppConfigurationClient client,
            IEnumerable<FeatureFlagSelector> featureFlagSelectors,
            CancellationToken cancellationToken)
        {
            var featureFlags = new List<FeatureFlag>();

            var pages = new Dictionary<FeatureFlagSelector, IEnumerable<WatchedPage>>();

            foreach (FeatureFlagSelector ffSelector in featureFlagSelectors)
            {
                var selector = new AppConfigFeatureFlagSelector
                {
                    NameFilter = ffSelector.NameFilter,
                    LabelFilter = ffSelector.LabelFilter
                };

                if (ffSelector.TagFilters != null)
                {
                    foreach (string tag in ffSelector.TagFilters)
                    {
                        selector.TagsFilter.Add(tag);
                    }
                }

                var pageWatchers = new List<WatchedPage>();

                await CallWithRequestTracing(async () =>
                {
                    AsyncPageable<FeatureFlag> pageable = client.GetFeatureFlagsAsync(selector, cancellationToken);

                    await foreach (Page<FeatureFlag> page in pageable.AsPages(_options.FeatureFlagPageIterator).ConfigureAwait(false))
                    {
                        using Response rawResponse = page.GetRawResponse();
                        DateTimeOffset serverResponseTime = rawResponse.GetMsDate();

                        foreach (FeatureFlag ff in page.Values)
                        {
                            featureFlags.Add(ff);
                        }

                        pageWatchers.Add(new WatchedPage()
                        {
                            MatchConditions = new MatchConditions { IfNoneMatch = rawResponse.Headers.ETag },
                            LastServerResponseTime = serverResponseTime
                        });
                    }
                }).ConfigureAwait(false);

                pages[ffSelector] = pageWatchers;
            }

            return new FeatureFlagLoadResult
            {
                FeatureFlags = featureFlags,
                Pages = pages
            };
        }

        private async Task<Dictionary<string, ConfigurationSetting>> LoadSnapshotData(string snapshotName, IAppConfigurationClient client, CancellationToken cancellationToken)
        {
            var resolvedSettings = new Dictionary<string, ConfigurationSetting>();

            Debug.Assert(!string.IsNullOrWhiteSpace(snapshotName));

            ConfigurationSnapshot snapshot = null;

            try
            {
                await CallWithRequestTracing(async () => snapshot = await client.GetSnapshotAsync(snapshotName, cancellationToken: cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
            }
            catch (RequestFailedException rfe) when (rfe.Status == (int)HttpStatusCode.NotFound)
            {

                return resolvedSettings; // Return empty dictionary if snapshot not found
            }

            if (snapshot.SnapshotComposition != SnapshotComposition.Key)
            {
                throw new InvalidOperationException(string.Format(ErrorMessages.SnapshotInvalidComposition, nameof(snapshot.SnapshotComposition), snapshot.Name, snapshot.SnapshotComposition));
            }

            IAsyncEnumerable<ConfigurationSetting> settingsEnumerable = client.GetConfigurationSettingsForSnapshotAsync(
                snapshotName,
                cancellationToken);

            await CallWithRequestTracing(async () =>
            {
                await foreach (ConfigurationSetting setting in settingsEnumerable.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    resolvedSettings[setting.Key] = setting;
                }
            }).ConfigureAwait(false);

            return resolvedSettings;
        }

        private async Task<Dictionary<KeyValueIdentifier, ConfigurationSetting>> LoadIndividualWatchedSettings(
            IAppConfigurationClient client,
            IDictionary<string, ConfigurationSetting> existingSettings,
            CancellationToken cancellationToken)
        {
            var watchedIndividualKvs = new Dictionary<KeyValueIdentifier, ConfigurationSetting>(_watchedIndividualKvs);

            Debug.Assert(!_options.IsAfdUsed || !_options.IndividualKvWatchers.Any());

            foreach (KeyValueWatcher kvWatcher in _options.IndividualKvWatchers)
            {
                string watchedKey = kvWatcher.Key;
                string watchedLabel = kvWatcher.Label;

                var watchedKeyLabel = new KeyValueIdentifier(watchedKey, watchedLabel);

                // Skip the loading for the key-value in case it has already been loaded
                if (existingSettings.TryGetValue(watchedKey, out ConfigurationSetting loadedKv)
                    && watchedKeyLabel.Equals(new KeyValueIdentifier(loadedKv.Key, loadedKv.Label)))
                {
                    // create a new instance to avoid that reference could be modified when mapping data
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
                    // create a new instance to avoid that reference could be modified when mapping data
                    watchedIndividualKvs[watchedKeyLabel] = new ConfigurationSetting(watchedKv.Key, watchedKv.Value, watchedKv.Label, watchedKv.ETag);

                    if (watchedKv.ContentType == SnapshotReferenceConstants.ContentType)
                    {
                        // Track snapshot reference usage for telemetry
                        if (_requestTracingEnabled && _requestTracingOptions != null)
                        {
                            _requestTracingOptions.UsesSnapshotReference = true;
                        }

                        SnapshotReference.SnapshotReference snapshotReference = SnapshotReferenceParser.Parse(watchedKv);

                        Dictionary<string, ConfigurationSetting> resolvedSettings = await LoadSnapshotData(snapshotReference.SnapshotName, client, cancellationToken).ConfigureAwait(false);

                        if (_requestTracingEnabled && _requestTracingOptions != null)
                        {
                            _requestTracingOptions.UsesSnapshotReference = false;
                        }

                        foreach (KeyValuePair<string, ConfigurationSetting> resolvedSetting in resolvedSettings)
                        {
                            existingSettings[resolvedSetting.Key] = resolvedSetting.Value;
                        }
                    }
                    else
                    {
                        existingSettings[watchedKey] = watchedKv;
                    }
                }
            }

            return watchedIndividualKvs;
        }

        private async Task<bool> RefreshIndividualKvWatchers(
            IAppConfigurationClient client,
            List<KeyValueChange> keyValueChanges,
            IEnumerable<KeyValueWatcher> refreshableIndividualKvWatchers,
            Uri endpoint,
            StringBuilder logDebugBuilder,
            StringBuilder logInfoBuilder,
            CancellationToken cancellationToken)
        {
            Debug.Assert(!_options.IsAfdUsed || !_options.IndividualKvWatchers.Any());

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
                    await CallWithRequestTracing(async () =>
                        change = await client.GetKeyValueChange(
                            watchedKv,
                            cancellationToken).ConfigureAwait(false)
                    ).ConfigureAwait(false);
                }
                else
                {
                    // Load the key-value in case the previous load attempts had failed
                    try
                    {
                        await CallWithRequestTracing(async () =>
                            watchedKv = await client.GetConfigurationSettingAsync(
                                watchedKey,
                                watchedLabel,
                                cancellationToken).ConfigureAwait(false)
                        ).ConfigureAwait(false);
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

                    // If the watcher is set to refresh all, or the content type matches the snapshot reference content type then refresh all
                    if (kvWatcher.RefreshAll || watchedKv.ContentType == SnapshotReferenceConstants.ContentType)
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
                FeatureFlagTracing = _options.FeatureFlagTracing,
                IsLoadBalancingEnabled = _options.LoadBalancingEnabled,
                IsAfdUsed = _options.IsAfdUsed
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
            IEnumerable<IAppConfigurationClient> clients,
            Func<IAppConfigurationClient, Task<T>> funcToExecute,
            CancellationToken cancellationToken = default)
        {
            if (_requestTracingEnabled && _requestTracingOptions != null)
            {
                _requestTracingOptions.IsFailoverRequest = false;
            }

            if (_options.LoadBalancingEnabled && _lastSuccessfulEndpoint != null && clients.Count() > 1)
            {
                int nextClientIndex = 0;

                foreach (IAppConfigurationClient clientWrapper in clients)
                {
                    nextClientIndex++;

                    if (clientWrapper.Endpoint == _lastSuccessfulEndpoint)
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

            using IEnumerator<IAppConfigurationClient> clientEnumerator = clients.GetEnumerator();

            clientEnumerator.MoveNext();

            Uri previousEndpoint = clientEnumerator.Current?.Endpoint;
            IAppConfigurationClient currentClient;

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

                    _lastSuccessfulEndpoint = currentClient.Endpoint;
                    _lastSuccessfulAttempt = DateTime.UtcNow;

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
                        _lastFailedAttempt = DateTime.UtcNow;
                        _logger.LogWarning(LogHelper.BuildLastEndpointFailedMessage(previousEndpoint?.ToString()));

                        do
                        {
                            UpdateClientBackoffStatus(currentClient.Endpoint, success);

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

                Uri currentEndpoint = clientEnumerator.Current?.Endpoint;

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
            IEnumerable<IAppConfigurationClient> clients,
            Func<IAppConfigurationClient, Task> funcToExecute,
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
            if (ex.InnerExceptions?.Any(e => e is TaskCanceledException) == true)
            {
                return true;
            }

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

                    _requestTracingOptions.AspireComponentVersion = TracingUtils.GetAssemblyVersion(RequestTracingConstants.AspireComponentAssemblyName);

                    if (TracingUtils.GetAssemblyVersion(RequestTracingConstants.SignalRAssemblyName) != null)
                    {
                        _requestTracingOptions.IsSignalRUsed = true;
                    }
                }
            }
        }

        private void UpdateClientBackoffStatus(Uri endpoint, bool successful)
        {
            if (!_clientBackoffs.TryGetValue(endpoint, out ClientBackoffStatus clientBackoffStatus))
            {
                clientBackoffStatus = new ClientBackoffStatus();
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

            _clientBackoffs[endpoint] = clientBackoffStatus;
        }

        private async Task<bool> HaveCollectionsChanged(
            IEnumerable<KeyValueSelector> selectors,
            Dictionary<KeyValueSelector, IEnumerable<WatchedPage>> pageWatchers,
            IAppConfigurationClient client,
            CancellationToken cancellationToken)
        {
            bool haveCollectionsChanged = false;

            foreach (KeyValueSelector selector in selectors)
            {
                if (pageWatchers.TryGetValue(selector, out IEnumerable<WatchedPage> watchers))
                {
                    await TracingUtils.CallWithRequestTracing(_requestTracingEnabled, RequestType.Watch, _requestTracingOptions,
                        async () => haveCollectionsChanged = await client.HaveCollectionsChanged(
                            selector,
                            watchers,
                            _options.ConfigurationSettingPageIterator,
                            makeConditionalRequest: !_options.IsAfdUsed,
                            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

                    if (haveCollectionsChanged)
                    {
                        return true;
                    }
                }
            }

            return haveCollectionsChanged;
        }

        private async Task<bool> HaveClassicFeatureFlagsChanged(
            IEnumerable<FeatureFlagSelector> selectors,
            Dictionary<FeatureFlagSelector, IEnumerable<WatchedPage>> classicFeatureFlagPageWatchers,
            IAppConfigurationClient client,
            CancellationToken cancellationToken)
        {
            bool haveClassicFeatureFlagsChanged = false;

            foreach (FeatureFlagSelector selector in selectors)
            {
                if (!_options.ExcludeClassicFeatureFlags &&
                    classicFeatureFlagPageWatchers.TryGetValue(selector, out IEnumerable<WatchedPage> classicPages) &&
                    classicPages != null)
                {
                    var classicFfSelector = new KeyValueSelector
                    {
                        KeyFilter = FeatureManagementConstants.FeatureFlagMarker + selector.NameFilter,
                        LabelFilter = selector.LabelFilter,
                        TagFilters = selector.TagFilters,
                    };

                    await TracingUtils.CallWithRequestTracing(_requestTracingEnabled, RequestType.Watch, _requestTracingOptions,
                        async () => haveClassicFeatureFlagsChanged = await client.HaveCollectionsChanged(
                            classicFfSelector,
                            classicPages,
                            _options.ConfigurationSettingPageIterator,
                            makeConditionalRequest: !_options.IsAfdUsed,
                            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

                    if (haveClassicFeatureFlagsChanged)
                    {
                        return true;
                    }
                }
            }

            return haveClassicFeatureFlagsChanged;
        }

        private async Task<bool> HaveFeatureFlagsChanged(
            IEnumerable<FeatureFlagSelector> selectors,
            Dictionary<FeatureFlagSelector, IEnumerable<WatchedPage>> featureFlagPageWatchers,
            IAppConfigurationClient client,
            CancellationToken cancellationToken)
        {
            bool haveFeatureFlagsChanged = false;

            foreach (FeatureFlagSelector selector in selectors)
            {
                if (featureFlagPageWatchers.TryGetValue(selector, out IEnumerable<WatchedPage> featureFlagPages) &&
                    featureFlagPages != null)
                {
                    await TracingUtils.CallWithRequestTracing(_requestTracingEnabled, RequestType.Watch, _requestTracingOptions,
                        async () => haveFeatureFlagsChanged = await client.HaveFeatureFlagsChanged(
                            selector,
                            featureFlagPages,
                            _options.FeatureFlagPageIterator,
                            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

                    if (haveFeatureFlagsChanged)
                    {
                        return true;
                    }
                }
            }

            return haveFeatureFlagsChanged;
        }

        private async Task ProcessKeyValueChangesAsync(
            IEnumerable<KeyValueChange> keyValueChanges,
            Dictionary<string, ConfigurationSetting> mappedData,
            Dictionary<KeyValueIdentifier, ConfigurationSetting> watchedIndividualKvs)
        {
            foreach (KeyValueChange change in keyValueChanges)
            {
                KeyValueIdentifier changeIdentifier = new KeyValueIdentifier(change.Key, change.Label);
                Debug.Assert(watchedIndividualKvs.ContainsKey(changeIdentifier));

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
                        mappedData.Remove(change.Key);
                    }
                    else
                    {
                        mappedData[change.Key] = setting;
                    }
                }
                else if (change.ChangeType == KeyValueChangeType.Deleted)
                {
                    mappedData.Remove(change.Key);

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

        public void Dispose()
        {
            (_clientManager as AppConfigurationClientManager)?.Dispose();
            _activitySource?.Dispose();
        }
    }
}
