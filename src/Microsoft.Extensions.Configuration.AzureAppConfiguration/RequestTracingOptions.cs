// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;

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
        /// Type of feature filters used by the application.
        /// </summary>
        public FeatureFilterTelemetry FilterTelemetry { get; set; } = new FeatureFilterTelemetry();

        /// <summary>
        /// Version of the Microsoft.FeatureManagement assembly being used.
        /// </summary>
        public string FeatureManagementVersion { get; set; }

        /// <summary>
        /// Version of the Microsoft.FeatureManagement.AspNetCore assembly being used.
        /// </summary>
        public string FeatureManagementAspNetCoreVersion { get; set; }
    }
}
