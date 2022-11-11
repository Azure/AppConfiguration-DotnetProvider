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

        [Event(1, Message = "Startup", Level = EventLevel.Informational)]
        public void Startup() { WriteEvent(1); }

        [Event(2, Message = "Key-Value retrieved from App Configuration. Modified: {0}. Key: {1}. Label: {2}.", Level = EventLevel.Verbose)]
        public void LogDebugKeyValue(bool modified, string key, string label) { WriteEvent(2, modified, key, label); }

        [Event(3, Message = "Secret loaded from KeyVault for key-value. Key: {0}. Label: {1}.", Level = EventLevel.Verbose)]
        public void LogDebugKeyVault(string key, string label) { WriteEvent(3, key, label); }

        [Event(4, Message = "Feature Flag retrieved from App Configuration. Key: {0}. Label: {1}.", Level = EventLevel.Verbose)]
        public void LogDebugFeatureFlag(string key, string label) { WriteEvent(4, key, label); }

        [Event(5, Message = "Configuration setting updated. Key: {0}. Endpoint: {1}.", Level = EventLevel.Informational)]
        public void LogInformation(string key, string endpoint) { WriteEvent(5, key, endpoint); }

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
