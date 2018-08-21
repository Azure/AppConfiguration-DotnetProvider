
namespace Microsoft.Extensions.Configuration.Azconfig
{
    using Microsoft.Extensions.Configuration.Azconfig.Models;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public class RemoteConfigurationOptions
    {
        private Dictionary<string, KeyValueWatcher> _changeWatchers = new Dictionary<string, KeyValueWatcher>();

        public List<KeyValueSelector> KeyValueSelectors { get; } = new List<KeyValueSelector>();

        public IEnumerable<KeyValueWatcher> ChangeWatchers {
            get
            {
                return _changeWatchers.Values;
            }
        }

        public RemoteConfigurationOptions Watch(string key, int pollInterval, string label = "")
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
        public RemoteConfigurationOptions Use(string keyFilter)
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
        public RemoteConfigurationOptions Use(string keyFilter, string labelFilter)
        {
            // Do not support * and , for label filter for now.
            if (labelFilter.Contains('*') || labelFilter.Contains(','))
            {
                throw new ArgumentException(string.Format("'*' and ',' are not supported for labelFilter."));
            }

            var keyValueSelectors = new KeyValueSelector ()
            {
                KeyFilter = keyFilter,
                LabelFilter = labelFilter
            };

            KeyValueSelectors.Add(keyValueSelectors);

            return this;
        }
    }
}
