// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class FeatureManagementConstants
    {
        public const string FeatureFlagMarker = ".appconfig.featureflag/";
        public const string ContentType = "application/vnd.microsoft.appconfig.ff+json";
        public const string SectionName = "FeatureManagement";
        public const string EnabledFor = "EnabledFor";
        public const string Variants = "Variants";
        public const string Allocation = "Allocation";
        public const string User = "User";
        public const string Group = "Group";
        public const string Percentile = "Percentile";
        public const string Telemetry = "Telemetry";
        public const string Enabled = "Enabled";
        public const string Metadata = "Metadata";
        public const string RequirementType = "RequirementType";
        public const string Name = "Name";
        public const string Parameters = "Parameters";
        public const string Variant = "Variant";
        public const string ConfigurationValue = "ConfigurationValue";
        public const string ConfigurationReference = "ConfigurationReference";
        public const string StatusOverride = "StatusOverride";
        public const string DefaultWhenDisabled = "DefaultWhenDisabled";
        public const string DefaultWhenEnabled = "DefaultWhenEnabled";
        public const string Users = "Users";
        public const string Groups = "Groups";
        public const string From = "From";
        public const string To = "To";
        public const string Seed = "Seed";
        public const string ETag = "ETag";
        public const string FeatureFlagId = "FeatureFlagId";
        public const string FeatureFlagReference = "FeatureFlagReference";
        public const string Status = "Status";
        public const string AlwaysOnFilter = "AlwaysOn";
        public const string Conditional = "Conditional";
        public const string Disabled = "Disabled";
    }
}
