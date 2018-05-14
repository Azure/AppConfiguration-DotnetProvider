namespace Microsoft.Extensions.Configuration.AppConfig
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    class AppConfigConfigurationProvider : ConfigurationProvider
    {
        private readonly IAppConfigClient _client;
        private RemoteConfigurationOptions _options;
        private IDictionary<string, IKeyValue> _settings;
        private IDictionary<string, DateTimeOffset> _dueTimes;

        public AppConfigConfigurationProvider(IAppConfigClient client, RemoteConfigurationOptions options)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));

            _options = options ?? throw new ArgumentNullException(nameof(options));

            //
            // Set up value watching for configuration keys
            if (_options.ChangeListeners != null)
            {
                UpdateDueTimes(options.ChangeListeners.Select(cl => cl.Key));
            }
        }

        public override void Load()
        {
            LoadAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        }

        private async Task LoadAsync()
        {
            var settings = await _client.GetSettings(_options.Prefix);

            var data = new Dictionary<string, IKeyValue>(StringComparer.OrdinalIgnoreCase);

            foreach (var setting in settings)
            {
                data.Add(setting.Key, setting);
            }

            SetData(data);

            //
            // Set up configuration watcher logic

            SetupWatcher();
        }

        private void SetupWatcher()
        {
            //
            // No-op if configuration watching is disabled
            if (_options.ChangeListeners == null || _options.ChangeListeners.Count() == 0)
            {
                return;
            }

            DateTimeOffset now = DateTime.UtcNow;
            DateTimeOffset earliest = _dueTimes[_dueTimes.First().Key];

            foreach (KeyValuePair<string, DateTimeOffset> dueTime in _dueTimes)
            {
                if (dueTime.Value < earliest)
                {
                    earliest = dueTime.Value;
                }
            }

            if (earliest < now)
            {
                earliest = now;
            }

            List<string> targets = new List<string>();

            foreach (KeyValuePair<string, DateTimeOffset> dueTime in _dueTimes)
            {
                //
                // Provide interval for batching
                if (dueTime.Value <= earliest + TimeSpan.FromMilliseconds(100))
                {
                    targets.Add(dueTime.Key);
                }
            }

            UpdateDueTimes(targets);

            Task.Delay((int) (earliest - now).TotalMilliseconds).ContinueWith(async (task) => {

                if (await CheckForChanges(targets))
                {
                    SetData(_settings);
                }

                //
                // Continue watching

                SetupWatcher();
            });
        }

        private async Task<bool> CheckForChanges(IEnumerable<string> keys)
        {
            bool changed = false;

            foreach (string key in keys)
            {
                string prefixedKey = (_options.Prefix ?? string.Empty) + key;

                if (!_settings.ContainsKey(prefixedKey))
                {
                    continue;
                }

                if (_settings[prefixedKey].ETag != await _client.GetETag(prefixedKey))
                {
                    _settings[prefixedKey] = await _client.GetSetting(prefixedKey);

                    changed = true;
                    
                    foreach (var changeListener in _options.ChangeListeners)
                    {
                        if (changeListener.Key == key)
                        {
                            changeListener.Callback?.Invoke();

                            break;
                        }
                    }
                }
            }

            return changed;
        }

        private void SetData(IDictionary<string, IKeyValue> data)
        {
            //
            // Update cache of settings
            this._settings = data;

            //
            // Set the application data for the configuration provider

            var applicationData = new Dictionary<string, string>();

            foreach (KeyValuePair<string, IKeyValue> kvp in data)
            {
                applicationData.Add(kvp.Key.Substring((_options.Prefix ?? string.Empty).Length), _options.KeyValueFormatter?.Format(kvp.Value) ?? kvp.Value.Value);
            }

            Data = applicationData;

            //
            // Notify that the configuration has been updated

            OnReload();
        }

        private void UpdateDueTimes(IEnumerable<string> keys)
        {
            if (_dueTimes == null)
            {
                _dueTimes = new Dictionary<string, DateTimeOffset>();
            }

            DateTimeOffset now = DateTime.UtcNow;

            foreach (KeyValueListener changeListener in _options.ChangeListeners)
            {
                _dueTimes[changeListener.Key] = now + TimeSpan.FromMilliseconds(changeListener.PollInterval);
            }
        }
    }
}