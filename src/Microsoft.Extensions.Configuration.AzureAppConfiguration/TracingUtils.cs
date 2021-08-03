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

        public static bool IsDevEnvironment()
        {
            try
            {
                string envType = Environment.GetEnvironmentVariable(RequestTracingConstants.AspNetCoreEnvironmentVariable) ??
                                    Environment.GetEnvironmentVariable(RequestTracingConstants.DotNetCoreEnvironmentVariable);
                if (envType != null && envType.Equals(RequestTracingConstants.DevelopmentEnvironmentName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch (SecurityException) { }

            return false;
        }

        public static async Task CallWithRequestTracing(bool tracingEnabled, RequestType requestType, RequestTracingOptions requestTracingOptions, Action clientCall)
        {
            await CallWithRequestTracing(tracingEnabled, requestType, requestTracingOptions, () =>
            {
                clientCall();
                return Task.CompletedTask;
            }).ConfigureAwait(false);
        }

        public static async Task CallWithRequestTracing(bool tracingEnabled, RequestType requestType, RequestTracingOptions requestTracingOptions, Func<Task> clientCall)
        {
            string correlationContextHeader = "";

            if (tracingEnabled)
            {
                correlationContextHeader = CreateCorrelationContextHeader(requestType, requestTracingOptions);
            }

            var activity = new Activity(RequestTracingConstants.DiagnosticHeaderActivityName);
            activity.Start();

            try
            {
                if (!string.IsNullOrWhiteSpace(correlationContextHeader))
                {
                    activity.AddTag(RequestTracingConstants.CorrelationContextHeader, correlationContextHeader);
                }

                await clientCall().ConfigureAwait(false);
            }
            finally
            {
                activity.Stop();
            }
        }

        private static string CreateCorrelationContextHeader(RequestType requestType, RequestTracingOptions requestTracingOptions)
        {
            IList<KeyValuePair<string, string>> correlationContextKeyValues = new List<KeyValuePair<string, string>>();
            IList<string> correlationContextTags = new List<string>();
            
            AddRequestType(correlationContextKeyValues, requestType);

            if (requestTracingOptions.HostType != HostType.Unidentified)
            {
                AddHostType(correlationContextKeyValues, requestTracingOptions.HostType);
            }

            if (requestTracingOptions.IsDevEnvironment)
            {
                correlationContextTags.Add(RequestTracingConstants.DevEnvironmentTag);
            }

            if (requestTracingOptions.IsKeyVaultConfigured)
            {
                correlationContextTags.Add(RequestTracingConstants.KvrConfiguredTag);
            }

            if (requestTracingOptions.IsOfflineCacheConfigured)
            {
                correlationContextTags.Add(RequestTracingConstants.OfflineCacheConfiguredTag);
            }

            string headerKeyValues = "";
            string headerTags = "";
            string fullHeader = "";

            if (correlationContextKeyValues.Count > 0)
            {
                headerKeyValues = string.Join(",", correlationContextKeyValues.Select(kvp => $"{kvp.Key}={kvp.Value}"));
            }
                
            if (correlationContextTags.Count > 0)
            {
                headerTags = string.Join(",", correlationContextTags);
            }

            if (!string.IsNullOrWhiteSpace(headerKeyValues) && !string.IsNullOrWhiteSpace(headerTags))
            {
                fullHeader = string.Join(",", headerKeyValues, headerTags);
            }
            else
            {
                fullHeader = !string.IsNullOrWhiteSpace(headerKeyValues) ? headerKeyValues : headerTags;
            }

            return fullHeader;
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
