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

        public static async Task<IEnumerable<MatchConditions>> GetNewMatchConditions(this ConfigurationClient client, KeyValueIdentifier keyValueIdentifier, IEnumerable<MatchConditions> matchConditions, ConfigurationSettingPageableManager pageableManager, CancellationToken cancellationToken)
        {
            if (matchConditions == null)
            {
                throw new ArgumentNullException(nameof(matchConditions));
            }

            SettingSelector selector = new SettingSelector
            {
                KeyFilter = keyValueIdentifier.Key,
                LabelFilter = keyValueIdentifier.Label
            };

            bool hasCollectionChanged = false;

            List<MatchConditions> newMatchConditions = new List<MatchConditions>();

            AsyncPageable<ConfigurationSetting> pageable = client.GetConfigurationSettingsAsync(selector, cancellationToken);

            await foreach (Page<ConfigurationSetting> page in pageableManager.GetPages(pageable, matchConditions).ConfigureAwait(false))
            {
                Response response = page.GetRawResponse();

                newMatchConditions.Add(new MatchConditions { IfNoneMatch = response.Headers.ETag });

                if (response.Status == (int)HttpStatusCode.OK)
                {
                    hasCollectionChanged = true;
                }
            }

            if (hasCollectionChanged || (!newMatchConditions.Any() && matchConditions.Any()))
            {
                return newMatchConditions;
            }

            return null;
        }
    }
}
