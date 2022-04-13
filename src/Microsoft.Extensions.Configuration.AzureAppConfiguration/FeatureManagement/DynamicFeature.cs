// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class DynamicFeature
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("client_assigner")]
        public string ClientAssigner { get; set; }

        [JsonPropertyName("variants")]
        public List<FeatureVariant> Variants { get; set; } = new List<FeatureVariant>();

        // Reserved to allow variants list to be pulled from separate configuration.
        [JsonPropertyName("variants_reference")]
        public string VariantsReference { get; set; }
    }
}
