// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
using System.Net.Mime;
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
        /// Version of the Aspire.Microsoft.Extensions.Configuration.AzureAppConfiguration assembly, if present in the application.
        /// </summary>
        public string AspireComponentVersion { get; set; }

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
        /// Flag to indicate whether push refresh is used.
        /// </summary>
        public bool IsPushRefreshUsed { get; set; } = false;

        /// <summary>
        /// Flag to indicate wether the request is sent to a AFD.
        /// </summary>
        public bool IsAfdUsed { get; set; } = false;

        /// <summary>
        /// Flag to indicate whether any key-value uses the json content type and contains
        /// a parameter indicating an AI profile.
        /// </summary>
        public bool UsesAIConfiguration { get; set; } = false;

        /// <summary>
        /// Flag to indicate whether any key-value uses the json content type and contains
        /// a parameter indicating an AI chat completion profile.
        /// </summary>
        public bool UsesAIChatCompletionConfiguration { get; set; } = false;

        /// <summary>
        /// Flag to indicate whether any key-value uses snapshot references.
        /// </summary>
        public bool UsesSnapshotReference { get; set; } = false;

        /// <summary>
        /// Resets the AI configuration tracing flags.
        /// </summary>
        public void ResetAiConfigurationTracing()
        {
            UsesAIConfiguration = false;
            UsesAIChatCompletionConfiguration = false;
        }

        /// <summary>
        /// Updates AI configuration tracing flags based on the provided content type.
        /// </summary>
        /// <param name="contentTypeString">The content type to analyze.</param>
        public void UpdateAiConfigurationTracing(string contentTypeString)
        {
            if (!UsesAIChatCompletionConfiguration &&
                !string.IsNullOrWhiteSpace(contentTypeString) &&
                contentTypeString.TryParseContentType(out ContentType contentType) &&
                contentType.IsAi())
            {
                UsesAIConfiguration = true;

                if (contentType.IsAiChatCompletion())
                {
                    UsesAIChatCompletionConfiguration = true;
                }
            }
        }

        /// <summary>
        /// Checks whether any tracing feature is used.
        /// </summary>
        /// <returns>true if any tracing feature is used, otherwise false.</returns>
        public bool UsesAnyTracingFeature()
        {
            return IsLoadBalancingEnabled ||
                IsSignalRUsed ||
                UsesAIConfiguration ||
                UsesAIChatCompletionConfiguration ||
                UsesSnapshotReference ||
                IsAfdUsed;
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

            if (UsesAIConfiguration)
            {
                if (sb.Length > 0)
                {
                    sb.Append(RequestTracingConstants.Delimiter);
                }

                sb.Append(RequestTracingConstants.AIConfigurationTag);
            }

            if (UsesAIChatCompletionConfiguration)
            {
                if (sb.Length > 0)
                {
                    sb.Append(RequestTracingConstants.Delimiter);
                }

                sb.Append(RequestTracingConstants.AIChatCompletionConfigurationTag);
            }

            if (UsesSnapshotReference)
            {
                if (sb.Length > 0)
                {
                    sb.Append(RequestTracingConstants.Delimiter);
                }

                sb.Append(RequestTracingConstants.SnapshotReferenceTag);
            }

            return sb.ToString();
        }
    }
}
