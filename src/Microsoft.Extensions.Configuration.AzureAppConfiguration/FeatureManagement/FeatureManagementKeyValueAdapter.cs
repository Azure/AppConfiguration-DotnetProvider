// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Data.AppConfiguration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class FeatureManagementKeyValueAdapter : IKeyValueAdapter
    {
        public Task<IEnumerable<KeyValuePair<string, string>>> ProcessKeyValue(ConfigurationSetting setting, Logger logger, CancellationToken cancellationToken)
        {
            FeatureFlag featureFlag;
            try
            {
                 featureFlag = JsonSerializer.Deserialize<FeatureFlag>(setting.Value);
            }
            catch (JsonException e)
            {
                throw new FormatException(setting.Key, e);
            }

            var keyValues = new List<KeyValuePair<string, string>>();

            if (featureFlag.Enabled)
            {
                //if (featureFlag.Conditions?.ClientFilters == null)
                if (featureFlag.Conditions?.ClientFilters == null || !featureFlag.Conditions.ClientFilters.Any()) // workaround since we are not yet setting client filters to null
                {
                    //
                    // Always on
                    keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.SectionName}:{featureFlag.Id}", true.ToString()));
                }
                else
                {
                    //
                    // Conditionally on based on feature filters
                    for (int i = 0; i < featureFlag.Conditions.ClientFilters.Count; i++)
                    {
                        ClientFilter clientFilter = featureFlag.Conditions.ClientFilters[i];

                        keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.SectionName}:{featureFlag.Id}:{FeatureManagementConstants.EnabledFor}:{i}:Name", clientFilter.Name));

                        foreach (KeyValuePair<string, string> kvp in new JsonFlattener().FlattenJson(clientFilter.Parameters))
                        {
                            keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.SectionName}:{featureFlag.Id}:{FeatureManagementConstants.EnabledFor}:{i}:Parameters:{kvp.Key}", kvp.Value));
                        }
                    }

                    //
                    // process RequirementType only when filters are not empty
                    if (featureFlag.Conditions.RequirementType != null)
                    {
                        keyValues.Add(new KeyValuePair<string, string>(
                            $"{FeatureManagementConstants.SectionName}:{featureFlag.Id}:{FeatureManagementConstants.RequirementType}", 
                            featureFlag.Conditions.RequirementType));
                    }
                }
            }
            else
            {
                keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.SectionName}:{featureFlag.Id}", false.ToString()));
            }

            if (featureFlag.Variants != null)
            {
                for (int i = 0; i < featureFlag.Variants.Count; i++)
                {
                    FeatureVariant featureVariant = featureFlag.Variants[i];

                    keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.SectionName}:{featureFlag.Id}:{FeatureManagementConstants.Variants}:{i}:Name", featureVariant.Name));

                    if (featureVariant.ConfigurationValue != null)
                    {
                        keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.SectionName}:{featureFlag.Id}:{FeatureManagementConstants.Variants}:{i}:ConfigurationValue", featureVariant.ConfigurationValue));
                    }

                    if (featureVariant.ConfigurationReference != null)
                    {
                        keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.SectionName}:{featureFlag.Id}:{FeatureManagementConstants.Variants}:{i}:ConfigurationReference", featureVariant.ConfigurationReference));
                    }

                    if (featureVariant.StatusOverride != null)
                    {
                        keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.SectionName}:{featureFlag.Id}:{FeatureManagementConstants.Variants}:{i}:StatusOverride", featureVariant.StatusOverride));
                    }
                }
            }

            if (featureFlag.Allocation != null)
            {
                FeatureAllocation featureAllocation = featureFlag.Allocation;

                if (featureAllocation.DefaultWhenDisabled != null)
                {
                    keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.SectionName}:{featureFlag.Id}:{FeatureManagementConstants.Allocation}:DefaultWhenDisabled", featureAllocation.DefaultWhenDisabled));
                }

                if (featureAllocation.DefaultWhenEnabled != null)
                {
                    keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.SectionName}:{featureFlag.Id}:{FeatureManagementConstants.Allocation}:DefaultWhenEnabled", featureAllocation.DefaultWhenEnabled));
                }

                if (featureAllocation.User != null)
                {
                    for (int i = 0; i < featureAllocation.User.Count; i++)
                    {
                        FeatureUserAllocation featureUserAllocation = featureAllocation.User[i];

                        keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.SectionName}:{featureFlag.Id}:{FeatureManagementConstants.Allocation}:{FeatureManagementConstants.User}:{i}:Variant", featureUserAllocation.Variant));

                        for (int j = 0; j < featureUserAllocation.Users.Count; j++)
                        {
                            keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.SectionName}:{featureFlag.Id}:{FeatureManagementConstants.Allocation}:{FeatureManagementConstants.User}:{i}:Users:{j}", featureUserAllocation.Users[j]));
                        }
                    }
                }

                if (featureAllocation.Group != null)
                {
                    for (int i = 0; i < featureAllocation.Group.Count; i++)
                    {
                        FeatureGroupAllocation featureGroupAllocation = featureAllocation.Group[i];

                        keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.SectionName}:{featureFlag.Id}:{FeatureManagementConstants.Allocation}:{FeatureManagementConstants.Group}:{i}:Variant", featureGroupAllocation.Variant));

                        for (int j = 0; j < featureGroupAllocation.Groups.Count; j++)
                        {
                            keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.SectionName}:{featureFlag.Id}:{FeatureManagementConstants.Allocation}:{FeatureManagementConstants.Group}:{i}:Groups:{j}", featureGroupAllocation.Groups[j]));
                        }
                    }
                }

                if (featureAllocation.Percentile != null)
                {
                    for (int i = 0; i < featureAllocation.Percentile.Count; i++)
                    {
                        FeaturePercentileAllocation featurePercentileAllocation = featureAllocation.Percentile[i];

                        keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.SectionName}:{featureFlag.Id}:{FeatureManagementConstants.Allocation}:{FeatureManagementConstants.Percentile}:{i}:Variant", featurePercentileAllocation.Variant));

                        keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.SectionName}:{featureFlag.Id}:{FeatureManagementConstants.Allocation}:{FeatureManagementConstants.Percentile}:{i}:From", featurePercentileAllocation.From.ToString()));

                        keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.SectionName}:{featureFlag.Id}:{FeatureManagementConstants.Allocation}:{FeatureManagementConstants.Percentile}:{i}:To", featurePercentileAllocation.To.ToString()));
                    }
                }

                if (featureAllocation.Seed != null)
                {
                    keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.SectionName}:{featureFlag.Id}:{FeatureManagementConstants.Allocation}:Seed", featureAllocation.Seed));
                }
            }

            if (featureFlag.TelemetryEnabled)
            {
                keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.SectionName}:{featureFlag.Id}:{FeatureManagementConstants.TelemetryEnabled}", featureFlag.TelemetryEnabled.ToString()));
            }

            if (featureFlag.TelemetryMetadata != null)
            {
                foreach (KeyValuePair<string, string> kvp in featureFlag.TelemetryMetadata)
                {
                    keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.SectionName}:{featureFlag.Id}:{FeatureManagementConstants.TelemetryMetadata}:{kvp.Key}", kvp.Value));
                }
            }

            return Task.FromResult<IEnumerable<KeyValuePair<string, string>>>(keyValues);
        }

        public bool CanProcess(ConfigurationSetting setting)
        {
            string contentType = setting?.ContentType?.Split(';')[0].Trim();

            return string.Equals(contentType, FeatureManagementConstants.ContentType) ||
                                 setting.Key.StartsWith(FeatureManagementConstants.FeatureFlagMarker);
        }

        public void InvalidateCache(ConfigurationSetting setting = null)
        {
            return;
        }

        public bool NeedsRefresh()
        {
            return false;
        }
    }
}
