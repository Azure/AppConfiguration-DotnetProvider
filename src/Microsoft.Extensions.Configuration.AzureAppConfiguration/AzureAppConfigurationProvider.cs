namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    using System;
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
        private IDictionary<string, IKeyValue> _settings;
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

            ObserveKeyValue();
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
                                                                             changeWatcher.PollInterval,
                                                                             Scheduler.Default);
                _subscriptions.Add(observable.Subscribe((observedKv) =>
                {
                    if (observedKv == null)
                    {
                        _settings.Remove(watchedKey);
                    }
                    else
                    {
                        _settings[watchedKey] = observedKv;
                    }

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
        }

        private void SetData(IDictionary<string, IKeyValue> data)
        {
            //
            // Update cache of settings
            this._settings = data;

            //
            // Set the application data for the configuration provider
            var applicationData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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