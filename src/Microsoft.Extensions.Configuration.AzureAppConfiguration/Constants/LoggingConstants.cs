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
        public const string SuccessfulConfigurationUpdated = "IConfiguration updated.";
        public const string SuccessfulKeyValueDeleted = "Key-Value deleted: ";
        public const string SuccessfulKeyValueModified = "Key-Value modified: ";
        public const string SuccessfulKeyVaultSecretUpdated = "KeyVault secret updated: ";
        public const string SuccessfulFeatureFlagDeleted = "Feature flag deleted: ";
        public const string SuccessfulFeatureFlagModified = "Feature flag modified: ";
    }
}
