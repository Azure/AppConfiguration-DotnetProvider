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
        public const string RequirementType = "RequirementType";
        public const string EnabledJsonPropertyName = "enabled";
        public const string IdJsonPropertyName = "id";
        public const string ConditionsJsonPropertyName = "conditions";
        public const string RequirementTypeJsonPropertyName = "requirement_type";
        public const string ClientFiltersJsonPropertyName = "client_filters";
        public const string NameJsonPropertyName = "name";
        public const string ParametersJsonPropertyName = "parameters";
    }
}
