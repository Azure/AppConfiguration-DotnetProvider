// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    /// <summary>
    /// Converts a standalone feature flag (returned by the feature-flag endpoint as an Azure SDK
    /// <see cref="FeatureFlag"/>) directly into the flattened feature-management configuration key-values
    /// consumed by <c>Microsoft.FeatureManagement</c>.
    /// </summary>
    internal static class FeatureFlagConverter
    {
        /// <summary>
        /// Produces the feature-management configuration key-values for a single standalone feature flag.
        /// Standalone feature flags are always emitted using the Microsoft schema. 
        /// </summary>
        /// <param name="flag">The feature flag to convert.</param>
        /// <param name="endpoint">The endpoint used to build the feature flag reference for telemetry.</param>
        /// <param name="featureFlagIndex">The index of the feature flag in the feature flags array.</param>
        public static IEnumerable<KeyValuePair<string, string>> ToConfiguration(
            FeatureFlag flag,
            Uri endpoint,
            int featureFlagIndex)
        {
            string key = FeatureManagementConstants.FeatureFlagMarker + (flag.Name ?? string.Empty);

            var metadata = new FeatureFlagMetadata(key, flag.Label, flag.Etag ?? default);

            return ProcessMicrosoftSchemaFeatureFlag(flag, metadata, endpoint, featureFlagIndex);
        }

        private static List<KeyValuePair<string, string>> ProcessMicrosoftSchemaFeatureFlag(
            FeatureFlag featureFlag,
            FeatureFlagMetadata metadata,
            Uri endpoint,
            int featureFlagIndex)
        {
            var keyValues = new List<KeyValuePair<string, string>>();

            if (string.IsNullOrEmpty(featureFlag.Name))
            {
                return keyValues;
            }

            string featureFlagPath = $"{FeatureManagementConstants.FeatureManagementSectionName}:{FeatureManagementConstants.FeatureFlagsSectionName}:{featureFlagIndex}";

            bool enabled = featureFlag.Enabled ?? false;

            keyValues.Add(new KeyValuePair<string, string>($"{featureFlagPath}:{FeatureManagementConstants.Id}", featureFlag.Name));

            keyValues.Add(new KeyValuePair<string, string>($"{featureFlagPath}:{FeatureManagementConstants.Enabled}", enabled.ToString()));

            if (enabled)
            {
                if (featureFlag.Conditions?.Filters != null && featureFlag.Conditions.Filters.Any())
                {
                    //
                    // Conditionally based on feature filters
                    for (int i = 0; i < featureFlag.Conditions.Filters.Count; i++)
                    {
                        FeatureFilter clientFilter = featureFlag.Conditions.Filters[i];

                        string clientFiltersPath = $"{featureFlagPath}:{FeatureManagementConstants.Conditions}:{FeatureManagementConstants.ClientFilters}:{i}";

                        keyValues.Add(new KeyValuePair<string, string>($"{clientFiltersPath}:{FeatureManagementConstants.Name}", clientFilter.Name));

                        foreach (KeyValuePair<string, string> kvp in new JsonFlattener().FlattenJson(BuildParametersElement(clientFilter.Parameters)))
                        {
                            keyValues.Add(new KeyValuePair<string, string>($"{clientFiltersPath}:{FeatureManagementConstants.Parameters}:{kvp.Key}", kvp.Value));
                        }
                    }

                    //
                    // process RequirementType only when filters are not empty
                    if (featureFlag.Conditions.RequirementType != null)
                    {
                        keyValues.Add(new KeyValuePair<string, string>(
                            $"{featureFlagPath}:{FeatureManagementConstants.Conditions}:{FeatureManagementConstants.RequirementType}",
                            featureFlag.Conditions.RequirementType.Value.ToString()));
                    }
                }
            }

            if (featureFlag.Variants != null)
            {
                int i = 0;

                foreach (FeatureFlagVariantDefinition featureVariant in featureFlag.Variants)
                {
                    string variantsPath = $"{featureFlagPath}:{FeatureManagementConstants.Variants}:{i}";

                    keyValues.Add(new KeyValuePair<string, string>($"{variantsPath}:{FeatureManagementConstants.Name}", featureVariant.Name));

                    foreach (KeyValuePair<string, string> kvp in new JsonFlattener().FlattenJson(BuildVariantValueElement(featureVariant.Value, featureVariant.ContentType)))
                    {
                        keyValues.Add(new KeyValuePair<string, string>($"{variantsPath}:{FeatureManagementConstants.ConfigurationValue}" +
                            (string.IsNullOrEmpty(kvp.Key) ? "" : $":{kvp.Key}"), kvp.Value));
                    }

                    if (featureVariant.StatusOverride != null)
                    {
                        keyValues.Add(new KeyValuePair<string, string>($"{variantsPath}:{FeatureManagementConstants.StatusOverride}", featureVariant.StatusOverride.Value.ToString()));
                    }

                    i++;
                }
            }

            if (featureFlag.Allocation != null)
            {
                FeatureFlagAllocation allocation = featureFlag.Allocation;

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
                    int j = 0;

                    foreach (UserAllocation userAllocation in allocation.User)
                    {
                        keyValues.Add(new KeyValuePair<string, string>($"{allocationPath}:{FeatureManagementConstants.UserAllocation}:{j}:{FeatureManagementConstants.Variant}", userAllocation.Variant));

                        int k = 0;

                        foreach (string user in userAllocation.Users)
                        {
                            keyValues.Add(new KeyValuePair<string, string>($"{allocationPath}:{FeatureManagementConstants.UserAllocation}:{j}:{FeatureManagementConstants.Users}:{k}", user));

                            k++;
                        }

                        j++;
                    }
                }

                if (allocation.Group != null)
                {
                    int j = 0;

                    foreach (GroupAllocation groupAllocation in allocation.Group)
                    {
                        keyValues.Add(new KeyValuePair<string, string>($"{allocationPath}:{FeatureManagementConstants.GroupAllocation}:{j}:{FeatureManagementConstants.Variant}", groupAllocation.Variant));

                        int k = 0;

                        foreach (string group in groupAllocation.Groups)
                        {
                            keyValues.Add(new KeyValuePair<string, string>($"{allocationPath}:{FeatureManagementConstants.GroupAllocation}:{j}:{FeatureManagementConstants.Groups}:{k}", group));

                            k++;
                        }

                        j++;
                    }
                }

                if (allocation.Percentile != null)
                {
                    int j = 0;

                    foreach (PercentileAllocation percentileAllocation in allocation.Percentile)
                    {
                        keyValues.Add(new KeyValuePair<string, string>($"{allocationPath}:{FeatureManagementConstants.PercentileAllocation}:{j}:{FeatureManagementConstants.Variant}", percentileAllocation.Variant));

                        keyValues.Add(new KeyValuePair<string, string>($"{allocationPath}:{FeatureManagementConstants.PercentileAllocation}:{j}:{FeatureManagementConstants.From}", percentileAllocation.From.ToString()));

                        keyValues.Add(new KeyValuePair<string, string>($"{allocationPath}:{FeatureManagementConstants.PercentileAllocation}:{j}:{FeatureManagementConstants.To}", percentileAllocation.To.ToString()));

                        j++;
                    }
                }

                if (allocation.Seed != null)
                {
                    keyValues.Add(new KeyValuePair<string, string>($"{allocationPath}:{FeatureManagementConstants.Seed}", allocation.Seed));
                }
            }

            if (featureFlag.Telemetry != null)
            {
                FeatureFlagTelemetryConfiguration telemetry = featureFlag.Telemetry;

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

                    if (endpoint != null)
                    {
                        string featureFlagReference = $"{endpoint.AbsoluteUri}ff/{metadata.Key}{(!string.IsNullOrWhiteSpace(metadata.Label) ? $"?label={metadata.Label}" : "")}";

                        keyValues.Add(new KeyValuePair<string, string>($"{telemetryPath}:{FeatureManagementConstants.Metadata}:{FeatureManagementConstants.FeatureFlagReference}", featureFlagReference));
                    }

                    keyValues.Add(new KeyValuePair<string, string>($"{telemetryPath}:{FeatureManagementConstants.Metadata}:{FeatureManagementConstants.ETag}", metadata.ETag.ToString()));

                    keyValues.Add(new KeyValuePair<string, string>($"{telemetryPath}:{FeatureManagementConstants.Enabled}", telemetry.Enabled.ToString()));

                    if (featureFlag.Allocation != null)
                    {
                        string allocationId = CalculateAllocationId(featureFlag);

                        if (allocationId != null)
                        {
                            keyValues.Add(new KeyValuePair<string, string>($"{telemetryPath}:{FeatureManagementConstants.Metadata}:{FeatureManagementConstants.AllocationId}", allocationId));
                        }
                    }
                }
            }

            return keyValues;
        }

        private static string CalculateAllocationId(FeatureFlag flag)
        {
            Debug.Assert(flag.Allocation != null);

            StringBuilder inputBuilder = new StringBuilder();

            // Seed
            inputBuilder.Append($"seed={flag.Allocation.Seed ?? string.Empty}");

            var allocatedVariants = new HashSet<string>();

            // DefaultWhenEnabled
            if (flag.Allocation.DefaultWhenEnabled != null)
            {
                allocatedVariants.Add(flag.Allocation.DefaultWhenEnabled);
            }

            inputBuilder.Append($"\ndefault_when_enabled={flag.Allocation.DefaultWhenEnabled ?? string.Empty}");

            // Percentiles
            inputBuilder.Append("\npercentiles=");

            if (flag.Allocation.Percentile != null && flag.Allocation.Percentile.Any())
            {
                IEnumerable<PercentileAllocation> sortedPercentiles = flag.Allocation.Percentile
                    .Where(p => p.From != p.To)
                    .OrderBy(p => p.From)
                    .ToList();

                allocatedVariants.UnionWith(sortedPercentiles.Select(p => p.Variant));

                inputBuilder.Append(string.Join(";", sortedPercentiles.Select(p => $"{p.From},{p.Variant.ToBase64String()},{p.To}")));
            }

            // If there's no custom seed and no variants allocated, stop now and return null
            if (flag.Allocation.Seed == null &&
                !allocatedVariants.Any())
            {
                return null;
            }

            // Variants
            inputBuilder.Append("\nvariants=");

            if (allocatedVariants.Any() && flag.Variants != null && flag.Variants.Any())
            {
                IEnumerable<FeatureFlagVariantDefinition> sortedVariants = flag.Variants
                    .Where(variant => allocatedVariants.Contains(variant.Name))
                    .OrderBy(variant => variant.Name)
                    .ToList();

                inputBuilder.Append(string.Join(";", sortedVariants.Select(v =>
                {
                    var variantValue = string.Empty;

                    JsonElement configurationValue = BuildVariantValueElement(v.Value, v.ContentType);

                    if (configurationValue.ValueKind != JsonValueKind.Null && configurationValue.ValueKind != JsonValueKind.Undefined)
                    {
                        variantValue = configurationValue.SerializeWithSortedKeys();
                    }

                    return $"{v.Name.ToBase64String()},{(variantValue)}";
                })));
            }

            // Example input string
            // input == "seed=123abc\ndefault_when_enabled=Control\npercentiles=0,Blshdk,20;20,Test,100\nvariants=TdLa,standard;Qfcd,special"
            string input = inputBuilder.ToString();

            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] truncatedHash = new byte[15];
                Array.Copy(sha256.ComputeHash(Encoding.UTF8.GetBytes(input)), truncatedHash, 15);
                return truncatedHash.ToBase64Url();
            }
        }

        // The SDK exposes filter parameters as IDictionary<string, string>. The feature-management
        // flattening produces per-leaf keys (e.g. Audience:Users:0), so build a JsonElement here. Parameter
        // values that are JSON-encoded strings are embedded as parsed JSON so the flattening produces the
        // nested keys that feature-management filters bind against.
        private static JsonElement BuildParametersElement(IDictionary<string, string> parameters)
        {
            if (parameters == null || parameters.Count == 0)
            {
                return default;
            }

            using var stream = new MemoryStream();

            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();

                foreach (KeyValuePair<string, string> kvp in parameters)
                {
                    writer.WritePropertyName(kvp.Key);
                    WriteParameterValue(writer, kvp.Value);
                }

                writer.WriteEndObject();
            }

            using JsonDocument doc = JsonDocument.Parse(stream.ToArray());

            return doc.RootElement.Clone();
        }

        private static void WriteParameterValue(Utf8JsonWriter writer, string value)
        {
            if (value == null)
            {
                writer.WriteNullValue();

                return;
            }

            string trimmed = value.TrimStart();

            if (trimmed.Length > 0 && (trimmed[0] == '{' || trimmed[0] == '['))
            {
                try
                {
                    using JsonDocument doc = JsonDocument.Parse(value);
                    doc.RootElement.WriteTo(writer);

                    return;
                }
                catch (JsonException)
                {
                    // Fall through and write the original literal string.
                }
            }

            writer.WriteStringValue(value);
        }

        // Variant values are exposed by the SDK as a string plus a content type. When the content type
        // is JSON-shaped, embed the parsed JSON so consumers see a real object/array/number rather than
        // a string literal; otherwise produce a JSON string element.
        private static JsonElement BuildVariantValueElement(string value, string contentType)
        {
            if (value == null)
            {
                return default;
            }

            bool looksLikeJson = !string.IsNullOrEmpty(contentType) &&
                contentType.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0;

            if (looksLikeJson)
            {
                try
                {
                    using JsonDocument doc = JsonDocument.Parse(value);

                    return doc.RootElement.Clone();
                }
                catch (JsonException)
                {
                    // Fall through to writing as a raw string when the body is not valid JSON.
                }
            }

            using var stream = new MemoryStream();

            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStringValue(value);
            }

            using JsonDocument stringDoc = JsonDocument.Parse(stream.ToArray());

            return stringDoc.RootElement.Clone();
        }
    }
}
