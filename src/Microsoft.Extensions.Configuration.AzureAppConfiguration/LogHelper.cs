using Microsoft.Extensions.Logging;
using System;
using System.Text;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal static class LogHelper
    {
        public static string FormatLog(string loggingConstant, string changeType = null, string key = null, string label = LabelFilter.Null, string endpoint = null)
        {
            StringBuilder fullLogMessage = new StringBuilder();
            fullLogMessage.Append(loggingConstant);

            if (changeType != null)
            {
                fullLogMessage.Append($" Change:'{changeType}'");
            }

            if (key != null)
            {
                fullLogMessage.Append($" Key:'{key}'");
            }

            if (label != LabelFilter.Null)
            {
                fullLogMessage.Append($" Label:'{label}'");
            }

            if (endpoint != null)
            {
                fullLogMessage.Append($" Endpoint:'{endpoint.TrimEnd('/')}'");
            }

            return fullLogMessage.ToString();
        }

        public static void LogDebug(ILogger logger, string message)
        {
            if (logger != null)
            {
                logger.LogDebug(message);
            }
            else
            {
                AzureAppConfigurationProviderRefreshEventSource.Log.LogDebug(message);
            }
        }

        public static void LogInformation(ILogger logger, string message)
        {
            if (logger != null)
            {
                logger.LogInformation(message);
            }
            else
            {
                AzureAppConfigurationProviderRefreshEventSource.Log.LogInformation(message);
            }
        }

        public static void LogWarning(ILogger logger, string message, Exception e)
        {
            if (logger != null)
            {
                logger.LogWarning(message);
            }
            else
            {
                AzureAppConfigurationProviderRefreshEventSource.Log.LogWarning(message, e);
            }
        }
    }
}
