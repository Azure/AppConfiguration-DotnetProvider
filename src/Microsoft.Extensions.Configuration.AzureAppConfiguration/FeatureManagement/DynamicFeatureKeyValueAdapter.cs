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
        private static readonly string DynamicFeatureSectionPrefix = FeatureManagementConstants.FeatureManagementSectionName + 
                                                                     ConfigurationPath.KeyDelimiter +
                                                                     FeatureManagementConstants.DynamicFeaturesSectionName;

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

            keyValues.Add(
                new KeyValuePair<string, string>(
                    $"{DynamicFeatureSectionPrefix}:{dynamicFeature.Id}:{FeatureManagementConstants.Assigner}",
                    dynamicFeature.ClientAssigner));

            for (int i = 0; i < dynamicFeature.Variants.Count; i++)
            {
                FeatureVariant variant = dynamicFeature.Variants[i];
                string variantsSectionPrefix = $"{DynamicFeatureSectionPrefix}:{dynamicFeature.Id}:{FeatureManagementConstants.Variants}:{i}";

                keyValues.Add(
                    new KeyValuePair<string, string>(
                        $"{variantsSectionPrefix}:{FeatureManagementConstants.Name}", 
                        variant.Name));

                keyValues.Add(
                    new KeyValuePair<string, string>(
                        $"{variantsSectionPrefix}:{FeatureManagementConstants.ConfigurationReference}", 
                        variant.ConfigurationReference));
                
                if (variant.Default)
                {
                    keyValues.Add(
                        new KeyValuePair<string, string>(
                            $"{variantsSectionPrefix}:{FeatureManagementConstants.Default}", 
                            variant.Default.ToString()));
                }

                foreach (KeyValuePair<string, string> kvp in new JsonFlattener().FlattenJson(variant.AssignmentParameters))
                {
                    keyValues.Add(
                        new KeyValuePair<string, string>(
                            $"{variantsSectionPrefix}:{FeatureManagementConstants.AssignmentParameters}:{kvp.Key}", 
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
