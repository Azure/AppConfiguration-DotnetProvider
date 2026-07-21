// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class ClassicFeatureUserAllocation
    {
        public string Variant { get; set; }

        public IEnumerable<string> Users { get; set; }
    }
}
