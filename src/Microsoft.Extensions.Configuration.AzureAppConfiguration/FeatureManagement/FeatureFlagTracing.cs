// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    /// <summary>
    /// Tracing for tracking built-in feature filter usage.
    /// </summary>
    internal class FeatureFlagTracing
    {
        private const string CustomFilter = "CSTM";
        private const string PercentageFilter = "PRCNT";
        private const string TimeWindowFilter = "TIME";
        private const string TargetingFilter = "TRGT";

        // Built-in Feature Filter Names
        private readonly List<string> PercentageFilterNames = new List<string> { "Percentage", "Microsoft.Percentage", "PercentageFilter", "Microsoft.PercentageFilter" };
        private readonly List<string> TimeWindowFilterNames = new List<string> { "TimeWindow", "Microsoft.TimeWindow", "TimeWindowFilter", "Microsoft.TimeWindowFilter" };
        private readonly List<string> TargetingFilterNames = new List<string> { "Targeting", "Microsoft.Targeting", "TargetingFilter", "Microsoft.TargetingFilter" };

        public bool UsesCustomFilter { get; set; } = false;
        public bool UsesPercentageFilter { get; set; } = false;
        public bool UsesTimeWindowFilter { get; set; } = false;
        public bool UsesTargetingFilter { get; set; } = false;
        public bool UsesSeed { get; set; } = false;
        public bool UsesTelemetry { get; set; } = false;
        public bool UsesVariantConfigurationReference { get; set; } = false;
        public int MaxVariants { get; set; }

        public bool UsesAnyFeatureFilter()
        {
            return UsesCustomFilter || UsesPercentageFilter || UsesTimeWindowFilter || UsesTargetingFilter;
        }

        public bool UsesAnyTracingFeature()
        {
            return UsesSeed || UsesTelemetry || UsesVariantConfigurationReference;
        }

        public void ResetFeatureFlagTracing()
        {
            UsesCustomFilter = false;
            UsesPercentageFilter = false;
            UsesTimeWindowFilter = false;
            UsesTargetingFilter = false;
            UsesSeed = false;
            UsesTelemetry = false;
            UsesVariantConfigurationReference = false;
            MaxVariants = 0;
        }

        public void UpdateFeatureFilterTracing(string filterName)
        {
            if (PercentageFilterNames.Any(name => string.Equals(name, filterName, StringComparison.OrdinalIgnoreCase)))
            {
                UsesPercentageFilter = true;
            }
            else if (TimeWindowFilterNames.Any(name => string.Equals(name, filterName, StringComparison.OrdinalIgnoreCase)))
            {
                UsesTimeWindowFilter = true;
            }
            else if (TargetingFilterNames.Any(name => string.Equals(name, filterName, StringComparison.OrdinalIgnoreCase)))
            {
                UsesTargetingFilter = true;
            }
            else
            {
                UsesCustomFilter = true;
            }
        }

        public void NotifyMaxVariants(int currentFlagTotalVariants)
        {
            if (currentFlagTotalVariants > MaxVariants)
            {
                MaxVariants = currentFlagTotalVariants;
            }
        }
    }
}
