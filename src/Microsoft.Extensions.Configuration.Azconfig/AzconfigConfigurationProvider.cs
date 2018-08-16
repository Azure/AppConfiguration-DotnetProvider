namespace Microsoft.Extensions.Configuration.Azconfig
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reactive.Concurrency;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azconfig.Client;
    using Microsoft.Extensions.Configuration.Azconfig.Models;

    class AzconfigConfigurationProvider : ConfigurationProvider
    {
        private RemoteConfigurationOptions _options;
        private IDictionary<string, IKeyValue> _settings;
        private readonly IAzconfigReader _reader;
        private readonly IAzconfigWatcher _watcher;

        public AzconfigConfigurationProvider(IAzconfigReader reader, IAzconfigWatcher watcher, RemoteConfigurationOptions options)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _watcher = watcher ?? throw new ArgumentNullException(nameof(watcher));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public override void Load()
        {
            LoadAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        }

        private async Task LoadAsync()
        {
            var data = new Dictionary<string, IKeyValue>();

            var queryKeyValueCollectionOptions = new QueryKeyValueCollectionOptions()
            {
                KeyFilter = _options.LoadSettingsOptions.KeyFilter,
                LabelFilter = _options.LoadSettingsOptions.Label
            };

            await _reader.GetKeyValues(queryKeyValueCollectionOptions).ForEachAsync(kv => data.Add(kv.Key, kv));

            SetData(data);

            ObserveKeyvalue();
        }

        private async Task ObserveKeyvalue()
        {
            foreach (KeyValueListener changeListener in _options.ChangeListeners)
            {
                IKeyValue retrievedKv = await _reader.GetKeyValue(changeListener.Key,
                                                                  new QueryKeyValueOptions() { Label = changeListener.Label },
                                                                  CancellationToken.None);
                IObservable<IKeyValue> observable = _watcher.ObserveKeyValue(retrievedKv,
                                                                             TimeSpan.FromMilliseconds(changeListener.PollInterval),
                                                                             Scheduler.Default);
                observable.Subscribe((observedKey) =>
                {
                    _settings[changeListener.Key] = observedKey;
                    SetData(_settings);
                });
            }
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
                applicationData.Add(kvp.Key, kvp.Value.Value);
            }

            Data = applicationData;

            //
            // Notify that the configuration has been updated
            OnReload();
        }
    }
}