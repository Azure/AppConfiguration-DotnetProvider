// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class ClassicFeatureAllocation
    {
        public string DefaultWhenDisabled { get; set; }

        public string DefaultWhenEnabled { get; set; }

        public IEnumerable<ClassicFeatureUserAllocation> User { get; set; }

        public IEnumerable<ClassicFeatureGroupAllocation> Group { get; set; }

        public IEnumerable<ClassicFeaturePercentileAllocation> Percentile { get; set; }

        public string Seed { get; set; }
    }
}
