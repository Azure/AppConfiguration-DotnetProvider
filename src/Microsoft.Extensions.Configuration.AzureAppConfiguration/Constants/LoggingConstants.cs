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
        public const string RefreshFailedDueToKeyVaultError = "A refresh operation failed while resolving a Key Vault reference.";
        public const string RefreshFailedError = "A refresh operation failed.";
        public const string RefreshCanceledError = "A refresh operation was canceled.";

        // Successful update messages
        public const string RefreshConfigurationUpdatedSuccess = "Configuration reloaded for all selected key-values from endpoint: ";
        public const string RefreshKeyValueUnchanged = "No new changes for key-value: ";
        public const string RefreshKeyValueChanged = "Change detected for key-value ";
        public const string RefreshKeyVaultSecretUpdatedSuccess = "Key Vault secret updated from vault: ";
        public const string RefreshKeyVaultSecretChanged = "Key Vault secret changed: ";
        public const string RefreshFeatureFlagUpdatedSuccess = "Feature flags updated from Azure App Configuration endpoint: ";

        // Other
        public const string RefreshCanceledDueToNoAvailableEndpoints = "Skipping refresh operation because all AppConfig endpoints are backed off due to previous failures.";
    }
}
