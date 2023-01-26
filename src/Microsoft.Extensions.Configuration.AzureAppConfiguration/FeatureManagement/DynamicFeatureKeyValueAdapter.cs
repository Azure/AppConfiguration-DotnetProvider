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
        private static readonly string DynamicFeatureSectionPrefix =
            $"{FeatureManagementConstants.FeatureManagementSectionName}:{FeatureManagementConstants.DynamicFeatureSectionName}";
                                                                    
        public Task<IEnumerable<KeyValuePair<string, string>>> ProcessKeyValue(ConfigurationSetting setting, Logger logger, CancellationToken cancellationToken)
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

            keyValues.Add(
                new KeyValuePair<string, string>(
                    $"{DynamicFeatureSectionPrefix}:{dynamicFeature.Id}:{FeatureManagementConstants.DynamicFeatureAssigner}",
                    dynamicFeature.ClientAssigner));

            for (int i = 0; i < dynamicFeature.Variants.Count; i++)
            {
                FeatureVariant variant = dynamicFeature.Variants[i];
                string variantsSectionPrefix = $"{DynamicFeatureSectionPrefix}:{dynamicFeature.Id}:{FeatureManagementConstants.DynamicFeatureVariants}:{i}";

                keyValues.Add(
                    new KeyValuePair<string, string>(
                        $"{variantsSectionPrefix}:{FeatureManagementConstants.DynamicFeatureVariantName}", 
                        variant.Name));

                keyValues.Add(
                    new KeyValuePair<string, string>(
                        $"{variantsSectionPrefix}:{FeatureManagementConstants.DynamicFeatureConfigurationReference}", 
                        variant.ConfigurationReference));
                
                if (variant.Default)
                {
                    keyValues.Add(
                        new KeyValuePair<string, string>(
                            $"{variantsSectionPrefix}:{FeatureManagementConstants.DynamicFeatureDefault}", 
                            variant.Default.ToString()));
                }

                foreach (KeyValuePair<string, string> kvp in new JsonFlattener().FlattenJson(variant.AssignmentParameters))
                {
                    keyValues.Add(
                        new KeyValuePair<string, string>(
                            $"{variantsSectionPrefix}:{FeatureManagementConstants.DynamicFeatureAssignmentParameters}:{kvp.Key}", 
                            kvp.Value));
                }
            }

            return Task.FromResult<IEnumerable<KeyValuePair<string, string>>>(keyValues);
        }

        public bool CanProcess(ConfigurationSetting setting)
        {
            if (setting != null && setting.Key != null && setting.ContentType != null)
            {
                var endIndex = setting.ContentType.IndexOf(";");
                if (endIndex > 0)
                {
                    return string.Equals(setting.ContentType.Substring(0, endIndex), FeatureManagementConstants.DynamicFeatureContentType) &&
                                       setting.Key.StartsWith(FeatureManagementConstants.FeatureFlagMarker);
                }
            }

            return false;
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
