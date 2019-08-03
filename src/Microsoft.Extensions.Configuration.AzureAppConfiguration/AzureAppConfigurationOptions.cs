namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    using Microsoft.Azure.AppConfiguration.Azconfig;
    using Microsoft.Azure.AppConfiguration.ManagedIdentityConnector;
    using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
    using Microsoft.Extensions.Configuration.AzureAppConfiguration.Models;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Options used to configure the behavior of an Azure App Configuration provider.
    /// </summary>
    public class AzureAppConfigurationOptions
    {
        internal static readonly TimeSpan DefaultFeatureFlagsCacheExpiration = TimeSpan.FromSeconds(30);
        internal static readonly TimeSpan MinimumFeatureFlagsCacheExpiration = TimeSpan.FromMilliseconds(1000);

        private Dictionary<string, KeyValueWatcher> _changeWatchers = new Dictionary<string, KeyValueWatcher>();
        private List<KeyValueWatcher> _multiKeyWatchers = new List<KeyValueWatcher>();
        private List<IKeyValueAdapter> _adapters = new List<IKeyValueAdapter>();
        private List<KeyValueSelector> _kvSelectors = new List<KeyValueSelector>();
        private IConfigurationRefresher _refresher = new AzureAppConfigurationRefresher();

        private SortedSet<string> _keyPrefixes = new SortedSet<string>(Comparer<string>.Create((k1, k2) => -string.Compare(k1, k2, StringComparison.InvariantCultureIgnoreCase)));

        /// <summary>
        /// A collection of <see cref="KeyValueSelector"/>.
        /// </summary>
        public IEnumerable<KeyValueSelector> KeyValueSelectors => _kvSelectors;

        /// <summary>
        /// A collection of <see cref="KeyValueWatcher"/>.
        /// </summary>
        internal IEnumerable<KeyValueWatcher> ChangeWatchers => _changeWatchers.Values;

        /// <summary>
        /// A collection of <see cref="KeyValueWatcher"/>.
        /// </summary>
        internal IEnumerable<KeyValueWatcher> MultiKeyWatchers => _multiKeyWatchers;

        /// <summary>
        /// A collection of <see cref="KeyValueWatcher"/>.
        /// </summary>
        internal IEnumerable<IKeyValueAdapter> Adapters => _adapters;

        /// <summary>
        /// A collection of key prefixes to be trimmed.
        /// </summary>
        internal IEnumerable<string> KeyPrefixes => _keyPrefixes;

        /// <summary>
        /// An offline cache provider which can be used to enable offline data retrieval and storage.
        /// </summary>
        public IOfflineCache OfflineCache { get; private set; }

        /// <summary>
        /// The connection string to use to connect to Azure App Configuration.
        /// </summary>
        public string ConnectionString { get; set; }

        /// <summary>
        /// An optional client that can be used to communicate with Azure App Configuration. If provided, the connection string property will be ignored.
        /// </summary>
        internal AzconfigClient Client { get; set; }

        /// <summary>
        /// Specify what key-values to include in the configuration provider.
        /// <see cref="Use"/> can be called multiple times to include multiple sets of key-values.
        /// </summary>
        /// <param name="keyFilter">
        /// The key filter to apply when querying Azure App Configuration for key-values. Built-in key filter options: <see cref="KeyFilter"/>
        /// </param>
        /// <param name="labelFilter">
        /// The label filter to apply when querying Azure App Configuration for key-values. By default the null label will be used. Built-in label filter options: <see cref="LabelFilter"/>
        /// Does not support '*' and ','.
        /// </param>
        /// <param name="preferredDateTime">
        /// Used to query key-values in the state that they existed at the time provided.
        /// </param>
        public AzureAppConfigurationOptions Use(string keyFilter, string labelFilter = LabelFilter.Null, DateTimeOffset? preferredDateTime = null)
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

            if (!_kvSelectors.Any(s => s.KeyFilter.Equals(keyFilter) && s.LabelFilter.Equals(labelFilter) && Nullable<DateTimeOffset>.Equals(s.PreferredDateTime, preferredDateTime)))
            {
                _kvSelectors.Add(new KeyValueSelector
                {
                    KeyFilter = keyFilter,
                    LabelFilter = labelFilter,
                    PreferredDateTime = preferredDateTime
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

            if (options.CacheExpirationTime < MinimumFeatureFlagsCacheExpiration)
            {
                throw new ArgumentOutOfRangeException(nameof(options.CacheExpirationTime), options.CacheExpirationTime.TotalMilliseconds,
                    string.Format(Constants.ErrorMessages.CacheExpirationTimeTooShort, MinimumFeatureFlagsCacheExpiration.TotalMilliseconds));
            }

            if (!(_kvSelectors.Any(selector => selector.KeyFilter.StartsWith(FeatureManagementConstants.FeatureFlagMarker) && selector.LabelFilter.Equals(options.Label))))
            {
                Use(FeatureManagementConstants.FeatureFlagMarker + "*", options.Label);
            }

            if (!_adapters.Any(a => a is FeatureManagementKeyValueAdapter))
            {
                _adapters.Add(new FeatureManagementKeyValueAdapter());
            }

            if (!_multiKeyWatchers.Any(kw => kw.Key.Equals(FeatureManagementConstants.FeatureFlagMarker)))
            {
                _multiKeyWatchers.Add(new KeyValueWatcher
                {
                    Key = FeatureManagementConstants.FeatureFlagMarker,
                    Label = options.Label,
                    CacheExpirationTime = options.CacheExpirationTime
                });
            }

            return this;
        }

        /// <summary>
        /// Use an offline file cache to store Azure App Configuration data or retrieve previously stored data during offline periods.
        /// </summary>
        /// <param name="offlineCache">The offline file cache to use for storing/retrieving Azure App Configuration data.</param>
        public AzureAppConfigurationOptions SetOfflineCache(IOfflineCache offlineCache)
        {
            OfflineCache = offlineCache ?? throw new ArgumentNullException(nameof(offlineCache));

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
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentNullException(nameof(connectionString));
            }

            ConnectionString = connectionString;
            return this;
        }


        /// <summary>
        /// Connect the provider to Azure App Configuration using the managed identity of an Azure resource.
        /// </summary>
        /// <param name="endpoint">
        /// The endpoint of the Azure App Configuration store to connect to.
        /// </param>
        public AzureAppConfigurationOptions ConnectWithManagedIdentity(string endpoint)
        {
            if (string.IsNullOrEmpty(endpoint))
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri uri))
            {
                throw new ArgumentException(nameof(endpoint));
            }

            Client = AzconfigClientFactory.CreateClient(uri, new AzconfigClientFactoryOptions()
            {
                Permissions = Permissions.Read
            }).ConfigureAwait(false).GetAwaiter().GetResult();

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
        /// Configure refresh for key-values in the configuration provider.
        /// </summary>
        /// <param name="configure">>A callback used to configure Azure App Configuration refresh options.</param>
        public AzureAppConfigurationOptions ConfigureRefresh(Action<AzureAppConfigurationRefreshOptions> configure)
        {
            var options = new AzureAppConfigurationRefreshOptions();
            configure?.Invoke(options);

            foreach (var item in options.RefreshRegistrations)
            {
                item.Value.CacheExpirationTime = options.CacheExpirationTime;
                _changeWatchers[item.Key] = item.Value;
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
    }
}
