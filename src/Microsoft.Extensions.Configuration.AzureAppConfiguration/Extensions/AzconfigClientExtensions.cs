﻿using Azure;
using Azure.Data.AppConfiguration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class AzconfigClientExtensions
    {
        public static async Task<KeyValueChange> GetKeyValueChange(this ConfigurationClient client, ConfigurationSetting setting, CancellationToken cancellationToken)
        {
            if (setting == null)
            {
                throw new ArgumentNullException(nameof(setting));
            }

            if (string.IsNullOrEmpty(setting.Key))
            {
                throw new ArgumentNullException($"{nameof(setting)}.{nameof(setting.Key)}");
            }

            KeyValueChange change = null;

            try
            {
                Response<ConfigurationSetting> response = await client.GetAsync(setting, onlyIfChanged: true, cancellationToken).ConfigureAwait(false);

                switch (response.GetRawResponse().Status)
                {
                    case (int)HttpStatusCode.OK:
                        change = new KeyValueChange
                        {
                            ChangeType = KeyValueChangeType.Modified,
                            Current = response.Value,
                            Key = setting.Key,
                            Label = setting.Label
                        };
                        break;
                    case (int)HttpStatusCode.NotModified:
                        // No change.
                    default:
                        // Get threw.
                        break;
                }
            }
            catch (RequestFailedException e)
            {
                if (e.Status == (int)HttpStatusCode.NotFound && setting.ETag != default)
                {
                    change = new KeyValueChange
                    {
                        ChangeType = KeyValueChangeType.Deleted,
                        Current = null,
                        Key = setting.Key,
                        Label = setting.Label
                    };
                }
                else
                {
                    throw e;
                }
            }

            return change;
        }

        public static async Task<IEnumerable<KeyValueChange>> GetKeyValueChangeCollection(this ConfigurationClient client, IEnumerable<ConfigurationSetting> keyValues, GetKeyValueChangeCollectionOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (keyValues == null)
            {
                keyValues = Enumerable.Empty<ConfigurationSetting>();
            }

            if (options.Prefix == null)
            {
                options.Prefix = string.Empty;
            }

            if (options.Prefix.Contains('*'))
            {
                throw new ArgumentException("The prefix cannot contain '*'", $"{nameof(options)}.{nameof(options.Prefix)}");
            }

            if (keyValues.Any(k => string.IsNullOrEmpty(k.Key)))
            {
                throw new ArgumentNullException($"{nameof(keyValues)}[].{nameof(ConfigurationSetting.Key)}");
            }

            if (!string.IsNullOrEmpty(options.Prefix) && keyValues.Any(k => !k.Key.StartsWith(options.Prefix)))
            {
                throw new ArgumentException("All key-values registered for refresh must start with the provided prefix.", $"{nameof(keyValues)}[].{nameof(ConfigurationSetting.Key)}");
            }

            if (keyValues.Any(k => !string.Equals(k.Label.NormalizeNull(), options.Label.NormalizeNull())))
            {
                throw new ArgumentException("All key-values registered for refresh must use the same label.", $"{nameof(keyValues)}[].{nameof(ConfigurationSetting.Label)}");
            }

            if (keyValues.Any(k => k.Label != null && k.Label.Contains("*")))
            {
                throw new ArgumentException("The label filter cannot contain '*'", $"{nameof(options)}.{nameof(options.Label)}");
            }

            var hasKeyValueCollectionChanged = false;

            var keyFilter = options.Prefix + "*";
            var labelFilter = string.IsNullOrEmpty(options.Label) ? LabelFilter.Null : options.Label;
            var selector = new SettingSelector(keyFilter, labelFilter)
            {
                Fields = SettingFields.ETag | SettingFields.Key
            };

            TracingUtils.ConfigureRequestTracing(options.RequestTracingEnabled, RequestType.Watch, options.HostType);

            // Fetch e-tags for prefixed key-values that can be used to detect changes
            var kvs = client.GetSettingsAsync(selector);

            // Dictionary of eTags that we write to and use for comparison
            var eTagMap = keyValues.ToDictionary(kv => kv.Key, kv => kv.Value);

            // Check for any modifications/creations
            var enumerator = kvs.GetAsyncEnumerator();
            while (await enumerator.MoveNextAsync().ConfigureAwait(false))
            {
                if (!eTagMap.TryGetValue(enumerator.Current.Value.Key, out string etag) || !etag.Equals(enumerator.Current.Value.ETag))
                {
                    hasKeyValueCollectionChanged = true;
                    break;
                }

                eTagMap.Remove(enumerator.Current.Value.Key);
            }

            // Check for any deletions
            if (eTagMap.Any())
            {
                hasKeyValueCollectionChanged = true;
            }

            var changes = new List<KeyValueChange>();

            // If changes have been observed, refresh prefixed key-values
            if (hasKeyValueCollectionChanged)
            {
                keyFilter = options.Prefix + "*";
                labelFilter = string.IsNullOrEmpty(options.Label) ? LabelFilter.Null : options.Label;
                selector = new SettingSelector(keyFilter, labelFilter);

                TracingUtils.ConfigureRequestTracing(options.RequestTracingEnabled, RequestType.Watch, options.HostType);

                kvs = client.GetSettingsAsync(selector);
                enumerator = kvs.GetAsyncEnumerator(CancellationToken.None);
                eTagMap = keyValues.ToDictionary(kv => kv.Key, kv => kv.ETag.ToString());

                while (await enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    if (!eTagMap.TryGetValue(enumerator.Current.Value.Key, out string etag) || !etag.Equals(enumerator.Current.Value.ETag))
                    {
                        changes.Add(new KeyValueChange
                        {
                            ChangeType = KeyValueChangeType.Modified,
                            Key = enumerator.Current.Value.Key,
                            Label = options.Label.NormalizeNull(),
                            Current = enumerator.Current.Value
                        });
                    }

                    eTagMap.Remove(enumerator.Current.Value.Key);
                }

                foreach (var kvp in eTagMap)
                {
                    changes.Add(new KeyValueChange
                    {
                        ChangeType = KeyValueChangeType.Deleted,
                        Key = kvp.Key,
                        Label = options.Label.NormalizeNull(),
                        Current = null
                    });
                }
            }

            return changes;
        }
    }
}
