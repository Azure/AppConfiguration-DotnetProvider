namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Reactive.Concurrency;
    using System.Reactive.Linq;
    using System.Security;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.AppConfiguration.Azconfig;
    using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
    using Microsoft.Extensions.Configuration.AzureAppConfiguration.Models;
    using Newtonsoft.Json;

    class AzureAppConfigurationProvider : ConfigurationProvider, IDisposable
    {
        private AzureAppConfigurationOptions _options;
        private bool _optional;
        private ConcurrentDictionary<string, IKeyValue> _settings;
        private List<IDisposable> _subscriptions;
        private readonly AzconfigClient _client;
        private bool _requestTracingEnabled;

        public AzureAppConfigurationProvider(AzconfigClient client, AzureAppConfigurationOptions options, bool optional)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _optional = optional;
            _subscriptions = new List<IDisposable>();

            string requestTracingDisabled = null;
            try
            {
                requestTracingDisabled = Environment.GetEnvironmentVariable(RequestTracingConstants.RequestTracingDisabledEnvironmentVariable);
            }
            catch (Exception ex) when (ex is ArgumentNullException || ex is SecurityException) { }

            if (!Boolean.TryParse(requestTracingDisabled, out _requestTracingEnabled))
            {
                //
                // Enable request tracing by default (if no valid environmental variable option is specified).
                _requestTracingEnabled = true;
            }
        }

        public void Dispose()
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }
        }

        public override async void Load()
        {
            LoadAll(RequestType.Startup);

            await ObserveKeyValues().ConfigureAwait(false);
        }

        private void LoadAll(RequestType requestType)
        {
             IDictionary<string, IKeyValue> data = new Dictionary<string, IKeyValue>(StringComparer.OrdinalIgnoreCase);

            try
            {
                bool useDefaultQuery = !_options.KeyValueSelectors.Any(selector => !selector.KeyFilter.StartsWith(FeatureManagementConstants.FeatureFlagMarker));

                if (useDefaultQuery)
                {
                    var options = new QueryKeyValueCollectionOptions()
                    {
                        KeyFilter = KeyFilter.Any,
                        LabelFilter = LabelFilter.Null
                    };
                    
                    //
                    // Load all key-values with the null label.
                    _client.GetKeyValues(options).ForEach(kv => { data[kv.Key] = kv; });
                }

                foreach (var loadOption in _options.KeyValueSelectors)
                {
                    if ((useDefaultQuery && LabelFilter.Null.Equals(loadOption.LabelFilter)) ||
                        _options.KeyValueSelectors.Any(s => s != loadOption && 
                           string.Equals(s.KeyFilter, KeyFilter.Any) && 
                           string.Equals(s.LabelFilter, loadOption.LabelFilter) && 
                           Nullable<DateTimeOffset>.Equals(s.PreferredDateTime, loadOption.PreferredDateTime)))
                    {
                        //
                        // This selection was already encapsulated by a wildcard query
                        // We skip it to prevent unnecessary requests
                        continue;
                    }

                    var queryKeyValueCollectionOptions = new QueryKeyValueCollectionOptions()
                    {
                        KeyFilter = loadOption.KeyFilter,
                        LabelFilter = loadOption.LabelFilter,
                        PreferredDateTime = loadOption.PreferredDateTime
                    };

                    if (_requestTracingEnabled)
                    {
                        queryKeyValueCollectionOptions.AddRequestType(requestType);
                    }

                    _client.GetKeyValues(queryKeyValueCollectionOptions).ForEach(kv => { data[kv.Key] = kv; });
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
                    var options = new QueryKeyValueOptions() { Label = watchedLabel };
                    if (_requestTracingEnabled)
                    {
                        options.AddRequestType(RequestType.Watch);
                    }

                    // Send out another request to retrieved observed kv, since it may not be loaded or with a different label.
                    watchedKv = await _client.GetKeyValue(watchedKey, options, CancellationToken.None) ?? new KeyValue(watchedKey) { Label = watchedLabel };
                }

                IObservable<KeyValueChange> observable = this.GetObservablesForKeyValue(watchedKv, changeWatcher.PollInterval, Scheduler.Default);

                _subscriptions.Add(observable.Subscribe((observedChange) =>
                {
                    ProcessChanges(Enumerable.Repeat(observedChange, 1));

                    if (changeWatcher.ReloadAll)
                    {
                        LoadAll(RequestType.Watch);
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

                IObservable<IEnumerable<KeyValueChange>> observable = this.GetObservableCollection(
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
                    string key = kv.Key;
                    foreach (string prefix in _options.KeyPrefixes)
                    {
                        if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            key = key.Substring(prefix.Length);
                            break;
                        }
                    }

                    applicationData[key] = kv.Value;
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

            return keyValues ?? Enumerable.Repeat(new KeyValuePair<string, string>(keyValue.Key, keyValue.Value), 1);
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

        private IObservable<KeyValueChange> GetObservablesForKeyValue(IKeyValue keyValue, TimeSpan pollInterval, IScheduler scheduler = null)
        {
            scheduler = scheduler ?? Scheduler.Default;
            var options = new QueryKeyValueOptions() { Label = keyValue.Label };
            if (_requestTracingEnabled)
            {
                options.AddRequestType(RequestType.Watch);
            }

            return Observable
                .Timer(pollInterval, scheduler)
                .SelectMany(_ => Observable
                    .FromAsync((cancellationToken) => _client.GetCurrentKeyValue(keyValue, cancellationToken))
                    .Delay(pollInterval, scheduler)
                    .Repeat()
                    .Where(kv => kv != keyValue)
                    .Select(kv =>
                    {
                        keyValue = kv ?? new KeyValue(keyValue.Key)
                        {
                            Label = keyValue.Label
                        };

                        return new KeyValueChange()
                        {
                            ChangeType = kv == null ? KeyValueChangeType.Deleted : KeyValueChangeType.Modified,
                            Current = kv,
                            Key = keyValue.Key,
                            Label = keyValue.Label
                        };
                    }));
        }

        private IObservable<IEnumerable<KeyValueChange>> GetObservableCollection(ObserveKeyValueCollectionOptions options, IEnumerable<IKeyValue> keyValues)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (keyValues == null)
            {
                keyValues = Enumerable.Empty<IKeyValue>();
            }

            if (options.Prefix == null)
            {
                options.Prefix = string.Empty;
            }

            if (options.Prefix.Contains("*"))
            {
                throw new ArgumentException("The prefix cannot contain '*'", $"{nameof(options)}.{nameof(options.Prefix)}");
            }

            if (keyValues.Any(k => string.IsNullOrEmpty(k.Key)))
            {
                throw new ArgumentNullException($"{nameof(keyValues)}[].{nameof(IKeyValue.Key)}");
            }

            if (string.IsNullOrEmpty(options.Prefix) && keyValues.Any(k => !k.Key.StartsWith(options.Prefix)))
            {
                throw new ArgumentException("All observed key-values must start with the provided prefix.", $"{nameof(keyValues)}[].{nameof(IKeyValue.Key)}");
            }

            if (keyValues.Any(k => !string.Equals(NormalizeNull(k.Label), NormalizeNull(options.Label))))
            {
                throw new ArgumentException("All observed key-values must use the same label.", $"{nameof(keyValues)}[].{nameof(IKeyValue.Key)}");
            }

            if (keyValues.Any(k => k.Label != null && k.Label.Contains("*")))
            {
                throw new ArgumentException("The label filter cannot contain '*'", $"{nameof(options)}.{nameof(options.Label)}");
            }

            var scheduler = options.Scheduler ?? Scheduler.Default;
            Dictionary<string, string> currentEtags = keyValues.ToDictionary(kv => kv.Key, kv => kv.ETag);
            var queryOptions = new QueryKeyValueCollectionOptions()
            {
                KeyFilter = options.Prefix + "*",
                LabelFilter = string.IsNullOrEmpty(options.Label) ? LabelFilter.Null : options.Label,
                FieldsSelector = KeyValueFields.ETag | KeyValueFields.Key
            };

            if (_requestTracingEnabled)
            {
                queryOptions.AddRequestType(RequestType.Watch);
            }

            return Observable
                .Timer(options.PollInterval, scheduler)
                .SelectMany(_ => Observable
                    .FromAsync((cancellationToken) => _client.GetKeyValues(queryOptions)
                        .ToEnumerableAsync(cancellationToken))
                        .Delay(options.PollInterval, scheduler)
                        .Repeat()
                        .Where(kvs =>
                        {
                            bool changed = false;
                            var etags = currentEtags.ToDictionary(kv => kv.Key, kv => kv.Value);
                            foreach (IKeyValue kv in kvs)
                            {
                                if (!etags.TryGetValue(kv.Key, out string etag) || !etag.Equals(kv.ETag))
                                {
                                    changed = true;
                                    break;
                                }

                                etags.Remove(kv.Key);
                            }

                            if (!changed && etags.Any())
                            {
                                changed = true;
                            }

                            return changed;
                        }))
                        .SelectMany(_ => Observable.FromAsync(async cancellationToken =>
                        {
                            queryOptions = new QueryKeyValueCollectionOptions()
                            {
                                KeyFilter = options.Prefix + "*",
                                LabelFilter = string.IsNullOrEmpty(options.Label) ? LabelFilters.Null : options.Label
                            };

                            if (_requestTracingEnabled)
                            {
                                queryOptions.AddRequestType(RequestType.Watch);
                            }

                            IEnumerable<IKeyValue> kvs = await _client.GetKeyValues(queryOptions).ToEnumerableAsync(cancellationToken);

                            var etags = currentEtags.ToDictionary(kv => kv.Key, kv => kv.Value);
                            currentEtags = kvs.ToDictionary(kv => kv.Key, kv => kv.ETag);
                            var changes = new List<KeyValueChange>();

                            foreach (IKeyValue kv in kvs)
                            {
                                if (!etags.TryGetValue(kv.Key, out string etag) || !etag.Equals(kv.ETag))
                                {
                                    changes.Add(new KeyValueChange()
                                    {
                                        ChangeType = KeyValueChangeType.Modified,
                                        Key = kv.Key,
                                        Label = NormalizeNull(options.Label),
                                        Current = kv
                                    });
                                }

                                etags.Remove(kv.Key);
                            }

                            foreach (var kvp in etags)
                            {
                                changes.Add(new KeyValueChange()
                                {
                                    ChangeType = KeyValueChangeType.Deleted,
                                    Key = kvp.Key,
                                    Label = NormalizeNull(options.Label),
                                    Current = null
                                });
                            }

                            return changes;
                        }));
        }

        public string NormalizeNull(string s)
        {
            if (s == null || s == LabelFilter.Null)
            {
                return null;
            }

            return s;
        }
    }
}