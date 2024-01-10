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
    }
}
