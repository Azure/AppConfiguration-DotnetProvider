// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Data.AppConfiguration;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SdkFeatureFlag = Azure.Data.AppConfiguration.FeatureFlag;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    /// <summary>
    /// Converts an Azure SDK <see cref="SdkFeatureFlag"/> (returned by the new feature-flag endpoint)
    /// into a synthesized <see cref="ConfigurationSetting"/> whose JSON value matches the feature-management
    /// schema that <see cref="FeatureManagementKeyValueAdapter"/> already parses
    /// (i.e. uses <c>id</c>, <c>client_filters</c>, <c>requirement_type</c>, etc.).
    /// </summary>
    internal static class FeatureFlagSettingConverter
    {
        public static ConfigurationSetting ToConfigurationSetting(SdkFeatureFlag featureFlag)
        {
            string key = FeatureManagementConstants.FeatureFlagMarker + (featureFlag.Name ?? string.Empty);
            string json = SerializeFeatureManagementJson(featureFlag);
            ETag etag = string.IsNullOrEmpty(featureFlag.Etag) ? default : new ETag(featureFlag.Etag);

            ConfigurationSetting setting = ConfigurationModelFactory.ConfigurationSetting(
                key: key,
                value: json,
                label: featureFlag.Label,
                contentType: FeatureManagementConstants.ContentType,
                eTag: etag);

            if (featureFlag.Tags != null)
            {
                foreach (KeyValuePair<string, string> tag in featureFlag.Tags)
                {
                    setting.Tags[tag.Key] = tag.Value;
                }
            }

            return setting;
        }

        private static string SerializeFeatureManagementJson(SdkFeatureFlag ff)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();

                writer.WriteString(FeatureManagementConstants.Id, ff.Name);

                if (ff.Enabled.HasValue)
                {
                    writer.WriteBoolean(FeatureManagementConstants.Enabled, ff.Enabled.Value);
                }

                if (ff.Conditions != null)
                {
                    writer.WritePropertyName(FeatureManagementConstants.Conditions);
                    WriteConditions(writer, ff.Conditions);
                }

                if (ff.Variants != null && ff.Variants.Count > 0)
                {
                    writer.WritePropertyName(FeatureManagementConstants.Variants);
                    writer.WriteStartArray();

                    foreach (FeatureFlagVariantDefinition variant in ff.Variants)
                    {
                        WriteVariant(writer, variant);
                    }

                    writer.WriteEndArray();
                }

                if (ff.Allocation != null)
                {
                    writer.WritePropertyName(FeatureManagementConstants.Allocation);
                    WriteAllocation(writer, ff.Allocation);
                }

                if (ff.Telemetry != null)
                {
                    writer.WritePropertyName(FeatureManagementConstants.Telemetry);
                    WriteTelemetry(writer, ff.Telemetry);
                }

                writer.WriteEndObject();
            }

            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }

        private static void WriteConditions(Utf8JsonWriter writer, FeatureFlagConditions conditions)
        {
            writer.WriteStartObject();

            if (conditions.Filters != null && conditions.Filters.Count > 0)
            {
                writer.WritePropertyName(FeatureManagementConstants.ClientFilters);
                writer.WriteStartArray();

                foreach (FeatureFlagFilter filter in conditions.Filters)
                {
                    WriteFilter(writer, filter);
                }

                writer.WriteEndArray();
            }

            if (conditions.RequirementType.HasValue)
            {
                writer.WriteString(FeatureManagementConstants.RequirementType, conditions.RequirementType.Value.ToString());
            }

            writer.WriteEndObject();
        }

        private static void WriteFilter(Utf8JsonWriter writer, FeatureFlagFilter filter)
        {
            writer.WriteStartObject();
            writer.WriteString(FeatureManagementConstants.Name, filter.Name);

            if (filter.Parameters != null && filter.Parameters.Count > 0)
            {
                writer.WritePropertyName(FeatureManagementConstants.Parameters);
                writer.WriteStartObject();

                foreach (KeyValuePair<string, object> kvp in filter.Parameters)
                {
                    writer.WritePropertyName(kvp.Key);
                    WriteFilterParameterValue(writer, kvp.Value);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        // Parameter values are JSON-encoded strings rather than nested structures, embed the parsed JSON so downstream flattening produces the per-leaf keys
        // (e.g. Audience:Users:0) that feature-management filters expect to bind against.
        private static void WriteFilterParameterValue(Utf8JsonWriter writer, object value)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            if (value is string s)
            {
                string trimmed = s.TrimStart();

                if (trimmed.Length > 0 && (trimmed[0] == '{' || trimmed[0] == '['))
                {
                    try
                    {
                        using JsonDocument doc = JsonDocument.Parse(s);
                        doc.RootElement.WriteTo(writer);
                        return;
                    }
                    catch (JsonException)
                    {
                        // Fall through and write the original literal string.
                    }
                }

                writer.WriteStringValue(s);
                return;
            }

            JsonSerializer.Serialize(writer, value);
        }

        private static void WriteVariant(Utf8JsonWriter writer, FeatureFlagVariantDefinition variant)
        {
            writer.WriteStartObject();

            if (variant.Name != null)
            {
                writer.WriteString(FeatureManagementConstants.Name, variant.Name);
            }

            if (variant.Value != null)
            {
                writer.WritePropertyName(FeatureManagementConstants.ConfigurationValue);
                WriteVariantValue(writer, variant.Value, variant.ContentType);
            }

            if (variant.StatusOverride.HasValue)
            {
                writer.WriteString(FeatureManagementConstants.StatusOverride, variant.StatusOverride.Value.ToString());
            }

            writer.WriteEndObject();
        }

        private static void WriteVariantValue(Utf8JsonWriter writer, string value, string contentType)
        {
            // When the variant declares a JSON-shaped content type, embed the parsed JSON node so
            // FeatureManagement consumers see a real object/array/number rather than a string literal.
            bool looksLikeJson = !string.IsNullOrEmpty(contentType) &&
                contentType.IndexOf("json", System.StringComparison.OrdinalIgnoreCase) >= 0;

            if (looksLikeJson)
            {
                try
                {
                    using JsonDocument doc = JsonDocument.Parse(value);
                    doc.RootElement.WriteTo(writer);
                    return;
                }
                catch (JsonException)
                {
                    // Fall through to writing as a raw string when the body is not valid JSON.
                }
            }

            writer.WriteStringValue(value);
        }

        private static void WriteAllocation(Utf8JsonWriter writer, FeatureFlagAllocation allocation)
        {
            writer.WriteStartObject();

            if (allocation.DefaultWhenDisabled != null)
            {
                writer.WriteString(FeatureManagementConstants.DefaultWhenDisabled, allocation.DefaultWhenDisabled);
            }

            if (allocation.DefaultWhenEnabled != null)
            {
                writer.WriteString(FeatureManagementConstants.DefaultWhenEnabled, allocation.DefaultWhenEnabled);
            }

            if (allocation.User != null && allocation.User.Count > 0)
            {
                writer.WritePropertyName(FeatureManagementConstants.UserAllocation);
                writer.WriteStartArray();

                foreach (UserAllocation user in allocation.User)
                {
                    writer.WriteStartObject();
                    writer.WriteString(FeatureManagementConstants.Variant, user.Variant);
                    writer.WritePropertyName(FeatureManagementConstants.Users);
                    writer.WriteStartArray();

                    foreach (string u in user.Users ?? System.Linq.Enumerable.Empty<string>())
                    {
                        writer.WriteStringValue(u);
                    }

                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }

            if (allocation.Group != null && allocation.Group.Count > 0)
            {
                writer.WritePropertyName(FeatureManagementConstants.GroupAllocation);
                writer.WriteStartArray();

                foreach (GroupAllocation group in allocation.Group)
                {
                    writer.WriteStartObject();
                    writer.WriteString(FeatureManagementConstants.Variant, group.Variant);
                    writer.WritePropertyName(FeatureManagementConstants.Groups);
                    writer.WriteStartArray();

                    foreach (string g in group.Groups ?? System.Linq.Enumerable.Empty<string>())
                    {
                        writer.WriteStringValue(g);
                    }

                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }

            if (allocation.Percentile != null && allocation.Percentile.Count > 0)
            {
                writer.WritePropertyName(FeatureManagementConstants.PercentileAllocation);
                writer.WriteStartArray();

                foreach (PercentileAllocation p in allocation.Percentile)
                {
                    writer.WriteStartObject();
                    writer.WriteString(FeatureManagementConstants.Variant, p.Variant);
                    writer.WriteNumber(FeatureManagementConstants.From, p.From);
                    writer.WriteNumber(FeatureManagementConstants.To, p.To);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }

            if (allocation.Seed != null)
            {
                writer.WriteString(FeatureManagementConstants.Seed, allocation.Seed);
            }

            writer.WriteEndObject();
        }

        private static void WriteTelemetry(Utf8JsonWriter writer, FeatureFlagTelemetryConfiguration telemetry)
        {
            writer.WriteStartObject();

            writer.WriteBoolean(FeatureManagementConstants.Enabled, telemetry.Enabled);

            if (telemetry.Metadata != null && telemetry.Metadata.Count > 0)
            {
                writer.WritePropertyName(FeatureManagementConstants.Metadata);
                writer.WriteStartObject();

                foreach (KeyValuePair<string, string> kvp in telemetry.Metadata)
                {
                    writer.WriteString(kvp.Key, kvp.Value);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }
    }
}
