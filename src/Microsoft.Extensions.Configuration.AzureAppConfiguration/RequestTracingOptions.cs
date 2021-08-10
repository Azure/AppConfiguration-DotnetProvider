﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
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
        /// Flag to indicate whether Offline Cache has been configured.
        /// </summary>
        public bool IsOfflineCacheConfigured { get; set; } = false;

        /// <summary>
        /// Flag to indicate whether the request is from a development environment.
        /// </summary>
        public bool IsDevEnvironment { get; set; } = false;
    }
}
