// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.Extensions.Configuration.AzureAppConfiguration.SnapshotReference;

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
        public const string SnapshotReferenceInvalidJsonProperty = "Invalid snapshot reference format for key '{0}' (label: '{1}'). The '" + JsonFields.SnapshotName + "' property must be a string value, but found {2}.";
        public const string SnapshotReferenceNull = "Invalid snapshot reference format. The 'snapshot_name' property must not be null.";
        public const string SnapshotInvalidComposition = "{0} for the selected snapshot with name '{1}' must be 'key', found '{2}'.";
    }
}
