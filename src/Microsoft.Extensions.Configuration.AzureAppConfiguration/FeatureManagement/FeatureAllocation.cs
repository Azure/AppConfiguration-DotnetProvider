// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class FeatureAllocation
    {
        public string DefaultWhenDisabled { get; set; }

        public string DefaultWhenEnabled { get; set; }

        public IEnumerable<FeatureUserAllocation> User { get; set; }

        public IEnumerable<FeatureGroupAllocation> Group { get; set; }

        public IEnumerable<FeaturePercentileAllocation> Percentile { get; set; }

        public string Seed { get; set; }
    }
}
