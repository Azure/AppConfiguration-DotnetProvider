using Microsoft.Azure.AppConfiguration.Azconfig;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class AzconfigClientExtensions
    {
        public static async Task<KeyValueChange> GetKeyValueChange(this AzconfigClient client, IKeyValue keyValue, CancellationToken cancellationToken, bool requestTracingEnabled = true, HostType hostType = HostType.None)
        {
            if (keyValue == null)
            {
                throw new ArgumentNullException(nameof(keyValue));
            }

            if (string.IsNullOrEmpty(keyValue.Key))
            {
                throw new ArgumentNullException($"{nameof(keyValue)}.{nameof(keyValue.Key)}");
            }

            RequestOptionsBase options = requestTracingEnabled ? new RequestOptionsBase() : null;
            options.ConfigureRequestTracingOptions(requestTracingEnabled, true, hostType);
            var currentKeyValue = await client.GetCurrentKeyValue(keyValue, options, cancellationToken);

            if (currentKeyValue == keyValue)
            {
                return null;
            }

            return new KeyValueChange
            {
                ChangeType = currentKeyValue == null ? KeyValueChangeType.Deleted : KeyValueChangeType.Modified,
                Current = currentKeyValue,
                Key = keyValue.Key,
                Label = keyValue.Label
            };
        }

        public static async Task<IEnumerable<KeyValueChange>> GetKeyValueChangeCollection(this AzconfigClient client, GetKeyValueChangeCollectionOptions options, IEnumerable<IKeyValue> keyValues, bool requestTracingEnabled = true, HostType hostType = HostType.None)
        {
            ValidateInputForGetKeyValueChangeCollection(options, keyValues);

            IEnumerable<KeyValueChange> changes = null;
            var currentEtags = keyValues.ToDictionary(kv => kv.Key, kv => kv.ETag);

            if (await HasKeyValueCollectionChanged(client, options, currentEtags, requestTracingEnabled, hostType))
            {
                changes = await GetKeyValueChangeCollectionHelper(client, options, keyValues, currentEtags, requestTracingEnabled, hostType);
            }

            return changes;
        }

        private static void ValidateInputForGetKeyValueChangeCollection(GetKeyValueChangeCollectionOptions options, IEnumerable<IKeyValue> keyValues)
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

            if (options.Prefix.Contains('*'))
            {
                throw new ArgumentException("The prefix cannot contain '*'", $"{nameof(options)}.{nameof(options.Prefix)}");
            }

            if (keyValues.Any(k => string.IsNullOrEmpty(k.Key)))
            {
                throw new ArgumentNullException($"{nameof(keyValues)}[].{nameof(IKeyValue.Key)}");
            }

            if (!string.IsNullOrEmpty(options.Prefix) && keyValues.Any(k => !k.Key.StartsWith(options.Prefix)))
            {
                throw new ArgumentException("All key-values registered for refresh must start with the provided prefix.", $"{nameof(keyValues)}[].{nameof(IKeyValue.Key)}");
            }

            if (keyValues.Any(k => !string.Equals(k.Label.NormalizeNull(), options.Label.NormalizeNull())))
            {
                throw new ArgumentException("All key-values registered for refresh must use the same label.", $"{nameof(keyValues)}[].{nameof(IKeyValue.Label)}");
            }

            if (keyValues.Any(k => k.Label != null && k.Label.Contains("*")))
            {
                throw new ArgumentException("The label filter cannot contain '*'", $"{nameof(options)}.{nameof(options.Label)}");
            }
        }

        private static async Task<bool> HasKeyValueCollectionChanged(AzconfigClient client, GetKeyValueChangeCollectionOptions options, Dictionary<string, string> eTagMap, bool requestTracingEnabled, HostType hostType)
        {
            var queryOptions = new QueryKeyValueCollectionOptions
            {
                KeyFilter = options.Prefix + "*",
                LabelFilter = string.IsNullOrEmpty(options.Label) ? LabelFilter.Null : options.Label,
                FieldsSelector = KeyValueFields.ETag | KeyValueFields.Key
            };

            queryOptions.ConfigureRequestTracingOptions(requestTracingEnabled, true, hostType);
            var keyValues = await client.GetKeyValues(queryOptions).ToEnumerableAsync(CancellationToken.None);

            // Copy of eTags that we write to and use for comparison
            var eTags = eTagMap.ToDictionary(kv => kv.Key, kv => kv.Value);

            // Check for any modifications/creations
            foreach (IKeyValue kv in keyValues)
            {
                if (!eTags.TryGetValue(kv.Key, out string etag) || !etag.Equals(kv.ETag))
                {
                    return true;
                }

                eTags.Remove(kv.Key);
            }

            // Check for any deletions
            if (eTags.Any())
            {
                return true;
            }

            return false;
        }

        private static async Task<IEnumerable<KeyValueChange>> GetKeyValueChangeCollectionHelper(AzconfigClient client, GetKeyValueChangeCollectionOptions options, IEnumerable<IKeyValue> keyValues, Dictionary<string, string> currentEtags, bool requestTracingEnabled, HostType hostType)
        {
            var changes = new List<KeyValueChange>();
            var queryOptions = new QueryKeyValueCollectionOptions
            {
                KeyFilter = options.Prefix + "*",
                LabelFilter = string.IsNullOrEmpty(options.Label) ? LabelFilters.Null : options.Label
            };

            queryOptions.ConfigureRequestTracingOptions(requestTracingEnabled, true, hostType);

            // Changes have been observed, refresh prefixed key-values
            IEnumerable<IKeyValue> kvs = await client.GetKeyValues(queryOptions).ToEnumerableAsync(CancellationToken.None);

            // Copy for current eTags for comparison, since we plan to edit them
            var etags = currentEtags.ToDictionary(kv => kv.Key, kv => kv.Value);

            // Update current etags with the latest value seen from the server
            currentEtags = kvs.ToDictionary(kv => kv.Key, kv => kv.ETag);

            foreach (IKeyValue kv in kvs)
            {
                if (!etags.TryGetValue(kv.Key, out string etag) || !etag.Equals(kv.ETag))
                {
                    changes.Add(new KeyValueChange
                    {
                        ChangeType = KeyValueChangeType.Modified,
                        Key = kv.Key,
                        Label = options.Label.NormalizeNull(),
                        Current = kv
                    });
                }

                etags.Remove(kv.Key);
            }

            foreach (var kvp in etags)
            {
                changes.Add(new KeyValueChange
                {
                    ChangeType = KeyValueChangeType.Deleted,
                    Key = kvp.Key,
                    Label = options.Label.NormalizeNull(),
                    Current = null
                });
            }

            return changes;
        }
    }
}
