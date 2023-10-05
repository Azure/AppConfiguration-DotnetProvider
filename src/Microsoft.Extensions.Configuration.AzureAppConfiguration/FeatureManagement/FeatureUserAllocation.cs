// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class FeatureUserAllocation
    {
        [JsonPropertyName("variant")]
        public string Variant { get; set; }

        [JsonPropertyName("users")]
        public List<string> Users { get; set; }
    }
}
