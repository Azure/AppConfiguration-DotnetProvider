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
        public static async Task<KeyValueChange> GetKeyValueChange(this ConfigurationClient client, ConfigurationSetting setting, bool makeConditionalRequest, CancellationToken cancellationToken)
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
                Response<ConfigurationSetting> response = await client.GetConfigurationSettingAsync(setting, onlyIfChanged: makeConditionalRequest, cancellationToken).ConfigureAwait(false);
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

        public static async Task<bool> HaveCollectionsChanged(this ConfigurationClient client, KeyValueSelector keyValueSelector, IEnumerable<MatchConditions> matchConditions, IConfigurationSettingPageIterator pageIterator, ICdnCacheBustingAccessor cdnCacheBustingAccessor, CancellationToken cancellationToken)
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

            bool isCdnEnabled = cdnCacheBustingAccessor != null;

            IAsyncEnumerable<Page<ConfigurationSetting>> pages = isCdnEnabled ? pageable.AsPages() : pageable.AsPages(pageIterator, matchConditions);

            bool canPeek = existingMatchConditionsEnumerator.MoveNext();

            await foreach (Page<ConfigurationSetting> page in pages.ConfigureAwait(false))
            {
                using Response response = page.GetRawResponse();

                if ((!canPeek ||
                    !existingMatchConditionsEnumerator.Current.IfNoneMatch.Equals(response.Headers.ETag)) &&
                    response.Status == (int)HttpStatusCode.OK)
                {
                    if (isCdnEnabled)
                    {
                        cdnCacheBustingAccessor.CurrentToken = response.Headers.ETag.ToString();
                    }

                    return true;
                }

                canPeek = existingMatchConditionsEnumerator.MoveNext();

                if (isCdnEnabled && canPeek)
                {
                    cdnCacheBustingAccessor.CurrentToken = existingMatchConditionsEnumerator.Current.IfNoneMatch.ToString();
                }
            }

            // Need to check if pages were deleted and no change was found within the new shorter list of match conditions
            return existingMatchConditionsEnumerator.MoveNext();
        }
    }
}
