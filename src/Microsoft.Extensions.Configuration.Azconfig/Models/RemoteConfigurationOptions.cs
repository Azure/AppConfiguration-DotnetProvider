
namespace Microsoft.Extensions.Configuration.Azconfig.Models
{
    using System.Collections.Generic;

    public class RemoteConfigurationOptions
    {
        private Dictionary<string, KeyValueListener> _changeListeners = new Dictionary<string, KeyValueListener>();

        public LoadSettingsOptions LoadSettingsOptions { get; set; } = new LoadSettingsOptions() { Label = string.Empty };

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
    }
}
