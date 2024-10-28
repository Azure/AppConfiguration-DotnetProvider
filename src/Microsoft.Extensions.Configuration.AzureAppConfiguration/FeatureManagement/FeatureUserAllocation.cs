// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class FeatureUserAllocation
    {
        public string Variant { get; set; }

        public IEnumerable<string> Users { get; set; }
    }
}
