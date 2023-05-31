// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Core.Diagnostics;
using System.Diagnostics.Tracing;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    [EventSource(Name = EventSourceName)]
    internal sealed class AzureAppConfigurationProviderRefreshEventSource : EventSource
    {
        /// <summary>The name to use for the event source.</summary>
        private const string EventSourceName = "Microsoft-Extensions-Configuration-AzureAppConfiguration-Refresh";

        private const int LogDebugEvent = 1;
        private const int LogInformationEvent = 2;
        private const int LogWarningEvent = 3;

        private AzureAppConfigurationProviderRefreshEventSource()
            : base(
                EventSourceName,
                EventSourceSettings.Default,
                AzureEventSourceListener.TraitName,
                AzureEventSourceListener.TraitValue)
        {
        }

        /// <summary>
        ///   Provides a singleton instance of the event source for callers to
        ///   use for logging.
        /// </summary>
        public static AzureAppConfigurationProviderRefreshEventSource Log { get; } = new AzureAppConfigurationProviderRefreshEventSource();

        [Event(LogDebugEvent, Message = "{0}", Level = EventLevel.Verbose)]
        public void LogDebug(string message)
        {
            WriteEvent(LogDebugEvent, message);
        }

        [Event(LogInformationEvent, Message = "{0}", Level = EventLevel.Informational)]
        public void LogInformation(string message)
        {
            WriteEvent(LogInformationEvent, message);
        }

        [Event(LogWarningEvent, Message = "{0}", Level = EventLevel.Warning)]
        public void LogWarning(string message)
        {
            WriteEvent(LogWarningEvent, message);
        }
    }
}
