// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class ErrorMessages
    {
        public const string RefreshIntervalTooShort = "The refresh interval cannot be less than {0} milliseconds.";
        public const string SecretRefreshIntervalTooShort = "The secret refresh interval cannot be less than {0} milliseconds.";
        public const string FeatureFlagInvalidJsonProperty = "Invalid property '{0}' for feature flag. Key: '{1}'. Found type: '{2}'. Expected type: '{3}'.";
        public const string FeatureFlagInvalidFormat = "Invalid json format for feature flag. Key: '{0}'.";
        public const string InvalidKeyVaultReference = "Invalid Key Vault reference.";
        public const string SnapshotReferenceInvalidFormat = "Invalid snapshot reference format for key '{0}' (label: '{1}').";
        public const string SnapshotReferenceInvalidJsonProperty = "Invalid snapshot reference format for key '{0}' (label: '{1}'). The '{2}' property must be a string value, but found {3}.";
        public const string SnapshotReferencePropertyMissing = "Invalid snapshot reference format for key '{0}' (label: '{1}'). The '{2}' property is required.";
        public const string SnapshotInvalidComposition = "{0} for the selected snapshot with name '{1}' must be 'key', found '{2}'.";
        public const string ConnectionConflict = "Cannot connect to both Azure App Configuration and Azure Front Door at the same time.";
        public const string AfdConnectionConflict = "Cannot connect to multiple Azure Front Doors.";
        public const string AfdLoadBalancingUnsupported = "Load balancing is not supported when connecting to Azure Front Door. For guidance on how to take advantage of geo-replication when Azure Front Door is used, visit https://aka.ms/appconfig/geo-replication-with-afd";
        public const string AfdCustomClientFactoryUnsupported = "Custom client factory is not supported when connecting to Azure Front Door.";
    }
}
