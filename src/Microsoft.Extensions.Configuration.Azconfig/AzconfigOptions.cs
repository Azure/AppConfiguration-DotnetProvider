
namespace Microsoft.Extensions.Configuration.Azconfig
{
    using Microsoft.Azconfig.Client;
    using Microsoft.Extensions.Configuration.Azconfig.Models;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public class AzconfigOptions
    {
        private Dictionary<string, KeyValueWatcher> _changeWatchers = new Dictionary<string, KeyValueWatcher>();

        private List<KeyValueSelector> _kvSelectors = new List<KeyValueSelector>();

        public IEnumerable<KeyValueSelector> KeyValueSelectors => _kvSelectors;

        /// <summary>
        /// The connection string to use to connect to the configuration store.
        /// </summary>
        public string ConnectionString { get; set; }

        /// <summary>
        /// An optional client that can be used to communicate with the configuration store. If provided, connection string will be ignored.
        /// </summary>
        public AzconfigClient Client { get; set; }

        public bool Optional { get; set; }

        public IEnumerable<KeyValueWatcher> ChangeWatchers {
            get
            {
                return _changeWatchers.Values;
            }
        }

        public AzconfigOptions Watch(string key, int pollInterval, string label = "")
        {
            _changeWatchers[key] = new KeyValueWatcher()
            {
                Key = key,
                Label = label,
                PollInterval = pollInterval
            };
            return this;
        }

        /// <summary>
        /// Load key-values into configuration with specified key filter and null label.
        /// If no method called, load all keys with null label.
        /// </summary>
        /// <param name="keyFilter">Key filters for query key-values.</param>
        public AzconfigOptions Use(string keyFilter)
        {
            Use(keyFilter, string.Empty);
            return this;
        }

        /// <summary>
        /// Load key-values into configuration with specified key, label filter.
        /// If no method called, load all keys with null label.
        /// </summary>
        /// <param name="keyFilter">
        /// Key filters for query key-values.
        /// </param>
        /// <param name="labelFilter">Label filters for query key-values.
        /// Do not support '*' and ',' yet.
        /// </param>
        public AzconfigOptions Use(string keyFilter, string labelFilter)
        {
            // Do not support * and , for label filter for now.
            if (labelFilter.Contains('*') || labelFilter.Contains(','))
            {
                throw new ArgumentException("The characters '*' and ',' are not supported in label filters.", nameof(labelFilter));
            }

            var keyValueSelectors = new KeyValueSelector()
            {
                KeyFilter = keyFilter,
                LabelFilter = labelFilter
            };

            _kvSelectors.Add(keyValueSelectors);

            return this;
        }

        public AzconfigOptions Connect(string connectionString)
        {
            ConnectionString = connectionString;
            return this;
        }
    }
}
