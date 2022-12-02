using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class LoggingExtensions
    {
        public static string FormatLog(string loggingConstant, string changeType = null, string key = null, string label = LabelFilter.Null, string endpoint = null)
        {
            StringBuilder fullLogMessage = new StringBuilder();
            fullLogMessage.Append(loggingConstant);

            if (changeType != null)
            {
                fullLogMessage.Append($" Change: {changeType}.");
            }

            if (key != null)
            {
                fullLogMessage.Append($" Key: '{key}'.");
            }

            if (label != LabelFilter.Null)
            {
                fullLogMessage.Append($" Label: '{label}'.");
            }

            if (endpoint != null)
            {
                fullLogMessage.Append($" Endpoint: [ {endpoint} ].");
            }

            return fullLogMessage.ToString();
        }

        public static void HandleLog(ILogger logger, LogLevel logLevel, Exception e, string message)
        {
            if (logger != null)
            {
                switch (logLevel)
                {
                    case LogLevel.Debug:
                        logger.LogDebug(message);
                        break;
                    case LogLevel.Information:
                        logger.LogInformation(message);
                        break;
                    case LogLevel.Warning:
                        logger.LogWarning(e, message);
                        break;
                    default:
                        break;
                }
            }
            else
            {
                switch (logLevel)
                {
                    case LogLevel.Debug:
                        AzureAppConfigurationProviderEventSource.Log.LogDebug(message);
                        break;
                    case LogLevel.Information:
                        AzureAppConfigurationProviderEventSource.Log.LogInformation(message);
                        break;
                    case LogLevel.Warning:
                        AzureAppConfigurationProviderEventSource.Log.LogWarning(e, message);
                        break;
                    default:
                        break;
                }
            }
        }
    }
}
