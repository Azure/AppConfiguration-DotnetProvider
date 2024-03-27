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
            var keyValues = new List<KeyValuePair<string, string>>();

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
                                $"{JsonValueKind.True} or {JsonValueKind.False}");
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
                                JsonElement clientFilterElement = clientFiltersElement[i];

                                if (clientFilterElement.TryGetProperty(FeatureManagementConstants.NameJsonPropertyName, out JsonElement clientFilterNameElement))
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

                                if (clientFilterElement.TryGetProperty(FeatureManagementConstants.ParametersJsonPropertyName, out JsonElement parametersElement))
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
    }
}
