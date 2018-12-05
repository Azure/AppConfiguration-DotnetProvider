namespace Microsoft.Extensions.Configuration.Azconfig
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Reactive.Concurrency;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azconfig.Client;
    using Microsoft.Extensions.Configuration.Azconfig.Models;

    class AzconfigConfigurationProvider : ConfigurationProvider, IDisposable
    {
        private AzconfigOptions _options;
        private IDictionary<string, IKeyValue> _settings;
        private List<IDisposable> _subscriptions = new List<IDisposable>();
        private readonly AzconfigClient _client;

        public AzconfigConfigurationProvider(AzconfigClient client, AzconfigOptions options)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public override void Load()
        {
            var data = new Dictionary<string, IKeyValue>();
            try
            {
                if (!_options.KeyValueSelectors.Any())
                {
                    // Load all key-values by default
                    _client.GetKeyValues(new QueryKeyValueCollectionOptions()).ForEach(kv => { data[kv.Key] = kv; });
                }
                else
                {
                    foreach (var loadOption in _options.KeyValueSelectors)
                    {
                        var queryKeyValueCollectionOptions = new QueryKeyValueCollectionOptions()
                        {
                            KeyFilter = loadOption.KeyFilter,
                            LabelFilter = loadOption.LabelFilter
                        };
                        _client.GetKeyValues(queryKeyValueCollectionOptions).ForEach(kv => { data[kv.Key] = kv; });
                    }
                }
            }
            catch (Exception exception) when ((exception.InnerException is HttpRequestException || 
                                               exception.InnerException is UnauthorizedAccessException) && _options.Optional)
            {
                return;
            }

            SetData(data);

            ObserveKeyValue();
        }

        private async Task ObserveKeyValue()
        {
            foreach (KeyValueWatcher changeWatcher in _options.ChangeWatchers)
            {
                IKeyValue watchedKv = null;
                string watchedKey = changeWatcher.Key;
                string watchedLabel = changeWatcher.Label;

                if (_settings.ContainsKey(watchedKey) && _settings[watchedKey].Label == watchedLabel)
                {
                    watchedKv = _settings[watchedKey];
                }
                else
                {
                    // Send out another request to retrieved observed kv, since it may not be loaded or with a different label.
                    watchedKv = await _client.GetKeyValue(watchedKey,
                                                            new QueryKeyValueOptions() { Label = watchedLabel },
                                                            CancellationToken.None) ??
                                 new KeyValue(watchedKey) { Label = watchedLabel };
                }

                IObservable<IKeyValue> observable = _client.ObserveKeyValue(watchedKv,
                                                                             TimeSpan.FromMilliseconds(changeWatcher.PollInterval),
                                                                             Scheduler.Default);
                _subscriptions.Add(observable.Subscribe((observedKv) =>
                {
                    _settings[watchedKey] = observedKv;
                    SetData(_settings);
                }));
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

        public void Dispose()
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }
        }
    }
}