namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Reactive.Concurrency;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.AppConfiguration.Azconfig;
    using Microsoft.Extensions.Configuration.AzureAppConfiguration.Models;
    using Newtonsoft.Json;

    class AzureAppConfigurationProvider : ConfigurationProvider, IDisposable
    {
        private AzureAppConfigurationOptions _options;
        private bool _optional;
        private ConcurrentDictionary<string, IKeyValue> _settings;
        private List<IDisposable> _subscriptions;
        private readonly AzconfigClient _client;

        public AzureAppConfigurationProvider(AzconfigClient client, AzureAppConfigurationOptions options, bool optional)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _optional = optional;
            _subscriptions = new List<IDisposable>();
        }

        public void Dispose()
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }
        }

        public override void Load()
        {
            LoadAll();

            ObserveKeyValues();
        }

        private void LoadAll()
        {
            IDictionary<string, IKeyValue> data = new Dictionary<string, IKeyValue>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (!_options.KeyValueSelectors.Any())
                {
                    // Load all key-values by null label.
                    _client.GetKeyValues(
                        new QueryKeyValueCollectionOptions()
                        {
                            KeyFilter = KeyFilter.Any,
                            LabelFilter = LabelFilter.Null,
                        })
                    .ForEach(kv => { data[kv.Key] = kv; });
                }
                else
                {
                    foreach (var loadOption in _options.KeyValueSelectors)
                    {
                        var queryKeyValueCollectionOptions = new QueryKeyValueCollectionOptions()
                        {
                            KeyFilter = loadOption.KeyFilter,
                            LabelFilter = loadOption.LabelFilter,
                            PreferredDateTime = loadOption.PreferredDateTime
                        };
                        _client.GetKeyValues(queryKeyValueCollectionOptions).ForEach(kv => { data[kv.Key] = kv; });
                    }
                }
            }
            catch (Exception exception) when (exception.InnerException is HttpRequestException ||
                                              exception.InnerException is UnauthorizedAccessException)
            {
                if (_options.OfflineCache != null)
                {
                    data = JsonConvert.DeserializeObject<IDictionary<string, IKeyValue>>(_options.OfflineCache.Import(_options), new KeyValueConverter());
                    if (data != null)
                    {
                        SetData(data);
                        return;
                    }
                }

                if (!_optional)
                {
                    throw;
                }

                return;
            }

            SetData(data);

            if (_options.OfflineCache != null)
            {
                _options.OfflineCache.Export(_options, JsonConvert.SerializeObject(data));
            }
        }

        private async Task ObserveKeyValues()
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

                IObservable<KeyValueChange> observable = _client.ObserveKeyValue(watchedKv,
                                                                             changeWatcher.PollInterval,
                                                                             Scheduler.Default);

                _subscriptions.Add(observable.Subscribe((observedChange) =>
                {
                    ProcessChanges(Enumerable.Repeat(observedChange, 1));

                    if (changeWatcher.ReloadAll)
                    {
                        LoadAll();
                    }
                    else
                    {
                        SetData(_settings);
                    }
                }));
            }

            foreach (KeyValueWatcher changeWatcher in _options.MultiKeyWatchers)
            {
                IEnumerable<IKeyValue> existing = _settings.Values.Where(kv => {

                    return kv.Key.StartsWith(changeWatcher.Key) && ((changeWatcher.Label == LabelFilter.Null && kv.Label == null) || kv.Label.Equals(changeWatcher.Label));

                });

                IObservable<IEnumerable<KeyValueChange>> observable = _client.ObserveKeyValueCollection(
                    new ObserveKeyValueCollectionOptions
                    {
                        Prefix = changeWatcher.Key,
                        Label = changeWatcher.Label == LabelFilter.Null ? null : changeWatcher.Label,
                        PollInterval = changeWatcher.PollInterval,
                        Scheduler = Scheduler.Default
                    },
                    existing);

                _subscriptions.Add(observable.Subscribe((observedChanges) =>
                {
                    ProcessChanges(observedChanges);

                    SetData(_settings);
                }));
            }
        }

        private void SetData(IDictionary<string, IKeyValue> data)
        {
            //
            // Update cache of settings
            this._settings = data as ConcurrentDictionary<string, IKeyValue> ?? 
                new ConcurrentDictionary<string, IKeyValue>(data, StringComparer.OrdinalIgnoreCase);

            //
            // Set the application data for the configuration provider
            var applicationData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, IKeyValue> kvp in data)
            {
                foreach (KeyValuePair<string, string> kv in ProcessAdapters(kvp.Value))
                {
                    applicationData[kv.Key] = kv.Value;
                }
            }

            Data = applicationData;
            
            //
            // Notify that the configuration has been updated
            OnReload();
        }

        private IEnumerable<KeyValuePair<string, string>> ProcessAdapters(IKeyValue keyValue)
        {
            List<KeyValuePair<string, string>> keyValues = null;

            foreach (IKeyValueAdapter adapter in _options.Adapters)
            {
                IEnumerable<KeyValuePair<string, string>> kvs = adapter.GetKeyValues(keyValue);

                if (kvs != null)
                {
                    keyValues = keyValues ?? new List<KeyValuePair<string, string>>();

                    keyValues.AddRange(kvs);
                }
            }

            return keyValues != null ?
                keyValues :
                Enumerable.Repeat(new KeyValuePair<string, string>(keyValue.Key, keyValue.Value), 1);
        }

        private void ProcessChanges(IEnumerable<KeyValueChange> changes)
        {
            foreach (KeyValueChange change in changes)
            {
                if (change.ChangeType == KeyValueChangeType.Deleted)
                {
                    _settings.TryRemove(change.Key, out IKeyValue removed);
                }
                else if (change.ChangeType == KeyValueChangeType.Modified)
                {
                    _settings[change.Key] = change.Current;
                }
            }
        }
    }
}