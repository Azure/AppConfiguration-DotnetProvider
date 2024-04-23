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
        private FeatureFilterTracing _featureFilterTracing;

        public FeatureManagementKeyValueAdapter(FeatureFilterTracing featureFilterTracing)
        {
            _featureFilterTracing = featureFilterTracing ?? throw new ArgumentNullException(nameof(featureFilterTracing));
        }

        public Task<IEnumerable<KeyValuePair<string, string>>> ProcessKeyValue(ConfigurationSetting setting, Logger logger, CancellationToken cancellationToken)
        {
            FeatureFlag featureFlag;
            try
            {
                 featureFlag = JsonSerializer.Deserialize<FeatureFlag>(setting.Value);
            }
            catch (JsonException e)
            {
                throw new FormatException($"Failed to parse invalid feature flag json. Key: '{setting.Key}'", e);
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

                        _featureFilterTracing.UpdateFeatureFilterTracing(clientFilter.Name);

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
