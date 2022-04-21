// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class FeatureManagementConstants
    {
        // Common
        public const string FeatureManagementSchemaEnvironmentVariable = "AZURE_APP_CONFIGURATION_FEATURE_MANAGEMENT_SCHEMA_VERSION";
        public const string FeatureManagementSchemaV1 = "1";
        public const string FeatureManagementSchemaV2 = "2";
        public const string FeatureManagementDefaultSchema = FeatureManagementSchemaV1;
        public const string FeatureManagementSectionName = "FeatureManagement";
        public const string FeatureFlagMarker = ".appconfig.featureflag/";

        // Feature Flags
        public const string FeatureFlagEnabledFor = "EnabledFor";
        public const string FeatureFlagContentType = "application/vnd.microsoft.appconfig.ff+json";
        public const string FeatureFlagSectionName = "FeatureFlags";
        public const string FeatureFlagParameters = "Parameters";
        public const string FeatureFlagFilterName = "Name";

        // Dynamic Features
        public const string DynamicFeatureAssigner = "Assigner";
        public const string DynamicFeatureAssignmentParameters = "AssignmentParameters";
        public const string DynamicFeatureConfigurationReference = "ConfigurationReference";
        public const string DynamicFeatureDefault = "Default";
        public const string DynamicFeatureContentType = "application/vnd.microsoft.appconfig.df+json";
        public const string DynamicFeatureSectionName = "DynamicFeatures";
        public const string DynamicFeatureVariants = "Variants";
        public const string DynamicFeatureVariantName = "Name";
    }
}
