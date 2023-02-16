// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal static class LogHelper
    {
        public static string BuildKeyValueReadMessage(KeyValueChangeType changeType, string key, string label, string endpoint)
        {
            return LoggingConstants.RefreshKeyValueRead + $" Change:'{changeType}' Key:'{key}' Label:'{label}' Endpoint:'{endpoint.TrimEnd('/')}'";
        }

        public static string BuildKeyValueSettingUpdatedMessage(string key)
        {
            return LoggingConstants.RefreshKeyValueSettingUpdated + $" Key:'{key}'";
        }

        public static string BuildConfigurationUpdatedMessage()
        {
            return LoggingConstants.RefreshConfigurationUpdatedSuccess;
        }

        public static string BuildFeatureFlagsUnchangedMessage(string endpoint)
        {
            return LoggingConstants.RefreshFeatureFlagsUnchanged + $" Endpoint:'{endpoint.TrimEnd('/')}'";
        }

        public static string BuildFeatureFlagReadMessage(string key, string label, string endpoint)
        {
            return LoggingConstants.RefreshFeatureFlagRead + $" Key:'{key}' Label:'{label}' Endpoint:'{endpoint.TrimEnd('/')}'";
        }

        public static string BuildFeatureFlagUpdatedMessage(string key)
        {
            return LoggingConstants.RefreshFeatureFlagUpdated + $" Key:'{key}'";
        }

        public static string BuildKeyVaultSecretReadMessage(string key, string label)
        {
            return LoggingConstants.RefreshKeyVaultSecretRead + $" Key:'{key}' Label:'{label}'";
        }

        public static string BuildKeyVaultSettingUpdatedMessage(string key)
        {
            return LoggingConstants.RefreshKeyVaultSettingUpdated + $" Key:'{key}'";
        }
    }
}
