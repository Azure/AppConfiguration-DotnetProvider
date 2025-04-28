// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class FeatureManagementKeyValueAdapter : IKeyValueAdapter
    {
        private FeatureFlagTracing _featureFlagTracing;
        private static int _globalFeatureFlagCounter = 1000000;
        private readonly int _startFeatureFlagIndex;
        private int _currentFeatureFlagIndex;
        private int _processedFlagsCount;
        private int _maxProcessedFlagsPerRefresh;

        public FeatureManagementKeyValueAdapter(FeatureFlagTracing featureFlagTracing)
        {
            _featureFlagTracing = featureFlagTracing ?? throw new ArgumentNullException(nameof(featureFlagTracing));

            _startFeatureFlagIndex = Interlocked.Add(ref _globalFeatureFlagCounter, 0);
            _currentFeatureFlagIndex = _startFeatureFlagIndex;
            _processedFlagsCount = 0;
            _maxProcessedFlagsPerRefresh = 0;
        }

        public Task<IEnumerable<KeyValuePair<string, string>>> ProcessKeyValue(ConfigurationSetting setting, Uri endpoint, Logger logger, CancellationToken cancellationToken)
        {
            FeatureFlag featureFlag = ParseFeatureFlag(setting.Key, setting.Value);

            var keyValues = new List<KeyValuePair<string, string>>();

            // Check if we need to process the feature flag using the microsoft schema
            if ((featureFlag.Variants != null && featureFlag.Variants.Any()) || featureFlag.Allocation != null || featureFlag.Telemetry != null)
            {
                keyValues = ProcessMicrosoftSchemaFeatureFlag(featureFlag, setting, endpoint);
            }
            else
            {
                keyValues = ProcessDotnetSchemaFeatureFlag(featureFlag, setting, endpoint);
            }

            return Task.FromResult<IEnumerable<KeyValuePair<string, string>>>(keyValues);
        }

        private void TrackProcessedFlag()
        {
            _processedFlagsCount++;

            // Update the max flags count if we have more flags this time compared to last load/refresh
            if (_processedFlagsCount > _maxProcessedFlagsPerRefresh)
            {
                _maxProcessedFlagsPerRefresh = _processedFlagsCount;

                // Update the global counter to reserve enough space for our maximum observed flags
                Interlocked.CompareExchange(
                    ref _globalFeatureFlagCounter,
                    _startFeatureFlagIndex + _maxProcessedFlagsPerRefresh,
                    _globalFeatureFlagCounter);
            }
        }

        public bool CanProcess(ConfigurationSetting setting)
        {
            if (setting == null ||
                string.IsNullOrWhiteSpace(setting.Value) ||
                string.IsNullOrWhiteSpace(setting.ContentType))
            {
                return false;
            }

            if (setting.Key.StartsWith(FeatureManagementConstants.FeatureFlagMarker))
            {
                return true;
            }

            return setting.ContentType.TryParseContentType(out ContentType contentType) &&
                contentType.IsFeatureFlag();
        }

        public bool NeedsRefresh()
        {
            return false;
        }

        public void OnChangeDetected(ConfigurationSetting setting = null)
        {
            return;
        }

        public void OnConfigUpdated()
        {
            _currentFeatureFlagIndex = _startFeatureFlagIndex;

            _processedFlagsCount = 0;

            return;
        }

        private List<KeyValuePair<string, string>> ProcessDotnetSchemaFeatureFlag(FeatureFlag featureFlag, ConfigurationSetting setting, Uri endpoint)
        {
            var keyValues = new List<KeyValuePair<string, string>>();

            if (string.IsNullOrEmpty(featureFlag.Id))
            {
                return keyValues;
            }

            string featureFlagPath = $"{FeatureManagementConstants.DotnetSchemaSectionName}:{featureFlag.Id}";

            if (featureFlag.Enabled)
            {
                if (featureFlag.Conditions?.ClientFilters == null || !featureFlag.Conditions.ClientFilters.Any())
                {
                    keyValues.Add(new KeyValuePair<string, string>(featureFlagPath, true.ToString()));
                }
                else
                {
                    for (int i = 0; i < featureFlag.Conditions.ClientFilters.Count; i++)
                    {
                        ClientFilter clientFilter = featureFlag.Conditions.ClientFilters[i];

                        _featureFlagTracing.UpdateFeatureFilterTracing(clientFilter.Name);

                        string clientFiltersPath = $"{featureFlagPath}:{FeatureManagementConstants.DotnetSchemaEnabledFor}:{i}";

                        keyValues.Add(new KeyValuePair<string, string>($"{clientFiltersPath}:Name", clientFilter.Name));

                        foreach (KeyValuePair<string, string> kvp in new JsonFlattener().FlattenJson(clientFilter.Parameters))
                        {
                            keyValues.Add(new KeyValuePair<string, string>($"{clientFiltersPath}:Parameters:{kvp.Key}", kvp.Value));
                        }
                    }

                    //
                    // process RequirementType only when filters are not empty
                    if (featureFlag.Conditions.RequirementType != null)
                    {
                        keyValues.Add(new KeyValuePair<string, string>(
                            $"{featureFlagPath}:{FeatureManagementConstants.DotnetSchemaRequirementType}",
                            featureFlag.Conditions.RequirementType));
                    }
                }
            }
            else
            {
                keyValues.Add(new KeyValuePair<string, string>($"{featureFlagPath}", false.ToString()));
            }

            return keyValues;
        }

        private List<KeyValuePair<string, string>> ProcessMicrosoftSchemaFeatureFlag(FeatureFlag featureFlag, ConfigurationSetting setting, Uri endpoint)
        {
            var keyValues = new List<KeyValuePair<string, string>>();

            if (string.IsNullOrEmpty(featureFlag.Id))
            {
                return keyValues;
            }

            string featureFlagPath = $"{FeatureManagementConstants.FeatureManagementSectionName}:{FeatureManagementConstants.FeatureFlagsSectionName}:{_currentFeatureFlagIndex}";

            _currentFeatureFlagIndex++;
            TrackProcessedFlag();

            keyValues.Add(new KeyValuePair<string, string>($"{featureFlagPath}:{FeatureManagementConstants.Id}", featureFlag.Id));

            keyValues.Add(new KeyValuePair<string, string>($"{featureFlagPath}:{FeatureManagementConstants.Enabled}", featureFlag.Enabled.ToString()));

            if (featureFlag.Enabled)
            {
                if (featureFlag.Conditions?.ClientFilters != null && featureFlag.Conditions.ClientFilters.Any()) // workaround since we are not yet setting client filters to null
                {
                    //
                    // Conditionally based on feature filters
                    for (int i = 0; i < featureFlag.Conditions.ClientFilters.Count; i++)
                    {
                        ClientFilter clientFilter = featureFlag.Conditions.ClientFilters[i];

                        _featureFlagTracing.UpdateFeatureFilterTracing(clientFilter.Name);

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

                    if (featureVariant.StatusOverride != null)
                    {
                        keyValues.Add(new KeyValuePair<string, string>($"{variantsPath}:{FeatureManagementConstants.StatusOverride}", featureVariant.StatusOverride));
                    }

                    i++;
                }

                _featureFlagTracing.NotifyMaxVariants(i);
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
                    _featureFlagTracing.UsesSeed = true;

                    keyValues.Add(new KeyValuePair<string, string>($"{allocationPath}:{FeatureManagementConstants.Seed}", allocation.Seed));
                }
            }

            if (featureFlag.Telemetry != null)
            {
                FeatureTelemetry telemetry = featureFlag.Telemetry;

                string telemetryPath = $"{featureFlagPath}:{FeatureManagementConstants.Telemetry}";

                if (telemetry.Enabled)
                {
                    _featureFlagTracing.UsesTelemetry = true;

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

            return keyValues;
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
            var featureFlag = new FeatureFlag();

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
                                    List<FeatureVariant> variants = new List<FeatureVariant>();

                                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                                    {
                                        int i = 0;

                                        if (reader.TokenType == JsonTokenType.StartObject)
                                        {
                                            FeatureVariant featureVariant = ParseFeatureVariant(ref reader, settingKey);

                                            if (featureVariant.Name != null)
                                            {
                                                variants.Add(featureVariant);
                                            }
                                        }
                                        else if (reader.TokenType != JsonTokenType.Null)
                                        {
                                            throw CreateFeatureFlagFormatException(
                                                $"{FeatureManagementConstants.Variants}[{i}]",
                                                settingKey,
                                                reader.TokenType.ToString(),
                                                JsonTokenType.StartObject.ToString());
                                        }

                                        i++;
                                    }

                                    featureFlag.Variants = variants;
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
                            if (reader.Read() && reader.TokenType == JsonTokenType.StartArray)
                            {
                                int i = 0;

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
                                    else if (reader.TokenType != JsonTokenType.Null)
                                    {
                                        throw CreateFeatureFlagFormatException(
                                            $"{FeatureManagementConstants.ClientFilters}[{i}]",
                                            settingKey,
                                            reader.TokenType.ToString(),
                                            JsonTokenType.StartObject.ToString());
                                    }

                                    i++;
                                }
                            }
                            else if (reader.TokenType != JsonTokenType.Null)
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
                                List<FeatureUserAllocation> userAllocations = new List<FeatureUserAllocation>();

                                int i = 0;

                                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                                {
                                    if (reader.TokenType == JsonTokenType.StartObject)
                                    {
                                        FeatureUserAllocation featureUserAllocation = ParseFeatureUserAllocation(ref reader, settingKey);

                                        if (featureUserAllocation.Variant != null ||
                                            (featureUserAllocation.Users != null &&
                                            featureUserAllocation.Users.Any()))
                                        {
                                            userAllocations.Add(featureUserAllocation);
                                        }
                                    }
                                    else if (reader.TokenType != JsonTokenType.Null)
                                    {
                                        throw CreateFeatureFlagFormatException(
                                            $"{FeatureManagementConstants.UserAllocation}[{i}]",
                                            settingKey,
                                            reader.TokenType.ToString(),
                                            JsonTokenType.StartObject.ToString());
                                    }

                                    i++;
                                }

                                featureAllocation.User = userAllocations;
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
                                List<FeatureGroupAllocation> groupAllocations = new List<FeatureGroupAllocation>();

                                int i = 0;

                                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                                {
                                    if (reader.TokenType == JsonTokenType.StartObject)
                                    {
                                        FeatureGroupAllocation featureGroupAllocation = ParseFeatureGroupAllocation(ref reader, settingKey);

                                        if (featureGroupAllocation.Variant != null ||
                                            (featureGroupAllocation.Groups != null &&
                                            featureGroupAllocation.Groups.Any()))
                                        {
                                            groupAllocations.Add(featureGroupAllocation);
                                        }
                                    }
                                    else if (reader.TokenType != JsonTokenType.Null)
                                    {
                                        throw CreateFeatureFlagFormatException(
                                            $"{FeatureManagementConstants.GroupAllocation}[{i}]",
                                            settingKey,
                                            reader.TokenType.ToString(),
                                            JsonTokenType.StartObject.ToString());
                                    }

                                    i++;
                                }

                                featureAllocation.Group = groupAllocations;
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
                                List<FeaturePercentileAllocation> percentileAllocations = new List<FeaturePercentileAllocation>();

                                int i = 0;

                                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                                {
                                    if (reader.TokenType == JsonTokenType.StartObject)
                                    {
                                        FeaturePercentileAllocation featurePercentileAllocation = ParseFeaturePercentileAllocation(ref reader, settingKey);

                                        percentileAllocations.Add(featurePercentileAllocation);
                                    }
                                    else if (reader.TokenType != JsonTokenType.Null)
                                    {
                                        throw CreateFeatureFlagFormatException(
                                            $"{FeatureManagementConstants.PercentileAllocation}[{i}]",
                                            settingKey,
                                            reader.TokenType.ToString(),
                                            JsonTokenType.StartObject.ToString());
                                    }

                                    i++;
                                }

                                featureAllocation.Percentile = percentileAllocations;
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

                    case FeatureManagementConstants.Seed:
                        {
                            if (reader.Read() && reader.TokenType == JsonTokenType.String)
                            {
                                featureAllocation.Seed = reader.GetString();
                            }
                            else if (reader.TokenType != JsonTokenType.Null)
                            {
                                throw CreateFeatureFlagFormatException(
                                    FeatureManagementConstants.Seed,
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

            return featureAllocation;
        }

        private FeatureUserAllocation ParseFeatureUserAllocation(ref Utf8JsonReader reader, string settingKey)
        {
            var featureUserAllocation = new FeatureUserAllocation();

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    continue;
                }

                string userAllocationPropertyName = reader.GetString();

                switch (userAllocationPropertyName)
                {
                    case FeatureManagementConstants.Variant:
                        {
                            if (reader.Read() && reader.TokenType == JsonTokenType.String)
                            {
                                featureUserAllocation.Variant = reader.GetString();
                            }
                            else if (reader.TokenType != JsonTokenType.Null)
                            {
                                throw CreateFeatureFlagFormatException(
                                    FeatureManagementConstants.Variant,
                                    settingKey,
                                    reader.TokenType.ToString(),
                                    JsonTokenType.String.ToString());
                            }

                            break;
                        }

                    case FeatureManagementConstants.Users:
                        {
                            if (reader.Read() && reader.TokenType == JsonTokenType.StartArray)
                            {
                                List<string> users = new List<string>();

                                int i = 0;

                                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                                {
                                    if (reader.TokenType == JsonTokenType.String)
                                    {
                                        users.Add(reader.GetString());
                                    }
                                    else if (reader.TokenType != JsonTokenType.Null)
                                    {
                                        throw CreateFeatureFlagFormatException(
                                            $"{FeatureManagementConstants.Users}[{i}]",
                                            settingKey,
                                            reader.TokenType.ToString(),
                                            JsonTokenType.String.ToString());
                                    }

                                    i++;
                                }

                                featureUserAllocation.Users = users;
                            }
                            else if (reader.TokenType != JsonTokenType.Null)
                            {
                                throw CreateFeatureFlagFormatException(
                                    FeatureManagementConstants.Users,
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

            return featureUserAllocation;
        }

        private FeatureGroupAllocation ParseFeatureGroupAllocation(ref Utf8JsonReader reader, string settingKey)
        {
            var featureGroupAllocation = new FeatureGroupAllocation();

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    continue;
                }

                string groupAllocationPropertyName = reader.GetString();

                switch (groupAllocationPropertyName)
                {
                    case FeatureManagementConstants.Variant:
                        {
                            if (reader.Read() && reader.TokenType == JsonTokenType.String)
                            {
                                featureGroupAllocation.Variant = reader.GetString();
                            }
                            else if (reader.TokenType != JsonTokenType.Null)
                            {
                                throw CreateFeatureFlagFormatException(
                                    FeatureManagementConstants.Variant,
                                    settingKey,
                                    reader.TokenType.ToString(),
                                    JsonTokenType.String.ToString());
                            }

                            break;
                        }

                    case FeatureManagementConstants.Groups:
                        {
                            if (reader.Read() && reader.TokenType == JsonTokenType.StartArray)
                            {
                                List<string> groups = new List<string>();

                                int i = 0;

                                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                                {
                                    if (reader.TokenType == JsonTokenType.String)
                                    {
                                        groups.Add(reader.GetString());
                                    }
                                    else if (reader.TokenType != JsonTokenType.Null)
                                    {
                                        throw CreateFeatureFlagFormatException(
                                            $"{FeatureManagementConstants.Groups}[{i}]",
                                            settingKey,
                                            reader.TokenType.ToString(),
                                            JsonTokenType.String.ToString());
                                    }

                                    i++;
                                }

                                featureGroupAllocation.Groups = groups;
                            }
                            else if (reader.TokenType != JsonTokenType.Null)
                            {
                                throw CreateFeatureFlagFormatException(
                                    FeatureManagementConstants.Groups,
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

            return featureGroupAllocation;
        }

        private FeaturePercentileAllocation ParseFeaturePercentileAllocation(ref Utf8JsonReader reader, string settingKey)
        {
            var featurePercentileAllocation = new FeaturePercentileAllocation();

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    continue;
                }

                string percentileAllocationPropertyName = reader.GetString();

                switch (percentileAllocationPropertyName)
                {
                    case FeatureManagementConstants.Variant:
                        {
                            if (reader.Read() && reader.TokenType == JsonTokenType.String)
                            {
                                featurePercentileAllocation.Variant = reader.GetString();
                            }
                            else if (reader.TokenType != JsonTokenType.Null)
                            {
                                throw CreateFeatureFlagFormatException(
                                    FeatureManagementConstants.Variant,
                                    settingKey,
                                    reader.TokenType.ToString(),
                                    JsonTokenType.String.ToString());
                            }

                            break;
                        }

                    case FeatureManagementConstants.From:
                        {
                            if (reader.Read() &&
                                ((reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int from)) ||
                                (reader.TokenType == JsonTokenType.String && int.TryParse(reader.GetString(), out from))))
                            {
                                featurePercentileAllocation.From = from;
                            }
                            else if (reader.TokenType != JsonTokenType.Null)
                            {
                                throw CreateFeatureFlagFormatException(
                                    FeatureManagementConstants.From,
                                    settingKey,
                                    reader.TokenType.ToString(),
                                    JsonTokenType.Number.ToString());
                            }

                            break;
                        }

                    case FeatureManagementConstants.To:
                        {
                            if (reader.Read() &&
                                ((reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int to)) ||
                                (reader.TokenType == JsonTokenType.String && int.TryParse(reader.GetString(), out to))))
                            {
                                featurePercentileAllocation.To = to;
                            }
                            else if (reader.TokenType != JsonTokenType.Null)
                            {
                                throw CreateFeatureFlagFormatException(
                                    FeatureManagementConstants.To,
                                    settingKey,
                                    reader.TokenType.ToString(),
                                    JsonTokenType.Number.ToString());
                            }

                            break;
                        }

                    default:
                        reader.Skip();

                        break;
                }
            }

            return featurePercentileAllocation;
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

                    case FeatureManagementConstants.ConfigurationValue:
                        {
                            if (reader.Read())
                            {
                                featureVariant.ConfigurationValue = JsonDocument.ParseValue(ref reader).RootElement;
                            }

                            break;
                        }

                    case FeatureManagementConstants.StatusOverride:
                        {
                            if (reader.Read() && reader.TokenType == JsonTokenType.String)
                            {
                                featureVariant.StatusOverride = reader.GetString();
                            }
                            else if (reader.TokenType != JsonTokenType.Null)
                            {
                                throw CreateFeatureFlagFormatException(
                                    FeatureManagementConstants.StatusOverride,
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
                                    $"{JsonTokenType.True}' or '{JsonTokenType.False}");
                            }

                            break;
                        }

                    case FeatureManagementConstants.Metadata:
                        {
                            if (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
                            {
                                featureTelemetry.Metadata = new Dictionary<string, string>();

                                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                                {
                                    if (reader.TokenType != JsonTokenType.PropertyName)
                                    {
                                        continue;
                                    }

                                    string metadataPropertyName = reader.GetString();

                                    if (reader.Read() && reader.TokenType == JsonTokenType.String)
                                    {
                                        featureTelemetry.Metadata[metadataPropertyName] = reader.GetString();
                                    }
                                    else if (reader.TokenType != JsonTokenType.Null)
                                    {
                                        throw CreateFeatureFlagFormatException(
                                            metadataPropertyName,
                                            settingKey,
                                            reader.TokenType.ToString(),
                                            JsonTokenType.String.ToString());
                                    }
                                }
                            }
                            else if (reader.TokenType != JsonTokenType.Null)
                            {
                                throw CreateFeatureFlagFormatException(
                                    FeatureManagementConstants.Metadata,
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
