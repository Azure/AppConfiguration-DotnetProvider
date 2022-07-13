// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class FeatureManagementTelemetryHelper
    {
        // Built-in Feature Filter Names
        private static List<string> PercentageFilterNames = new List<string> { "Percentage", "Microsoft.Percentage", "PercentageFilter", "Microsoft.PercentageFilter" };
        private static List<string> TimeWindowFilterNames = new List<string> { "TimeWindow", "Microsoft.TimeWindow", "TimeWindowFilter", "Microsoft.TimeWindowFilter" };
        private static List<string> TargetingFilterNames = new List<string> { "Targeting", "Microsoft.Targeting", "TargetingFilter", "Microsoft.TargetingFilter" };

        public static FeatureFilterType GetFilterTypeFromName (string filterName)
        {
            if (PercentageFilterNames.Any(name => name == filterName))
                return FeatureFilterType.Percent;

            if (TimeWindowFilterNames.Any(name => name == filterName))
                return FeatureFilterType.Time;
           
            if (TargetingFilterNames.Any(name => name == filterName))
                return FeatureFilterType.Target;

            return FeatureFilterType.Custom;
        }
    }
}
