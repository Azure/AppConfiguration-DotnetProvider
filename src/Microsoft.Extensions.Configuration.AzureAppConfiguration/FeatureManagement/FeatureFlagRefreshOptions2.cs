// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    /// <summary>
    /// Options used to configure refresh for feature flags loaded via
    /// <see cref="AzureAppConfigurationOptions.ConfigureFeatureFlags"/>.
    /// </summary>
    /// <remarks>
    /// Draft name. Will be renamed prior to GA of the new feature flag experience.
    /// </remarks>
    public class FeatureFlagRefreshOptions2
    {
        /// <summary>
        /// Whether feature flag refresh is enabled. Defaults to <c>false</c>; the new API requires
        /// the caller to opt in to background refresh explicitly.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// The minimum time interval between consecutive refresh operations for feature flags.
        /// Must be greater than or equal to <see cref="RefreshConstants.MinimumFeatureFlagRefreshInterval"/>.
        /// Defaults to <see cref="RefreshConstants.DefaultFeatureFlagRefreshInterval"/>.
        /// </summary>
        public TimeSpan RefreshInterval { get; set; } = RefreshConstants.DefaultFeatureFlagRefreshInterval;
    }
}
