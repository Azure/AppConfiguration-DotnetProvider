// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class FeaturePercentileAllocation
    {
        [JsonPropertyName("variant")]
        public string Variant { get; set; }

        [JsonPropertyName("from")]
        public double From { get; set; }

        [JsonPropertyName("to")]
        public double To { get; set; }
    }
}
