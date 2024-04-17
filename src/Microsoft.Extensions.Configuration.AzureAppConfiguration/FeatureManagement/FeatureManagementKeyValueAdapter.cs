// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Data.AppConfiguration;
using System;
using System.Collections.Generic;
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

            if (featureFlag != null)
            {
                // TODO turn into configuration
                if (featureFlag.Conditions.ClientFilters.Count > 0)
                {

                }
            }

            try
            {
                using (JsonDocument document = JsonDocument.Parse(setting.Value))
                {
                    JsonElement root = document.RootElement;

                    bool isFeatureEnabled = false;

                    if (root.TryGetProperty(FeatureManagementConstants.EnabledJsonPropertyName, out JsonElement enabledElement))
                    {
                        if (enabledElement.ValueKind == JsonValueKind.True || enabledElement.ValueKind == JsonValueKind.False)
                        {
                            isFeatureEnabled = enabledElement.GetBoolean();
                        }
                        else if (enabledElement.ValueKind == JsonValueKind.String && bool.TryParse(enabledElement.GetString(), out bool enabled))
                        {
                            isFeatureEnabled = enabled;
                        }
                        else
                        {
                            throw CreateFeatureFlagFormatException(
                                FeatureManagementConstants.EnabledJsonPropertyName,
                                setting.Key,
                                enabledElement.ValueKind.ToString(),
                                $"{JsonValueKind.True}' or '{JsonValueKind.False}");
                        }
                    }

                    string id = null;

                    if (root.TryGetProperty(FeatureManagementConstants.IdJsonPropertyName, out JsonElement idElement))
                    {
                        if (idElement.ValueKind == JsonValueKind.String)
                        {
                            id = idElement.GetString();
                        }
                        else if (idElement.ValueKind != JsonValueKind.Null)
                        {
                            throw CreateFeatureFlagFormatException(
                                FeatureManagementConstants.IdJsonPropertyName,
                                setting.Key,
                                idElement.ValueKind.ToString(),
                                JsonValueKind.String.ToString());
                        }
                    }

                    if (isFeatureEnabled)
                    {
                        bool conditionsPresent = root.TryGetProperty(FeatureManagementConstants.ConditionsJsonPropertyName, out JsonElement conditionsElement);

                        if (conditionsPresent &&
                            conditionsElement.ValueKind != JsonValueKind.Object && 
                            conditionsElement.ValueKind != JsonValueKind.Null)
                        {
                            throw CreateFeatureFlagFormatException(
                                FeatureManagementConstants.ConditionsJsonPropertyName,
                                setting.Key,
                                conditionsElement.ValueKind.ToString(),
                                JsonValueKind.Object.ToString());
                        }

                        // If conditions is null or missing, it is not present
                        conditionsPresent = conditionsElement.ValueKind == JsonValueKind.Object;

                        bool clientFiltersPresent = false;

                        JsonElement clientFiltersElement = default;

                        if (conditionsPresent) 
                        {
                            clientFiltersPresent = conditionsElement.TryGetProperty(FeatureManagementConstants.ClientFiltersJsonPropertyName, out clientFiltersElement);

                            if (clientFiltersPresent &&
                                clientFiltersElement.ValueKind != JsonValueKind.Array && 
                                clientFiltersElement.ValueKind != JsonValueKind.Null)
                            {
                                throw CreateFeatureFlagFormatException(
                                    FeatureManagementConstants.ClientFiltersJsonPropertyName,
                                    setting.Key,
                                    clientFiltersElement.ValueKind.ToString(),
                                    JsonValueKind.Array.ToString());
                            }

                            // Only consider client filters present if it's a non-empty array
                            clientFiltersPresent = clientFiltersElement.ValueKind == JsonValueKind.Array && clientFiltersElement.GetArrayLength() > 0;
                        }

                        if (!clientFiltersPresent)
                        {
                            keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.SectionName}:{id}", true.ToString()));
                        }
                        else
                        {
                            for (int i = 0; i < clientFiltersElement.GetArrayLength(); i++)
                            {
                                JsonElement currentClientFilterElement = clientFiltersElement[i];

                                if (currentClientFilterElement.TryGetProperty(FeatureManagementConstants.NameJsonPropertyName, out JsonElement clientFilterNameElement))
                                {
                                    if (clientFilterNameElement.ValueKind == JsonValueKind.String)
                                    {
                                        string clientFilterName = clientFilterNameElement.GetString();

                                        _featureFilterTracing.UpdateFeatureFilterTracing(clientFilterName);

                                        keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.SectionName}:{id}:{FeatureManagementConstants.EnabledFor}:{i}:{FeatureManagementConstants.Name}", clientFilterName));
                                    }
                                    else if (clientFilterNameElement.ValueKind != JsonValueKind.Null)
                                    {
                                        throw CreateFeatureFlagFormatException(
                                            FeatureManagementConstants.NameJsonPropertyName,
                                            setting.Key,
                                            clientFilterNameElement.ValueKind.ToString(),
                                            JsonValueKind.String.ToString());
                                    }
                                }

                                if (currentClientFilterElement.TryGetProperty(FeatureManagementConstants.ParametersJsonPropertyName, out JsonElement parametersElement))
                                {
                                    if (parametersElement.ValueKind != JsonValueKind.Object && 
                                        parametersElement.ValueKind != JsonValueKind.Null)
                                    {
                                        throw CreateFeatureFlagFormatException(
                                            FeatureManagementConstants.ParametersJsonPropertyName,
                                            setting.Key,
                                            parametersElement.ValueKind.ToString(),
                                            JsonValueKind.Object.ToString());
                                    }

                                    foreach (KeyValuePair<string, string> kvp in new JsonFlattener().FlattenJson(parametersElement))
                                    {
                                        keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.SectionName}:{id}:{FeatureManagementConstants.EnabledFor}:{i}:{FeatureManagementConstants.Parameters}:{kvp.Key}", kvp.Value));
                                    }
                                }
                            }

                            if (conditionsElement.TryGetProperty(FeatureManagementConstants.RequirementTypeJsonPropertyName, out JsonElement requirementTypeElement))
                            {
                                if (requirementTypeElement.ValueKind == JsonValueKind.String)
                                {
                                    keyValues.Add(new KeyValuePair<string, string>(
                                        $"{FeatureManagementConstants.SectionName}:{id}:{FeatureManagementConstants.RequirementType}",
                                        requirementTypeElement.GetString()));
                                }
                                else if (requirementTypeElement.ValueKind != JsonValueKind.Null)
                                {
                                    throw CreateFeatureFlagFormatException(
                                        FeatureManagementConstants.RequirementTypeJsonPropertyName,
                                        setting.Key,
                                        requirementTypeElement.ValueKind.ToString(),
                                        JsonValueKind.String.ToString());
                                }
                            }
                        }
                    }
                    else
                    {
                        keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.SectionName}:{id}", false.ToString()));
                    }
                }
            }
            catch (JsonException e)
            {
                throw new FormatException(setting.Key, e);
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
                while (reader.Read())
                {
                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        break;
                    }

                    string propertyName = reader.GetString();

                    switch (propertyName)
                    {
                        case FeatureManagementConstants.IdJsonPropertyName:
                            {
                                if (reader.Read() && reader.TokenType == JsonTokenType.Null)
                                {
                                    return null;
                                }
                                else if (reader.TokenType == JsonTokenType.String)
                                {
                                    featureFlag.Id = reader.GetString();
                                }
                                else
                                {
                                    throw CreateFeatureFlagFormatException(
                                        FeatureManagementConstants.IdJsonPropertyName,
                                        settingKey,
                                        reader.TokenType.ToString(),
                                        JsonTokenType.String.ToString());
                                }

                                continue;
                            }

                        case FeatureManagementConstants.EnabledJsonPropertyName:
                            {
                                if (reader.Read() && (reader.TokenType == JsonTokenType.False || reader.TokenType == JsonTokenType.True))
                                {
                                    featureFlag.Enabled = reader.GetBoolean();
                                }
                                else
                                {
                                    throw CreateFeatureFlagFormatException(
                                        FeatureManagementConstants.EnabledJsonPropertyName,
                                        settingKey,
                                        reader.TokenType.ToString(),
                                        $"{JsonTokenType.True}' or '{JsonTokenType.False}");
                                }

                                continue;
                            }

                        case FeatureManagementConstants.ConditionsJsonPropertyName:
                            {
                                if (reader.Read() && reader.TokenType == JsonTokenType.Null)
                                {
                                    continue;
                                }
                                else if (reader.TokenType == JsonTokenType.StartObject)
                                {
                                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                                    {
                                        if (reader.TokenType == JsonTokenType.PropertyName)
                                        {
                                            string conditionsPropertyName = reader.GetString();

                                            switch (conditionsPropertyName)
                                            {
                                                case FeatureManagementConstants.ClientFiltersJsonPropertyName:
                                                    {
                                                        if (reader.Read() && reader.TokenType == JsonTokenType.Null)
                                                        {
                                                            continue;
                                                        }
                                                        else if (reader.TokenType == JsonTokenType.StartArray)
                                                        {
                                                            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                                                            {
                                                                while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
                                                                {
                                                                    ClientFilter clientFilter = new ClientFilter();

                                                                    if (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
                                                                    {
                                                                        string clientFiltersPropertyName = reader.GetString();

                                                                        switch (clientFiltersPropertyName)
                                                                        {
                                                                            case FeatureManagementConstants.NameJsonPropertyName:
                                                                                {
                                                                                    if (reader.Read() && reader.TokenType == JsonTokenType.String)
                                                                                    {
                                                                                        clientFilter.Name = reader.GetString();
                                                                                    }
                                                                                    else
                                                                                    {
                                                                                        throw CreateFeatureFlagFormatException(
                                                                                            FeatureManagementConstants.NameJsonPropertyName,
                                                                                            settingKey,
                                                                                            reader.TokenType.ToString(),
                                                                                            JsonTokenType.String.ToString());
                                                                                    }

                                                                                    continue;
                                                                                }

                                                                            case FeatureManagementConstants.ParametersJsonPropertyName:
                                                                                {
                                                                                    if (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
                                                                                    {
                                                                                        
                                                                                    }
                                                                                    else
                                                                                    {
                                                                                        throw CreateFeatureFlagFormatException(
                                                                                            FeatureManagementConstants.ParametersJsonPropertyName,
                                                                                            settingKey,
                                                                                            reader.TokenType.ToString(),
                                                                                            JsonTokenType.StartObject.ToString());
                                                                                    }

                                                                                    continue;
                                                                                }

                                                                            default:
                                                                                continue;
                                                                        }
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

                                                        continue;
                                                    }

                                                case FeatureManagementConstants.RequirementTypeJsonPropertyName:
                                                    {
                                                        if (reader.Read() && reader.TokenType == JsonTokenType.String)
                                                        {
                                                            featureFlag.Conditions.RequirementType = reader.GetString();
                                                        }
                                                        else
                                                        {
                                                            throw CreateFeatureFlagFormatException(
                                                                FeatureManagementConstants.RequirementTypeJsonPropertyName,
                                                                settingKey,
                                                                reader.TokenType.ToString(),
                                                                JsonTokenType.String.ToString());
                                                        }

                                                        continue;
                                                    }

                                                default:
                                                    continue;
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    throw CreateFeatureFlagFormatException(
                                        FeatureManagementConstants.ConditionsJsonPropertyName,
                                        settingKey,
                                        reader.TokenType.ToString(),
                                        JsonTokenType.StartObject.ToString());
                                }

                                continue;
                            }

                        default:
                            continue;
                    }
                }
            }
            catch (JsonException e)
            {
                throw new FormatException(settingKey, e);
            }

            return featureFlag;
        }
    }
}
