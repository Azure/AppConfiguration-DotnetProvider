
namespace Microsoft.Extensions.Configuration.Azconfig
{
    using Microsoft.Extensions.Configuration.Azconfig.Models;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public class RemoteConfigurationOptions
    {
        private Dictionary<string, KeyValueListener> _changeListeners = new Dictionary<string, KeyValueListener>();

        private bool _LoadDefaultSetting = true;

        public List<LoadSettingsOption> LoadSettingsOptions { get; } = new List<LoadSettingsOption>()
        {
            // Add default option. Load all key-values with empty Label.
            new LoadSettingsOption()
            {
                LabelFilter = string.Empty
            }
        };

        public IEnumerable<KeyValueListener> ChangeListeners {
            get
            {
                return _changeListeners.Values;
            }
        }

        public RemoteConfigurationOptions Listen(string key, int pollInterval, string label = "")
        {
            _changeListeners[key] = new KeyValueListener()
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
                throw new ArgumentException(string.Format("'*' and ',' are not valid characters for labelFilter."));
            }

            var loadSettingOpt = new LoadSettingsOption()
            {
                KeyFilter = keyFilter,
                LabelFilter = labelFilter
            };

            if (_LoadDefaultSetting)
            {
                // remove the default setting.
                LoadSettingsOptions.RemoveAt(0);
                _LoadDefaultSetting = false;
            }

            LoadSettingsOptions.Add(loadSettingOpt);

            return this;
        }
    }
}
