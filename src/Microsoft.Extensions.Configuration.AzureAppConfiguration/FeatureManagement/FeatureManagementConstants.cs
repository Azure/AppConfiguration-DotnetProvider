// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class FeatureManagementConstants
    {
        public const string FeatureFlagMarker = ".appconfig.featureflag/";
        public const string ContentType = "application/vnd.microsoft.appconfig.ff+json";

        // Feature management section keys
        public const string FeatureManagementSectionName = "feature_management";
        public const string FeatureFlagsSectionName = "feature_flags";

        // Feature flag properties
        public const string Id = "id";
        public const string Enabled = "enabled";
        public const string Conditions = "conditions";
        public const string ClientFilters = "client_filters";
        public const string Variants = "variants";
        public const string Allocation = "allocation";
        public const string UserAllocation = "user";
        public const string GroupAllocation = "group";
        public const string PercentileAllocation = "percentile";
        public const string Telemetry = "telemetry";
        public const string Metadata = "metadata";
        public const string RequirementType = "requirement_type";
        public const string Name = "name";
        public const string Parameters = "parameters";
        public const string Variant = "variant";
        public const string ConfigurationValue = "configuration_value";
        public const string ConfigurationReference = "configuration_reference";
        public const string StatusOverride = "status_override";
        public const string DefaultWhenDisabled = "default_when_disabled";
        public const string DefaultWhenEnabled = "default_when_enabled";
        public const string Users = "users";
        public const string Groups = "groups";
        public const string From = "from";
        public const string To = "to";
        public const string Seed = "seed";

        // Feature flag status values
        public const string Conditional = "Conditional";
        public const string Disabled = "Disabled";

        // Default filters
        public const string AlwaysOnFilter = "AlwaysOn";

        // Telemetry metadata keys
        public const string ETag = "ETag";
        public const string FeatureFlagId = "FeatureFlagId";
        public const string FeatureFlagReference = "FeatureFlagReference";

    }
}
