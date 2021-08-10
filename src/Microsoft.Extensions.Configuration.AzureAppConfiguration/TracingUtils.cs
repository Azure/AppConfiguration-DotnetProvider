// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security;
using System.Text;
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

            if (tracingEnabled && requestTracingOptions != null)
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
            
            correlationContextKeyValues.Add(new KeyValuePair<string, string>(RequestTracingConstants.RequestTypeKey, Enum.GetName(typeof(RequestType), requestType)));

            if (requestTracingOptions.HostType != HostType.Unidentified)
            {
                correlationContextKeyValues.Add(new KeyValuePair<string, string>(RequestTracingConstants.HostTypeKey, Enum.GetName(typeof(HostType), requestTracingOptions.HostType)));
            }

            if (requestTracingOptions.IsDevEnvironment)
            {
                correlationContextKeyValues.Add(new KeyValuePair<string, string>(RequestTracingConstants.EnvironmentKey, RequestTracingConstants.DevEnvironmentValue));
            }

            if (requestTracingOptions.IsKeyVaultConfigured)
            {
                correlationContextTags.Add(RequestTracingConstants.KeyVaultConfiguredTag);
            }

            if (requestTracingOptions.IsKeyVaultRefreshConfigured)
            {
                correlationContextTags.Add(RequestTracingConstants.KeyVaultRefreshConfiguredTag);
            }

            if (requestTracingOptions.IsOfflineCacheConfigured)
            {
                correlationContextTags.Add(RequestTracingConstants.OfflineCacheConfiguredTag);
            }

            var sb = new StringBuilder();

            foreach (KeyValuePair<string,string> kvp in correlationContextKeyValues)
            {
                if (sb.Length > 0)
                {
                    sb.Append(",");
                }

                sb.Append($"{kvp.Key}={kvp.Value}");
            }

            foreach (string tag in correlationContextTags)
            {
                if (sb.Length > 0)
                {
                    sb.Append(",");
                }

                sb.Append($"{tag}");
            }

            return sb.ToString();
        }
    }
}
