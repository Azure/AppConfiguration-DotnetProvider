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

        [JsonPropertyName("default")]
        public bool Default { get; set; }

        [JsonPropertyName("configuration_reference")]
        public string ConfigurationReference { get; set; }

        [JsonPropertyName("assignment_parameters")]
        public JsonElement AssignmentParameters { get; set; }
    }
}
