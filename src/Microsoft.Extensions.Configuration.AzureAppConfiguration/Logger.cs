using Microsoft.Extensions.Logging;

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
