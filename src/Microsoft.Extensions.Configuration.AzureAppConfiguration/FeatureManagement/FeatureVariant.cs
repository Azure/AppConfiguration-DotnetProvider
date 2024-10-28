// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System.Text.Json;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class FeatureVariant
    {
        public string Name { get; set; }

        public JsonElement ConfigurationValue { get; set; }

        public string ConfigurationReference { get; set; }

        public string StatusOverride { get; set; }
    }
}
