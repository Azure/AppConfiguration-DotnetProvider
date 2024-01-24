// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class FeatureAllocation
    {
        [JsonPropertyName("default_when_disabled")]
        public string DefaultWhenDisabled { get; set; }

        [JsonPropertyName("default_when_enabled")]
        public string DefaultWhenEnabled { get; set; }

        [JsonPropertyName("user")]
        public IEnumerable<FeatureUserAllocation> User { get; set; }

        [JsonPropertyName("group")]
        public IEnumerable<FeatureGroupAllocation> Group { get; set; }

        [JsonPropertyName("percentile")]
        public IEnumerable<FeaturePercentileAllocation> Percentile { get; set; }

        [JsonPropertyName("seed")]
        public string Seed { get; set; }
    }
}
