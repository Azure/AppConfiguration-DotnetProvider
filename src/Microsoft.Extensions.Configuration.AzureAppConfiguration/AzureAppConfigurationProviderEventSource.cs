using Azure.Core.Diagnostics;
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Text;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    [EventSource(Name = EventSourceName)]
    internal class AzureAppConfigurationProviderEventSource : EventSource
    {
        /// <summary>The name to use for the event source.</summary>
        private const string EventSourceName = "AzureAppConfigurationProvider";

        /// <summary>
        ///   Provides a singleton instance of the event source for callers to
        ///   use for logging.
        /// </summary>
        public static AzureAppConfigurationProviderEventSource Log { get; } = new AzureAppConfigurationProviderEventSource();

        [Event(1, Message = "{0}", Level = EventLevel.Verbose)]
        public void LogDebug(string message) { WriteEvent(1, message); }

        [Event(2, Message = "{0}", Level = EventLevel.Informational)]
        public void LogInformation(string message) { WriteEvent(2, message); }

        protected AzureAppConfigurationProviderEventSource()
           : base(
                EventSourceName,
                EventSourceSettings.Default,
                "AzureEventSource",
                "true")
        {
        }
    }
}
