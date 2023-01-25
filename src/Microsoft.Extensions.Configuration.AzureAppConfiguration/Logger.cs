﻿using Microsoft.Extensions.Logging;
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class Logger
    {
        ILogger _logger;

        public Logger() { }

        public Logger(ILogger logger)
        {
            _logger = logger;
        }

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

        public void LogDebug(string message)
        {
            if (_logger != null)
            {
                _logger.LogDebug(message);
            }
            else
            {
                AzureAppConfigurationProviderRefreshEventSource.Log.LogDebug(message);
            }
        }

        public void LogInformation(string message)
        {
            if (_logger != null)
            {
                _logger.LogInformation(message);
            }
            else
            {
                AzureAppConfigurationProviderRefreshEventSource.Log.LogInformation(message);
            }
        }

        public void LogWarning(string message)
        {
            if (_logger != null)
            {
                _logger.LogWarning(message);
            }
            else
            {
                AzureAppConfigurationProviderRefreshEventSource.Log.LogWarning(message);
            }
        }
    }
}
