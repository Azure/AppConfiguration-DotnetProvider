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
                if (response.GetRawResponse().Status == (int)HttpStatusCode.OK)
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

        public static async Task<IEnumerable<MatchConditions>> GetNewMatchConditions(this ConfigurationClient client, KeyValueSelector keyValueSelector, IEnumerable<MatchConditions> matchConditions, ConfigurationSettingPageableManager pageableManager, CancellationToken cancellationToken)
        {
            if (matchConditions == null)
            {
                throw new ArgumentNullException(nameof(matchConditions));
            }

            SettingSelector selector = new SettingSelector
            {
                KeyFilter = keyValueSelector.KeyFilter,
                LabelFilter = keyValueSelector.LabelFilter
            };

            bool hasCollectionChanged = false;

            var newMatchConditions = new List<MatchConditions>();

            AsyncPageable<ConfigurationSetting> pageable = client.GetConfigurationSettingsAsync(selector, cancellationToken);

            using IEnumerator<MatchConditions> existingMatchConditionsEnumerator = matchConditions.GetEnumerator();

            await foreach (Page<ConfigurationSetting> page in pageableManager.GetPages(pageable, matchConditions).ConfigureAwait(false))
            {
                ETag serverEtag = (ETag)page?.GetRawResponse()?.Headers.ETag;

                if (page?.Values == null)
                {
                    throw new RequestFailedException(ErrorMessages.InvalidConfigurationSettingPage);
                }

                Response response = page.GetRawResponse();

                newMatchConditions.Add(new MatchConditions { IfNoneMatch = serverEtag });

                // Set hasCollectionChanged to true if the lists of etags are different, and continue iterating to get all of newMatchConditions
                if ((!existingMatchConditionsEnumerator.MoveNext() ||
                    !existingMatchConditionsEnumerator.Current.IfNoneMatch.Equals(serverEtag)) &&
                    response.Status == (int)HttpStatusCode.OK)
                {
                    hasCollectionChanged = true;
                }
            }

            // Need to check if pages were deleted since hasCollectionsChanged wouldn't have been set
            if (hasCollectionChanged || existingMatchConditionsEnumerator.MoveNext())
            {
                return newMatchConditions;
            }

            return null;
        }
    }
}
