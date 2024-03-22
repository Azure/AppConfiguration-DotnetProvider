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
            FeatureFlag featureFlag;

            var keyValues = new List<KeyValuePair<string, string>>();

            try
            {
                using (JsonDocument document = JsonDocument.Parse(setting.Value))
                {
                    JsonElement root = document.RootElement;

                    featureFlag = new FeatureFlag();

                    if (root.TryGetProperty(FeatureManagementConstants.EnabledJsonPropertyName, out JsonElement enabledElement))
                    {
                        if (enabledElement.ValueKind == JsonValueKind.True)
                        {
                            featureFlag.Enabled = true;
                        }
                        else if (enabledElement.ValueKind == JsonValueKind.String && bool.TryParse(enabledElement.GetString(), out bool enabled))
                        {
                            featureFlag.Enabled = enabled;
                        }
                    }

                    if (root.TryGetProperty(FeatureManagementConstants.IdJsonPropertyName, out JsonElement idElement) && idElement.ValueKind == JsonValueKind.String)
                    {
                        featureFlag.Id = idElement.GetString();
                    }

                    if (root.TryGetProperty(FeatureManagementConstants.ConditionsJsonPropertyName, out JsonElement conditionsElement) && conditionsElement.ValueKind == JsonValueKind.Object)
                    {
                        featureFlag.Conditions = new FeatureConditions();

                        if (conditionsElement.TryGetProperty(FeatureManagementConstants.ClientFiltersJsonPropertyName, out JsonElement clientFiltersElement) && clientFiltersElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (JsonElement clientFilterElement in clientFiltersElement.EnumerateArray())
                            {
                                ClientFilter clientFilter = new ClientFilter();

                                if (clientFilterElement.TryGetProperty(FeatureManagementConstants.NameJsonPropertyName, out JsonElement nameElement) && nameElement.ValueKind == JsonValueKind.String)
                                {
                                    clientFilter.Name = nameElement.GetString();
                                }

                                if (clientFilterElement.TryGetProperty(FeatureManagementConstants.ParametersJsonPropertyName, out JsonElement parametersElement) && parametersElement.ValueKind == JsonValueKind.Object)
                                {
                                    clientFilter.Parameters = parametersElement;
                                }

                                featureFlag.Conditions.ClientFilters.Add(clientFilter);
                            }
                        }

                        if (conditionsElement.TryGetProperty(FeatureManagementConstants.RequirementTypeJsonPropertyName, out JsonElement requirementTypeElement) && requirementTypeElement.ValueKind == JsonValueKind.String)
                        {
                            featureFlag.Conditions.RequirementType = requirementTypeElement.GetString();
                        }
                    }

                    if (featureFlag.Enabled)
                    {
                        //if (featureFlag.Conditions?.ClientFilters == null)
                        if (featureFlag.Conditions?.ClientFilters == null || !featureFlag.Conditions.ClientFilters.Any()) // workaround since we are not yet setting client filters to null
                        {
                            //
                            // Always on
                            keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.SectionName}:{featureFlag.Id}", true.ToString()));
                        }
                        else
                        {
                            //
                            // Conditionally on based on feature filters
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
    }
}
