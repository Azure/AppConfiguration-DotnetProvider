// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Mime;
using System.Security.Cryptography;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal partial class FeatureManagementKeyValueAdapter : IKeyValueAdapter
    {
        private FeatureFlagTracing _featureFlagTracing;
        private int _featureFlagIndex = 0;
        private bool _fmSchemaCompatibilityDisabled = false;

        public FeatureManagementKeyValueAdapter(FeatureFlagTracing featureFlagTracing)
        {
            _featureFlagTracing = featureFlagTracing ?? throw new ArgumentNullException(nameof(featureFlagTracing));

            _fmSchemaCompatibilityDisabled = EnvironmentVariableHelper.GetBoolOrDefault(EnvironmentVariableNames.FmSchemacompatibilityDisabled);
        }

        public Task<IEnumerable<KeyValuePair<string, string>>> ProcessKeyValue(ConfigurationSetting setting, Uri endpoint, Logger logger, CancellationToken cancellationToken)
        {
            ClassicFeatureFlag featureFlag = ParseFeatureFlag(setting.Key, setting.Value);

            var metadata = new FeatureFlagMetadata(setting.Key, setting.Label, setting.ETag);

            IEnumerable<KeyValuePair<string, string>> keyValues = ProcessFeatureFlag(featureFlag, metadata, endpoint);

            return Task.FromResult(keyValues);
        }

        // Emits feature-management configuration key-values for a classic feature flag (parsed from a
        // ConfigurationSetting in the ".appconfig.featureflag/" key-value namespace).
        public IEnumerable<KeyValuePair<string, string>> ProcessFeatureFlag(ClassicFeatureFlag featureFlag, FeatureFlagMetadata metadata, Uri endpoint)
        {
            // Check if we need to process the feature flag using the microsoft schema
            if (_fmSchemaCompatibilityDisabled ||
                (featureFlag.Variants != null && featureFlag.Variants.Any()) ||
                featureFlag.Allocation != null ||
                featureFlag.Telemetry != null)
            {
                return ProcessMicrosoftSchemaFeatureFlag(featureFlag, metadata, endpoint);
            }

            return ProcessDotnetSchemaFeatureFlag(featureFlag);
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
            _featureFlagIndex = 0;

            return;
        }

        private List<KeyValuePair<string, string>> ProcessDotnetSchemaFeatureFlag(ClassicFeatureFlag featureFlag)
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
                        ClassicClientFilter clientFilter = featureFlag.Conditions.ClientFilters[i];

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

        private List<KeyValuePair<string, string>> ProcessMicrosoftSchemaFeatureFlag(ClassicFeatureFlag featureFlag, FeatureFlagMetadata metadata, Uri endpoint)
        {
            var keyValues = new List<KeyValuePair<string, string>>();

            if (string.IsNullOrEmpty(featureFlag.Id))
            {
                return keyValues;
            }

            string featureFlagPath = $"{FeatureManagementConstants.FeatureManagementSectionName}:{FeatureManagementConstants.FeatureFlagsSectionName}:{_featureFlagIndex}";

            _featureFlagIndex++;

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
                        ClassicClientFilter clientFilter = featureFlag.Conditions.ClientFilters[i];

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

                foreach (ClassicFeatureVariant featureVariant in featureFlag.Variants)
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
                ClassicFeatureAllocation allocation = featureFlag.Allocation;

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

                    foreach (ClassicFeatureUserAllocation userAllocation in allocation.User)
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

                    foreach (ClassicFeatureGroupAllocation groupAllocation in allocation.Group)
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

                    foreach (ClassicFeaturePercentileAllocation percentileAllocation in allocation.Percentile)
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
                ClassicFeatureTelemetry telemetry = featureFlag.Telemetry;

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

                    if (endpoint != null)
                    {
                        string featureFlagReference = $"{endpoint.AbsoluteUri}kv/{metadata.Key}{(!string.IsNullOrWhiteSpace(metadata.Label) ? $"?label={metadata.Label}" : "")}";

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

        private string CalculateAllocationId(ClassicFeatureFlag flag)
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
                IEnumerable<ClassicFeaturePercentileAllocation> sortedPercentiles = flag.Allocation.Percentile
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
                IEnumerable<ClassicFeatureVariant> sortedVariants = flag.Variants
                    .Where(variant => allocatedVariants.Contains(variant.Name))
                    .OrderBy(variant => variant.Name)
                    .ToList();

                inputBuilder.Append(string.Join(";", sortedVariants.Select(v =>
                {
                    var variantValue = string.Empty;

                    if (v.ConfigurationValue.ValueKind != JsonValueKind.Null && v.ConfigurationValue.ValueKind != JsonValueKind.Undefined)
                    {
                        variantValue = v.ConfigurationValue.SerializeWithSortedKeys();
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

        private FormatException CreateFeatureFlagFormatException(string jsonPropertyName, string settingKey, string foundJsonValueKind, string expectedJsonValueKind)
        {
            return new FormatException(string.Format(
                ErrorMessages.FeatureFlagInvalidJsonProperty,
                jsonPropertyName,
                settingKey,
                foundJsonValueKind,
                expectedJsonValueKind));
        }

        private ClassicFeatureFlag ParseFeatureFlag(string settingKey, string settingValue)
        {
            var featureFlag = new ClassicFeatureFlag();

            var reader = new Utf8JsonReader(
                System.Text.Encoding.UTF8.GetBytes(settingValue),
                new JsonReaderOptions
                {
                    AllowTrailingCommas = true
                });

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
                                    List<ClassicFeatureVariant> variants = new List<ClassicFeatureVariant>();

                                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                                    {
                                        int i = 0;

                                        if (reader.TokenType == JsonTokenType.StartObject)
                                        {
                                            ClassicFeatureVariant featureVariant = ParseFeatureVariant(ref reader, settingKey);

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

        private ClassicFeatureConditions ParseFeatureConditions(ref Utf8JsonReader reader, string settingKey)
        {
            var featureConditions = new ClassicFeatureConditions();

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
                                        ClassicClientFilter clientFilter = ParseClientFilter(ref reader, settingKey);

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

        private ClassicClientFilter ParseClientFilter(ref Utf8JsonReader reader, string settingKey)
        {
            var clientFilter = new ClassicClientFilter();

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

        private ClassicFeatureAllocation ParseFeatureAllocation(ref Utf8JsonReader reader, string settingKey)
        {
            var featureAllocation = new ClassicFeatureAllocation();

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
                                List<ClassicFeatureUserAllocation> userAllocations = new List<ClassicFeatureUserAllocation>();

                                int i = 0;

                                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                                {
                                    if (reader.TokenType == JsonTokenType.StartObject)
                                    {
                                        ClassicFeatureUserAllocation featureUserAllocation = ParseFeatureUserAllocation(ref reader, settingKey);

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
                                List<ClassicFeatureGroupAllocation> groupAllocations = new List<ClassicFeatureGroupAllocation>();

                                int i = 0;

                                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                                {
                                    if (reader.TokenType == JsonTokenType.StartObject)
                                    {
                                        ClassicFeatureGroupAllocation featureGroupAllocation = ParseFeatureGroupAllocation(ref reader, settingKey);

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
                                List<ClassicFeaturePercentileAllocation> percentileAllocations = new List<ClassicFeaturePercentileAllocation>();

                                int i = 0;

                                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                                {
                                    if (reader.TokenType == JsonTokenType.StartObject)
                                    {
                                        ClassicFeaturePercentileAllocation featurePercentileAllocation = ParseFeaturePercentileAllocation(ref reader, settingKey);

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

        private ClassicFeatureUserAllocation ParseFeatureUserAllocation(ref Utf8JsonReader reader, string settingKey)
        {
            var featureUserAllocation = new ClassicFeatureUserAllocation();

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

        private ClassicFeatureGroupAllocation ParseFeatureGroupAllocation(ref Utf8JsonReader reader, string settingKey)
        {
            var featureGroupAllocation = new ClassicFeatureGroupAllocation();

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

        private ClassicFeaturePercentileAllocation ParseFeaturePercentileAllocation(ref Utf8JsonReader reader, string settingKey)
        {
            var featurePercentileAllocation = new ClassicFeaturePercentileAllocation();

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

        private ClassicFeatureVariant ParseFeatureVariant(ref Utf8JsonReader reader, string settingKey)
        {
            var featureVariant = new ClassicFeatureVariant();

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

        private ClassicFeatureTelemetry ParseFeatureTelemetry(ref Utf8JsonReader reader, string settingKey)
        {
            var featureTelemetry = new ClassicFeatureTelemetry();

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
    }
}
