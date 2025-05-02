// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// Options used to configure the behavior of an Azure App Configuration provider.         
    /// If neither <see cref="Select"/> nor <see cref="SelectSnapshot"/> is ever called, all key-values with no label are included in the configuration provider.
    /// </summary>
    public class AzureAppConfigurationOptions : IDisposable
    {
        private const int MaxRetries = 2;
        private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan NetworkTimeout = TimeSpan.FromSeconds(10);
        private static readonly KeyValueSelector DefaultQuery = new KeyValueSelector { KeyFilter = KeyFilter.Any, LabelFilter = LabelFilter.Null };

        private List<KeyValueWatcher> _individualKvWatchers = new List<KeyValueWatcher>();
        private List<KeyValueWatcher> _ffWatchers = new List<KeyValueWatcher>();
        private List<IKeyValueAdapter> _adapters;
        private List<Func<ConfigurationSetting, ValueTask<ConfigurationSetting>>> _mappers = new List<Func<ConfigurationSetting, ValueTask<ConfigurationSetting>>>();
        private List<KeyValueSelector> _selectors;
        private IConfigurationRefresher _refresher = new AzureAppConfigurationRefresher();
        private bool _selectCalled = false;
        private HttpClientTransport _clientOptionsTransport = new HttpClientTransport(new HttpClient()
        {
            Timeout = NetworkTimeout
        });

        // The following set is sorted in descending order.
        // Since multiple prefixes could start with the same characters, we need to trim the longest prefix first.
        private SortedSet<string> _keyPrefixes = new SortedSet<string>(Comparer<string>.Create((k1, k2) => -string.Compare(k1, k2, StringComparison.OrdinalIgnoreCase)));

        /// <summary>
        /// Flag to indicate whether replica discovery is enabled.
        /// </summary>
        public bool ReplicaDiscoveryEnabled { get; set; } = true;

        /// <summary>
        /// Flag to indicate whether load balancing is enabled.
        /// </summary>
        public bool LoadBalancingEnabled { get; set; }

        /// <summary>
        /// The list of connection strings used to connect to an Azure App Configuration store and its replicas.
        /// </summary>
        internal IEnumerable<string> ConnectionStrings { get; private set; }

        /// <summary>
        /// The list of endpoints of an Azure App Configuration store.
        /// If this property is set, the <see cref="Credential"/> property also needs to be set.
        /// </summary>
        internal IEnumerable<Uri> Endpoints { get; private set; }

        /// <summary>
        /// The credential used to connect to the Azure App Configuration.
        /// If this property is set, the <see cref="Endpoints"/> property also needs to be set.
        /// </summary>
        internal TokenCredential Credential { get; private set; }

        /// <summary>
        /// A collection of <see cref="KeyValueSelector"/> specified by user.
        /// </summary>
        internal IEnumerable<KeyValueSelector> Selectors => _selectors;

        /// <summary>
        /// Indicates if <see cref="AzureAppConfigurationRefreshOptions.RegisterAll"/> was called.
        /// </summary>
        internal bool RegisterAllEnabled { get; private set; }

        /// <summary>
        /// Refresh interval for selected key-value collections when <see cref="AzureAppConfigurationRefreshOptions.RegisterAll"/> is called.
        /// </summary>
        internal TimeSpan KvCollectionRefreshInterval { get; private set; }

        /// <summary>
        /// A collection of <see cref="KeyValueWatcher"/>.
        /// </summary>
        internal IEnumerable<KeyValueWatcher> IndividualKvWatchers => _individualKvWatchers;

        /// <summary>
        /// A collection of <see cref="KeyValueWatcher"/>.
        /// </summary>
        internal IEnumerable<KeyValueWatcher> FeatureFlagWatchers => _ffWatchers;

        /// <summary>
        /// A collection of <see cref="IKeyValueAdapter"/>.
        /// </summary>
        internal IEnumerable<IKeyValueAdapter> Adapters
        {
            get => _adapters;
            set => _adapters = value?.ToList();
        }

        /// <summary>
        /// A collection of user defined functions that transform each <see cref="ConfigurationSetting"/>.
        /// </summary>
        internal IEnumerable<Func<ConfigurationSetting, ValueTask<ConfigurationSetting>>> Mappers => _mappers;

        /// <summary>
        /// A collection of key prefixes to be trimmed.
        /// </summary>
        internal IEnumerable<string> KeyPrefixes => _keyPrefixes;

        /// <summary>
        /// For use in tests only. An optional configuration client manager that can be used to provide clients to communicate with Azure App Configuration.
        /// </summary>
        internal IConfigurationClientManager ClientManager { get; set; }

        /// <summary>
        /// For use in tests only. An optional class used to process pageable results from Azure App Configuration.
        /// </summary>
        internal IConfigurationSettingPageIterator ConfigurationSettingPageIterator { get; set; }

        /// <summary>
        /// An optional timespan value to set the minimum backoff duration to a value other than the default.
        /// </summary>
        internal TimeSpan MinBackoffDuration { get; set; } = FailOverConstants.MinBackoffDuration;

        /// <summary>
        /// Options used to configure the client used to communicate with Azure App Configuration.
        /// </summary>
        internal ConfigurationClientOptions ClientOptions { get; }

        /// <summary>
        /// Flag to indicate whether Key Vault options have been configured.
        /// </summary>
        internal bool IsKeyVaultConfigured { get; private set; } = false;

        /// <summary>
        /// Flag to indicate whether Key Vault secret values will be refreshed automatically.
        /// </summary>
        internal bool IsKeyVaultRefreshConfigured { get; private set; } = false;

        /// <summary>
        /// Indicates all feature flag features used by the application.
        /// </summary>
        internal FeatureFlagTracing FeatureFlagTracing { get; set; } = new FeatureFlagTracing();

        /// <summary>
        /// Options used to configure provider startup.
        /// </summary>
        internal StartupOptions Startup { get; set; } = new StartupOptions();

        /// <summary>
        /// Initializes a new instance of the <see cref="AzureAppConfigurationOptions"/> class.
        /// </summary>
        public AzureAppConfigurationOptions()
        {
            _adapters = new List<IKeyValueAdapter>()
            {
                new AzureKeyVaultKeyValueAdapter(new AzureKeyVaultSecretProvider()),
                new JsonKeyValueAdapter(),
                new FeatureManagementKeyValueAdapter(FeatureFlagTracing)
            };

            // Adds the default query to App Configuration if <see cref="Select"/> and <see cref="SelectSnapshot"/> are never called.
            _selectors = new List<KeyValueSelector> { DefaultQuery };

            ClientOptions = GetDefaultClientOptions();
        }

        /// <summary>
        /// Specify what key-values to include in the configuration provider.
        /// <see cref="Select"/> can be called multiple times to include multiple sets of key-values.
        /// </summary>
        /// <param name="keyFilter">
        /// The key filter to apply when querying Azure App Configuration for key-values.
        /// An asterisk (*) can be added to the end to return all key-values whose key begins with the key filter.
        /// e.g. key filter `abc*` returns all key-values whose key starts with `abc`.
        /// A comma (,) can be used to select multiple key-values. Comma separated filters must exactly match a key to select it.
        /// Using asterisk to select key-values that begin with a key filter while simultaneously using comma separated key filters is not supported.
        /// E.g. the key filter `abc*,def` is not supported. The key filters `abc*` and `abc,def` are supported.
        /// For all other cases the characters: asterisk (*), comma (,), and backslash (\) are reserved. Reserved characters must be escaped using a backslash (\).
        /// e.g. the key filter `a\\b\,\*c*` returns all key-values whose key starts with `a\b,*c`.
        /// Built-in key filter options: <see cref="KeyFilter"/>.
        /// </param>
        /// <param name="labelFilter">
        /// The label filter to apply when querying Azure App Configuration for key-values. By default the null label will be used. Built-in label filter options: <see cref="LabelFilter"/>
        /// The characters asterisk (*) and comma (,) are not supported. Backslash (\) character is reserved and must be escaped using another backslash (\).
        /// </param>
        /// <param name="tagFilters">
        /// In addition to key and label filters, key-values from Azure App Configuration can be filtered based on their tag names and values.
        /// Each tag filter must follow the format "tagName=tagValue". Only those key-values will be loaded whose tags match all the tags provided here.
        /// Built in tag filter values: <see cref="TagValue"/>. For example, $"tagName={<see cref="TagValue.Null"/>}".
        /// The characters asterisk (*), comma (,) and backslash (\) are reserved and must be escaped using a backslash (\).
        /// Up to 5 tag filters can be provided. If no tag filters are provided, key-values will not be filtered based on tags.
        /// </param>
        public AzureAppConfigurationOptions Select(string keyFilter, string labelFilter = LabelFilter.Null, IEnumerable<string> tagFilters = null)
        {
            if (string.IsNullOrEmpty(keyFilter))
            {
                throw new ArgumentNullException(nameof(keyFilter));
            }

            // Do not support * and , for label filter for now.
            if (labelFilter != null && (labelFilter.Contains('*') || labelFilter.Contains(',')))
            {
                throw new ArgumentException("The characters '*' and ',' are not supported in label filters.", nameof(labelFilter));
            }

            if (string.IsNullOrWhiteSpace(labelFilter))
            {
                labelFilter = LabelFilter.Null;
            }

            if (tagFilters != null)
            {
                foreach (string tag in tagFilters)
                {
                    if (string.IsNullOrEmpty(tag) || !tag.Contains('=') || tag.IndexOf('=') == 0)
                    {
                        throw new ArgumentException($"Tag filter '{tag}' does not follow the format \"tagName=tagValue\".", nameof(tagFilters));
                    }
                }
            }

            if (!_selectCalled)
            {
                _selectors.Remove(DefaultQuery);

                _selectCalled = true;
            }

            _selectors.AppendUnique(new KeyValueSelector
            {
                KeyFilter = keyFilter,
                LabelFilter = labelFilter,
                TagFilters = tagFilters
            });

            return this;
        }

        /// <summary>
        /// Specify a snapshot and include its contained key-values in the configuration provider.
        /// <see cref="SelectSnapshot"/> can be called multiple times to include key-values from multiple snapshots.
        /// </summary>
        /// <param name="name">The name of the snapshot in Azure App Configuration.</param>
        public AzureAppConfigurationOptions SelectSnapshot(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (!_selectCalled)
            {
                _selectors.Remove(DefaultQuery);

                _selectCalled = true;
            }

            _selectors.AppendUnique(new KeyValueSelector
            {
                SnapshotName = name
            });

            return this;
        }

        /// <summary>
        /// Configures options for Azure App Configuration feature flags that will be parsed and transformed into feature management configuration.
        /// If no filtering is specified via the <see cref="FeatureFlagOptions"/> then all feature flags with no label are loaded.
        /// All loaded feature flags will be automatically registered for refresh as a collection.
        /// </summary>
        /// <param name="configure">A callback used to configure feature flag options.</param>
        public AzureAppConfigurationOptions UseFeatureFlags(Action<FeatureFlagOptions> configure = null)
        {
            FeatureFlagOptions options = new FeatureFlagOptions();
            configure?.Invoke(options);

            if (options.RefreshInterval < RefreshConstants.MinimumFeatureFlagRefreshInterval)
            {
                throw new ArgumentOutOfRangeException(nameof(options.RefreshInterval), options.RefreshInterval.TotalMilliseconds,
                    string.Format(ErrorMessages.RefreshIntervalTooShort, RefreshConstants.MinimumFeatureFlagRefreshInterval.TotalMilliseconds));
            }

            if (options.FeatureFlagSelectors.Count() != 0 && options.Label != null)
            {
                throw new InvalidOperationException($"Please select feature flags by either the {nameof(options.Select)} method or by setting the {nameof(options.Label)} property, not both.");
            }

            if (options.FeatureFlagSelectors.Count() == 0)
            {
                // Select clause is not present
                options.FeatureFlagSelectors.Add(new KeyValueSelector
                {
                    KeyFilter = FeatureManagementConstants.FeatureFlagMarker + "*",
                    LabelFilter = string.IsNullOrWhiteSpace(options.Label) ? LabelFilter.Null : options.Label,
                    IsFeatureFlagSelector = true
                });
            }

            foreach (KeyValueSelector featureFlagSelector in options.FeatureFlagSelectors)
            {
                _selectors.AppendUnique(featureFlagSelector);

                _ffWatchers.AppendUnique(new KeyValueWatcher
                {
                    Key = featureFlagSelector.KeyFilter,
                    Label = featureFlagSelector.LabelFilter,
                    Tags = featureFlagSelector.TagFilters,
                    // If UseFeatureFlags is called multiple times for the same key and label filters, last refresh interval wins
                    RefreshInterval = options.RefreshInterval
                });
            }

            return this;
        }

        /// <summary>
        /// Connect the provider to the Azure App Configuration service via a connection string.
        /// </summary>
        /// <param name="connectionString">
        /// Used to authenticate with Azure App Configuration.
        /// </param>
        public AzureAppConfigurationOptions Connect(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentNullException(nameof(connectionString));
            }

            return Connect(new List<string> { connectionString });
        }

        /// <summary>
        /// Connect the provider to an Azure App Configuration store and its replicas via a list of connection strings.
        /// </summary>
        /// <param name="connectionStrings">
        /// Used to authenticate with Azure App Configuration.
        /// </param>
        public AzureAppConfigurationOptions Connect(IEnumerable<string> connectionStrings)
        {
            if (connectionStrings == null || !connectionStrings.Any())
            {
                throw new ArgumentNullException(nameof(connectionStrings));
            }

            if (connectionStrings.Distinct().Count() != connectionStrings.Count())
            {
                throw new ArgumentException($"All values in '{nameof(connectionStrings)}' must be unique.");
            }

            Endpoints = null;
            Credential = null;
            ConnectionStrings = connectionStrings;
            return this;
        }

        /// <summary>
        /// Connect the provider to Azure App Configuration using endpoint and token credentials.
        /// </summary>
        /// <param name="endpoint">The endpoint of the Azure App Configuration to connect to.</param>
        /// <param name="credential">Token credentials to use to connect.</param>
        public AzureAppConfigurationOptions Connect(Uri endpoint, TokenCredential credential)
        {
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            if (credential == null)
            {
                throw new ArgumentNullException(nameof(credential));
            }

            return Connect(new List<Uri>() { endpoint }, credential);
        }

        /// <summary>
        /// Connect the provider to an Azure App Configuration store and its replicas using a list of endpoints and a token credential.
        /// </summary>
        /// <param name="endpoints">The list of endpoints of an Azure App Configuration store and its replicas to connect to.</param>
        /// <param name="credential">Token credential to use to connect.</param>
        public AzureAppConfigurationOptions Connect(IEnumerable<Uri> endpoints, TokenCredential credential)
        {
            if (endpoints == null || !endpoints.Any())
            {
                throw new ArgumentNullException(nameof(endpoints));
            }

            if (endpoints.Distinct(new EndpointComparer()).Count() != endpoints.Count())
            {
                throw new ArgumentException($"All values in '{nameof(endpoints)}' must be unique.");
            }

            Credential = credential ?? throw new ArgumentNullException(nameof(credential));

            Endpoints = endpoints;
            ConnectionStrings = null;
            return this;
        }

        /// <summary>
        /// Trims the provided prefix from the keys of all key-values retrieved from Azure App Configuration.
        /// </summary>
        /// <param name="prefix">The prefix to be trimmed.</param>
        public AzureAppConfigurationOptions TrimKeyPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix))
            {
                throw new ArgumentNullException(nameof(prefix));
            }

            _keyPrefixes.Add(prefix);
            return this;
        }

        /// <summary>
        /// Configure the client(s) used to communicate with Azure App Configuration.
        /// </summary>
        /// <param name="configure">A callback used to configure Azure App Configuration client options.</param>
        public AzureAppConfigurationOptions ConfigureClientOptions(Action<ConfigurationClientOptions> configure)
        {
            configure?.Invoke(ClientOptions);
            return this;
        }

        /// <summary>
        /// Configure refresh for key-values in the configuration provider.
        /// </summary>
        /// <param name="configure">A callback used to configure Azure App Configuration refresh options.</param>
        public AzureAppConfigurationOptions ConfigureRefresh(Action<AzureAppConfigurationRefreshOptions> configure)
        {
            if (RegisterAllEnabled)
            {
                throw new InvalidOperationException($"{nameof(ConfigureRefresh)}() cannot be invoked multiple times when {nameof(AzureAppConfigurationRefreshOptions.RegisterAll)} has been invoked.");
            }

            var refreshOptions = new AzureAppConfigurationRefreshOptions();
            configure?.Invoke(refreshOptions);

            bool isRegisterCalled = refreshOptions.RefreshRegistrations.Any();
            RegisterAllEnabled = refreshOptions.RegisterAllEnabled;

            if (!isRegisterCalled && !RegisterAllEnabled)
            {
                throw new InvalidOperationException($"{nameof(ConfigureRefresh)}() must call either {nameof(AzureAppConfigurationRefreshOptions.Register)}()" +
                    $" or {nameof(AzureAppConfigurationRefreshOptions.RegisterAll)}()");
            }

            // Check if both register methods are called at any point
            if (RegisterAllEnabled && (_individualKvWatchers.Any() || isRegisterCalled))
            {
                throw new InvalidOperationException($"Cannot call both {nameof(AzureAppConfigurationRefreshOptions.RegisterAll)} and "
                + $"{nameof(AzureAppConfigurationRefreshOptions.Register)}.");
            }

            if (RegisterAllEnabled)
            {
                KvCollectionRefreshInterval = refreshOptions.RefreshInterval;
            }
            else
            {
                foreach (KeyValueWatcher item in refreshOptions.RefreshRegistrations)
                {
                    item.RefreshInterval = refreshOptions.RefreshInterval;
                    _individualKvWatchers.Add(item);
                }
            }

            return this;
        }

        /// <summary>
        /// Get an instance of <see cref="IConfigurationRefresher"/> that can be used to trigger a refresh for the registered key-values.
        /// </summary>
        /// <returns>An instance of <see cref="IConfigurationRefresher"/>.</returns>
        public IConfigurationRefresher GetRefresher()
        {
            return _refresher;
        }

        /// <summary>
        /// Configures the Azure App Configuration provider to use the provided Key Vault configuration to resolve key vault references.
        /// </summary>
        /// <param name="configure">A callback used to configure Azure App Configuration key vault options.</param>
        public AzureAppConfigurationOptions ConfigureKeyVault(Action<AzureAppConfigurationKeyVaultOptions> configure)
        {
            var keyVaultOptions = new AzureAppConfigurationKeyVaultOptions();
            configure?.Invoke(keyVaultOptions);

            if (keyVaultOptions.Credential != null && keyVaultOptions.SecretResolver != null)
            {
                throw new InvalidOperationException($"Cannot configure both default credentials and secret resolver for Key Vault references. Please call either {nameof(keyVaultOptions.SetCredential)} or {nameof(keyVaultOptions.SetSecretResolver)} method, not both.");
            }

            _adapters.RemoveAll(a => a is AzureKeyVaultKeyValueAdapter);
            _adapters.Add(new AzureKeyVaultKeyValueAdapter(new AzureKeyVaultSecretProvider(keyVaultOptions)));

            IsKeyVaultRefreshConfigured = keyVaultOptions.IsKeyVaultRefreshConfigured;
            IsKeyVaultConfigured = true;
            return this;
        }

        /// <summary>
        /// Provides a way to transform settings retrieved from App Configuration before they are processed by the configuration provider.
        /// </summary>
        /// <param name="mapper">A callback registered by the user to transform each configuration setting.</param>
        public AzureAppConfigurationOptions Map(Func<ConfigurationSetting, ValueTask<ConfigurationSetting>> mapper)
        {
            if (mapper == null)
            {
                throw new ArgumentNullException(nameof(mapper));
            }

            _mappers.Add(mapper);
            return this;
        }

        /// <summary>
        /// Configure the provider behavior when loading data from Azure App Configuration on startup.
        /// </summary>
        /// <param name="configure">A callback used to configure Azure App Configuration startup options.</param>
        public AzureAppConfigurationOptions ConfigureStartupOptions(Action<StartupOptions> configure)
        {
            configure?.Invoke(Startup);
            return this;
        }

        private ConfigurationClientOptions GetDefaultClientOptions()
        {
            var clientOptions = new ConfigurationClientOptions(ConfigurationClientOptions.ServiceVersion.V2023_10_01);
            clientOptions.Retry.MaxRetries = MaxRetries;
            clientOptions.Retry.MaxDelay = MaxRetryDelay;
            clientOptions.Retry.Mode = RetryMode.Exponential;
            clientOptions.AddPolicy(new UserAgentHeaderPolicy(), HttpPipelinePosition.PerCall);
            clientOptions.Transport = _clientOptionsTransport;

            return clientOptions;
        }

        /// <summary>
        /// Disposes of this instance of <see cref="AzureAppConfigurationOptions"/> and any resources it holds.
        /// </summary>
        public void Dispose()
        {
            if (_clientOptionsTransport != null)
            {
                _clientOptionsTransport.Dispose();
            }
        }
    }
}
