// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
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
                else if (Environment.GetEnvironmentVariable(RequestTracingConstants.ContainerAppEnvironmentVariable) != null)
                {
                    hostType = HostType.ContainerApp;
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

        public static string GetAssemblyVersion(string assemblyName)
        {
            if (!string.IsNullOrEmpty(assemblyName))
            {
                Assembly infoVersionAttribute = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(assembly => assembly.GetName().Name == assemblyName);

                if (infoVersionAttribute != null)
                {
                    string informationalVersion = infoVersionAttribute.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

                    if (string.IsNullOrEmpty(informationalVersion))
                    {
                        return null;
                    }

                    // Commit information is appended to the informational version starting with a '+', so we remove
                    // the commit information to get just the full name of the version.
                    int plusIndex = informationalVersion.IndexOf('+');

                    if (plusIndex != -1)
                    {
                        informationalVersion = informationalVersion.Substring(0, plusIndex);
                    }

                    return informationalVersion;
                }
            }

            return null;
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

            if (requestTracingOptions.ReplicaCount > 0)
            {
                correlationContextKeyValues.Add(new KeyValuePair<string, string>(RequestTracingConstants.ReplicaCountKey, requestTracingOptions.ReplicaCount.ToString()));
            }

            if (requestTracingOptions.HostType != HostType.Unidentified)
            {
                correlationContextKeyValues.Add(new KeyValuePair<string, string>(RequestTracingConstants.HostTypeKey, Enum.GetName(typeof(HostType), requestTracingOptions.HostType)));
            }

            if (requestTracingOptions.IsDevEnvironment)
            {
                correlationContextKeyValues.Add(new KeyValuePair<string, string>(RequestTracingConstants.EnvironmentKey, RequestTracingConstants.DevEnvironmentValue));
            }

            if (requestTracingOptions.FeatureFlagTracing.UsesAnyFeatureFilter())
            {
                correlationContextKeyValues.Add(new KeyValuePair<string, string>(RequestTracingConstants.FilterTypeKey, requestTracingOptions.FeatureFlagTracing.CreateFiltersString()));
            }

            if (requestTracingOptions.FeatureFlagTracing.MaxVariants > 0)
            {
                correlationContextKeyValues.Add(new KeyValuePair<string, string>(RequestTracingConstants.FeatureFlagMaxVariantsKey, requestTracingOptions.FeatureFlagTracing.MaxVariants.ToString()));
            }

            if (requestTracingOptions.FeatureFlagTracing.AnyTracingFeaturesUsed())
            {
                correlationContextKeyValues.Add(new KeyValuePair<string, string>(RequestTracingConstants.FeaturesKey, CreateFeaturesString(requestTracingOptions)));
            }

            if (requestTracingOptions.FeatureManagementVersion != null)
            {
                correlationContextKeyValues.Add(new KeyValuePair<string, string>(RequestTracingConstants.FeatureManagementVersionKey, requestTracingOptions.FeatureManagementVersion));
            }

            if (requestTracingOptions.FeatureManagementAspNetCoreVersion != null)
            {
                correlationContextKeyValues.Add(new KeyValuePair<string, string>(RequestTracingConstants.FeatureManagementAspNetCoreVersionKey, requestTracingOptions.FeatureManagementAspNetCoreVersion));
            }

            if (requestTracingOptions.IsKeyVaultConfigured)
            {
                correlationContextTags.Add(RequestTracingConstants.KeyVaultConfiguredTag);
            }

            if (requestTracingOptions.IsKeyVaultRefreshConfigured)
            {
                correlationContextTags.Add(RequestTracingConstants.KeyVaultRefreshConfiguredTag);
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

        private static string CreateFeaturesString(RequestTracingOptions requestTracingOptions)
        {
            var sb = new StringBuilder();

            if (requestTracingOptions.FeatureFlagTracing.UsesSeed)
            {
                sb.Append(RequestTracingConstants.FeatureFlagUsesSeedTag);
            }

            if (requestTracingOptions.FeatureFlagTracing.UsesVariantConfigurationReference)
            {
                if (sb.Length > 0)
                {
                    sb.Append(RequestTracingConstants.Delimiter);
                }

                sb.Append(RequestTracingConstants.FeatureFlagUsesVariantConfigurationReferenceTag);
            }

            if (requestTracingOptions.FeatureFlagTracing.UsesTelemetry)
            {
                if (sb.Length > 0)
                {
                    sb.Append(RequestTracingConstants.Delimiter);
                }

                sb.Append(RequestTracingConstants.FeatureFlagUsesTelemetryTag);
            }

            return sb.ToString();
        }
    }
}
