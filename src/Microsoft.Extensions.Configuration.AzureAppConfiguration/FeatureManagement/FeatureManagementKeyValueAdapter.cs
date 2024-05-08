﻿// Copyright (c) Microsoft Corporation.
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
        private int _featureFlagIndex = 0;

        public FeatureManagementKeyValueAdapter(FeatureFilterTracing featureFilterTracing)
        {
            _featureFilterTracing = featureFilterTracing ?? throw new ArgumentNullException(nameof(featureFilterTracing));
        }

        public Task<IEnumerable<KeyValuePair<string, string>>> ProcessKeyValue(ConfigurationSetting setting, Uri endpoint, Logger logger, CancellationToken cancellationToken)
        {
            FeatureFlag featureFlag = ParseFeatureFlag(setting.Key, setting.Value);

            var keyValues = new List<KeyValuePair<string, string>>();

            if (!string.IsNullOrEmpty(featureFlag.Id))
            {
                return Task.FromResult<IEnumerable<KeyValuePair<string, string>>>(keyValues);
            }

            string featureFlagPath = $"{FeatureManagementConstants.FeatureManagementSectionName}:{FeatureManagementConstants.FeatureFlagsSectionName}:{_featureFlagIndex}";

            _featureFlagIndex++;

            keyValues.Add(new KeyValuePair<string, string>($"{featureFlagPath}:{FeatureManagementConstants.Id}", featureFlag.Id));

            keyValues.Add(new KeyValuePair<string, string>($"{featureFlagPath}:{FeatureManagementConstants.Enabled}", featureFlag.Enabled.ToString()));

            if (featureFlag.Enabled)
            {
                //if (featureFlag.Conditions?.ClientFilters == null)
                if (featureFlag.Conditions?.ClientFilters == null || !featureFlag.Conditions.ClientFilters.Any()) // workaround since we are not yet setting client filters to null
                {
                     keyValues.Add(new KeyValuePair<string, string>($"{featureFlagPath}:{FeatureManagementConstants.Conditions}:{FeatureManagementConstants.ClientFilters}:{0}:{FeatureManagementConstants.Name}", FeatureManagementConstants.AlwaysOnFilter));
                }
                else
                {
                    //
                    // Conditionally on based on feature filters
                    for (int i = 0; i < featureFlag.Conditions.ClientFilters.Count; i++)
                    {
                        ClientFilter clientFilter = featureFlag.Conditions.ClientFilters[i];

                        _featureFilterTracing.UpdateFeatureFilterTracing(clientFilter.Name);

                        string clientFiltersPath = $"{featureFlagPath}:{FeatureManagementConstants.Conditions}:{FeatureManagementConstants.ClientFilters}:{i}";

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
                            $"{featureFlagPath}:{FeatureManagementConstants.Conditions}:{FeatureManagementConstants.RequirementType}", 
                            featureFlag.Conditions.RequirementType));
                    }
                }
            }
            else
            {
                keyValues.Add(new KeyValuePair<string, string>($"{featureFlagPath}:{FeatureManagementConstants.Status}", FeatureManagementConstants.Disabled));
            }

            if (featureFlag.Variants != null)
            {
                int i = 0;

                foreach (FeatureVariant featureVariant in featureFlag.Variants)
                {
                    string variantsPath = $"{featureFlagPath}:{FeatureManagementConstants.Variants}:{i}";

                    keyValues.Add(new KeyValuePair<string, string>($"{variantsPath}:{FeatureManagementConstants.Name}", featureVariant.Name));

                    foreach (KeyValuePair<string, string> kvp in new JsonFlattener().FlattenJson(featureVariant.ConfigurationValue))
                    {
                        keyValues.Add(new KeyValuePair<string, string>($"{variantsPath}:{FeatureManagementConstants.ConfigurationValue}" +
                            (string.IsNullOrEmpty(kvp.Key) ? "" : $":{kvp.Key}"), kvp.Value));
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
                        keyValues.Add(new KeyValuePair<string, string>($"{allocationPath}:{FeatureManagementConstants.UserAllocation}:{i}:{FeatureManagementConstants.Variant}", userAllocation.Variant));

                        int j = 0;

                        foreach (string user in userAllocation.Users)
                        {
                            keyValues.Add(new KeyValuePair<string, string>($"{allocationPath}:{FeatureManagementConstants.UserAllocation}:{i}:{FeatureManagementConstants.Users}:{j}", user));

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
                        keyValues.Add(new KeyValuePair<string, string>($"{allocationPath}:{FeatureManagementConstants.GroupAllocation}:{i}:{FeatureManagementConstants.Variant}", groupAllocation.Variant));

                        int j = 0;

                        foreach (string group in groupAllocation.Groups)
                        {
                            keyValues.Add(new KeyValuePair<string, string>($"{allocationPath}:{FeatureManagementConstants.GroupAllocation}:{i}:{FeatureManagementConstants.Groups}:{j}", group));

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
                        keyValues.Add(new KeyValuePair<string, string>($"{allocationPath}:{FeatureManagementConstants.PercentileAllocation}:{i}:{FeatureManagementConstants.Variant}", percentileAllocation.Variant));

                        keyValues.Add(new KeyValuePair<string, string>($"{allocationPath}:{FeatureManagementConstants.PercentileAllocation}:{i}:{FeatureManagementConstants.From}", percentileAllocation.From.ToString()));

                        keyValues.Add(new KeyValuePair<string, string>($"{allocationPath}:{FeatureManagementConstants.PercentileAllocation}:{i}:{FeatureManagementConstants.To}", percentileAllocation.To.ToString()));

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

        public void ResetState()
        {
            _featureFlagIndex = 0;

            return;
        }

        private FormatException CreateFeatureFlagFormatException(string jsonPropertyName, string settingKey, string foundJsonValueKind, string expectedJsonValueKind)
        {
            return new FormatException(string.Format(
                ErrorMessages.FeatureFlagInvalidJsonProperty,
                jsonPropertyName,
                settingKey,
                foundJsonValueKind,
                expectedJsonValueKind));
        }

        private FeatureFlag ParseFeatureFlag(string settingKey, string settingValue)
        {
            FeatureFlag featureFlag = new FeatureFlag();

            var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(settingValue));

            try
            {
                if (reader.Read() && reader.TokenType != JsonTokenType.StartObject)
                {
                    throw new FormatException(string.Format(ErrorMessages.FeatureFlagInvalidFormat, settingKey));
                }

                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        continue;
                    }

                    string propertyName = reader.GetString();

                    switch (propertyName)
                    {
                        case FeatureManagementConstants.Id:
                            {
                                if (reader.Read() && reader.TokenType == JsonTokenType.String)
                                {
                                    featureFlag.Id = reader.GetString();
                                }
                                else if (reader.TokenType != JsonTokenType.Null)
                                {
                                    throw CreateFeatureFlagFormatException(
                                        FeatureManagementConstants.Id,
                                        settingKey,
                                        reader.TokenType.ToString(),
                                        JsonTokenType.String.ToString());
                                }

                                break;
                            }

                        case FeatureManagementConstants.Enabled:
                            {
                                if (reader.Read() && (reader.TokenType == JsonTokenType.False || reader.TokenType == JsonTokenType.True))
                                {
                                    featureFlag.Enabled = reader.GetBoolean();
                                }
                                else if (reader.TokenType == JsonTokenType.String && bool.TryParse(reader.GetString(), out bool enabled))
                                {
                                    featureFlag.Enabled = enabled;
                                }
                                else
                                {
                                    throw CreateFeatureFlagFormatException(
                                        FeatureManagementConstants.Enabled,
                                        settingKey,
                                        reader.TokenType.ToString(),
                                        $"{JsonTokenType.True}' or '{JsonTokenType.False}");
                                }

                                break;
                            }

                        case FeatureManagementConstants.Conditions:
                            {
                                if (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
                                {
                                    featureFlag.Conditions = ParseFeatureConditions(ref reader, settingKey);
                                }
                                else if (reader.TokenType != JsonTokenType.Null)
                                {
                                    throw CreateFeatureFlagFormatException(
                                        FeatureManagementConstants.Conditions,
                                        settingKey,
                                        reader.TokenType.ToString(),
                                        JsonTokenType.StartObject.ToString());
                                }

                                break;
                            }

                        case FeatureManagementConstants.Allocation:
                            {
                                if (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
                                {
                                    featureFlag.Allocation = ParseFeatureAllocation(ref reader, settingKey);
                                }
                                else if (reader.TokenType != JsonTokenType.Null)
                                {
                                    throw CreateFeatureFlagFormatException(
                                        FeatureManagementConstants.Allocation,
                                        settingKey,
                                        reader.TokenType.ToString(),
                                        JsonTokenType.StartObject.ToString());
                                }

                                break;
                            }

                        case FeatureManagementConstants.Variants:
                            {
                                if (reader.Read() && reader.TokenType == JsonTokenType.StartArray)
                                {
                                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                                    {
                                        if (reader.TokenType == JsonTokenType.StartObject)
                                        {
                                            FeatureVariant featureVariant = ParseFeatureVariant(ref reader, settingKey);

                                            if (variant.Name != null)
                                            {
                                                featureFlag.Variants.Append(featureVariant);
                                            }
                                        }
                                    }
                                }
                                else if (reader.TokenType != JsonTokenType.Null)
                                {
                                    throw CreateFeatureFlagFormatException(
                                        FeatureManagementConstants.Variants,
                                        settingKey,
                                        reader.TokenType.ToString(),
                                        JsonTokenType.StartArray.ToString());
                                }

                                break;
                            }

                        case FeatureManagementConstants.Telemetry:
                            {
                                if (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
                                {
                                    featureFlag.Telemetry = ParseFeatureTelemetry(ref reader, settingKey);
                                }
                                else if (reader.TokenType != JsonTokenType.Null)
                                {
                                    throw CreateFeatureFlagFormatException(
                                        FeatureManagementConstants.Telemetry,
                                        settingKey,
                                        reader.TokenType.ToString(),
                                        JsonTokenType.StartObject.ToString());
                                }

                                break;
                            }

                        default:
                            reader.Skip();

                            break;
                    }
                }
            }
            catch (JsonException e)
            {
                throw new FormatException(settingKey, e);
            }

            return featureFlag;
        }

        private FeatureConditions ParseFeatureConditions(ref Utf8JsonReader reader, string settingKey)
        {
            var featureConditions = new FeatureConditions();

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    continue;
                }

                string conditionsPropertyName = reader.GetString();

                switch (conditionsPropertyName)
                {
                    case FeatureManagementConstants.ClientFilters:
                        {
                            if (reader.Read() && reader.TokenType == JsonTokenType.Null)
                            {
                                break;
                            }
                            else if (reader.TokenType == JsonTokenType.StartArray)
                            {
                                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                                {
                                    if (reader.TokenType == JsonTokenType.StartObject)
                                    {
                                        ClientFilter clientFilter = ParseClientFilter(ref reader, settingKey);

                                        if (clientFilter.Name != null ||
                                            (clientFilter.Parameters.ValueKind == JsonValueKind.Object &&
                                            clientFilter.Parameters.EnumerateObject().Any()))
                                        {
                                            featureConditions.ClientFilters.Add(clientFilter);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                throw CreateFeatureFlagFormatException(
                                    FeatureManagementConstants.ClientFilters,
                                    settingKey,
                                    reader.TokenType.ToString(),
                                    JsonTokenType.StartArray.ToString());
                            }

                            break;
                        }

                    case FeatureManagementConstants.RequirementType:
                        {
                            if (reader.Read() && reader.TokenType == JsonTokenType.String)
                            {
                                featureConditions.RequirementType = reader.GetString();
                            }
                            else if (reader.TokenType != JsonTokenType.Null)
                            {
                                throw CreateFeatureFlagFormatException(
                                    FeatureManagementConstants.RequirementType,
                                    settingKey,
                                    reader.TokenType.ToString(),
                                    JsonTokenType.String.ToString());
                            }

                            break;
                        }

                    default:
                        reader.Skip();

                        break;
                }
            }

            return featureConditions;
        }

        private ClientFilter ParseClientFilter(ref Utf8JsonReader reader, string settingKey)
        {
            var clientFilter = new ClientFilter();

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    continue;
                }

                string clientFiltersPropertyName = reader.GetString();

                switch (clientFiltersPropertyName)
                {
                    case FeatureManagementConstants.Name:
                        {
                            if (reader.Read() && reader.TokenType == JsonTokenType.String)
                            {
                                clientFilter.Name = reader.GetString();
                            }
                            else if (reader.TokenType != JsonTokenType.Null)
                            {
                                throw CreateFeatureFlagFormatException(
                                    FeatureManagementConstants.Name,
                                    settingKey,
                                    reader.TokenType.ToString(),
                                    JsonTokenType.String.ToString());
                            }

                            break;
                        }

                    case FeatureManagementConstants.Parameters:
                        {
                            if (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
                            {
                                clientFilter.Parameters = JsonDocument.ParseValue(ref reader).RootElement;
                            }
                            else if (reader.TokenType != JsonTokenType.Null)
                            {
                                throw CreateFeatureFlagFormatException(
                                    FeatureManagementConstants.Parameters,
                                    settingKey,
                                    reader.TokenType.ToString(),
                                    JsonTokenType.StartObject.ToString());
                            }

                            break;
                        }

                    default:
                        reader.Skip();

                        break;
                }
            }

            return clientFilter;
        }

        private FeatureAllocation ParseFeatureAllocation(ref Utf8JsonReader reader, string settingKey)
        {
            var featureAllocation = new FeatureAllocation();

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    continue;
                }

                string allocationPropertyName = reader.GetString();

                switch (allocationPropertyName)
                {
                    case FeatureManagementConstants.DefaultWhenDisabled:
                        {
                            if (reader.Read() && reader.TokenType == JsonTokenType.String)
                            {
                                featureAllocation.DefaultWhenDisabled = reader.GetString();
                            }
                            else if (reader.TokenType != JsonTokenType.Null)
                            {
                                throw CreateFeatureFlagFormatException(
                                    FeatureManagementConstants.DefaultWhenDisabled,
                                    settingKey,
                                    reader.TokenType.ToString(),
                                    JsonTokenType.String.ToString());
                            }

                            break;
                        }

                    case FeatureManagementConstants.DefaultWhenEnabled:
                        {
                            if (reader.Read() && reader.TokenType == JsonTokenType.String)
                            {
                                featureAllocation.DefaultWhenEnabled = reader.GetString();
                            }
                            else if (reader.TokenType != JsonTokenType.Null)
                            {
                                throw CreateFeatureFlagFormatException(
                                    FeatureManagementConstants.DefaultWhenEnabled,
                                    settingKey,
                                    reader.TokenType.ToString(),
                                    JsonTokenType.String.ToString());
                            }

                            break;
                        }

                    case FeatureManagementConstants.UserAllocation:
                        {
                            if (reader.Read() && reader.TokenType == JsonTokenType.StartArray)
                            {
                                featureAllocation.UserAllocation = reader.GetString();
                            }
                            else if (reader.TokenType != JsonTokenType.Null)
                            {
                                throw CreateFeatureFlagFormatException(
                                    FeatureManagementConstants.UserAllocation,
                                    settingKey,
                                    reader.TokenType.ToString(),
                                    JsonTokenType.StartArray.ToString());
                            }

                            break;
                        }

                    case FeatureManagementConstants.GroupAllocation:
                        {
                            if (reader.Read() && reader.TokenType == JsonTokenType.StartArray)
                            {
                                featureAllocation.GroupAllocation = reader.GetString();
                            }
                            else if (reader.TokenType != JsonTokenType.Null)
                            {
                                throw CreateFeatureFlagFormatException(
                                    FeatureManagementConstants.GroupAllocation,
                                    settingKey,
                                    reader.TokenType.ToString(),
                                    JsonTokenType.StartArray.ToString());
                            }

                            break;
                        }

                    case FeatureManagementConstants.PercentileAllocation:
                        {
                            if (reader.Read() && reader.TokenType == JsonTokenType.StartArray)
                            {
                                featureAllocation.PercentileAllocation = reader.GetString();
                            }
                            else if (reader.TokenType != JsonTokenType.Null)
                            {
                                throw CreateFeatureFlagFormatException(
                                    FeatureManagementConstants.PercentileAllocation,
                                    settingKey,
                                    reader.TokenType.ToString(),
                                    JsonTokenType.StartArray.ToString());
                            }

                            break;
                        }

                    default:
                        reader.Skip();

                        break;
                }
            }

            return featureAllocation;
        }

        private FeatureVariant ParseFeatureVariant(ref Utf8JsonReader reader, string settingKey)
        {
            var featureVariant = new FeatureVariant();

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    continue;
                }

                string variantPropertyName = reader.GetString();

                switch (variantPropertyName)
                {
                    case FeatureManagementConstants.Name:
                        {
                            if (reader.Read() && reader.TokenType == JsonTokenType.String)
                            {
                                featureVariant.Name = reader.GetString();
                            }
                            else if (reader.TokenType != JsonTokenType.Null)
                            {
                                throw CreateFeatureFlagFormatException(
                                    FeatureManagementConstants.Name,
                                    settingKey,
                                    reader.TokenType.ToString(),
                                    JsonTokenType.String.ToString());
                            }

                            break;
                        }

                    case FeatureManagementConstants.ConfigurationReference:
                        {
                            if (reader.Read() && reader.TokenType == JsonTokenType.String)
                            {
                                featureVariant.ConfigurationReference = reader.GetString();
                            }
                            else if (reader.TokenType != JsonTokenType.Null)
                            {
                                throw CreateFeatureFlagFormatException(
                                    FeatureManagementConstants.ConfigurationReference,
                                    settingKey,
                                    reader.TokenType.ToString(),
                                    JsonTokenType.String.ToString());
                            }

                            break;
                        }

                    case FeatureManagementConstants.ConfigurationValue:
                        {
                            if (reader.Read())
                            {
                                featureVariant.ConfigurationValue = JsonDocument.ParseValue(ref reader).RootElement;
                            }

                            break;
                        }

                    default:
                        reader.Skip();

                        break;
                }
            }

            return featureVariant;
        }

        private FeatureTelemetry ParseFeatureTelemetry(ref Utf8JsonReader reader, string settingKey)
        {
            var featureTelemetry = new FeatureTelemetry();

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    continue;
                }

                string telemetryPropertyName = reader.GetString();

                switch (telemetryPropertyName)
                {
                    case FeatureManagementConstants.Enabled:
                        {
                            if (reader.Read() && (reader.TokenType == JsonTokenType.False || reader.TokenType == JsonTokenType.True))
                            {
                                featureTelemetry.Enabled = reader.GetBoolean();
                            }
                            else if (reader.TokenType == JsonTokenType.String && bool.TryParse(reader.GetString(), out bool enabled))
                            {
                                featureTelemetry.Enabled = enabled;
                            }
                            else if (reader.TokenType != JsonTokenType.Null)
                            {
                                throw CreateFeatureFlagFormatException(
                                    FeatureManagementConstants.Enabled,
                                    settingKey,
                                    reader.TokenType.ToString(),
                                    JsonTokenType.String.ToString());
                            }

                            break;
                        }

                        //TODO

                    default:
                        reader.Skip();

                        break;
                }
            }

            return featureTelemetry;
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
