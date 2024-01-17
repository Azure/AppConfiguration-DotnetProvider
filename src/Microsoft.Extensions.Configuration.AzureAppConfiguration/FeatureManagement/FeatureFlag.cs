// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class FeatureFlag
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("conditions")]
        public FeatureConditions Conditions { get; set; }

        [JsonPropertyName("variants")]
        public IEnumerable<FeatureVariant> Variants { get; set; }

        [JsonPropertyName("allocation")]
        public FeatureAllocation Allocation { get; set; }

        [JsonPropertyName("telemetry")]
        public FeatureTelemetry Telemetry { get; set; }
    }
}
