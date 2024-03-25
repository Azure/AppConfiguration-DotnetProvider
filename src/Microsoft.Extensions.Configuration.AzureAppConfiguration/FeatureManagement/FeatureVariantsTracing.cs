// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using System.Text;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    /// <summary>
    /// Tracing for tracking feature variants usage.
    /// </summary>
    internal class FeatureVariantsTracing
    {
        private const string DefaultWhenDisabled = "DEFD";
        private const string DefaultWhenEnabled = "DEFE";
        private const string UserAllocation = "USR";
        private const string GroupAllocation = "GRP";
        private const string PercentileAllocation = "PRCT";
        private const string Seed = "SEED";
        private const string Delimiter = "+";

        public bool UsesDefaultWhenDisabled { get; set; } = false;
        public bool UsesDefaultWhenEnabled { get; set; } = false;
        public bool UsesUserAllocation { get; set; } = false;
        public bool UsesGroupAllocation { get; set; } = false;
        public bool UsesPercentileAllocation { get; set; } = false;
        public bool UsesSeed { get; set; } = false;

        public bool UsesAnyVariants()
        {
            return UsesDefaultWhenDisabled || UsesDefaultWhenEnabled || UsesUserAllocation || UsesGroupAllocation || UsesPercentileAllocation || UsesSeed;
        }

        public void ResetFeatureVariantsTracing()
        {
            UsesDefaultWhenDisabled = false;
            UsesDefaultWhenEnabled = false;
            UsesUserAllocation = false;
            UsesGroupAllocation = false;
            UsesPercentileAllocation = false;
            UsesSeed = false;
        }

        /// <summary>
        /// Returns a formatted string containing code names, indicating which feature filters are used by the application.
        /// </summary>
        /// <returns>Formatted string like: "CSTM+PRCNT+TIME+TRGT", "PRCNT+TRGT", etc. If no filters are used, empty string will be returned.</returns>
        public override string ToString()
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
