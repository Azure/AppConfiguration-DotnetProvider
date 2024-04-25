// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class ConfigurationClientExtensions
    {
        public static async Task<KeyValueChange> GetKeyValueChange(this ConfigurationClient client, SettingSelector selector, IEnumerable<MatchConditions> matchConditions, CancellationToken cancellationToken)
        {
            if (selector == null)
            {
                throw new ArgumentNullException(nameof(selector));
            }

            if (matchConditions == null)
            {
                throw new ArgumentNullException(nameof(matchConditions));
            }

            if (matchConditions.Count() != 1)
            {
                throw new ArgumentException("Requires exactly one MatchConditions value.", nameof(matchConditions));
            }

            MatchConditions condition = matchConditions.First();

            if (condition.IfNoneMatch.HasValue)
            {
                throw new ArgumentException("Must have valid IfNoneMatch header.", nameof(matchConditions));
            }

            ConfigurationSetting setting = new ConfigurationSetting(selector.KeyFilter, null, selector.LabelFilter, condition.IfNoneMatch.Value);

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
            catch (RequestFailedException e) when (e.Status == (int)HttpStatusCode.NotFound && condition.IfNoneMatch.Value != default)
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
                Current = null,
                Key = setting.Key,
                Label = setting.Label
            };
        }

        public static async Task<bool> HasAnyKeyValueChanged(this ConfigurationClient client, SettingSelector selector, IEnumerable<MatchConditions> matchConditions, CancellationToken cancellationToken)
        {
            if (selector == null)
            {
                throw new ArgumentNullException(nameof(selector));
            }

            if (matchConditions == null)
            {
                throw new ArgumentNullException(nameof(matchConditions));
            }

            if (!matchConditions.Any())
            {
                throw new ArgumentException("Requires at least one MatchConditions value.", nameof(matchConditions));
            }

            foreach (MatchConditions condition in matchConditions)
            {
                selector.MatchConditions.Add(condition);
            }

            await foreach (Page<ConfigurationSetting> page in client.GetConfigurationSettingsAsync(selector, cancellationToken).AsPages().ConfigureAwait(false))
            {
                Response response = page.GetRawResponse();

                if (response.Status == (int)HttpStatusCode.OK)
                {
                    return true;
                }
            }

            return false;
        }

        public static async Task<IEnumerable<KeyValueChange>> GetKeyValueChangeCollection(
            this ConfigurationClient client,
            GetKeyValueChangeCollectionOptions options,
            StringBuilder logDebugBuilder,
            StringBuilder logInfoBuilder,
            Uri endpoint,
            CancellationToken cancellationToken)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (options.Selector == null)
            {
                throw new ArgumentNullException($"{nameof(options)}.{nameof(options.Selector)}");
            }

            if (string.IsNullOrEmpty(options.Selector.KeyFilter))
            {
                throw new ArgumentNullException($"{nameof(options)}.{nameof(options.Selector)}.{nameof(SettingSelector.KeyFilter)}");
            }

            if (options.Selector.LabelFilter != null && options.Selector.LabelFilter.Contains("*"))
            {
                throw new ArgumentException("The label filter cannot contain '*'", $"{nameof(options)}.{nameof(options.Selector)}.{nameof(options.Selector.LabelFilter)}");
            }

            bool hasKeyValueCollectionChanged = false;

            await TracingUtils.CallWithRequestTracing(options.RequestTracingEnabled, RequestType.Watch, options.RequestTracingOptions,
                async () => hasKeyValueCollectionChanged = await client.HasAnyKeyValueChanged(options.Selector, options.MatchConditions, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

            var changes = new List<KeyValueChange>();

            if (!hasKeyValueCollectionChanged)
            {
                return changes;
            }

            await TracingUtils.CallWithRequestTracing(options.RequestTracingEnabled, RequestType.Watch, options.RequestTracingOptions,
                async () =>
                {
                    await foreach (ConfigurationSetting setting in client.GetConfigurationSettingsAsync(options.Selector, cancellationToken).ConfigureAwait(false))
                    {
                        if (!eTagMap.TryGetValue(setting.Key, out ETag etag) || !etag.Equals(setting.ETag))
                        {
                            changes.Add(new KeyValueChange
                            {
                                ChangeType = KeyValueChangeType.Modified,
                                Key = setting.Key,
                                Label = options.Label.NormalizeNull(),
                                Current = setting
                            });
                            string key = setting.Key.Substring(FeatureManagementConstants.FeatureFlagMarker.Length);
                            logDebugBuilder.AppendLine(LogHelper.BuildFeatureFlagReadMessage(key, options.Label.NormalizeNull(), endpoint.ToString()));
                            logInfoBuilder.AppendLine(LogHelper.BuildFeatureFlagUpdatedMessage(key));
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
                string key = kvp.Key.Substring(FeatureManagementConstants.FeatureFlagMarker.Length);
                logDebugBuilder.AppendLine(LogHelper.BuildFeatureFlagReadMessage(key, options.Label.NormalizeNull(), endpoint.ToString()));
                logInfoBuilder.AppendLine(LogHelper.BuildFeatureFlagUpdatedMessage(key));
            }
            
            return changes;
        }
    }
}
