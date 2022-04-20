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
    internal class FeatureFlagKeyValueAdapter : IKeyValueAdapter
    {
        private static string _schemaVersion = FeatureManagementConstants.FeatureManagementSchemaV1;

        public FeatureFlagKeyValueAdapter(string schemaVersion)
        {
            _schemaVersion = schemaVersion;
        }

        public Task<IEnumerable<KeyValuePair<string, string>>> ProcessKeyValue(ConfigurationSetting setting, CancellationToken cancellationToken)
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

            if (cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            List<KeyValuePair<string, string>> keyValues;

            if (_schemaVersion == FeatureManagementConstants.FeatureManagementSchemaV1)
            {
                keyValues = FlattenFeatureFlagWithV1Schema(featureFlag);
            }
            else
            {
                keyValues = FlattenFeatureFlagWithV2Schema(featureFlag);
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
                    return string.Equals(setting.ContentType.Substring(0, endIndex), FeatureManagementConstants.FeatureFlagContentType) &&
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

        private List<KeyValuePair<string, string>> FlattenFeatureFlagWithV1Schema(FeatureFlag featureFlag)
        {
            var keyValues = new List<KeyValuePair<string, string>>();

            if (featureFlag.Enabled)
            {
                //if (featureFlag.Conditions?.ClientFilters == null)
                if (featureFlag.Conditions?.ClientFilters == null || !featureFlag.Conditions.ClientFilters.Any()) // workaround since we are not yet setting client filters to null
                {
                    //
                    // Always on
                    keyValues.Add(
                        new KeyValuePair<string, string>(
                            $"{FeatureManagementConstants.FeatureManagementSectionName}:{featureFlag.Id}",
                            true.ToString()));
                }
                else
                {
                    //
                    // Conditionally on based on feature filters
                    for (int i = 0; i < featureFlag.Conditions.ClientFilters.Count; i++)
                    {
                        ClientFilter clientFilter = featureFlag.Conditions.ClientFilters[i];
                        string enabledForSectionPrefix =
                            $"{FeatureManagementConstants.FeatureManagementSectionName}:{featureFlag.Id}:{FeatureManagementConstants.FeatureFlagEnabledFor}:{i}";

                        keyValues.Add(
                            new KeyValuePair<string, string>(
                                $"{enabledForSectionPrefix}:{FeatureManagementConstants.FeatureFlagFilterName}",
                                clientFilter.Name));

                        foreach (KeyValuePair<string, string> kvp in new JsonFlattener().FlattenJson(clientFilter.Parameters))
                        {
                            keyValues.Add(
                                new KeyValuePair<string, string>(
                                    $"{enabledForSectionPrefix}:{FeatureManagementConstants.FeatureFlagParameters}:{kvp.Key}",
                                    kvp.Value));
                        }
                    }
                }
            }
            else
            {
                keyValues.Add(
                    new KeyValuePair<string, string>(
                        $"{FeatureManagementConstants.FeatureManagementSectionName}:{featureFlag.Id}",
                        false.ToString()));
            }

            return keyValues;
        }

        private List<KeyValuePair<string, string>> FlattenFeatureFlagWithV2Schema(FeatureFlag featureFlag)
        {
            var keyValues = new List<KeyValuePair<string, string>>();
            string FeatureFlagsSectionPrefix = FeatureManagementConstants.FeatureManagementSectionName +
                                               ConfigurationPath.KeyDelimiter +
                                               FeatureManagementConstants.FeatureFlagSectionName;
            if (featureFlag.Enabled)
            {
                //if (featureFlag.Conditions?.ClientFilters == null)
                if (featureFlag.Conditions?.ClientFilters == null || !featureFlag.Conditions.ClientFilters.Any()) // workaround since we are not yet setting client filters to null
                {
                    //
                    // Always on
                    keyValues.Add(
                        new KeyValuePair<string, string>(
                            $"{FeatureFlagsSectionPrefix}:{featureFlag.Id}",
                            true.ToString()));
                }
                else
                {
                    //
                    // Conditionally on based on feature filters
                    for (int i = 0; i < featureFlag.Conditions.ClientFilters.Count; i++)
                    {
                        ClientFilter clientFilter = featureFlag.Conditions.ClientFilters[i];
                        string enabledForSectionPrefix = $"{FeatureFlagsSectionPrefix}:{featureFlag.Id}:{FeatureManagementConstants.FeatureFlagEnabledFor}:{i}";

                        keyValues.Add(
                            new KeyValuePair<string, string>(
                                $"{enabledForSectionPrefix}:{FeatureManagementConstants.FeatureFlagFilterName}",
                                clientFilter.Name));

                        foreach (KeyValuePair<string, string> kvp in new JsonFlattener().FlattenJson(clientFilter.Parameters))
                        {
                            keyValues.Add(
                                new KeyValuePair<string, string>(
                                    $"{enabledForSectionPrefix}:{FeatureManagementConstants.FeatureFlagParameters}:{kvp.Key}",
                                    kvp.Value));
                        }
                    }
                }
            }
            else
            {
                keyValues.Add(
                    new KeyValuePair<string, string>(
                        $"{FeatureFlagsSectionPrefix}:{featureFlag.Id}",
                        false.ToString()));
            }

            return keyValues;
        }
    }
}
