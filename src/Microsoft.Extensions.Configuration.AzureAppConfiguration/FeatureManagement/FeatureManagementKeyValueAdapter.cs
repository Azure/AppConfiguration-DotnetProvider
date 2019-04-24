using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.AppConfiguration.Azconfig;
using Newtonsoft.Json;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class FeatureManagementKeyValueAdapter : IKeyValueAdapter
    {
        private static readonly JsonSerializerSettings s_SerializationSettings = new JsonSerializerSettings { DateParseHandling = DateParseHandling.None };

        public IEnumerable<KeyValuePair<string, string>> GetKeyValues(IKeyValue keyValue)
        {
            string contentType = keyValue?.ContentType?.Split(';')[0].Trim();

            if (!string.Equals(contentType, FeatureManagementConstants.ContentType) ||
                !keyValue.Key.Contains(FeatureManagementConstants.FeatureFlagMarker))
            {
                return null;
            }

            //
            // TODO error handling
            FeatureFlag featureFlag = JsonConvert.DeserializeObject<FeatureFlag>(keyValue.Value, s_SerializationSettings);

            var keyValues = new List<KeyValuePair<string, string>>();

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

                        keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.SectionName}:{featureFlag.Id}:{FeatureManagementConstants.EnabledFor}:{i}:Name", clientFilter.Name));

                        if (clientFilter.Parameters != null)
                        {
                            foreach (KeyValuePair<string, string> kvp in new JsonFlattener().FlattenJson(clientFilter.Parameters))
                            {
                                keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.SectionName}:{featureFlag.Id}:{FeatureManagementConstants.EnabledFor}:{i}:Parameters:{kvp.Key}", kvp.Value));
                            }
                        }
                    }
                }
            }
            else
            {
                keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.SectionName}:{featureFlag.Id}", false.ToString()));
            }

            return keyValues;
        }
    }
}
