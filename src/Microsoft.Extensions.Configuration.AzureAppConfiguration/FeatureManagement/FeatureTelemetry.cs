// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class FeatureTelemetry
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("metadata")]
        public IReadOnlyDictionary<string, string> Metadata { get; set; }
    }
}
