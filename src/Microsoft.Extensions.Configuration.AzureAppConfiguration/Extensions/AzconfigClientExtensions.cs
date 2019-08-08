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
        public static async Task<KeyValueChange> GetKeyValueChange(this AzconfigClient client, IKeyValue keyValue, IRequestOptions options, CancellationToken cancellationToken)
        {
            if (keyValue == null)
            {
                throw new ArgumentNullException(nameof(keyValue));
            }

            if (string.IsNullOrEmpty(keyValue.Key))
            {
                throw new ArgumentNullException($"{nameof(keyValue)}.{nameof(keyValue.Key)}");
            }

            var currentKeyValue = await client.GetCurrentKeyValue(keyValue, options, cancellationToken).ConfigureAwait(false);

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

        public static async Task<IEnumerable<KeyValueChange>> GetKeyValueChangeCollection(this AzconfigClient client, IEnumerable<IKeyValue> keyValues, GetKeyValueChangeCollectionOptions options)
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

            var hasKeyValueCollectionChanged = false;
            var queryOptions = new QueryKeyValueCollectionOptions
            {
                KeyFilter = options.Prefix + "*",
                LabelFilter = string.IsNullOrEmpty(options.Label) ? LabelFilter.Null : options.Label,
                FieldsSelector = KeyValueFields.ETag | KeyValueFields.Key
            };

            queryOptions.ConfigureRequestTracing(options.RequestTracingEnabled, RequestType.Watch, options.HostType);

            // Fetch e-tags for prefixed key-values that can be used to detect changes
            IEnumerable<IKeyValue> kvs = await client.GetKeyValues(queryOptions).ToEnumerableAsync(CancellationToken.None).ConfigureAwait(false);

            // Dictionary of eTags that we write to and use for comparison
            var eTagMap = keyValues.ToDictionary(kv => kv.Key, kv => kv.Value);
            
            // Check for any modifications/creations
            foreach (IKeyValue kv in kvs)
            {
                if (!eTagMap.TryGetValue(kv.Key, out string etag) || !etag.Equals(kv.ETag))
                {
                    hasKeyValueCollectionChanged = true;
                    break;
                }

                eTagMap.Remove(kv.Key);
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
                queryOptions = new QueryKeyValueCollectionOptions
                {
                    KeyFilter = options.Prefix + "*",
                    LabelFilter = string.IsNullOrEmpty(options.Label) ? LabelFilters.Null : options.Label
                };

                queryOptions.ConfigureRequestTracing(options.RequestTracingEnabled, RequestType.Watch, options.HostType);
                kvs = await client.GetKeyValues(queryOptions).ToEnumerableAsync(CancellationToken.None).ConfigureAwait(false);
                eTagMap = keyValues.ToDictionary(kv => kv.Key, kv => kv.ETag);

                foreach (IKeyValue kv in kvs)
                {
                    if (!eTagMap.TryGetValue(kv.Key, out string etag) || !etag.Equals(kv.ETag))
                    {
                        changes.Add(new KeyValueChange
                        {
                            ChangeType = KeyValueChangeType.Modified,
                            Key = kv.Key,
                            Label = options.Label.NormalizeNull(),
                            Current = kv
                        });
                    }

                    eTagMap.Remove(kv.Key);
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
