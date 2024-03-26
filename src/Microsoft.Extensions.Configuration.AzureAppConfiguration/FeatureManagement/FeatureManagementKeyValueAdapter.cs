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
                                $"{JsonValueKind.String} or {JsonValueKind.True} or {JsonValueKind.False}");
                        }
                    }

                    string id = "";

                    if (root.TryGetProperty(FeatureManagementConstants.IdJsonPropertyName, out JsonElement idElement) && idElement.ValueKind == JsonValueKind.String)
                    {
                        if (idElement.ValueKind != JsonValueKind.String)
                        {
                            throw CreateFeatureFlagFormatException(
                                FeatureManagementConstants.IdJsonPropertyName,
                                setting.Key,
                                JsonValueKind.String.ToString());
                        }

                        id = idElement.GetString();
                    }

                    if (isFeatureEnabled)
                    {
                        if (!root.TryGetProperty(FeatureManagementConstants.ConditionsJsonPropertyName, out JsonElement conditionsElement) || 
                            !(conditionsElement.TryGetProperty(FeatureManagementConstants.ClientFiltersJsonPropertyName, out JsonElement clientFiltersElement) && clientFiltersElement.ValueKind == JsonValueKind.Array) ||
                            clientFiltersElement.GetArrayLength() == 0)
                        {
                            keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.SectionName}:{id}", true.ToString()));
                        }
                        else
                        {
                            if (conditionsElement.ValueKind != JsonValueKind.Object)
                            {
                                throw CreateFeatureFlagFormatException(
                                    FeatureManagementConstants.ConditionsJsonPropertyName,
                                    setting.Key,
                                    JsonValueKind.Object.ToString());
                            }

                            if (clientFiltersElement.ValueKind != JsonValueKind.Array)
                            {
                                throw CreateFeatureFlagFormatException(
                                    FeatureManagementConstants.ClientFiltersJsonPropertyName,
                                    setting.Key,
                                    JsonValueKind.Array.ToString());
                            }

                            for (int i = 0; i < clientFiltersElement.GetArrayLength(); i++)
                            {
                                JsonElement clientFilterElement = clientFiltersElement[i];

                                if (clientFilterElement.TryGetProperty(FeatureManagementConstants.NameJsonPropertyName, out JsonElement clientFilterNameElement) && clientFilterNameElement.ValueKind == JsonValueKind.String)
                                {
                                    if (clientFilterNameElement.ValueKind != JsonValueKind.String)
                                    {
                                        throw CreateFeatureFlagFormatException(
                                            FeatureManagementConstants.NameJsonPropertyName,
                                            setting.Key,
                                            JsonValueKind.String.ToString());
                                    }

                                    string clientFilterName = clientFilterNameElement.GetString();

                                    _featureFilterTracing.UpdateFeatureFilterTracing(clientFilterName);

                                    keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.SectionName}:{id}:{FeatureManagementConstants.EnabledFor}:{i}:{FeatureManagementConstants.Name}", clientFilterName));
                                }

                                if (clientFilterElement.TryGetProperty(FeatureManagementConstants.ParametersJsonPropertyName, out JsonElement parametersElement) && parametersElement.ValueKind == JsonValueKind.Object)
                                {
                                    if (parametersElement.ValueKind != JsonValueKind.Object)
                                    {
                                        throw CreateFeatureFlagFormatException(
                                            FeatureManagementConstants.ParametersJsonPropertyName,
                                            setting.Key,
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
                                if (requirementTypeElement.ValueKind != JsonValueKind.String)
                                {
                                    throw CreateFeatureFlagFormatException(
                                        FeatureManagementConstants.RequirementTypeJsonPropertyName,
                                        setting.Key,
                                        JsonValueKind.String.ToString());
                                }

                                keyValues.Add(new KeyValuePair<string, string>(
                                    $"{FeatureManagementConstants.SectionName}:{id}:{FeatureManagementConstants.RequirementType}",
                                    requirementTypeElement.GetString()));
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

        private FormatException CreateFeatureFlagFormatException(string jsonPropertyName, string settingKey, string jsonValueKind)
        {
            return new FormatException(string.Format(
                ErrorMessages.FeatureFlagInvalidJsonProperty,
                jsonPropertyName,
                settingKey,
                jsonValueKind));
        }
    }
}
