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
        public const string Name = "Name";

        // Feature Flags
        public const string EnabledFor = "EnabledFor";
        public const string FeatureFlagContentType = "application/vnd.microsoft.appconfig.ff+json";
        public const string FeatureFlagsSectionName = "FeatureFlags";
        public const string Parameters = "Parameters";

        // Dynamic Features
        public const string Assigner = "Assigner";
        public const string AssignmentParameters = "AssignmentParameters";
        public const string ConfigurationReference = "ConfigurationReference";
        public const string Default = "Default";
        public const string DynamicFeatureContentType = "application/vnd.microsoft.appconfig.df+json";
        public const string DynamicFeaturesSectionName = "DynamicFeatures";
        public const string Variants = "Variants";
    }
}
