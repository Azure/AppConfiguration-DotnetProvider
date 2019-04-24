using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    public class FeatureFlagOptions
    {
        /// <summary>
        /// The label that feature flags will be selected from.
        /// </summary>
        public string Label { get; set; } = LabelFilter.Null;

        /// <summary>
        /// Interval used to check if any feature flags have been changed.
        /// </summary>
        public TimeSpan? PollInterval { get; set; } = AzureAppConfigurationOptions.DefaultPollInterval;
    }
}
