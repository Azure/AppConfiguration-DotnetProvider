// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    /// <summary>
    /// Options used to configure the feature flags to be parsed.
    /// </summary>
    public class FeatureFlagOptions
    {
        internal List<string> FeatureFlagKeyFilters { get; } = new List<string>();
        internal IList<string> RefreshRegistrationKeys = new List<string>();

        /// <summary>
        /// The label that feature flags will be selected from.
        /// </summary>
        public string Label { get; set; } = LabelFilter.Null;

        /// <summary>
        /// The time after which the cached values of the feature flags expire.  Must be greater than or equal to 1 second.
        /// </summary>
        public TimeSpan CacheExpirationInterval { get; set; } = AzureAppConfigurationOptions.DefaultFeatureFlagsCacheExpirationInterval;

        /// <summary>
        /// Specify what feature flags to include in the configuration provider.
        /// <see cref="Select"/> can be called multiple times to include multiple sets of key-values.
        /// If <see cref="Select"/> is not called, all the feature flags found are included.
        /// </summary>
        /// <param name="featureFlagKeyFilter">
        /// The key filter to apply when querying Azure App Configuration for key-values.
        /// The characters asterisk (*), comma (,) and backslash (\) are reserved and must be escaped using a backslash (\).
        /// Built-in key filter options: <see cref="KeyFilter"/>.
        /// </param>
        public FeatureFlagOptions Select(string featureFlagKeyFilter)
        {
            FeatureFlagKeyFilters.Add(featureFlagKeyFilter);
            return this;
        }

        /// <summary>
        /// Register the specified key that will refresh all feature flags being used by the configuration provider when the configuration provider's <see cref="IConfigurationRefresher"/> triggers a refresh.
        /// The <see cref="IConfigurationRefresher"/> instance can be obtained by calling <see cref="AzureAppConfigurationOptions.GetRefresher()"/>.
        /// </summary>
        /// <param name="key">Key of the key-value.</param>
        public FeatureFlagOptions RegisterRefreshKey(string key)
        {
            RefreshRegistrationKeys.Add(key);
            return this;
        }
    }
}
