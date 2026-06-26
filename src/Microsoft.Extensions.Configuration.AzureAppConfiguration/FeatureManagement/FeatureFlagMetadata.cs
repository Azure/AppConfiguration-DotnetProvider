// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    /// <summary>
    /// Identity information about a feature flag that is needed when emitting feature-management
    /// configuration key-values (in particular for telemetry metadata such as the feature flag
    /// reference and ETag). This decouples the emit logic from the source of the feature flag,
    /// allowing both classic feature flags (loaded as <see cref="Azure.Data.AppConfiguration.ConfigurationSetting"/>)
    /// and new feature flags (loaded from the standalone feature-flag endpoint) to share it.
    /// </summary>
    internal readonly struct FeatureFlagMetadata
    {
        public FeatureFlagMetadata(string key, string label, ETag etag)
        {
            Key = key;
            Label = label;
            ETag = etag;
        }

        /// <summary>
        /// The full key of the feature flag, including the ".appconfig.featureflag/" prefix.
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// The label of the feature flag.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// The ETag of the feature flag.
        /// </summary>
        public ETag ETag { get; }
    }
}
