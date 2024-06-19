﻿// Copyright (c) Microsoft Corporation.
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
        private const string Delimiter = "+";

        // Built-in Feature Filter Names
        private readonly List<string> PercentageFilterNames = new List<string> { "Percentage", "Microsoft.Percentage", "PercentageFilter", "Microsoft.PercentageFilter" };
        private readonly List<string> TimeWindowFilterNames = new List<string> { "TimeWindow", "Microsoft.TimeWindow", "TimeWindowFilter", "Microsoft.TimeWindowFilter" };
        private readonly List<string> TargetingFilterNames = new List<string> { "Targeting", "Microsoft.Targeting", "TargetingFilter", "Microsoft.TargetingFilter" };

        public bool UsesCustomFilter { get; set; } = false;
        public bool UsesPercentageFilter { get; set; } = false;
        public bool UsesTimeWindowFilter { get; set; } = false;
        public bool UsesTargetingFilter { get; set; } = false;
        public bool UsesSeed { get; set; } = false;
        public bool IsTelemetryEnabled { get; set; } = false;
        public int HighestVariants { get; set; }

        public bool UsesAnyFeatureFilter()
        {
            return UsesCustomFilter || UsesPercentageFilter || UsesTimeWindowFilter || UsesTargetingFilter;
        }

        public void ResetFeatureFlagTracing()
        {
            UsesCustomFilter = false;
            UsesPercentageFilter = false;
            UsesTimeWindowFilter = false;
            UsesTargetingFilter = false;
            UsesSeed = false;
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

        public void UpdateHighestVariants(int currentFlagTotalVariants)
        {
            if (currentFlagTotalVariants > HighestVariants)
            {
                HighestVariants = currentFlagTotalVariants;
            }
        }

        /// <summary>
        /// Returns a formatted string containing code names, indicating which feature filters are used by the application.
        /// </summary>
        /// <returns>Formatted string like: "CSTM+PRCNT+TIME+TRGT", "PRCNT+TRGT", etc. If no filters are used, empty string will be returned.</returns>
        public string CreateFiltersString()
        {
            if (!UsesAnyFeatureFilter())
            {
                return string.Empty;
            }

            var sb = new StringBuilder();

            if (UsesCustomFilter)
            {
                sb.Append(CustomFilter);
            }

            if (UsesPercentageFilter)
            {
                if (sb.Length > 0)
                {
                    sb.Append(Delimiter);
                }

                sb.Append(PercentageFilter);
            }

            if (UsesTimeWindowFilter)
            {
                if (sb.Length > 0)
                {
                    sb.Append(Delimiter);
                }

                sb.Append(TimeWindowFilter);
            }

            if (UsesTargetingFilter)
            {
                if (sb.Length > 0)
                {
                    sb.Append(Delimiter);
                }

                sb.Append(TargetingFilter);
            }

            return sb.ToString();
        }
    }
}
