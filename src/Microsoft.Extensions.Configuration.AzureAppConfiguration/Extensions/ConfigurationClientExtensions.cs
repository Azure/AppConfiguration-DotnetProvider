using Azure;
using Azure.Data.AppConfiguration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class ConfigurationClientExtensions
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

            try
            {
                Response<ConfigurationSetting> response = await client.GetConfigurationSettingAsync(setting, onlyIfChanged: true, cancellationToken).ConfigureAwait(false);
                if (response.GetRawResponse().Status == (int)HttpStatusCode.OK)
                {
                    return new KeyValueChange
                    {
                        ChangeType = KeyValueChangeType.Modified,
                        Current = response.Value,
                        Key = setting.Key,
                        Label = setting.Label
                    };
                }
            }
            catch (RequestFailedException e) when (e.Status == (int)HttpStatusCode.NotFound && setting.ETag != default)
            {
                return new KeyValueChange
                {
                    ChangeType = KeyValueChangeType.Deleted,
                    Current = null,
                    Key = setting.Key,
                    Label = setting.Label
                };
            }

            return new KeyValueChange
            {
                ChangeType = KeyValueChangeType.None,
                Current = setting,
                Key = setting.Key,
                Label = setting.Label
            };
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
            var selector = SelectorFactory.CreateSettingSelector(keyFilter, labelFilter, fields: SettingFields.ETag | SettingFields.Key);

            // Dictionary of eTags that we write to and use for comparison
            var eTagMap = keyValues.ToDictionary(kv => kv.Key, kv => kv.ETag.ToString());

            // Fetch e-tags for prefixed key-values that can be used to detect changes
            await TracingUtils.CallWithRequestTracing(options.RequestTracingEnabled, RequestType.Watch, options.HostType,
                async () =>
                {
                    await foreach(ConfigurationSetting setting in client.GetConfigurationSettingsAsync(selector).ConfigureAwait(false))
                    {
                        if (!eTagMap.TryGetValue(setting.Key, out string etag) || !etag.Equals(setting.ETag))
                        {
                            hasKeyValueCollectionChanged = true;
                            break;
                        }

                        eTagMap.Remove(setting.Key);
                    }
                }).ConfigureAwait(false);

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
                selector = SelectorFactory.CreateSettingSelector(keyFilter, labelFilter);

                eTagMap = keyValues.ToDictionary(kv => kv.Key, kv => kv.ETag.ToString());
                await TracingUtils.CallWithRequestTracing(options.RequestTracingEnabled, RequestType.Watch, options.HostType,
                    async () =>
                    {
                        await foreach (ConfigurationSetting setting in client.GetConfigurationSettingsAsync(selector).ConfigureAwait(false))
                        {
                            if (!eTagMap.TryGetValue(setting.Key, out string etag) || !etag.Equals(setting.ETag))
                            {
                                changes.Add(new KeyValueChange
                                {
                                    ChangeType = KeyValueChangeType.Modified,
                                    Key = setting.Key,
                                    Label = options.Label.NormalizeNull(),
                                    Current = setting
                                });
                            }

                            eTagMap.Remove(setting.Key);
                        }
                    }).ConfigureAwait(false);

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
