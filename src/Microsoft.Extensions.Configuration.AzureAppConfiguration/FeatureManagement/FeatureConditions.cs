// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class FeatureConditions
    {
        [JsonPropertyName("client_filters")]
        public List<ClientFilter> ClientFilters { get; set; } = new List<ClientFilter>();
    }
}
