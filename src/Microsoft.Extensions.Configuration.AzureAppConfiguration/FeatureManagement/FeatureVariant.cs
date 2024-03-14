// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class FeatureVariant
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("configuration_value")]
        public JsonElement ConfigurationValue { get; set; }

        [JsonPropertyName("configuration_reference")]
        public string ConfigurationReference { get; set; }

        [JsonPropertyName("status_override")]
        public string StatusOverride { get; set; }
    }
}
