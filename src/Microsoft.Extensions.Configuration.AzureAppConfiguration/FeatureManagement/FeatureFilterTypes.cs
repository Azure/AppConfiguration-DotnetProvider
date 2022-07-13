// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using System.Text;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    /// <summary>
    /// Types of built-in feature filters.
    /// </summary>
    internal class FeatureFilterTypes
    {
        private const string CustomFilter = "Custom";
        private const string PercentageFilter = "Percentage";
        private const string TimeWindowFilter = "Time";
        private const string TargetingFilter = "Targeting";
        private const string FilterTypeDelimiter = "+";

        public bool UsesCustomFilter { get; set; } = false;
        public bool UsesPercentageFilter { get; set; } = false;
        public bool UsesTimeWindowFilter { get; set; } = false;
        public bool UsesTargetingFilter { get; set; } = false;
       
        public bool UsesAnyFeatureFilter()
        {
            return UsesCustomFilter || UsesPercentageFilter || UsesTimeWindowFilter || UsesTargetingFilter;
        }

        public void ResetFeatureFilters()
        {
            UsesCustomFilter = false;
            UsesPercentageFilter = false;
            UsesTimeWindowFilter = false;
            UsesTargetingFilter = false;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();

            if (UsesCustomFilter)
            {
                sb.Append(CustomFilter);
            }

            if (UsesPercentageFilter)
            {
                if (sb.Length > 0)
                {
                    sb.Append(FilterTypeDelimiter);
                }

                sb.Append(PercentageFilter);
            }

            if (UsesTimeWindowFilter)
            {
                if (sb.Length > 0)
                {
                    sb.Append(FilterTypeDelimiter);
                }

                sb.Append(TimeWindowFilter);
            }

            if (UsesTargetingFilter)
            {
                if (sb.Length > 0)
                {
                    sb.Append(FilterTypeDelimiter);
                }

                sb.Append(TargetingFilter);
            }

            return sb.ToString();
        }
    }
}
