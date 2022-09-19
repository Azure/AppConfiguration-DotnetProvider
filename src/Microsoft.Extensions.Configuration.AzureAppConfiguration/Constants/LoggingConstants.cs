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
        public const string RefreshConfigurationUpdatedSuccess = "All configuration updated from Azure App Configuration endpoint: ";
        public const string RefreshKeyValueUpdatedSuccess = "Key-values updated from Azure App Configuration endpoint: ";
        public const string RefreshKeyValueUnchanged = "Key-value unchanged: ";
        public const string RefreshKeyValueChanged = "Key-value changed: ";
        public const string RefreshKeyVaultSecretUpdatedSuccess = "Key Vault secret updated from vault: ";
        public const string RefreshFeatureFlagUpdatedSuccess = "Feature flags updated from Azure App Configuration endpoint: ";

        // add endpoints to all constants, just success for now
    }
}
