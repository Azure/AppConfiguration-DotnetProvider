// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Models;
using System;
using System.Collections.Generic;
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
                if (response.GetRawResponse().Status == (int)HttpStatusCode.OK &&
                    !response.Value.ETag.Equals(setting.ETag))
                {
                    return new KeyValueChange
                    {
                        ChangeType = KeyValueChangeType.Modified,
                        Previous = setting,
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
                    Previous = setting,
                    Current = null,
                    Key = setting.Key,
                    Label = setting.Label
                };
            }

            return new KeyValueChange
            {
                ChangeType = KeyValueChangeType.None,
                Previous = setting,
                Current = setting,
                Key = setting.Key,
                Label = setting.Label
            };
        }

        public static async Task<bool> HaveCollectionsChanged(this ConfigurationClient client, KeyValueSelector keyValueSelector, IEnumerable<MatchConditions> matchConditions, IConfigurationSettingPageIterator pageIterator, CancellationToken cancellationToken)
        {
            if (matchConditions == null)
            {
                throw new ArgumentNullException(nameof(matchConditions));
            }

            if (keyValueSelector == null)
            {
                throw new ArgumentNullException(nameof(keyValueSelector));
            }

            if (keyValueSelector.SnapshotName != null)
            {
                throw new ArgumentException("Cannot check snapshot for changes.", $"{nameof(keyValueSelector)}.{nameof(keyValueSelector.SnapshotName)}");
            }

            SettingSelector selector = new SettingSelector
            {
                KeyFilter = keyValueSelector.KeyFilter,
                LabelFilter = keyValueSelector.LabelFilter
            };

            AsyncPageable<ConfigurationSetting> pageable = client.GetConfigurationSettingsAsync(selector, cancellationToken);

            using IEnumerator<MatchConditions> existingMatchConditionsEnumerator = matchConditions.GetEnumerator();

            await foreach (Page<ConfigurationSetting> page in pageable.AsPages(pageIterator, matchConditions).ConfigureAwait(false))
            {
                using Response response = page.GetRawResponse();

                // Return true if the lists of etags are different
                if ((!existingMatchConditionsEnumerator.MoveNext() ||
                    !existingMatchConditionsEnumerator.Current.IfNoneMatch.Equals(response.Headers.ETag)) &&
                    response.Status == (int)HttpStatusCode.OK)
                {
                    return true;
                }
            }

            // Need to check if pages were deleted and no change was found within the new shorter list of match conditions
            return existingMatchConditionsEnumerator.MoveNext();
        }
    }
}
