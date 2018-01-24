namespace Microsoft.Extensions.Configuration.AppConfig
{
    using System;
    using System.Collections.Generic;

    public class RemoteConfigurationOptions
    {
        private Dictionary<string, KeyValueListener> _changeListeners = new Dictionary<string, KeyValueListener>();

        public string AcceptVersion { get; set; } = "[1.0,1.0]";

        public string Prefix { get; set; } = string.Empty;

        public IKeyValueFormatter KeyValueFormatter { get; set; } = new KeyValueFormatter();

        public IEnumerable<KeyValueListener> ChangeListeners {
            get {
                return _changeListeners.Values;
            }
        }

        public RemoteConfigurationOptions Listen(string key, int pollInterval)
        {
            _changeListeners[key] = new KeyValueListener()
            {
                Key = key,
                PollInterval = pollInterval,
                Callback = null
            };

            return this;
        }
    }
}
