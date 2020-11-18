// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal static class TracingUtils
    {
        public static HostType GetHostType()
        {
            HostType hostType = HostType.Unidentified;

            try
            {
                if (Environment.GetEnvironmentVariable(RequestTracingConstants.AzureFunctionEnvironmentVariable) != null)
                {
                    hostType = HostType.AzureFunction;
                }
                else if (Environment.GetEnvironmentVariable(RequestTracingConstants.AzureWebAppEnvironmentVariable) != null)
                {
                    hostType = HostType.AzureWebApp;
                }
                else if (Environment.GetEnvironmentVariable(RequestTracingConstants.KubernetesEnvironmentVariable) != null)
                {
                    hostType = HostType.Kubernetes;
                }
                else if (Environment.GetEnvironmentVariable(RequestTracingConstants.ServiceFabricEnvironmentVariable) != null)
                {
                    hostType = HostType.ServiceFabric;
                }
                else
                {
                    try
                    {
                        string processName = Process.GetCurrentProcess().ProcessName;
                        if (processName != null && processName.Equals(RequestTracingConstants.IISExpressProcessName, StringComparison.OrdinalIgnoreCase))
                        {
                            hostType = HostType.IISExpress;
                        }
                    }
                    catch (InvalidOperationException) { }
                    catch (PlatformNotSupportedException) { }
                    catch (NotSupportedException) { }
                }
            }
            catch (SecurityException) { }

            return hostType;
        }

        public static async Task CallWithRequestTracing(bool tracingEnabled, RequestType requestType, HostType hostType, Action clientCall)
        {
            await CallWithRequestTracing(tracingEnabled, requestType, hostType, () =>
            {
                clientCall();
                return Task.CompletedTask;
            }).ConfigureAwait(false);
        }

        public static async Task CallWithRequestTracing(bool tracingEnabled, RequestType requestType, HostType hostType, Func<Task> clientCall)
        {
            IList<KeyValuePair<string, string>> correlationContext = new List<KeyValuePair<string, string>>();

            if (tracingEnabled)
            {
                AddRequestType(correlationContext, requestType);

                if (hostType != HostType.Unidentified)
                {
                    AddHostType(correlationContext, hostType);
                }
            }

            var activity = new Activity(RequestTracingConstants.DiagnosticHeaderActivityName);
            activity.Start();
            try
            {

                if (correlationContext.Count > 0)
                {
                    activity.AddTag(RequestTracingConstants.CorrelationContextHeader, string.Join(",", correlationContext.Select(kvp => $"{kvp.Key}={kvp.Value}")));
                }

                await clientCall().ConfigureAwait(false);
            }
            finally
            {
                activity.Stop();
            }
        }

        private static void AddRequestType(IList<KeyValuePair<string, string>> correlationContext, RequestType requestType)
        {
            string requestTypeValue = Enum.GetName(typeof(RequestType), requestType);
            correlationContext.Add(new KeyValuePair<string, string>(RequestTracingConstants.RequestTypeKey, requestTypeValue));
        }

        private static void AddHostType(IList<KeyValuePair<string, string>> correlationContext, HostType hostType)
        {
            string hostTypeValue = Enum.GetName(typeof(HostType), hostType);
            correlationContext.Add(new KeyValuePair<string, string>(RequestTracingConstants.HostTypeKey, hostTypeValue));
        }
    }
}
