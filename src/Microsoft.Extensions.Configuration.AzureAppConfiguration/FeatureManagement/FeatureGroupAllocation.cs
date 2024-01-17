// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class FeatureGroupAllocation
    {
        [JsonPropertyName("variant")]
        public string Variant { get; set; }

        [JsonPropertyName("groups")]
        public IEnumerable<string> Groups { get; set; }
    }
}
