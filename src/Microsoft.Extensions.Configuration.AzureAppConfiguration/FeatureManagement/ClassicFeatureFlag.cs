// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class ClassicFeatureFlag
    {
        public string Id { get; set; }

        public bool Enabled { get; set; }

        public ClassicFeatureConditions Conditions { get; set; }

        public IEnumerable<ClassicFeatureVariant> Variants { get; set; }

        public ClassicFeatureAllocation Allocation { get; set; }

        public ClassicFeatureTelemetry Telemetry { get; set; }
    }
}
