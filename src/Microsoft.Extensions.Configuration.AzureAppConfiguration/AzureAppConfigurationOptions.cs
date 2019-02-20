namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    using Microsoft.Azure.AppConfiguration.Azconfig;
    using Microsoft.Azure.AppConfiguration.ManagedIdentityConnector;
    using Microsoft.Extensions.Configuration.AzureAppConfiguration.Models;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public class AzureAppConfigurationOptions
    {
        private Dictionary<string, KeyValueWatcher> _changeWatchers = new Dictionary<string, KeyValueWatcher>();
        private readonly TimeSpan _defaultPollInterval = TimeSpan.FromSeconds(30);
        private List<KeyValueSelector> _kvSelectors = new List<KeyValueSelector>();

        /// <summary>
        /// A collection of <see cref="KeyValueSelector"/>.
        /// </summary>
        public IEnumerable<KeyValueSelector> KeyValueSelectors => _kvSelectors;

        /// <summary>
        /// A collection of <see cref="KeyValueWatcher"/>.
        /// </summary>
        internal IEnumerable<KeyValueWatcher> ChangeWatchers => _changeWatchers.Values;

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
        /// Monitor the specified the key-value and reload it if the value has changed.
        /// </summary>
        /// <param name="key">
        /// Key of the key-value to be watched.
        /// </param>
        /// <param name="pollInterval">
        /// Interval used to check if the key-value has been changed.
        /// </param>
        public AzureAppConfigurationOptions Watch(string key, TimeSpan pollInterval)
        {
            return Watch(key, LabelFilter.Null, pollInterval);
        }

        /// <summary>
        /// Monitor the specified the key-value and reload it if the value has changed.
        /// </summary>
        /// <param name="key">
        /// Key of the key-value to be watched.
        /// </param>
        /// <param name="label">
        /// Label of the key-value to be watched.
        /// </param>
        /// <param name="pollInterval">
        /// Interval used to check if the key-value has been changed.
        /// </param>
        public AzureAppConfigurationOptions Watch(string key, string label = LabelFilter.Null, TimeSpan? pollInterval = null)
        {
            return WatchKeyValue(key, label, pollInterval, false);
        }

        /// <summary>
        /// Monitor the specified key-value and reload all key-values if any property of the key-value has changed.
        /// <param name="key">
        /// Key of the key-value to be watched.
        /// </param>
        /// <param name="pollInterval">
        /// Interval used to check if the key-value has been changed.
        /// </param>
        public AzureAppConfigurationOptions WatchAndReloadAll(string key, TimeSpan pollInterval)
        {
            return WatchAndReloadAll(key, LabelFilter.Null, pollInterval);
        }

        /// <summary>
        /// Monitor the specified the key-value and reload all key-values if any property of the key-value has changed.
        /// <param name="key">
        /// Key of the key-value to be watched.
        /// </param>
        /// <param name="label">
        /// Label of the key-value to be watched.
        /// </param>
        /// <param name="pollInterval">
        /// Interval used to check if the key-value has been changed.
        /// </param>
        public AzureAppConfigurationOptions WatchAndReloadAll(string key, string label = LabelFilter.Null, TimeSpan? pollInterval = null)
        {
            return WatchKeyValue(key, label, pollInterval, true);
        }

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

            var keyValueSelector = new KeyValueSelector()
            {
                KeyFilter = keyFilter,
                LabelFilter = labelFilter,
                PreferredDateTime = preferredDateTime
            };

            _kvSelectors.Add(keyValueSelector);

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

            Client = AzconfigClientFactory.CreateClient(uri, Permissions.Read).Result;

            return this;
        }

        private AzureAppConfigurationOptions WatchKeyValue(string key, string label, TimeSpan? pollInterval, bool reloadAll)
        {
            TimeSpan interval;
            if (pollInterval != null && pollInterval.HasValue)
            {
                interval = pollInterval.Value;
            }
            else
            {
                interval = _defaultPollInterval;
            }

            _changeWatchers[key] = new KeyValueWatcher()
            {
                Key = key,
                Label = label,
                PollInterval = interval,
                ReloadAll = reloadAll
            };

            return this;
        }
    }
}
