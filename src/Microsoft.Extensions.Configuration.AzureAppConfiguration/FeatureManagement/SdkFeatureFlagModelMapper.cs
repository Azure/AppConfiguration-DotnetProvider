// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SdkFeatureFlag = Azure.Data.AppConfiguration.FeatureFlag;
using Azure.Data.AppConfiguration;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    /// <summary>
    /// Maps an Azure SDK <see cref="SdkFeatureFlag"/> (returned by the standalone feature-flag endpoint)
    /// directly to the internal <see cref="FeatureFlag"/> model that <see cref="FeatureManagementKeyValueAdapter"/>
    /// emits as feature-management configuration.
    /// </summary>
    internal static class SdkFeatureFlagModelMapper
    {
        public static FeatureFlag MapToModel(SdkFeatureFlag ff)
        {
            var featureFlag = new FeatureFlag
            {
                Id = ff.Name,
                Enabled = ff.Enabled ?? false
            };

            if (ff.Conditions != null)
            {
                featureFlag.Conditions = MapConditions(ff.Conditions);
            }

            if (ff.Variants != null && ff.Variants.Count > 0)
            {
                featureFlag.Variants = ff.Variants.Select(MapVariant).ToList();
            }

            if (ff.Allocation != null)
            {
                featureFlag.Allocation = MapAllocation(ff.Allocation);
            }

            if (ff.Telemetry != null)
            {
                featureFlag.Telemetry = MapTelemetry(ff.Telemetry);
            }

            return featureFlag;
        }

        private static FeatureConditions MapConditions(FeatureFlagConditions conditions)
        {
            var featureConditions = new FeatureConditions
            {
                RequirementType = conditions.RequirementType?.ToString()
            };

            if (conditions.Filters != null)
            {
                foreach (FeatureFilter filter in conditions.Filters)
                {
                    featureConditions.ClientFilters.Add(new ClientFilter
                    {
                        Name = filter.Name,
                        Parameters = BuildParametersElement(filter.Parameters)
                    });
                }
            }

            return featureConditions;
        }

        private static FeatureVariant MapVariant(FeatureFlagVariantDefinition variant)
        {
            return new FeatureVariant
            {
                Name = variant.Name,
                ConfigurationValue = BuildVariantValueElement(variant.Value, variant.ContentType),
                StatusOverride = variant.StatusOverride?.ToString()
            };
        }

        private static FeatureAllocation MapAllocation(FeatureFlagAllocation allocation)
        {
            return new FeatureAllocation
            {
                DefaultWhenDisabled = allocation.DefaultWhenDisabled,
                DefaultWhenEnabled = allocation.DefaultWhenEnabled,
                Seed = allocation.Seed,
                User = allocation.User?.Select(u => new FeatureUserAllocation
                {
                    Variant = u.Variant,
                    Users = u.Users?.ToList() ?? new List<string>()
                }).ToList(),
                Group = allocation.Group?.Select(g => new FeatureGroupAllocation
                {
                    Variant = g.Variant,
                    Groups = g.Groups?.ToList() ?? new List<string>()
                }).ToList(),
                Percentile = allocation.Percentile?.Select(p => new FeaturePercentileAllocation
                {
                    Variant = p.Variant,
                    From = p.From,
                    To = p.To
                }).ToList()
            };
        }

        private static FeatureTelemetry MapTelemetry(FeatureFlagTelemetryConfiguration telemetry)
        {
            return new FeatureTelemetry
            {
                Enabled = telemetry.Enabled,
                Metadata = telemetry.Metadata != null
                    ? new Dictionary<string, string>(telemetry.Metadata)
                    : null
            };
        }

        // The SDK exposes filter parameters as IDictionary<string, string>. The feature-management
        // adapter flattens a JsonElement to produce per-leaf keys (e.g. Audience:Users:0), so build a
        // JsonElement here. Parameter values that are JSON-encoded strings are embedded as parsed JSON
        // so the flattening produces the nested keys that feature-management filters bind against.
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
