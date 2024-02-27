// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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

        public Task<IEnumerable<KeyValuePair<string, string>>> ProcessKeyValue(ConfigurationSetting setting, Uri endpoint, Logger logger, CancellationToken cancellationToken)
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

            string featureFlagPath = $"{FeatureManagementConstants.SectionName}:{featureFlag.Id}";

            if (featureFlag.Enabled)
            {
                //if (featureFlag.Conditions?.ClientFilters == null)
                if (featureFlag.Conditions?.ClientFilters == null || !featureFlag.Conditions.ClientFilters.Any()) // workaround since we are not yet setting client filters to null
                {
                    if (featureFlag.Variants != null && featureFlag.Telemetry != null)
                    {
                        keyValues.Add(new KeyValuePair<string, string>($"{featureFlagPath}:{FeatureManagementConstants.EnabledFor}:{0}:{FeatureManagementConstants.Name}", FeatureManagementConstants.AlwaysOnFilter));
                    }
                    else
                    {
                        //
                        // Always on
                        keyValues.Add(new KeyValuePair<string, string>(featureFlagPath, true.ToString()));
                    }
                }
                else
                {
                    //
                    // Conditionally on based on feature filters
                    for (int i = 0; i < featureFlag.Conditions.ClientFilters.Count; i++)
                    {
                        ClientFilter clientFilter = featureFlag.Conditions.ClientFilters[i];

                        _featureFilterTracing.UpdateFeatureFilterTracing(clientFilter.Name);

                        string clientFiltersPath = $"{featureFlagPath}:{FeatureManagementConstants.EnabledFor}:{i}";

                        keyValues.Add(new KeyValuePair<string, string>($"{clientFiltersPath}:{FeatureManagementConstants.Name}", clientFilter.Name));

                        foreach (KeyValuePair<string, string> kvp in new JsonFlattener().FlattenJson(clientFilter.Parameters))
                        {
                            keyValues.Add(new KeyValuePair<string, string>($"{clientFiltersPath}:{FeatureManagementConstants.Parameters}:{kvp.Key}", kvp.Value));
                        }
                    }

                    //
                    // process RequirementType only when filters are not empty
                    if (featureFlag.Conditions.RequirementType != null)
                    {
                        keyValues.Add(new KeyValuePair<string, string>(
                            $"{featureFlagPath}:{FeatureManagementConstants.RequirementType}", 
                            featureFlag.Conditions.RequirementType));
                    }
                }

                keyValues.Add(new KeyValuePair<string, string>($"{featureFlagPath}:{FeatureManagementConstants.Status}", FeatureManagementConstants.Conditional));
            }
            else
            {
                if (featureFlag.Variants == null)
                {
                    keyValues.Add(new KeyValuePair<string, string>(featureFlagPath, false.ToString()));
                }

                keyValues.Add(new KeyValuePair<string, string>($"{featureFlagPath}:{FeatureManagementConstants.Status}", FeatureManagementConstants.Disabled));
            }

            if (featureFlag.Variants != null)
            {
                int i = 0;

                foreach (FeatureVariant featureVariant in featureFlag.Variants)
                {
                    string variantsPath = $"{featureFlagPath}:{FeatureManagementConstants.Variants}:{i}";

                    keyValues.Add(new KeyValuePair<string, string>($"{variantsPath}:{FeatureManagementConstants.Name}", featureVariant.Name));

                    if (featureVariant.ConfigurationValue != null)
                    {
                        keyValues.Add(new KeyValuePair<string, string>($"{variantsPath}:{FeatureManagementConstants.ConfigurationValue}", featureVariant.ConfigurationValue));
                    }

                    if (featureVariant.ConfigurationReference != null)
                    {
                        keyValues.Add(new KeyValuePair<string, string>($"{variantsPath}:{FeatureManagementConstants.ConfigurationReference}", featureVariant.ConfigurationReference));
                    }

                    if (featureVariant.StatusOverride != null)
                    {
                        keyValues.Add(new KeyValuePair<string, string>($"{variantsPath}:{FeatureManagementConstants.StatusOverride}", featureVariant.StatusOverride));
                    }

                    i++;
                }
            }

            if (featureFlag.Allocation != null)
            {
                FeatureAllocation allocation = featureFlag.Allocation;

                string allocationPath = $"{featureFlagPath}:{FeatureManagementConstants.Allocation}";

                if (allocation.DefaultWhenDisabled != null)
                {
                    keyValues.Add(new KeyValuePair<string, string>($"{allocationPath}:{FeatureManagementConstants.DefaultWhenDisabled}", allocation.DefaultWhenDisabled));
                }

                if (allocation.DefaultWhenEnabled != null)
                {
                    keyValues.Add(new KeyValuePair<string, string>($"{allocationPath}:{FeatureManagementConstants.DefaultWhenEnabled}", allocation.DefaultWhenEnabled));
                }

                if (allocation.User != null)
                {
                    int i = 0;

                    foreach (FeatureUserAllocation userAllocation in allocation.User)
                    {
                        keyValues.Add(new KeyValuePair<string, string>($"{allocationPath}:{FeatureManagementConstants.User}:{i}:{FeatureManagementConstants.Variant}", userAllocation.Variant));

                        int j = 0;

                        foreach (string user in userAllocation.Users)
                        {
                            keyValues.Add(new KeyValuePair<string, string>($"{allocationPath}:{FeatureManagementConstants.User}:{i}:{FeatureManagementConstants.Users}:{j}", user));

                            j++;
                        }

                        i++;
                    }
                }

                if (allocation.Group != null)
                {
                    int i = 0;

                    foreach (FeatureGroupAllocation groupAllocation in allocation.Group)
                    {
                        keyValues.Add(new KeyValuePair<string, string>($"{allocationPath}:{FeatureManagementConstants.Group}:{i}:{FeatureManagementConstants.Variant}", groupAllocation.Variant));

                        int j = 0;

                        foreach (string group in groupAllocation.Groups)
                        {
                            keyValues.Add(new KeyValuePair<string, string>($"{allocationPath}:{FeatureManagementConstants.Group}:{i}:{FeatureManagementConstants.Groups}:{j}", group));

                            j++;
                        }

                        i++;
                    }
                }

                if (allocation.Percentile != null)
                {
                    int i = 0;

                    foreach (FeaturePercentileAllocation percentileAllocation in allocation.Percentile)
                    {
                        keyValues.Add(new KeyValuePair<string, string>($"{allocationPath}:{FeatureManagementConstants.Percentile}:{i}:{FeatureManagementConstants.Variant}", percentileAllocation.Variant));

                        keyValues.Add(new KeyValuePair<string, string>($"{allocationPath}:{FeatureManagementConstants.Percentile}:{i}:{FeatureManagementConstants.From}", percentileAllocation.From.ToString()));

                        keyValues.Add(new KeyValuePair<string, string>($"{allocationPath}:{FeatureManagementConstants.Percentile}:{i}:{FeatureManagementConstants.To}", percentileAllocation.To.ToString()));

                        i++;
                    }
                }

                if (allocation.Seed != null)
                {
                    keyValues.Add(new KeyValuePair<string, string>($"{allocationPath}:{FeatureManagementConstants.Seed}", allocation.Seed));
                }
            }

            if (featureFlag.Telemetry != null)
            {
                FeatureTelemetry telemetry = featureFlag.Telemetry;

                string telemetryPath = $"{featureFlagPath}:{FeatureManagementConstants.Telemetry}";

                if (telemetry.Enabled)
                {
                    if (telemetry.Metadata != null)
                    {
                        foreach (KeyValuePair<string, string> kvp in telemetry.Metadata)
                        {
                            keyValues.Add(new KeyValuePair<string, string>($"{telemetryPath}:{FeatureManagementConstants.Metadata}:{kvp.Key}", kvp.Value));
                        }
                    }

                    string featureFlagId = CalculateFeatureFlagId(setting.Key, setting.Label);

                    keyValues.Add(new KeyValuePair<string, string>($"{telemetryPath}:{FeatureManagementConstants.Metadata}:{FeatureManagementConstants.FeatureFlagId}", featureFlagId));

                    if (endpoint != null)
                    {
                        string featureFlagReference = $"{endpoint.AbsoluteUri}kv/{setting.Key}{(!string.IsNullOrWhiteSpace(setting.Label) ? $"?label={setting.Label}" : "")}";

                        keyValues.Add(new KeyValuePair<string, string>($"{telemetryPath}:{FeatureManagementConstants.Metadata}:{FeatureManagementConstants.FeatureFlagReference}", featureFlagReference));
                    }

                    keyValues.Add(new KeyValuePair<string, string>($"{telemetryPath}:{FeatureManagementConstants.Metadata}:{FeatureManagementConstants.ETag}", setting.ETag.ToString()));

                    keyValues.Add(new KeyValuePair<string, string>($"{telemetryPath}:{FeatureManagementConstants.Enabled}", telemetry.Enabled.ToString()));
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

        private static string CalculateFeatureFlagId(string key, string label)
        {
            byte[] featureFlagIdHash;

            // Convert the value consisting of key, newline character, and label to a byte array using UTF8 encoding to hash it using SHA 256
            using (HashAlgorithm hashAlgorithm = SHA256.Create())
            {
                featureFlagIdHash = hashAlgorithm.ComputeHash(Encoding.UTF8.GetBytes($"{key}\n{(string.IsNullOrWhiteSpace(label) ? null : label)}"));
            }

            // Convert the hashed byte array to Base64Url
            return featureFlagIdHash.ToBase64Url();
        }
    }
}
