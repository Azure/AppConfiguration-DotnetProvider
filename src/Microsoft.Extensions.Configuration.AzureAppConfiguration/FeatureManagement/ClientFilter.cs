// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class ClientFilter
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("parameters")]
        public JsonElement Parameters { get; set; }
    }
}