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
        private const string PercentileAllocation = "PRCNT";
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
        /// Returns a formatted string containing code names, indicating which allocation methods are used by the application.
        /// </summary>
        /// <returns>Formatted string like: "DEFD+DEFE+USR+GRP", "DEFD+PRCNT", etc. If no allocations are used, empty string will be returned.</returns>
        public override string ToString()
        {
            if (!UsesAnyVariants())
            {
                return string.Empty;
            }

            var sb = new StringBuilder();

            if (UsesDefaultWhenDisabled)
            {
                sb.Append(DefaultWhenDisabled);
            }

            if (UsesDefaultWhenEnabled)
            {
                if (sb.Length > 0)
                {
                    sb.Append(Delimiter);
                }

                sb.Append(DefaultWhenEnabled);
            }

            if (UsesUserAllocation)
            {
                if (sb.Length > 0)
                {
                    sb.Append(Delimiter);
                }

                sb.Append(UserAllocation);
            }

            if (UsesGroupAllocation)
            {
                if (sb.Length > 0)
                {
                    sb.Append(Delimiter);
                }

                sb.Append(GroupAllocation);
            }

            if (UsesPercentileAllocation)
            {
                if (sb.Length > 0)
                {
                    sb.Append(Delimiter);
                }

                sb.Append(PercentileAllocation);
            }

            if (UsesSeed)
            {
                if (sb.Length > 0)
                {
                    sb.Append(Delimiter);
                }

                sb.Append(Seed);
            }

            return sb.ToString();
        }
    }
}
