using System;
using System.Diagnostics.Tracing;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    [EventSource(Name = EventSourceName)]
    internal class AzureAppConfigurationProviderRefreshEventSource : EventSource
    {
        /// <summary>The name to use for the event source.</summary>
        private const string EventSourceName = "Microsoft.Extensions.Configuration.AzureAppConfiguration.Refresh[0]";

        /// <summary>
        ///   Provides a singleton instance of the event source for callers to
        ///   use for logging.
        /// </summary>
        public static AzureAppConfigurationProviderRefreshEventSource Log { get; } = new AzureAppConfigurationProviderRefreshEventSource();

        [Event(1, Message = "\n{0}", Level = EventLevel.Verbose)]
        public void LogDebug(string message) { WriteEvent(1, message); }

        [Event(2, Message = "\n{0}", Level = EventLevel.Informational)]
        public void LogInformation(string message) { WriteEvent(2, message); }

        [Event(3, Message = "\n{0}", Level = EventLevel.Warning)]
        public void LogWarning(string message) { WriteEvent(3, message); }

        [NonEvent]
        public void LogWarning(Exception e, string message)
        {
            if (e != null)
            {
                LogWarning(message + " " + e.Message);
            }
            else
            {
                LogWarning(message);
            }
        }

        protected AzureAppConfigurationProviderRefreshEventSource()
           : base(
                EventSourceName,
                EventSourceSettings.Default,
                "AzureEventSource",
                "true")
        {
        }
    }
}
