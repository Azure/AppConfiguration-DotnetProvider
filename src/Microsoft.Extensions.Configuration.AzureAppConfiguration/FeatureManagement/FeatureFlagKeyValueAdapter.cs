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
        private static string _schemaVersion = FeatureManagementConstants.FeatureManagementDefaultSchema;
        private static string _featureFlagsSectionPrefix;

        public FeatureFlagKeyValueAdapter(string schemaVersion)
        {
            if (string.IsNullOrWhiteSpace(schemaVersion))
            {
                throw new ArgumentNullException(nameof(schemaVersion));
            }

            _schemaVersion = schemaVersion;

            if (_schemaVersion == FeatureManagementConstants.FeatureManagementSchemaV1)
            {
                _featureFlagsSectionPrefix = FeatureManagementConstants.FeatureManagementSectionName;
            }
            else
            {
                _featureFlagsSectionPrefix = $"{FeatureManagementConstants.FeatureManagementSectionName}:{FeatureManagementConstants.FeatureFlagSectionName}";
            }
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

            var keyValues = new List<KeyValuePair<string, string>>();

            if (featureFlag.Enabled)
            {
                if (featureFlag.Conditions?.ClientFilters == null || !featureFlag.Conditions.ClientFilters.Any()) // workaround since we are not yet setting client filters to null
                {
                    // Always on
                    keyValues.Add(
                        new KeyValuePair<string, string>(
                            $"{_featureFlagsSectionPrefix}:{featureFlag.Id}",
                            true.ToString()));
                }
                else
                {
                    // Conditionally on based on feature filters
                    for (int i = 0; i < featureFlag.Conditions.ClientFilters.Count; i++)
                    {
                        ClientFilter clientFilter = featureFlag.Conditions.ClientFilters[i];
                        string enabledForSectionPrefix =
                            $"{_featureFlagsSectionPrefix}:{featureFlag.Id}:{FeatureManagementConstants.FeatureFlagEnabledFor}:{i}";

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
                        $"{_featureFlagsSectionPrefix}:{featureFlag.Id}",
                        false.ToString()));
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
    }
}
