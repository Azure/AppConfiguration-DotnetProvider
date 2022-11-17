// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class LoggingConstants
    {
        // Categories
        public const string AppConfigRefreshLogCategory = "Microsoft.Extensions.Configuration.AzureAppConfiguration.Refresh";

        // Error messages
        public const string RefreshFailedDueToAuthenticationError = "A refresh operation failed due to an authentication error.";
        public const string RefreshFailedError = "A refresh operation failed.";
        public const string RefreshCanceledError = "A refresh operation was canceled.";

        // Successful update messages
        public const string RefreshConfigurationUpdatedSuccess = "Configuration reloaded for all selected key-values.";
        public const string RefreshKeyValueLoaded = "Key-value loaded from App Configuration.";
        public const string RefreshKeyValueSettingUpdated = "Value updated.";
        public const string RefreshKeyVaultSecretLoaded = "Secret loaded from Key Vault for key-value.";
        public const string RefreshKeyVaultSettingUpdated = "Value updated from Key Vault.";
        public const string RefreshFeatureFlagLoaded = "Feature flag loaded from App Configuration.";
        public const string RefreshFeatureFlagsUnchanged = "No change detected for feature flags.";
        public const string RefreshFeatureFlagValueUpdated = "Value updated for feature flag.";

        // Other
        public const string RefreshSkippedNoClientAvailable = "Refresh skipped because no endpoint is accessible.";
    }
}
