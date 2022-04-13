// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class FeatureManagementConstants
    {
        // Common
        public const string FeatureFlagMarker = ".appconfig.featureflag/";
        public const string FeatureManagementSectionName = "FeatureManagement";

        // Feature Flags
        public const string FeatureFlagContentType = "application/vnd.microsoft.appconfig.ff+json";
        public const string FeatureFlagsSectionName = "FeatureFlags";
        public const string EnabledFor = "EnabledFor";

        // Dynamic Features
        public const string DynamicFeatureContentType = "application/vnd.microsoft.appconfig.df+json";
        public const string DynamicFeaturesSectionName = "DynamicFeatures";
    }
}
