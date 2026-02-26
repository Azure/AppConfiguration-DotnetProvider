// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Models;
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
                using Response rawResponse = response.GetRawResponse();
                if (rawResponse.Status == (int)HttpStatusCode.OK &&
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

        public static async Task<bool> HaveCollectionsChanged(
            this ConfigurationClient client,
            KeyValueSelector keyValueSelector,
            IEnumerable<WatchedPage> pageWatchers,
            IConfigurationSettingPageIterator pageIterator,
            bool makeConditionalRequest,
            CancellationToken cancellationToken)
        {
            if (pageWatchers == null)
            {
                throw new ArgumentNullException(nameof(pageWatchers));
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

            AsyncPageable<ConfigurationSetting> pageable = client.CheckConfigurationSettingsAsync(selector, cancellationToken);

            using IEnumerator<WatchedPage> existingPageWatcherEnumerator = pageWatchers.GetEnumerator();

            IAsyncEnumerable<Page<ConfigurationSetting>> pages = makeConditionalRequest
                ? pageable.AsPages(pageIterator, pageWatchers.Select(p => p.MatchConditions))
                : pageable.AsPages(pageIterator);

            await foreach (Page<ConfigurationSetting> page in pages.ConfigureAwait(false))
            {
                using Response rawResponse = page.GetRawResponse();
                DateTimeOffset serverResponseTime = rawResponse.GetMsDate();

                if (!existingPageWatcherEnumerator.MoveNext() ||
                    (rawResponse.Status == (int)HttpStatusCode.OK &&
                    // if the server response time is later than last server response time, the change is considered detected
                    serverResponseTime >= existingPageWatcherEnumerator.Current.LastServerResponseTime &&
                    !existingPageWatcherEnumerator.Current.MatchConditions.IfNoneMatch.Equals(rawResponse.Headers.ETag)))
                {
                    return true;
                }
            }

            // Need to check if pages were deleted and no change was found within the new shorter list of page
            return existingPageWatcherEnumerator.MoveNext();
        }
    }
}
