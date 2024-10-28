// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class FeatureFlag
    {
        public string Id { get; set; }

        public bool Enabled { get; set; }

        public FeatureConditions Conditions { get; set; }

        public IEnumerable<FeatureVariant> Variants { get; set; }

        public FeatureAllocation Allocation { get; set; }

        public FeatureTelemetry Telemetry { get; set; }
    }
}
