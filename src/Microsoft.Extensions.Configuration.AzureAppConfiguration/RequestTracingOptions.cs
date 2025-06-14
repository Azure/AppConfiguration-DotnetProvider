// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
using System.Text;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class RequestTracingOptions
    {
        /// <summary>
        /// Type of host.
        /// </summary>
        public HostType HostType { get; set; }

        /// <summary>
        /// Flag to indicate whether Key Vault options have been configured.
        /// </summary>
        public bool IsKeyVaultConfigured { get; set; } = false;

        /// <summary>
        /// Flag to indicate whether Key Vault secret values will be refreshed automatically.
        /// </summary>
        public bool IsKeyVaultRefreshConfigured { get; set; } = false;

        /// <summary>
        /// Flag to indicate whether the request is from a development environment.
        /// </summary>
        public bool IsDevEnvironment { get; set; } = false;

        /// <summary>
        /// This value indicates number of replicas configured.
        /// </summary>
        public int ReplicaCount { get; set; } = 0;

        /// <summary>
        /// Information about feature flags in the application, like filter and variant usage.
        /// </summary>
        public FeatureFlagTracing FeatureFlagTracing { get; set; } = new FeatureFlagTracing();

        /// <summary>
        /// Version of the Microsoft.FeatureManagement assembly, if present in the application.
        /// </summary>
        public string FeatureManagementVersion { get; set; }

        /// <summary>
        /// Version of the Microsoft.FeatureManagement.AspNetCore assembly, if present in the application.
        /// </summary>
        public string FeatureManagementAspNetCoreVersion { get; set; }

        /// <summary>
        /// Flag to indicate whether Microsoft.AspNetCore.SignalR assembly is present in the application.
        /// </summary>
        public bool IsSignalRUsed { get; set; } = false;

        /// <summary>
        /// Flag to indicate whether load balancing is enabled.
        /// </summary>
        public bool IsLoadBalancingEnabled { get; set; } = false;

        /// <summary>
        /// Flag to indicate whether the request is triggered by a failover.
        /// </summary>
        public bool IsFailoverRequest { get; set; } = false;

        /// <summary>
        /// Flag to indicate wether the request is sent to a CDN.
        /// </summary>
        public bool IsCdnEnabled { get; set; } = false;

        /// <summary>
        /// Checks whether any tracing feature is used.
        /// </summary>
        /// <returns>True if any tracing feature is used, otherwise false.</returns>
        public bool UsesAnyTracingFeature()
        {
            return IsLoadBalancingEnabled || IsSignalRUsed;
        }

        /// <summary>
        /// Returns a formatted string containing code names, indicating which tracing features are used by the application.
        /// </summary>
        /// <returns>Formatted string like: "LB+SignalR". If no tracing features are used, empty string will be returned.</returns>
        public string CreateFeaturesString()
        {
            if (!UsesAnyTracingFeature())
            {
                return string.Empty;
            }

            var sb = new StringBuilder();

            if (IsLoadBalancingEnabled)
            {
                sb.Append(RequestTracingConstants.LoadBalancingEnabledTag);
            }

            if (IsSignalRUsed)
            {
                if (sb.Length > 0)
                {
                    sb.Append(RequestTracingConstants.Delimiter);
                }

                sb.Append(RequestTracingConstants.SignalRUsedTag);
            }

            return sb.ToString();
        }
    }
}
