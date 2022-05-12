// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Core;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// Options used to configure the behavior of an Azure App Configuration provider.
    /// </summary>
    public class AzureAppConfigurationOptions
    {
        private const int MaxRetries = 2;
        private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(1);

        private List<KeyValueWatcher> _changeWatchers = new List<KeyValueWatcher>();
        private List<KeyValueWatcher> _multiKeyWatchers = new List<KeyValueWatcher>();
        private List<IKeyValueAdapter> _adapters = new List<IKeyValueAdapter>() 
        { 
            new AzureKeyVaultKeyValueAdapter(new AzureKeyVaultSecretProvider()),
            new JsonKeyValueAdapter() 
        };
        private List<KeyValueSelector> _kvSelectors = new List<KeyValueSelector>();
        private IConfigurationRefresher _refresher = new AzureAppConfigurationRefresher();

        // The following set is sorted in descending order.
        // Since multiple prefixes could start with the same characters, we need to trim the longest prefix first.
        private SortedSet<string> _keyPrefixes = new SortedSet<string>(Comparer<string>.Create((k1, k2) => -string.Compare(k1, k2, StringComparison.OrdinalIgnoreCase)));

        /// <summary>
        /// The connection string to use to connect to Azure App Configuration.
        /// </summary>
        internal string ConnectionString { get; private set; }

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
        /// A collection of <see cref="KeyValueSelector"/>.
        /// </summary>
        public IEnumerable<KeyValueSelector> KeyValueSelectors => _kvSelectors;

        /// <summary>
        /// A collection of <see cref="KeyValueWatcher"/>.
        /// </summary>
        internal IEnumerable<KeyValueWatcher> ChangeWatchers => _changeWatchers;

        /// <summary>
        /// A collection of <see cref="KeyValueWatcher"/>.
        /// </summary>
        internal IEnumerable<KeyValueWatcher> MultiKeyWatchers => _multiKeyWatchers;

        /// <summary>
        /// A collection of <see cref="IKeyValueAdapter"/>.
        /// </summary>
        internal IEnumerable<IKeyValueAdapter> Adapters
        {
            get => _adapters;
            set => _adapters = value?.ToList();
        }

        /// <summary>
        /// A collection of key prefixes to be trimmed.
        /// </summary>
        internal IEnumerable<string> KeyPrefixes => _keyPrefixes;

        /// <summary>
        /// An optional configuration client provider that can be used to provide clients to communicate with Azure App Configuration.
        /// </summary>
        internal IConfigurationClientProvider ClientProvider { get; set; }

        /// <summary>
        /// Options used to configure the client used to communicate with Azure App Configuration.
        /// </summary>
        internal ConfigurationClientOptions ClientOptions { get; } = GetDefaultClientOptions();

        /// <summary>
        /// Flag to indicate whether Key Vault options have been configured.
        /// </summary>
        internal bool IsKeyVaultConfigured { get; private set; } = false;

        /// <summary>
        /// Flag to indicate whether Key Vault secret values will be refreshed automatically.
        /// </summary>
        internal bool IsKeyVaultRefreshConfigured { get; private set; } = false;

        /// <summary>
        /// This value indicates the feature management schema version being used. 
        /// </summary>
        internal string FeatureManagementSchemaVersion { get; private set; }

        /// <summary>
        /// Specify what key-values to include in the configuration provider.
        /// <see cref="Select"/> can be called multiple times to include multiple sets of key-values.
        /// </summary>
        /// <param name="keyFilter">
        /// The key filter to apply when querying Azure App Configuration for key-values.
        /// An asterisk (*) can be added to the end to return all key-values whose key begins with the key filter.
        /// e.g. key filter `abc*` returns all key-values whose key starts with `abc`.
        /// For all other cases the characters: asterisk (*), comma (,), and backslash (\) are reserved. Reserved characters must be escaped using a backslash (\).
        /// e.g. key filter `a\\b\,\*c*` returns all key-values whose key starts with `a\b,*c`.
        /// Built-in key filter options: <see cref="KeyFilter"/>.
        /// </param>
        /// <param name="labelFilter">
        /// The label filter to apply when querying Azure App Configuration for key-values. By default the null label will be used. Built-in label filter options: <see cref="LabelFilter"/>
        /// The characters asterisk (*) and comma (,) are not supported. Backslash (\) character is reserved and must be escaped using another backslash (\).
        /// </param>
        public AzureAppConfigurationOptions Select(string keyFilter, string labelFilter = LabelFilter.Null)
        {
            if (string.IsNullOrEmpty(keyFilter))
            {
                throw new ArgumentNullException(nameof(keyFilter));
            }

            if (labelFilter == null)
            {
                labelFilter = LabelFilter.Null;
            }

            // Do not support * and , for label filter for now.
            if (labelFilter.Contains('*') || labelFilter.Contains(','))
            {
                throw new ArgumentException("The characters '*' and ',' are not supported in label filters.", nameof(labelFilter));
            }

            if (!_kvSelectors.Any(s => s.KeyFilter.Equals(keyFilter) && s.LabelFilter.Equals(labelFilter)))
            {
                _kvSelectors.Add(new KeyValueSelector
                {
                    KeyFilter = keyFilter,
                    LabelFilter = labelFilter
                });
            }

            return this;
        }

        /// <summary>
        /// Enables Azure App Configuration feature flags to be parsed and transformed into feature management configuration.
        /// </summary>
        /// <param name="configure">A callback used to configure feature flag options.</param>
        public AzureAppConfigurationOptions UseFeatureFlags(Action<FeatureFlagOptions> configure = null)
        {
            FeatureFlagOptions options = new FeatureFlagOptions();
            configure?.Invoke(options);

            if (options.CacheExpirationInterval < RefreshConstants.MinimumFeatureFlagsCacheExpirationInterval)
            {
                throw new ArgumentOutOfRangeException(nameof(options.CacheExpirationInterval), options.CacheExpirationInterval.TotalMilliseconds,
                    string.Format(ErrorMessages.CacheExpirationTimeTooShort, RefreshConstants.MinimumFeatureFlagsCacheExpirationInterval.TotalMilliseconds));
            }

            if (options.FeatureFlagSelectors.Count() != 0 && options.Label != null)
            {
                throw new InvalidOperationException($"Please select feature flags by either the {nameof(options.Select)} method or by setting the {nameof(options.Label)} property, not both.");
            }

            if (!_adapters.Any(a => a is FeatureFlagKeyValueAdapter))
            {
                // Read Feature Management schema version from environment variables
                string featureManagementSchemaVersion = null;

                try
                {
                    featureManagementSchemaVersion = Environment.GetEnvironmentVariable(FeatureManagementConstants.FeatureManagementSchemaEnvironmentVariable);
                }
                catch (SecurityException) { }

                if (featureManagementSchemaVersion == FeatureManagementConstants.FeatureManagementSchemaV1 ||
                    featureManagementSchemaVersion == FeatureManagementConstants.FeatureManagementSchemaV2)
                {
                    FeatureManagementSchemaVersion = featureManagementSchemaVersion;
                }
                else
                {
                    FeatureManagementSchemaVersion = FeatureManagementConstants.FeatureManagementDefaultSchema;
                }

                _adapters.Add(new FeatureFlagKeyValueAdapter(FeatureManagementSchemaVersion));

                if (FeatureManagementSchemaVersion == FeatureManagementConstants.FeatureManagementSchemaV2)
                {
                    _adapters.Add(new DynamicFeatureKeyValueAdapter());
                }
            }

            if (options.FeatureFlagSelectors.Count() == 0)
            {
                // Select clause is not present
                options.FeatureFlagSelectors.Add(new KeyValueSelector
                {
                    KeyFilter = FeatureManagementConstants.FeatureFlagMarker + "*",
                    LabelFilter = options.Label == null ? LabelFilter.Null : options.Label
                });  
            }

            foreach (var featureFlagSelector in options.FeatureFlagSelectors)
            {
                var featureFlagFilter = featureFlagSelector.KeyFilter;
                var labelFilter = featureFlagSelector.LabelFilter;

                if (!_kvSelectors.Any(selector => selector.KeyFilter == featureFlagFilter && selector.LabelFilter == labelFilter))
                {
                    Select(featureFlagFilter, labelFilter);
                }

                var multiKeyWatcher = _multiKeyWatchers.FirstOrDefault(kw => kw.Key.Equals(featureFlagFilter) && kw.Label.NormalizeNull() == labelFilter.NormalizeNull());

                if (multiKeyWatcher == null)
                {
                    _multiKeyWatchers.Add(new KeyValueWatcher
                    {
                        Key = featureFlagFilter,
                        Label = labelFilter,
                        CacheExpirationInterval = options.CacheExpirationInterval
                    });
                }
                else
                {
                    // If UseFeatureFlags is called multiple times for the same key and label filters, last cache expiration time wins
                    multiKeyWatcher.CacheExpirationInterval = options.CacheExpirationInterval;
                }
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

            Endpoints = null;
            Credential = null;
            ConnectionString = connectionString;
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
        /// Connect the provider to Azure App Configuration and their replicas using list of endpoints and token credential.
        /// </summary>
        /// <param name="endpoints">The list of endpoints of an Azure App Configuration store and its replicas to connect to.</param>
        /// <param name="credential">Token credential to use to connect.</param>
        public AzureAppConfigurationOptions Connect(IEnumerable<Uri> endpoints, TokenCredential credential)
        {
            if (endpoints == null || !endpoints.Any())
            {
                throw new ArgumentNullException(nameof(endpoints));
            }

            if (endpoints.Distinct().Count() != endpoints.Count())
            {
                throw new ArgumentException($"All values in '{nameof(endpoints)}' must be unique.");
            }

            Credential = credential ?? throw new ArgumentNullException(nameof(credential));

            Endpoints = endpoints;
            ConnectionString = null;
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
        /// Configure the client used to communicate with Azure App Configuration.
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
            var refreshOptions = new AzureAppConfigurationRefreshOptions();
            configure?.Invoke(refreshOptions);

            if (!refreshOptions.RefreshRegistrations.Any())
            {
                throw new ArgumentException($"{nameof(ConfigureRefresh)}() must have at least one key-value registered for refresh.");
            }

            foreach (var item in refreshOptions.RefreshRegistrations)
            {
                item.CacheExpirationInterval = refreshOptions.CacheExpirationInterval;
                _changeWatchers.Add(item);
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

        private static ConfigurationClientOptions GetDefaultClientOptions()
        {
            var clientOptions = new ConfigurationClientOptions(ConfigurationClientOptions.ServiceVersion.V1_0);
            clientOptions.Retry.MaxRetries = MaxRetries;
            clientOptions.Retry.MaxDelay = MaxRetryDelay;
            clientOptions.Retry.Mode = RetryMode.Exponential;
            clientOptions.AddPolicy(new UserAgentHeaderPolicy(), HttpPipelinePosition.PerCall);

            return clientOptions;
        }
    }
}
