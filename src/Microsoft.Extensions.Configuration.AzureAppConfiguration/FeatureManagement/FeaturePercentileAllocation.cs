// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class FeaturePercentileAllocation
    {
        public string Variant { get; set; }

        public double From { get; set; }

        public double To { get; set; }
    }
}
