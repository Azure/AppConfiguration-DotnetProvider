// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class FeatureGroupAllocation
    {
        public string Variant { get; set; }

        public IEnumerable<string> Groups { get; set; }
    }
}
