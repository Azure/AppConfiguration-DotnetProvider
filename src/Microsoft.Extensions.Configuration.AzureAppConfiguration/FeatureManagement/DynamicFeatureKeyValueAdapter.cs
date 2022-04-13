// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Data.AppConfiguration;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class DynamicFeatureKeyValueAdapter : IKeyValueAdapter
    {
        public Task<IEnumerable<KeyValuePair<string, string>>> ProcessKeyValue(ConfigurationSetting setting, CancellationToken cancellationToken)
        {
            DynamicFeature dynamicFeature;

            try
            {
                 dynamicFeature = JsonSerializer.Deserialize<DynamicFeature>(setting.Value);
            }
            catch (JsonException e)
            {
                throw new FormatException(setting.Key, e);
            }

            var keyValues = new List<KeyValuePair<string, string>>();

            keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.FeatureManagementSectionName}:{FeatureManagementConstants.DynamicFeaturesSectionName}:{dynamicFeature.Id}:Assigner", dynamicFeature.ClientAssigner));

            for (int i = 0; i < dynamicFeature.Variants.Count; i++)
            {
                FeatureVariant variant = dynamicFeature.Variants[i];

                keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.FeatureManagementSectionName}:{FeatureManagementConstants.DynamicFeaturesSectionName}:{dynamicFeature.Id}:Variants:{i}:Name", variant.Name));
                keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.FeatureManagementSectionName}:{FeatureManagementConstants.DynamicFeaturesSectionName}:{dynamicFeature.Id}:Variants:{i}:ConfigurationReference", variant.ConfigurationReference));
                
                if (variant.Default)
                {
                    keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.FeatureManagementSectionName}:{FeatureManagementConstants.DynamicFeaturesSectionName}:{dynamicFeature.Id}:Variants:{i}:Default", variant.Default.ToString()));
                }

                foreach (KeyValuePair<string, string> kvp in new JsonFlattener().FlattenJson(variant.AssignmentParameters))
                {
                    keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.FeatureManagementSectionName}:{FeatureManagementConstants.DynamicFeaturesSectionName}:{dynamicFeature.Id}:Variants:{i}:AssignmentParameters:{kvp.Key}", kvp.Value));
                }
            }

            return Task.FromResult<IEnumerable<KeyValuePair<string, string>>>(keyValues);
        }

        public bool CanProcess(ConfigurationSetting setting)
        {
            string contentType = setting?.ContentType?.Split(';')[0].Trim();

            return string.Equals(contentType, FeatureManagementConstants.DynamicFeatureContentType) &&
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
