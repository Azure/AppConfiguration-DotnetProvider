using System;
using System.Diagnostics.Tracing;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    [EventSource(Name = EventSourceName)]
    internal class AzureAppConfigurationProviderRefreshEventSource : EventSource
    {
        /// <summary>The name to use for the event source.</summary>
        private const string EventSourceName = "Microsoft-Extensions-Configuration-AzureAppConfiguration-Refresh";

        private const int LogDebugEvent = 1;
        private const int LogInformationEvent = 2;
        private const int LogWarningEvent = 3;

        /// <summary>
        ///   Provides a singleton instance of the event source for callers to
        ///   use for logging.
        /// </summary>
        public static AzureAppConfigurationProviderRefreshEventSource Log { get; } = new AzureAppConfigurationProviderRefreshEventSource();

        [Event(LogDebugEvent, Message = "{0}", Level = EventLevel.Verbose)]
        public void LogDebug(string message)
        {
            WriteEvent(1, message);
        }

        [Event(LogInformationEvent, Message = "{0}", Level = EventLevel.Informational)]
        public void LogInformation(string message)
        {
            WriteEvent(2, message);
        }

        [Event(LogWarningEvent, Message = "{0}", Level = EventLevel.Warning)]
        public void LogWarning(string message, Exception e)
        {
            if (e != null)
            {
                message += " " + e.Message;
            }
            WriteEvent(3, message);
        }

        private AzureAppConfigurationProviderRefreshEventSource()
           : base(
                EventSourceName,
                EventSourceSettings.Default,
                "AzureEventSource",
                "true")
        {
        }
    }
}
