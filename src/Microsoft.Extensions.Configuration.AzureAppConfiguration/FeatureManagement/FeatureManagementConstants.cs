namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class FeatureManagementConstants
    {
        public const string FeatureFlagMarker = ".appconfig.featureflag/";
        public const string ContentType = "application/vnd.microsoft.appconfig.ff+json; charset=utf-8";
        public const string SectionName = "FeatureManagement";
    }
}
