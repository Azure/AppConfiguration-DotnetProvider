namespace Microsoft.Extensions.Configuration.Azconfig
{
    using Microsoft.Azconfig.Client;
    using Microsoft.Azconfig.ManagedIdentityConnector;
    using Microsoft.Extensions.Configuration.Azconfig.Models;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public class AzconfigOptions
    {
        private Dictionary<string, KeyValueWatcher> _changeWatchers = new Dictionary<string, KeyValueWatcher>();
        private readonly TimeSpan _defaultPollInterval = TimeSpan.FromSeconds(30);

        /// <summary>
        /// A collection of <see cref="KeyValueSelector"/>.
        /// </summary>
        public List<KeyValueSelector> KeyValueSelectors { get; } = new List<KeyValueSelector>();

        /// <summary>
        /// A collection of <see cref="KeyValueWatcher"/>.
        /// </summary>
        internal IEnumerable<KeyValueWatcher> ChangeWatchers => _changeWatchers.Values;

        /// <summary>
        /// Offline cache provider
        /// </summary>
        public IOfflineCache OfflineCache { get; set; }

        /// <summary>
        /// The connection string to use to connect to the App Configuration Hubs.
        /// </summary>
        public string ConnectionString { get; set; }

        /// <summary>
        /// An optional client that can be used to communicate with the App Configuration Hubs. If provided, connection string will be ignored.
        /// </summary>
        internal AzconfigClient Client { get; set; }

        /// <summary>
        /// Instructs the AzconfigOptions to poll the key-values matching the specified key at the provided polling interval.
        /// </summary>
        /// <param name="key">
        /// The key used for querying the App Configuration Hubs for key-values.
        /// </param>
        /// <param name="pollInterval">
        /// The interval used to poll query the App Configuration Hubs.
        /// </param>
        public AzconfigOptions Watch(string key, TimeSpan pollInterval)
        {
            return Watch(key, "", pollInterval);
        }

        /// <summary>
        /// Instructs the AzconfigOptions to poll the key-values matching the specified key at the provided polling interval. 
        /// </summary>
        /// <param name="key">
        /// The key used for querying the App Configuration Hubs for key-values.
        /// </param>
        /// <param name="label">
        /// The label used for querying the App Configuration Hubs for key-values.
        /// </param>
        /// <param name="pollInterval">
        /// The interval used to poll query the App Configuration Hubs.
        /// </param>
        public AzconfigOptions Watch(string key, string label = "", TimeSpan? pollInterval = null)
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
                PollInterval = interval
            };
            return this;
        }

        /// <summary>
        /// Instructs the AzconfigOptions to include all key-values with matching the specified key and label filters.
        /// </summary>
        /// <param name="keyFilter">
        /// The key filter to apply when querying the App Configuration Hubs for key-values. Built-in key filter options: <see cref="KeyFilter"/>
        /// </param>
        /// <param name="labelFilter">
        /// The label filter to apply when querying the App Configuration Hubs for key-values. By default the null label filter will be used. Built-in label filter options: <see cref="LabelFilter"/>
        /// Does not support '*' and ','.
        /// </param>
        /// <param name="preferredDateTime">
        /// Used to query key-values in the state that they existed at the time provided.
        /// </param>
        public AzconfigOptions Use(string keyFilter, string labelFilter = null, DateTimeOffset? preferredDateTime = null)
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

            KeyValueSelectors.Add(keyValueSelector);

            return this;
        }

        public AzconfigOptions AddOfflineCache(IOfflineCache offlineCache)
        {
            if (offlineCache == null)
            {
                throw new ArgumentNullException(nameof(offlineCache));
            }

            OfflineCache = offlineCache;

            return this;
        }

        /// <summary>
        /// Instructs the AzconfigOptions to connect the App Configuration Hubs via a connection string.
        /// </summary>
        /// <param name="connectionString">
        /// Used to authenticate with the App Configuration Hubs.
        /// </param>
        public AzconfigOptions Connect(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentNullException(nameof(connectionString));
            }

            ConnectionString = connectionString;
            return this;
        }

        public AzconfigOptions ConnectWithManagedIdentity(string endpoint)
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
    }
}
