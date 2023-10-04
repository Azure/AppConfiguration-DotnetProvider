// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class FeatureVariants
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("configuration_value")]
        public IConfigurationSection ConfigurationValue { get; set; }

        [JsonPropertyName("configuration_reference")]
        public string ConfigurationReference { get; set; }

        [JsonPropertyName("status_override")]
        public string StatusOverride { get; set; }
    }
}
