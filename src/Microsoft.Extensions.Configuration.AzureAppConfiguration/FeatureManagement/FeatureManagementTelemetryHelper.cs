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

        public static void UpdateFilterTypesTelemetry(string filterName, FeatureFilterTypes filterTypes)
        {
            if (PercentageFilterNames.Any(name => name == filterName))
            { 
                filterTypes.UsesPercentageFilter = true;
            }
            else if (TimeWindowFilterNames.Any(name => name == filterName))
            {
                filterTypes.UsesTimeWindowFilter = true;
            }
            else if (TargetingFilterNames.Any(name => name == filterName))
            {
                filterTypes.UsesTargetingFilter = true;
            }
            else
            {
                filterTypes.UsesCustomFilter = true;
            }
        }
    }
}
