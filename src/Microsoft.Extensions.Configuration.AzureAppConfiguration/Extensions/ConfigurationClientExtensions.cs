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
        public static async Task<KeyValueChange> GetKeyValueChange(this ConfigurationClient client, ConfigurationSetting setting, bool makeConditionalRequest, DateTimeOffset lastChangeDetectedTime, CancellationToken cancellationToken)
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
                using Response rawResponse = response.GetRawResponse();
                if (rawResponse.Status == (int)HttpStatusCode.OK &&
                    !response.Value.ETag.Equals(setting.ETag) &&
                    rawResponse.GetDate() >= lastChangeDetectedTime)
                {
                    return new KeyValueChange
                    {
                        ChangeType = KeyValueChangeType.Modified,
                        Previous = setting,
                        Current = response.Value,
                        Key = setting.Key,
                        Label = setting.Label,
                        DetectedTime = rawResponse.GetDate()
                    };
                }
            }
            catch (RequestFailedException e) when (e.Status == (int)HttpStatusCode.NotFound && setting.ETag != default)
            {
                using Response rawResponse = e.GetRawResponse();
                if (rawResponse.GetDate() >= lastChangeDetectedTime)
                {
                    return new KeyValueChange
                    {
                        ChangeType = KeyValueChangeType.Deleted,
                        Previous = setting,
                        Current = null,
                        Key = setting.Key,
                        Label = setting.Label,
                        DetectedTime = rawResponse.GetDate()
                    };
                }
            }

            return new KeyValueChange
            {
                ChangeType = KeyValueChangeType.None,
                Previous = setting,
                Current = setting,
                Key = setting.Key,
                Label = setting.Label,
                DetectedTime = lastChangeDetectedTime
            };
        }

        public static async Task<Page<ConfigurationSetting>> GetPageChange(this ConfigurationClient client, KeyValueSelector keyValueSelector, IEnumerable<PageWatcher> pageWatchers, IConfigurationSettingPageIterator pageIterator, bool makeConditionalRequest, CancellationToken cancellationToken)
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

            AsyncPageable<ConfigurationSetting> pageable = client.GetConfigurationSettingsAsync(selector, cancellationToken);

            IAsyncEnumerable<Page<ConfigurationSetting>> pages = makeConditionalRequest
                ? pageable.AsPages(pageIterator, pageWatchers.Select(p => p.Etag))
                : pageable.AsPages(pageIterator);

            List<PageWatcher> existingWatcherList = pageWatchers.ToList();

            int i = 0;

            await foreach (Page<ConfigurationSetting> page in pages.ConfigureAwait(false))
            {
                using Response response = page.GetRawResponse();
                DateTimeOffset timestamp = response.GetDate();

                if (i >= existingWatcherList.Count ||
                    (response.Status == (int)HttpStatusCode.OK &&
                    timestamp >= existingWatcherList[i].LastUpdateTime &&
                    !existingWatcherList[i].Etag.IfNoneMatch.Equals(response.Headers.ETag)))
                {
                    return page;
                }
            }

            return null;
        }
    }
}
