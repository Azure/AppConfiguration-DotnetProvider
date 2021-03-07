// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    /// <summary>
    /// Options used to configure the feature flags to be parsed.
    /// </summary>
    public class FeatureFlagOptions
    {
        /// <summary>
        /// The label that feature flags will be selected from.
        /// </summary>
        public string Label { get; set; } = LabelFilter.Null;

        /// <summary>
        /// The time after which the cached values of the feature flags expire.  Must be greater than or equal to 1 second.
        /// </summary>
        public TimeSpan CacheExpirationInterval { get; set; } = AzureAppConfigurationOptions.DefaultFeatureFlagsCacheExpirationInterval;

        /// <summary>
        /// Selectively load only those feature flags which begin with this prefix.
        /// The characters asterisk (*), comma (,) and backslash (\) are reserved and must be escaped using a backslash (\).
        /// </summary>
        public string Prefix { get; set; } = string.Empty;
    }
}
