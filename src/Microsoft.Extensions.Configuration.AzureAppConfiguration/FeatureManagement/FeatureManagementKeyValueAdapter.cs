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
    internal class FeatureManagementKeyValueAdapter : IKeyValueAdapter
    {
        private FeatureFilterTracing _featureFilterTracing;

        public FeatureManagementKeyValueAdapter(FeatureFilterTracing featureFilterTracing)
        {
            _featureFilterTracing = featureFilterTracing ?? throw new ArgumentNullException(nameof(featureFilterTracing));
        }

        public Task<IEnumerable<KeyValuePair<string, string>>> ProcessKeyValue(ConfigurationSetting setting, Logger logger, CancellationToken cancellationToken)
        {
            FeatureFlag featureFlag = ParseFeatureFlag(setting.Key, setting.Value);

            var keyValues = new List<KeyValuePair<string, string>>();

            if (!string.IsNullOrEmpty(featureFlag.Id))
            {
                if (featureFlag.Enabled)
                {
                    if (featureFlag.Conditions?.ClientFilters == null || !featureFlag.Conditions.ClientFilters.Any())
                    {
                        keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.SectionName}:{featureFlag.Id}", true.ToString()));
                    }
                    else
                    {
                        for (int i = 0; i < featureFlag.Conditions.ClientFilters.Count; i++)
                        {
                            ClientFilter clientFilter = featureFlag.Conditions.ClientFilters[i];

                            _featureFilterTracing.UpdateFeatureFilterTracing(clientFilter.Name);

                            keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.SectionName}:{featureFlag.Id}:{FeatureManagementConstants.EnabledFor}:{i}:Name", clientFilter.Name));

                            foreach (KeyValuePair<string, string> kvp in new JsonFlattener().FlattenJson(clientFilter.Parameters))
                            {
                                keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.SectionName}:{featureFlag.Id}:{FeatureManagementConstants.EnabledFor}:{i}:Parameters:{kvp.Key}", kvp.Value));
                            }
                        }

                        //
                        // process RequirementType only when filters are not empty
                        if (featureFlag.Conditions.RequirementType != null)
                        {
                            keyValues.Add(new KeyValuePair<string, string>(
                                $"{FeatureManagementConstants.SectionName}:{featureFlag.Id}:{FeatureManagementConstants.RequirementType}",
                                featureFlag.Conditions.RequirementType));
                        }
                    }
                }
                else
                {
                    keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.SectionName}:{featureFlag.Id}", false.ToString()));
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
                        case FeatureManagementConstants.IdJsonPropertyName:
                            {
                                if (reader.Read() && reader.TokenType == JsonTokenType.String)
                                {
                                    featureFlag.Id = reader.GetString();
                                }
                                else if (reader.TokenType != JsonTokenType.Null)
                                {
                                    throw CreateFeatureFlagFormatException(
                                        FeatureManagementConstants.IdJsonPropertyName,
                                        settingKey,
                                        reader.TokenType.ToString(),
                                        JsonTokenType.String.ToString());
                                }

                                break;
                            }

                        case FeatureManagementConstants.EnabledJsonPropertyName:
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
                                        FeatureManagementConstants.EnabledJsonPropertyName,
                                        settingKey,
                                        reader.TokenType.ToString(),
                                        $"{JsonTokenType.True}' or '{JsonTokenType.False}");
                                }

                                break;
                            }

                        case FeatureManagementConstants.ConditionsJsonPropertyName:
                            {
                                if (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
                                {
                                    featureFlag.Conditions = ParseFeatureConditions(ref reader, settingKey);
                                }
                                else if (reader.TokenType != JsonTokenType.Null)
                                {
                                    throw CreateFeatureFlagFormatException(
                                        FeatureManagementConstants.ConditionsJsonPropertyName,
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
                throw new FormatException(string.Format(ErrorMessages.FeatureFlagInvalidFormat, settingKey), e);
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
                    case FeatureManagementConstants.ClientFiltersJsonPropertyName:
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
                                    FeatureManagementConstants.ClientFiltersJsonPropertyName,
                                    settingKey,
                                    reader.TokenType.ToString(),
                                    JsonTokenType.StartArray.ToString());
                            }

                            break;
                        }

                    case FeatureManagementConstants.RequirementTypeJsonPropertyName:
                        {
                            if (reader.Read() && reader.TokenType == JsonTokenType.String)
                            {
                                featureConditions.RequirementType = reader.GetString();
                            }
                            else if (reader.TokenType != JsonTokenType.Null)
                            {
                                throw CreateFeatureFlagFormatException(
                                    FeatureManagementConstants.RequirementTypeJsonPropertyName,
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
                    case FeatureManagementConstants.NameJsonPropertyName:
                        {
                            if (reader.Read() && reader.TokenType == JsonTokenType.String)
                            {
                                clientFilter.Name = reader.GetString();
                            }
                            else if (reader.TokenType != JsonTokenType.Null)
                            {
                                throw CreateFeatureFlagFormatException(
                                    FeatureManagementConstants.NameJsonPropertyName,
                                    settingKey,
                                    reader.TokenType.ToString(),
                                    JsonTokenType.String.ToString());
                            }

                            break;
                        }

                    case FeatureManagementConstants.ParametersJsonPropertyName:
                        {
                            if (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
                            {
                                clientFilter.Parameters = JsonDocument.ParseValue(ref reader).RootElement;
                            }
                            else if (reader.TokenType != JsonTokenType.Null)
                            {
                                throw CreateFeatureFlagFormatException(
                                    FeatureManagementConstants.ParametersJsonPropertyName,
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
    }
}
