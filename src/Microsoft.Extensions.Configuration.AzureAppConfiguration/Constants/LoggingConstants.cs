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
        public const string RefreshFailedDueToKeyVaultError = "A refresh operation failed while resolving a Key Vault reference.";
        public const string PushNotificationUnregisteredEndpoint = "Ignoring the push notification received for the unregistered endpoint";

        // Successful update, debug log level
        public const string RefreshKeyValueRead = "Key-value read from App Configuration.";
        public const string RefreshKeyVaultSecretRead = "Secret read from Key Vault for key-value.";
        public const string RefreshFeatureFlagRead = "Feature flag read from App Configuration.";
        public const string RefreshFeatureFlagsUnchanged = "Feature flags read from App Configuration. Change:'None'";

        // Successful update, information log level
        public const string RefreshConfigurationUpdatedSuccess = "Configuration reloaded.";
        public const string RefreshKeyValueSettingUpdated = "Setting updated.";
        public const string RefreshKeyVaultSettingUpdated = "Setting updated from Key Vault.";
        public const string RefreshFeatureFlagUpdated = "Feature flag updated.";

        // Other
        public const string RefreshSkippedNoClientAvailable = "Refresh skipped because no endpoint is accessible.";
    }
}
