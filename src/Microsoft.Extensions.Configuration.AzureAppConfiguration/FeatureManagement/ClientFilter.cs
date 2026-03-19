// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System.Text.Json;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class ClientFilter
    {
        public string Name { get; set; } = string.Empty;

        public JsonElement Parameters { get; set; }
    }
}
