using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.AppConfiguration.Azconfig;
using Newtonsoft.Json;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class FeatureManagementKeyValueAdapter : IKeyValueAdapter
    {
        public IEnumerable<KeyValuePair<string, string>> GetKeyValues(IKeyValue keyValue)
        {
            if (keyValue == null ||
                !string.Equals(keyValue.ContentType.Replace(" ", string.Empty), FeatureManagementConstants.ContentType) ||
                !keyValue.Key.Contains(FeatureManagementConstants.FeatureFlagMarker))
            {
                return null;
            }

            //
            // TODO error handling
            FeatureFlag featureFlag = JsonConvert.DeserializeObject<FeatureFlag>(keyValue.Value);

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
                    const string EnabledFor = "EnabledFor";

                    //
                    // Conditionally on based on feature filters
                    for (int i = 0; i < featureFlag.Conditions.ClientFilters.Count; i++)
                    {
                        ClientFilter clientFilter = featureFlag.Conditions.ClientFilters[i];

                        keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.SectionName}:{featureFlag.Id}:{EnabledFor}:{i}:Name", clientFilter.Name));

                        if (clientFilter.Parameters != null)
                        {
                            foreach (KeyValuePair<string, string> kvp in new JsonFlattener().FlattenJson(clientFilter.Parameters))
                            {
                                keyValues.Add(new KeyValuePair<string, string>($"{FeatureManagementConstants.SectionName}:{featureFlag.Id}:{EnabledFor}:{i}:Parameters:{kvp.Key}", kvp.Value));
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
